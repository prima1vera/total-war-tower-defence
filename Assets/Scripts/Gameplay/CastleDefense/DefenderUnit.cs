using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class DefenderUnit : MonoBehaviour, IEnemyPathBlocker
{
    private static readonly int MoveXHash = Animator.StringToHash("moveX");
    private static readonly int MoveYHash = Animator.StringToHash("moveY");
    private static readonly int IsMovingHash = Animator.StringToHash("isMoving");
    private static readonly int AttackHash = Animator.StringToHash("attack");
    private static readonly int IsDeadHash = Animator.StringToHash("isDead");
    private const float SeparationRefreshInterval = 0.08f;
    private const float SeparationRefreshJitter = 0.015f;
    private const float DefenderSeparationRadius = 0.34f;
    private const float DefenderSeparationWeight = 0.24f;
    private const int MaxSeparationColliders = 16;
    private const float EngagementOrbitBaseRadius = 0.24f;
    private static readonly Dictionary<int, List<DefenderUnit>> DefendersByTarget = new Dictionary<int, List<DefenderUnit>>(64);

    [Header("Health")]
    [SerializeField, Min(1)] private int maxHealth = 40;

    [Header("Movement")]
    [SerializeField, Min(0.1f)] private float moveSpeed = 2.2f;
    [SerializeField, Min(0f)] private float stopDistance = 0.06f;

    [Header("Melee Combat")]
    [SerializeField, Min(0.2f)] private float engageRange = 2.1f;
    [SerializeField, Min(0.2f)] private float chaseLimitFromGuard = 3.2f;
    [SerializeField, Min(0.1f)] private float attackRange = 0.45f;
    [SerializeField, Min(1)] private int attackDamage = 2;
    [SerializeField, Min(0.05f)] private float attackInterval = 0.75f;
    [SerializeField, Min(0f)] private float attackKnockback = 0.04f;
    [SerializeField, Min(0.02f)] private float targetRefreshInterval = 0.12f;

    [Header("Blocking")]
    [SerializeField, Min(0.1f)] private float blockRadius = 0.35f;
    [SerializeField] private int pathPriority = 50;

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

        if (ShouldRefreshTarget())
        {
            RefreshTarget();
            targetRefreshTimer = targetRefreshInterval;
        }

        UnitHealth target = currentEnemyTarget;
        if (target != null && (target.IsDead || !target.gameObject.activeInHierarchy))
        {
            SetCurrentEnemyTarget(null);
            target = null;
        }

        if (target != null && TryGetGuardPoint(out Vector3 guardForLeash))
        {
            float hardLeashDistance = Mathf.Max(0.2f, chaseLimitFromGuard + 0.15f);
            float selfDistanceFromGuard = Vector2.Distance(transform.position, guardForLeash);
            if (selfDistanceFromGuard > hardLeashDistance)
            {
                SetCurrentEnemyTarget(null);
                MoveToGuardPoint();
                return;
            }
        }

        if (target == null)
        {
            MoveToGuardPoint();
            return;
        }

        Vector2 toTarget = target.transform.position - transform.position;
        float distance = toTarget.magnitude;
        float targetColliderPadding = 0f;
        Collider2D targetCollider = target.CachedCollider;
        if (targetCollider != null)
            targetColliderPadding = Mathf.Min(targetCollider.bounds.extents.x, targetCollider.bounds.extents.y) * 0.4f;

        float hitDistance = Mathf.Max(0.1f, attackRange + targetColliderPadding);
        Vector3 chaseDestination = ResolveChaseDestination(target);
        float distanceToSlot = Vector2.Distance(transform.position, chaseDestination);

        if (distance > hitDistance || distanceToSlot > Mathf.Max(stopDistance, 0.08f))
        {
            MoveTowards(chaseDestination);
            return;
        }

        ApplyMovingAnimation(false);
        UpdateAnimatorDirection(toTarget);
        if (attackTimer > 0f)
            return;

        Vector2 hitDirection = toTarget.sqrMagnitude > 0.001f ? toTarget.normalized : Vector2.down;
        float appliedKnockback = attackKnockback >= 0.08f ? attackKnockback : 0f;
        target.TakeDamage(attackDamage, DamageType.Normal, hitDirection, appliedKnockback);
        if (target != null && target.TryGetComponent(out UnitMovement movement))
            movement.NotifyBlockerAggro(this);
        TriggerAttackAnimation();
        attackTimer = attackInterval;
    }

    private bool ShouldRefreshTarget()
    {
        if (targetRefreshTimer > 0f)
            return false;

        if (currentEnemyTarget == null)
            return true;

        if (currentEnemyTarget.IsDead)
            return true;

        if (!TryGetGuardPoint(out Vector3 guardWorldPosition))
            return false;

        float distFromGuard = Vector2.Distance(currentEnemyTarget.transform.position, guardWorldPosition);
        if (distFromGuard > chaseLimitFromGuard)
            return true;

        float selfDistFromGuard = Vector2.Distance(transform.position, guardWorldPosition);
        return selfDistFromGuard > chaseLimitFromGuard + 0.15f;
    }

    private void RefreshTarget()
    {
        if (!TryGetGuardPoint(out Vector3 guardWorldPosition))
        {
            SetCurrentEnemyTarget(null);
            return;
        }

        float searchRadius = Mathf.Max(0.2f, Mathf.Min(engageRange, chaseLimitFromGuard + 0.35f));
        EnemyRegistry.TryGetNearestEnemy(guardWorldPosition, searchRadius, out UnitHealth refreshedTarget);
        SetCurrentEnemyTarget(refreshedTarget);
    }

    private void MoveToGuardPoint()
    {
        if (!TryGetGuardPoint(out Vector3 guardWorldPosition))
            return;

        MoveTowards(guardWorldPosition);
    }

    private void MoveTowards(Vector3 targetPosition)
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
        if (cachedSeparation.sqrMagnitude > 0.0001f)
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

    private Vector2 ResolveEngagementOffset(UnitHealth target)
    {
        if (target == null)
            return Vector2.zero;

        float radius = EngagementOrbitBaseRadius;
        Collider2D targetCollider = target.CachedCollider;
        if (targetCollider != null)
        {
            Bounds bounds = targetCollider.bounds;
            radius += Mathf.Min(bounds.extents.x, bounds.extents.y) * 0.35f;
        }

        int seed = Mathf.Abs((GetInstanceID() * 73856093) ^ (target.GetInstanceID() * 19349663));
        int slotIndex = 0;
        int slotCount = 1;
        ResolveSlotAroundTarget(target, out slotIndex, out slotCount);

        const int slotsPerRing = 4;
        int ringIndex = Mathf.Max(0, slotIndex / slotsPerRing);
        int indexInRing = Mathf.Max(0, slotIndex % slotsPerRing);
        int firstIndexInRing = ringIndex * slotsPerRing;
        int remaining = Mathf.Max(1, slotCount - firstIndexInRing);
        int ringCount = Mathf.Min(slotsPerRing, remaining);

        float slotStep = Mathf.PI * 2f / Mathf.Max(1, ringCount);
        float startAngle = (seed % 360) * Mathf.Deg2Rad;
        float angle = startAngle + slotStep * indexInRing + ringIndex * 0.31f;

        radius += ringIndex * 0.16f;
        return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
    }

    private Vector3 ResolveChaseDestination(UnitHealth target)
    {
        if (target == null)
            return transform.position;

        Vector3 chaseDestination = target.transform.position + (Vector3)ResolveEngagementOffset(target);
        if (TryGetGuardPoint(out Vector3 guardForClamp))
        {
            Vector2 fromGuard = (Vector2)(chaseDestination - guardForClamp);
            float leashDistance = Mathf.Max(0.2f, chaseLimitFromGuard);
            if (fromGuard.sqrMagnitude > leashDistance * leashDistance)
                chaseDestination = guardForClamp + (Vector3)(fromGuard.normalized * leashDistance);
        }

        return chaseDestination;
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
        SetCurrentEnemyTarget(null);
        ApplyMovingAnimation(false);
        ApplyDeadAnimation(true);

        if (blockerCollider != null)
            blockerCollider.enabled = false;

        Died?.Invoke(this);
        GlobalDefenderDied?.Invoke(this);
        gameObject.SetActive(false);
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
        if (currentEnemyTarget == newTarget)
            return;

        UpdateTargetSlotting(currentEnemyTarget, false);
        currentEnemyTarget = newTarget;
        UpdateTargetSlotting(currentEnemyTarget, true);
    }

    private void UpdateTargetSlotting(UnitHealth target, bool add)
    {
        if (target == null)
            return;

        int targetId = target.GetInstanceID();
        if (add)
        {
            if (!DefendersByTarget.TryGetValue(targetId, out List<DefenderUnit> defenders))
            {
                defenders = new List<DefenderUnit>(8);
                DefendersByTarget[targetId] = defenders;
            }

            if (!defenders.Contains(this))
                defenders.Add(this);

            return;
        }

        if (!DefendersByTarget.TryGetValue(targetId, out List<DefenderUnit> existingDefenders))
            return;
        

        existingDefenders.Remove(this);
        if (existingDefenders.Count == 0)
            DefendersByTarget.Remove(targetId);
    }

    private void ResolveSlotAroundTarget(UnitHealth target, out int slotIndex, out int slotCount)
    {
        slotIndex = 0;
        slotCount = 1;
        if (target == null)
            return;

        int targetId = target.GetInstanceID();
        if (!DefendersByTarget.TryGetValue(targetId, out List<DefenderUnit> defenders) || defenders == null)
            return;

        for (int i = defenders.Count - 1; i >= 0; i--)
        {
            DefenderUnit defender = defenders[i];
            if (defender == null || !defender.IsAlive || defender.currentEnemyTarget != target)
                defenders.RemoveAt(i);
        }

        if (defenders.Count == 0)
        {
            DefendersByTarget.Remove(targetId);
            return;
        }

        defenders.Sort((a, b) => a.GetInstanceID().CompareTo(b.GetInstanceID()));

        slotCount = defenders.Count;
        int index = defenders.IndexOf(this);
        slotIndex = index >= 0 ? index : 0;
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
