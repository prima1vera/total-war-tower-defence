using System;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class DefenderUnit : MonoBehaviour, IEnemyPathBlocker
{
    private static readonly int MoveXHash = Animator.StringToHash("moveX");
    private static readonly int MoveYHash = Animator.StringToHash("moveY");
    private static readonly int IsMovingHash = Animator.StringToHash("isMoving");
    private static readonly int AttackHash = Animator.StringToHash("attack");
    private static readonly int IsDeadHash = Animator.StringToHash("isDead");

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

    public event Action<DefenderUnit> Died;

    public bool IsAlive => initialized && !dead;
    public bool IsBlocking => IsAlive && gameObject.activeInHierarchy;
    public Vector2 WorldPosition => blockerCollider != null ? blockerCollider.bounds.center : (Vector2)transform.position;
    public float BlockRadius => ResolveBlockRadius();
    public int PathPriority => pathPriority;

    private void Awake()
    {
        if (blockerCollider == null)
            blockerCollider = GetComponent<Collider2D>();

        if (animator == null)
            animator = GetComponent<Animator>();

        CacheAnimatorBindings();
    }

    private void OnEnable()
    {
        EnemyPathBlockerRegistry.Register(this);
    }

    private void OnDisable()
    {
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

    public void ReceiveBlockDamage(int amount, UnitHealth attacker)
    {
        if (!initialized || dead || amount <= 0)
            return;

        currentHealth -= amount;
        if (currentHealth > 0)
            return;

        Die();
    }

    private void ActivateInternal()
    {
        currentEnemyTarget = null;
        currentHealth = Mathf.Max(1, maxHealth);
        dead = false;
        initialized = true;
        targetRefreshTimer = 0f;
        attackTimer = 0f;

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

        if (ShouldRefreshTarget())
        {
            RefreshTarget();
            targetRefreshTimer = targetRefreshInterval;
        }

        if (currentEnemyTarget == null)
        {
            MoveToGuardPoint();
            return;
        }

        Vector2 toTarget = currentEnemyTarget.transform.position - transform.position;
        float distance = toTarget.magnitude;
        float targetColliderPadding = 0f;
        Collider2D targetCollider = currentEnemyTarget.CachedCollider;
        if (targetCollider != null)
            targetColliderPadding = Mathf.Min(targetCollider.bounds.extents.x, targetCollider.bounds.extents.y) * 0.4f;

        float hitDistance = Mathf.Max(0.1f, attackRange + targetColliderPadding);

        if (distance > hitDistance)
        {
            MoveTowards(currentEnemyTarget.transform.position);
            return;
        }

        ApplyMovingAnimation(false);
        UpdateAnimatorDirection(toTarget);
        if (attackTimer > 0f)
            return;

        Vector2 hitDirection = toTarget.sqrMagnitude > 0.001f ? toTarget.normalized : Vector2.down;
        currentEnemyTarget.TakeDamage(attackDamage, DamageType.Normal, hitDirection, attackKnockback);
        if (currentEnemyTarget != null && currentEnemyTarget.TryGetComponent(out UnitMovement movement))
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
        return distFromGuard > chaseLimitFromGuard;
    }

    private void RefreshTarget()
    {
        if (!TryGetGuardPoint(out Vector3 guardWorldPosition))
        {
            currentEnemyTarget = null;
            return;
        }

        EnemyRegistry.TryGetNearestEnemy(guardWorldPosition, engageRange, out currentEnemyTarget);
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
        Vector2 next = current + direction * (moveSpeed * Time.deltaTime);
        transform.position = new Vector3(next.x, next.y, transform.position.z);
        ApplyMovingAnimation(true);
        UpdateAnimatorDirection(direction);
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
        currentEnemyTarget = null;
        ApplyMovingAnimation(false);
        ApplyDeadAnimation(true);

        if (blockerCollider != null)
            blockerCollider.enabled = false;

        Died?.Invoke(this);
        gameObject.SetActive(false);
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
