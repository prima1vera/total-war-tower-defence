using UnityEngine;

public class UnitMovement : MonoBehaviour
{
    private static readonly int MoveXHash = Animator.StringToHash("moveX");
    private static readonly int MoveYHash = Animator.StringToHash("moveY");
    private static readonly int IsMovingHash = Animator.StringToHash("isMoving");
    private static readonly int AttackHash = Animator.StringToHash("attack");

    private const float SeparationRefreshInterval = 0.06f;
    private const float SeparationRefreshJitter = 0.015f;
    private const float BlockerMaintainBehindRange = 0.95f;
    private const float BlockerAggroBehindRange = 1.2f;
    private const float BlockerAggroLanePadding = 0.2f;
    private const float ForcedAggroMaxDistance = 2.4f;
    private const float ForcedAggroBreakDistance = 2.9f;

    public float speed = 2f;

    private int waypointIndex = 0;
    private UnitHealth unitHealth;
    private Transform[] path;
    private Animator animator;
    private Vector2 knockbackVelocity;
    private float knockbackTimer;
    private float speedMultiplier = 1f;

    public float separationRadius = 0.1f;
    public float separationForce = 2f;

    [SerializeField] private LayerMask separationLayerMask = ~0;
    [SerializeField] private int maxSeparationColliders = 16;
    [SerializeField] private bool destroyOnGoalReached = true;

    [Header("Blocker Combat")]
    [SerializeField, Tooltip("If enabled, enemy can stop and attack barricades/defenders that block the lane.")]
    private bool attackPathBlockers = true;
    [SerializeField, Min(1), Tooltip("Damage dealt to blocker structures/defenders per hit.")]
    private int blockerAttackDamage = 1;
    [SerializeField, Min(0.05f), Tooltip("Seconds between enemy melee hits against blockers.")]
    private float blockerAttackInterval = 0.55f;
    [SerializeField, Min(0.2f), Tooltip("How far ahead enemy scans for blockers on its lane.")]
    private float blockerScanRange = 1.1f;
    [SerializeField, Min(0.05f), Tooltip("Half-width of the lane used to decide if blocker is in-path.")]
    private float blockerLaneHalfWidth = 0.38f;
    [SerializeField, Min(0.05f), Tooltip("Extra contact distance before enemy starts attacking blocker.")]
    private float blockerAttackRange = 0.15f;
    [SerializeField, Min(0.02f), Tooltip("Retarget cadence for blocker scan.")]
    private float blockerRetargetInterval = 0.08f;

    private Collider2D[] separationBuffer;
    private Transform cachedTransform;
    private EnemyPoolMember enemyPoolMember;
    private float separationTimer;
    private Vector2 cachedSeparation;
    private Vector2 currentPathDirection = Vector2.down;
    private IEnemyPathBlocker currentBlocker;
    private float blockerAttackTimer;
    private float blockerRetargetTimer;
    private int lastKnownBlockerRegistryVersion = -1;
    private bool hasForcedBlockerAggro;
    private bool hasIsMovingParam;
    private bool hasAttackBoolParam;
    private bool hasAttackTriggerParam;

    public Vector2 CurrentPathDirection => currentPathDirection;

    public void NotifyBlockerAggro(IEnemyPathBlocker blocker)
    {
        if (!attackPathBlockers || blocker == null || !blocker.IsBlocking)
            return;

        Vector2 toBlocker = blocker.WorldPosition - (Vector2)cachedTransform.position;
        float distance = toBlocker.magnitude;
        if (distance > ForcedAggroMaxDistance)
            return;

        currentBlocker = blocker;
        hasForcedBlockerAggro = true;
        blockerRetargetTimer = Mathf.Max(0.02f, blockerRetargetInterval);
        lastKnownBlockerRegistryVersion = EnemyPathBlockerRegistry.Version;
    }

    public void ApplyKnockback(Vector2 direction, float force)
    {
        knockbackVelocity = direction * force;
        knockbackTimer = 0.1f;
    }

    public void SetSpeedMultiplier(float value)
    {
        speedMultiplier = value;
    }

    void Awake()
    {
        cachedTransform = transform;
        enemyPoolMember = GetComponent<EnemyPoolMember>();

        int bufferSize = Mathf.Max(4, maxSeparationColliders);
        separationBuffer = new Collider2D[bufferSize];
    }

    void OnEnable()
    {
        waypointIndex = 0;
        knockbackVelocity = Vector2.zero;
        knockbackTimer = 0f;
        speedMultiplier = 1f;
        cachedSeparation = Vector2.zero;
        separationTimer = Random.Range(0f, SeparationRefreshInterval);
        currentPathDirection = Vector2.down;
        currentBlocker = null;
        blockerAttackTimer = 0f;
        blockerRetargetTimer = Random.Range(0f, blockerRetargetInterval);
        lastKnownBlockerRegistryVersion = -1;
        hasForcedBlockerAggro = false;
        SetAttackAnimation(false);
        SetMovingAnimation(false);
    }

