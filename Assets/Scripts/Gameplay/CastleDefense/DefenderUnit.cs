using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class DefenderUnit : MonoBehaviour, IEnemyPathBlocker, IEnemyBlockerEngagement
{
    private static readonly int MoveXHash = Animator.StringToHash("moveX");
    private static readonly int MoveYHash = Animator.StringToHash("moveY");
    private static readonly int IsMovingHash = Animator.StringToHash("isMoving");
    private static readonly int AttackHash = Animator.StringToHash("attack");
    private static readonly int IsDeadHash = Animator.StringToHash("isDead");
    private const float SeparationRefreshInterval = 0.08f;
    private const float SeparationRefreshJitter = 0.015f;
    private const float DefenderSeparationRadius = 0.28f;
    private const float DefenderSeparationWeight = 0.12f;
    private const int MaxSeparationColliders = 16;

    [Header("Health")]
    [SerializeField, Min(1)] private int maxHealth = 40;

    [Header("Movement")]
    [SerializeField, Min(0.1f)] private float moveSpeed = 2.2f;
    [SerializeField, Min(0f)] private float stopDistance = 0.06f;

    [Header("Melee Combat")]
    [SerializeField, Min(0.2f)] private float engageRange = 2.1f;
    [SerializeField, Min(0.2f)] private float chaseLimitFromGuard = 3.2f;
    [SerializeField, Min(0.1f)] private float attackRange = 0.65f;
    [SerializeField, Min(1)] private int attackDamage = 2;
    [SerializeField, Min(0.05f)] private float attackInterval = 0.75f;
    [SerializeField, Min(0f)] private float attackKnockback = 0.04f;
    [SerializeField, Min(0.02f)] private float targetRefreshInterval = 0.12f;
    [SerializeField] private bool notifyEnemyAggroOnHit = false;

    [Header("Blocking")]
    [SerializeField, Min(0.1f)] private float blockRadius = 0.35f;
    [SerializeField] private int pathPriority = 50;
    [SerializeField, Min(1)] private int maxAttackersPerDefender = 2;

    [Header("Scene Wiring")]
    [SerializeField] private Collider2D blockerCollider;
    [SerializeField] private Animator animator;

    private bool hasMoveXParam;
    private bool hasMoveYParam;
    private bool hasIsMovingParam;
    private bool hasAttackTriggerParam;
    private bool hasAttackBoolParam;
    private bool hasIsDeadParam;

    private Transform guardPoint;
    private Vector3 staticGuardPoint;
    private bool useStaticGuardPoint;
    private UnitHealth currentEnemyTarget;
    private int currentHealth;
    private bool initialized;
    private bool dead;
    private float targetRefreshTimer;
    private float attackTimer;
    private float separationRefreshTimer;
    private Vector2 cachedSeparation;
    private Collider2D[] separationBuffer;
    private readonly HashSet<UnitHealth> engagingAttackers = new HashSet<UnitHealth>();
    private bool hasPendingGuardPoint;
    private bool pendingGuardResetTarget;
    private Vector3 pendingGuardPoint;

    public event Action<DefenderUnit> Died;
    public event Action<DefenderDamageFeedbackEvent> DamageTaken;

    public static event Action<DefenderDamageFeedbackEvent> GlobalDamageTaken;
    public static event Action<DefenderUnit> GlobalDefenderDied;

    public bool IsAlive => initialized && !dead;
    public bool IsBlocking => IsAlive && gameObject.activeInHierarchy;
    public Vector2 WorldPosition => blockerCollider != null ? blockerCollider.bounds.center : (Vector2)transform.position;
    public float BlockRadius => ResolveBlockRadius();
    public int PathPriority => pathPriority;
    public float ChaseLimitFromGuard => chaseLimitFromGuard;
    public bool HasEngagingAttackers
    {
        get
        {
            CleanupEngagingAttackers();
            return engagingAttackers.Count > 0;
        }
    }
    public int CurrentHealth => Mathf.Max(0, currentHealth);
    public int MaxHealth => Mathf.Max(1, maxHealth);
    public float NormalizedHealth => Mathf.Clamp01((float)CurrentHealth / MaxHealth);

    private void Awake()
    {
        if (blockerCollider == null)
            blockerCollider = GetComponent<Collider2D>();

        if (animator == null)
            animator = GetComponent<Animator>();

        separationBuffer = new Collider2D[MaxSeparationColliders];

        CacheAnimatorBindings();
    }

    private void OnEnable()
    {
        EnemyPathBlockerRegistry.Register(this);
    }

    private void OnDisable()
    {
        engagingAttackers.Clear();
        SetCurrentEnemyTarget(null);
        EnemyPathBlockerRegistry.Unregister(this);
    }

    public void ActivateAt(Transform guard)
    {
        guardPoint = guard;
        useStaticGuardPoint = guard == null;
        staticGuardPoint = guard != null ? guard.position : transform.position;
        ActivateInternal();
    }

    public void ActivateAt(Vector3 guardPosition)
    {
        guardPoint = null;
        useStaticGuardPoint = true;
        staticGuardPoint = guardPosition;
        ActivateInternal();
    }

    public void UpdateGuardPoint(Vector3 guardPosition, bool resetTarget)
    {
        if (HasEngagingAttackers)
        {
            hasPendingGuardPoint = true;
            pendingGuardPoint = guardPosition;
            pendingGuardResetTarget |= resetTarget;
            return;
        }

        ApplyGuardPointInternal(guardPosition, resetTarget);
    }

    private void ApplyGuardPointInternal(Vector3 guardPosition, bool resetTarget)
    {
        guardPoint = null;
        useStaticGuardPoint = true;
        staticGuardPoint = guardPosition;

        if (!initialized || dead)
            return;

        if (resetTarget)
        {
            SetCurrentEnemyTarget(null);
            targetRefreshTimer = 0f;
        }
    }

    public void ReceiveBlockDamage(int amount, UnitHealth attacker)
    {
        if (!initialized || dead || amount <= 0)
            return;

        currentHealth -= amount;
        RaiseDamageTaken(amount);
        if (currentHealth > 0)
            return;

        Die();
    }

    public bool CanAcceptBlockerAttacker(UnitHealth attacker)
    {
        if (!IsAlive || attacker == null)
            return false;

        CleanupEngagingAttackers();
        if (engagingAttackers.Contains(attacker))
            return true;

        int capacity = Mathf.Max(1, maxAttackersPerDefender);
        return engagingAttackers.Count < capacity;
    }

    public bool HasBlockerAttacker(UnitHealth attacker)
    {
        if (attacker == null)
            return false;

        CleanupEngagingAttackers();
        return engagingAttackers.Contains(attacker);
    }

    public bool TryAcquireBlockerAttacker(UnitHealth attacker)
    {
        if (!CanAcceptBlockerAttacker(attacker))
            return false;

        engagingAttackers.Add(attacker);
        return true;
    }

    public void ReleaseBlockerAttacker(UnitHealth attacker)
    {
        if (attacker == null)
            return;

        engagingAttackers.Remove(attacker);
    }

    private void ActivateInternal()
    {
        SetCurrentEnemyTarget(null);
        currentHealth = Mathf.Max(1, maxHealth);
        dead = false;
        initialized = true;
        targetRefreshTimer = 0f;
        attackTimer = 0f;
        separationRefreshTimer = UnityEngine.Random.Range(0f, SeparationRefreshInterval);
        cachedSeparation = Vector2.zero;
        hasPendingGuardPoint = false;
        pendingGuardResetTarget = false;

        if (blockerCollider != null && !blockerCollider.enabled)
            blockerCollider.enabled = true;

        ApplyDeadAnimation(false);
        ApplyMovingAnimation(false);
        gameObject.SetActive(true);
    }

    private void Update()
    {
        if (!initialized || dead)
            return;

        targetRefreshTimer -= Time.deltaTime;
        attackTimer -= Time.deltaTime;
        separationRefreshTimer -= Time.deltaTime;

        if (separationRefreshTimer <= 0f)
        {
            cachedSeparation = CalculateSeparation();
            float jitter = UnityEngine.Random.Range(-SeparationRefreshJitter, SeparationRefreshJitter);
            separationRefreshTimer = Mathf.Max(0.02f, SeparationRefreshInterval + jitter);
        }

        CleanupEngagingAttackers();
        if (hasPendingGuardPoint && engagingAttackers.Count == 0)
        {
            ApplyGuardPointInternal(pendingGuardPoint, pendingGuardResetTarget);
            hasPendingGuardPoint = false;
            pendingGuardResetTarget = false;
        }

        if (!TryGetGuardPoint(out Vector3 guardWorldPosition))
            return;

        float distanceToGuard = Vector2.Distance(transform.position, guardWorldPosition);
        float combatAnchorRadius = Mathf.Max(attackRange + 0.18f, 0.55f);
        float hardAnchorRadius = Mathf.Max(chaseLimitFromGuard, combatAnchorRadius + 0.45f);
        if (distanceToGuard > hardAnchorRadius)
        {
            SetCurrentEnemyTarget(null);
            MoveToGuardPoint();
            SetAttackAnimation(false);
            return;
        }

        if (distanceToGuard > combatAnchorRadius && engagingAttackers.Count == 0)
        {
            SetCurrentEnemyTarget(null);
            MoveToGuardPoint();
            SetAttackAnimation(false);
            return;
        }

        if (ShouldRefreshTarget(guardWorldPosition))
        {
            RefreshTarget(guardWorldPosition);
            targetRefreshTimer = targetRefreshInterval;
        }

        UnitHealth target = currentEnemyTarget;
        if (target != null && (target.IsDead || !target.gameObject.activeInHierarchy))
        {
            SetCurrentEnemyTarget(null);
            target = null;
        }

        if (target != null)
        {
            float targetDistanceFromGuard = Vector2.Distance(target.transform.position, guardWorldPosition);
            float allowedTargetDistance = engagingAttackers.Contains(target)
                ? Mathf.Max(chaseLimitFromGuard + 0.6f, attackRange + 0.45f)
                : Mathf.Max(attackRange + 0.2f, chaseLimitFromGuard);

            if (targetDistanceFromGuard > allowedTargetDistance)
            {
                SetCurrentEnemyTarget(null);
                target = null;
            }
        }

        if (target == null)
        {
            ApplyMovingAnimation(false);
            SetAttackAnimation(false);
            return;
        }

        Vector2 selfPosition = WorldPosition;
        Vector2 targetPosition = target.transform.position;
        Collider2D targetCollider = target.CachedCollider;
        if (targetCollider != null)
            targetPosition = targetCollider.bounds.center;

        Vector2 toTarget = targetPosition - selfPosition;
        float distance = toTarget.magnitude;
        float targetColliderPadding = 0f;
        float targetColliderRadius = 0.1f;
        if (targetCollider != null)
        {
            targetColliderPadding = Mathf.Min(targetCollider.bounds.extents.x, targetCollider.bounds.extents.y) * 0.4f;
            targetColliderRadius = Mathf.Max(targetCollider.bounds.extents.x, targetCollider.bounds.extents.y);
        }

        float hitDistance = Mathf.Max(0.1f, attackRange + targetColliderPadding);
        if (engagingAttackers.Contains(target))
        {
            float meleeContactDistance = ResolveBlockRadius() + targetColliderRadius + 0.08f;
            hitDistance = Mathf.Max(hitDistance, meleeContactDistance);
        }
        if (distance > hitDistance)
        {
            if (engagingAttackers.Contains(target) && toTarget.sqrMagnitude > 0.0001f)
            {
                float maxStepDistance = hitDistance + 0.45f;
                if (distance <= maxStepDistance)
                {
                    Vector2 desiredContactPoint = targetPosition - toTarget.normalized * Mathf.Max(0.08f, hitDistance * 0.9f);
                    float contactDistanceFromGuard = Vector2.Distance(desiredContactPoint, guardWorldPosition);
                    if (contactDistanceFromGuard <= combatAnchorRadius + 0.08f)
                    {
                        MoveTowards(new Vector3(desiredContactPoint.x, desiredContactPoint.y, transform.position.z), applySeparation: false);
                        SetAttackAnimation(false);
                        return;
                    }
                }
            }

            ApplyMovingAnimation(false);
            SetAttackAnimation(false);
            UpdateAnimatorDirection(toTarget.sqrMagnitude > 0.0001f ? toTarget.normalized : Vector2.down);
            return;
        }

        ApplyMovingAnimation(false);
        UpdateAnimatorDirection(toTarget);
        SetAttackAnimation(true);
        if (attackTimer > 0f)
            return;

        Vector2 hitDirection = toTarget.sqrMagnitude > 0.001f ? toTarget.normalized : Vector2.down;
        float appliedKnockback = attackKnockback >= 0.08f ? attackKnockback : 0f;
        target.TakeDamage(attackDamage, DamageType.Normal, hitDirection, appliedKnockback);
        if (notifyEnemyAggroOnHit && target != null && target.TryGetComponent(out UnitMovement movement))
            movement.NotifyBlockerAggro(this);
        TriggerAttackAnimation();
        attackTimer = attackInterval;
    }

    private bool ShouldRefreshTarget(Vector3 guardWorldPosition)
    {
        if (targetRefreshTimer > 0f)
            return false;

        if (engagingAttackers.Count > 0 && (currentEnemyTarget == null || !engagingAttackers.Contains(currentEnemyTarget)))
            return true;

        if (currentEnemyTarget == null)
            return true;

        if (currentEnemyTarget.IsDead)
            return true;

        float distFromGuard = Vector2.Distance(currentEnemyTarget.transform.position, guardWorldPosition);
        if (distFromGuard > Mathf.Max(attackRange + 0.2f, engageRange))
            return true;

        return !currentEnemyTarget.gameObject.activeInHierarchy;
    }

    private void RefreshTarget(Vector3 guardWorldPosition)
    {
        if (TryGetNearestEngagingAttacker(out UnitHealth engagingTarget))
        {
            SetCurrentEnemyTarget(engagingTarget);
            return;
        }

        float searchRadius = Mathf.Max(0.2f, engageRange);
        EnemyRegistry.TryGetNearestEnemy(guardWorldPosition, searchRadius, out UnitHealth refreshedTarget);
        SetCurrentEnemyTarget(refreshedTarget);
    }

    private bool TryGetNearestEngagingAttacker(out UnitHealth nearest)
    {
        nearest = null;
        if (engagingAttackers.Count == 0)
            return false;

        float bestDistanceSqr = float.MaxValue;
        Vector2 selfPosition = transform.position;

        using var iterator = engagingAttackers.GetEnumerator();
        while (iterator.MoveNext())
        {
            UnitHealth attacker = iterator.Current;
            if (attacker == null || attacker.IsDead || !attacker.gameObject.activeInHierarchy)
                continue;

            Vector2 attackerPosition = attacker.transform.position;
            Collider2D attackerCollider = attacker.CachedCollider;
            if (attackerCollider != null)
                attackerPosition = attackerCollider.bounds.center;

            float distanceSqr = (attackerPosition - selfPosition).sqrMagnitude;
            if (distanceSqr >= bestDistanceSqr)
                continue;

            bestDistanceSqr = distanceSqr;
            nearest = attacker;
        }

        return nearest != null;
    }

    private void MoveToGuardPoint()
    {
        if (!TryGetGuardPoint(out Vector3 guardWorldPosition))
            return;

        MoveTowards(guardWorldPosition, applySeparation: true);
    }

    private void MoveTowards(Vector3 targetPosition, bool applySeparation)
    {
        Vector2 current = transform.position;
        Vector2 destination = targetPosition;
        Vector2 toTarget = destination - current;
        if (toTarget.sqrMagnitude <= stopDistance * stopDistance)
        {
            ApplyMovingAnimation(false);
            return;
        }

        Vector2 direction = toTarget.normalized;
        if (applySeparation && cachedSeparation.sqrMagnitude > 0.0001f)
        {
            // Keep defenders shoulder-to-shoulder without obvious "magnet" behavior.
            float separationBlend = Mathf.InverseLerp(stopDistance * 1.5f, stopDistance * 6f, toTarget.magnitude);
            float separationWeight = DefenderSeparationWeight * separationBlend;
            direction = (direction + cachedSeparation * separationWeight).normalized;
        }

        Vector2 next = current + direction * (moveSpeed * Time.deltaTime);
        transform.position = new Vector3(next.x, next.y, transform.position.z);
        ApplyMovingAnimation(true);
        UpdateAnimatorDirection(direction);
    }

    private Vector2 CalculateSeparation()
    {
        if (separationBuffer == null || separationBuffer.Length == 0)
            return Vector2.zero;

        Vector2 center = WorldPosition;
        int count = Physics2D.OverlapCircleNonAlloc(center, DefenderSeparationRadius, separationBuffer);
        if (count <= 1)
            return Vector2.zero;

        Vector2 separation = Vector2.zero;
        for (int i = 0; i < count; i++)
        {
            Collider2D neighborCollider = separationBuffer[i];
            separationBuffer[i] = null;

            if (neighborCollider == null || neighborCollider == blockerCollider)
                continue;

            DefenderUnit neighbor = neighborCollider.GetComponentInParent<DefenderUnit>();
            if (neighbor == null || neighbor == this || !neighbor.IsAlive)
                continue;

            Vector2 diff = center - neighbor.WorldPosition;
            float distSq = diff.sqrMagnitude;
            if (distSq <= 0.0001f)
                continue;

            float dist = Mathf.Sqrt(distSq);
            float falloff = 1f - Mathf.Clamp01(dist / DefenderSeparationRadius);
            if (falloff <= 0f)
                continue;

            separation += diff.normalized * falloff;
        }

        return Vector2.ClampMagnitude(separation, 1f);
    }

    private bool TryGetGuardPoint(out Vector3 guardWorldPosition)
    {
        if (!useStaticGuardPoint && guardPoint != null)
        {
            guardWorldPosition = guardPoint.position;
            return true;
        }

        if (useStaticGuardPoint)
        {
            guardWorldPosition = staticGuardPoint;
            return true;
        }

        guardWorldPosition = default;
        return false;
    }

    private void UpdateAnimatorDirection(Vector2 direction)
    {
        if (animator == null || direction.sqrMagnitude <= 0.0001f)
            return;

        if (hasMoveXParam)
            animator.SetFloat(MoveXHash, direction.x);

        if (hasMoveYParam)
            animator.SetFloat(MoveYHash, direction.y);
    }

    private float ResolveBlockRadius()
    {
        if (blockerCollider == null)
            return Mathf.Max(0.1f, blockRadius);

        Bounds bounds = blockerCollider.bounds;
        float radius = Mathf.Max(bounds.extents.x, bounds.extents.y);
        return Mathf.Max(0.1f, Mathf.Max(blockRadius, radius));
    }

    private void Die()
    {
        if (dead)
            return;

        dead = true;
        engagingAttackers.Clear();
        SetCurrentEnemyTarget(null);
        ApplyMovingAnimation(false);
        ApplyDeadAnimation(true);

        if (blockerCollider != null)
            blockerCollider.enabled = false;

        Died?.Invoke(this);
        GlobalDefenderDied?.Invoke(this);
        gameObject.SetActive(false);
    }

    private void CleanupEngagingAttackers()
    {
        if (engagingAttackers.Count == 0)
            return;

        using var iterator = engagingAttackers.GetEnumerator();
        List<UnitHealth> stale = null;
        while (iterator.MoveNext())
        {
            UnitHealth attacker = iterator.Current;
            if (attacker == null || attacker.IsDead || !attacker.gameObject.activeInHierarchy)
            {
                stale ??= new List<UnitHealth>(4);
                stale.Add(attacker);
            }
        }

        if (stale == null)
            return;

        for (int i = 0; i < stale.Count; i++)
            engagingAttackers.Remove(stale[i]);
    }

    private void RaiseDamageTaken(int amount)
    {
        DefenderDamageFeedbackEvent damageEvent = new DefenderDamageFeedbackEvent(
            this,
            amount,
            Mathf.Max(0, currentHealth),
            MaxHealth,
            currentHealth <= 0);

        DamageTaken?.Invoke(damageEvent);
        GlobalDamageTaken?.Invoke(damageEvent);
    }

    private void SetCurrentEnemyTarget(UnitHealth newTarget)
    {
        currentEnemyTarget = newTarget;
    }

    private void CacheAnimatorBindings()
    {
        hasMoveXParam = false;
        hasMoveYParam = false;
        hasIsMovingParam = false;
        hasAttackTriggerParam = false;
        hasAttackBoolParam = false;
        hasIsDeadParam = false;

        if (animator == null)
            return;

        AnimatorControllerParameter[] parameters = animator.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            AnimatorControllerParameter parameter = parameters[i];
            switch (parameter.nameHash)
            {
                case int nameHash when nameHash == MoveXHash:
                    hasMoveXParam = true;
                    break;
                case int nameHash when nameHash == MoveYHash:
                    hasMoveYParam = true;
                    break;
                case int nameHash when nameHash == IsMovingHash && parameter.type == AnimatorControllerParameterType.Bool:
                    hasIsMovingParam = true;
                    break;
                case int nameHash when nameHash == AttackHash && parameter.type == AnimatorControllerParameterType.Trigger:
                    hasAttackTriggerParam = true;
                    break;
                case int nameHash when nameHash == AttackHash && parameter.type == AnimatorControllerParameterType.Bool:
                    hasAttackBoolParam = true;
                    break;
                case int nameHash when nameHash == IsDeadHash && parameter.type == AnimatorControllerParameterType.Bool:
                    hasIsDeadParam = true;
                    break;
            }
        }
    }

    private void ApplyMovingAnimation(bool isMoving)
    {
        if (animator == null || !hasIsMovingParam)
            return;

        animator.SetBool(IsMovingHash, isMoving);
    }

    private void ApplyDeadAnimation(bool isDead)
    {
        if (animator == null || !hasIsDeadParam)
            return;

        animator.SetBool(IsDeadHash, isDead);
    }

    private void SetAttackAnimation(bool isAttacking)
    {
        if (animator == null || !hasAttackBoolParam)
            return;

        animator.SetBool(AttackHash, isAttacking);
    }

    private void TriggerAttackAnimation()
    {
        if (animator == null)
            return;

        if (hasAttackTriggerParam)
        {
            animator.SetTrigger(AttackHash);
            return;
        }

        if (hasAttackBoolParam)
        {
            animator.SetBool(AttackHash, true);
            animator.SetBool(AttackHash, false);
        }
    }
}

public readonly struct DefenderDamageFeedbackEvent
{
    public DefenderUnit Target { get; }
    public int Amount { get; }
    public int CurrentHealth { get; }
    public int MaxHealth { get; }
    public bool IsFatal { get; }
    public float NormalizedHealth => MaxHealth > 0 ? Mathf.Clamp01((float)CurrentHealth / MaxHealth) : 0f;

    public DefenderDamageFeedbackEvent(DefenderUnit target, int amount, int currentHealth, int maxHealth, bool isFatal)
    {
        Target = target;
        Amount = Mathf.Max(0, amount);
        CurrentHealth = Mathf.Max(0, currentHealth);
        MaxHealth = Mathf.Max(1, maxHealth);
        IsFatal = isFatal;
    }
}