    void Start()
    {
        unitHealth = GetComponent<UnitHealth>();
        animator = GetComponent<Animator>();
        CacheAnimatorBindings();

        if (Waypoints.AllPaths == null || Waypoints.AllPaths.Length == 0)
        {
            Debug.LogWarning($"{name}: Waypoints.AllPaths is not initialized. UnitMovement disabled.", this);
            enabled = false;
            return;
        }

        int pathIndex = Random.Range(0, Waypoints.AllPaths.Length);
        path = Waypoints.AllPaths[pathIndex];

        float yOffset = Random.Range(-5f, 5f);
        cachedTransform.position += new Vector3(0, yOffset, 0);
    }

    void Update()
    {
        if (path == null || path.Length == 0)
        {
            SetAttackAnimation(false);
            SetMovingAnimation(false);
            return;
        }

        if (unitHealth.CurrentState != UnitState.Moving)
        {
            SetAttackAnimation(false);
            SetMovingAnimation(false);
            return;
        }

        if (knockbackTimer > 0)
        {
            cachedTransform.Translate(knockbackVelocity * Time.deltaTime, Space.World);
            knockbackTimer -= Time.deltaTime;
            SetAttackAnimation(false);
            return;
        }

        Transform target = path[waypointIndex];
        Vector3 dir = target.position - cachedTransform.position;
        currentPathDirection = dir.sqrMagnitude > 0.0001f ? ((Vector2)dir).normalized : currentPathDirection;

        if (TryHandleBlockerCombat(currentPathDirection))
            return;

        separationTimer -= Time.deltaTime;
        if (separationTimer <= 0f)
        {
            cachedSeparation = CalculateSeparation();
            float jitter = Random.Range(-SeparationRefreshJitter, SeparationRefreshJitter);
            separationTimer = Mathf.Max(0.02f, SeparationRefreshInterval + jitter);
        }

        Vector2 separation = cachedSeparation;
        Vector3 movement = (dir.normalized + (Vector3)separation) * speed * speedMultiplier * Time.deltaTime;

        cachedTransform.Translate(movement, Space.World);

        if (animator != null)
        {
            Vector2 direction = dir.normalized;
            animator.SetFloat(MoveXHash, direction.x);
            animator.SetFloat(MoveYHash, direction.y);
        }
        SetAttackAnimation(false);
        SetMovingAnimation(true);

        if ((cachedTransform.position - target.position).sqrMagnitude < 0.01f)
        {
            waypointIndex++;
            if (waypointIndex >= path.Length)
            {
                EnemyRuntimeEvents.RaiseEnemyReachedGoal(unitHealth);

                if (enemyPoolMember != null && enemyPoolMember.TryDespawnToPool())
                    return;

                if (destroyOnGoalReached)
                    Destroy(gameObject);
                else
                    gameObject.SetActive(false);
            }
        }
    }

    private bool TryHandleBlockerCombat(Vector2 forward)
    {
        if (!attackPathBlockers)
            return false;

        blockerAttackTimer -= Time.deltaTime;
        blockerRetargetTimer -= Time.deltaTime;

        if (currentBlocker != null)
        {
            if (!currentBlocker.IsBlocking)
            {
                currentBlocker = null;
                hasForcedBlockerAggro = false;
            }
            else if (hasForcedBlockerAggro)
            {
                Vector2 toForcedBlocker = currentBlocker.WorldPosition - (Vector2)cachedTransform.position;
                float breakDistance = Mathf.Max(ForcedAggroBreakDistance, blockerScanRange + Mathf.Max(0.05f, currentBlocker.BlockRadius) + 0.8f);
                if (toForcedBlocker.sqrMagnitude > breakDistance * breakDistance)
                {
                    currentBlocker = null;
                    hasForcedBlockerAggro = false;
                }
            }
            else if (!IsBlockerRelevant(currentBlocker, forward))
            {
                currentBlocker = null;
            }
        }

        bool blockerRegistryChanged = lastKnownBlockerRegistryVersion != EnemyPathBlockerRegistry.Version;
        if (currentBlocker == null && (blockerRetargetTimer <= 0f || blockerRegistryChanged))
        {
            EnemyPathBlockerRegistry.TryGetFirstBlockingTarget(
                cachedTransform.position,
                forward,
                blockerScanRange,
                blockerLaneHalfWidth,
                out currentBlocker);

            if (currentBlocker != null)
                hasForcedBlockerAggro = false;

            blockerRetargetTimer = Mathf.Max(0.02f, blockerRetargetInterval);
            lastKnownBlockerRegistryVersion = EnemyPathBlockerRegistry.Version;
        }

        if (currentBlocker == null)
            return false;

        Vector2 toBlocker = currentBlocker.WorldPosition - (Vector2)cachedTransform.position;
        float distanceToCenter = toBlocker.magnitude;
        float contactDistance = Mathf.Max(0.05f, currentBlocker.BlockRadius + blockerAttackRange);

        if (distanceToCenter > contactDistance)
        {
            if (hasForcedBlockerAggro)
            {
                Vector2 pursuitDirection = toBlocker.sqrMagnitude > 0.0001f ? toBlocker.normalized : forward;
                Vector3 movement = (Vector3)pursuitDirection * speed * speedMultiplier * Time.deltaTime;
                cachedTransform.Translate(movement, Space.World);

                if (animator != null)
                {
                    animator.SetFloat(MoveXHash, pursuitDirection.x);
                    animator.SetFloat(MoveYHash, pursuitDirection.y);
                }

                SetMovingAnimation(true);
                SetAttackAnimation(false);
                return true;
            }

            SetAttackAnimation(false);
            return false;
        }

        if (animator != null && toBlocker.sqrMagnitude > 0.0001f)
        {
            Vector2 face = toBlocker.normalized;
            animator.SetFloat(MoveXHash, face.x);
            animator.SetFloat(MoveYHash, face.y);
        }

        SetMovingAnimation(false);
        SetAttackAnimation(true);

        if (blockerAttackTimer > 0f)
            return true;

        currentBlocker.ReceiveBlockDamage(Mathf.Max(1, blockerAttackDamage), unitHealth);
        TriggerAttackAnimation();
        blockerAttackTimer = Mathf.Max(0.05f, blockerAttackInterval);
        return true;
    }

    private bool IsBlockerRelevant(IEnemyPathBlocker blocker, Vector2 forward)
    {
        if (blocker == null || !blocker.IsBlocking)
            return false;

        Vector2 heading = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector2.down;
        Vector2 toBlocker = blocker.WorldPosition - (Vector2)cachedTransform.position;
        float projection = Vector2.Dot(toBlocker, heading);
        float blockerRadius = Mathf.Max(0.05f, blocker.BlockRadius);

        if (projection < -(blockerRadius + BlockerMaintainBehindRange) || projection > blockerScanRange + blockerRadius)
            return false;

        float perpendicular = Mathf.Abs(heading.x * toBlocker.y - heading.y * toBlocker.x);
        float allowedWidth = blockerLaneHalfWidth + blockerRadius;
        return perpendicular <= allowedWidth;
    }

    private void CacheAnimatorBindings()
    {
        hasIsMovingParam = false;
        hasAttackBoolParam = false;
        hasAttackTriggerParam = false;

        if (animator == null)
            return;

        AnimatorControllerParameter[] parameters = animator.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            AnimatorControllerParameter parameter = parameters[i];
            if (parameter.nameHash == IsMovingHash && parameter.type == AnimatorControllerParameterType.Bool)
                hasIsMovingParam = true;

            if (parameter.nameHash == AttackHash && parameter.type == AnimatorControllerParameterType.Bool)
                hasAttackBoolParam = true;

            if (parameter.nameHash == AttackHash && parameter.type == AnimatorControllerParameterType.Trigger)
                hasAttackTriggerParam = true;
        }
    }

    private void SetMovingAnimation(bool isMoving)
    {
        if (animator == null || !hasIsMovingParam)
            return;

        animator.SetBool(IsMovingHash, isMoving);
    }

    private void SetAttackAnimation(bool isAttacking)
    {
        if (animator == null || !hasAttackBoolParam)
            return;

        animator.SetBool(AttackHash, isAttacking);
    }

    private void TriggerAttackAnimation()
    {
        if (animator == null || !hasAttackTriggerParam)
            return;

        animator.SetTrigger(AttackHash);
    }

    Vector2 CalculateSeparation()
    {
        if (separationBuffer == null || separationBuffer.Length == 0)
            return Vector2.zero;

        int hitCount = Physics2D.OverlapCircleNonAlloc(
            cachedTransform.position,
            separationRadius,
            separationBuffer,
            separationLayerMask
        );

        Vector2 separation = Vector2.zero;

        for (int i = 0; i < hitCount; i++)
        {
            Collider2D neighbor = separationBuffer[i];
            if (neighbor == null)
                continue;

            if (neighbor.gameObject == gameObject || !neighbor.CompareTag("Enemy"))
                continue;

            Vector2 diff = (Vector2)(cachedTransform.position - neighbor.transform.position);
            float distSqr = diff.sqrMagnitude;
            if (distSqr <= 0.0001f)
                continue;

            separation += diff.normalized / Mathf.Sqrt(distSqr);
        }

        return separation * separationForce;
    }
}
