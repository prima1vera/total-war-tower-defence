using UnityEngine;

public class UnitMovement : MonoBehaviour
{
    private static readonly int MoveXHash = Animator.StringToHash("moveX");
    private static readonly int MoveYHash = Animator.StringToHash("moveY");
    private static readonly int IsMovingHash = Animator.StringToHash("isMoving");
    private static readonly int AttackHash = Animator.StringToHash("attack");

    private const float SeparationRefreshInterval = 0.06f;
    private const float SeparationRefreshJitter = 0.015f;
    private const float BlockerMaintainBehindRange = 0.55f;
    private const float BlockerAggroBehindRange = 0.45f;
    private const float BlockerAggroLanePadding = 0.2f;
    private const float ForcedAggroMaxDistance = 2.4f;
    private const float ForcedAggroBreakDistance = 2.9f;
    private const float EffectiveKnockbackThreshold = 0.08f;
    private const float BlockerQueueExtraDistance = 0.42f;
    private const float BlockerQueueRowSpacing = 0.26f;
    private const float BlockerQueueLateralSpacing = 0.52f;
    private const float BlockerFrontLateralSpacing = 0.24f;
    private const float BlockerQueueJitter = 0.08f;
    private const float BlockerSlotTolerance = 0.08f;
    private const int BlockerQueueColumns = 3;
    private const int BlockerQueueRows = 10;
    private const float MinEffectiveBlockerScanRange = 2.2f;
    private const float MinEffectiveBlockerLaneHalfWidth = 0.9f;
    private const float BlockerRetargetWhenFullDelay = 0.18f;
    private const float WaypointBehindDotThreshold = -0.02f;

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
    private IEnemyBlockerEngagement currentBlockerEngagement;
    private bool hasBlockerAttackReservation;
    private Vector2 currentBlockerApproachDirection = Vector2.zero;
    private float noReservationTimer;
    private float blockerAttackTimer;
    private float blockerRetargetTimer;
    private int lastKnownBlockerRegistryVersion = -1;
    private bool hasForcedBlockerAggro;
    private bool hasIsMovingParam;
    private bool hasAttackBoolParam;
    private bool hasAttackTriggerParam;
    private bool wasHandlingBlockerCombat;
    private Vector2 lastBlockerCombatForward = Vector2.down;

    public Vector2 CurrentPathDirection => currentPathDirection;

    public void NotifyBlockerAggro(IEnemyPathBlocker blocker)
    {
        if (!attackPathBlockers || blocker == null || !blocker.IsBlocking)
            return;

        Vector2 origin = cachedTransform.position;
        Vector2 toBlocker = blocker.WorldPosition - origin;
        float distance = toBlocker.magnitude;
        if (distance > ForcedAggroMaxDistance)
            return;

        Vector2 heading = currentPathDirection.sqrMagnitude > 0.0001f ? currentPathDirection.normalized : Vector2.down;
        float blockerRadius = Mathf.Max(0.05f, blocker.BlockRadius);
        float projection = Vector2.Dot(toBlocker, heading);
        if (projection < -(blockerRadius + BlockerAggroBehindRange))
            return;

        float perpendicular = Mathf.Abs(heading.x * toBlocker.y - heading.y * toBlocker.x);
        float allowedWidth = Mathf.Max(blockerLaneHalfWidth, MinEffectiveBlockerLaneHalfWidth) + blockerRadius + BlockerAggroLanePadding;
        if (perpendicular > allowedWidth)
            return;

        if (currentBlocker != null && currentBlocker != blocker && currentBlocker.IsBlocking)
        {
            if (hasBlockerAttackReservation)
                return;

            Vector2 toCurrent = currentBlocker.WorldPosition - origin;
            if (toCurrent.sqrMagnitude > 0.0001f && toBlocker.sqrMagnitude >= toCurrent.sqrMagnitude * 0.85f)
                return;
        }

        SetCurrentBlocker(blocker);
        hasForcedBlockerAggro = true;
        blockerRetargetTimer = Mathf.Max(0.02f, blockerRetargetInterval);
        lastKnownBlockerRegistryVersion = EnemyPathBlockerRegistry.Version;
    }

    public void ApplyKnockback(Vector2 direction, float force)
    {
        if (force < EffectiveKnockbackThreshold || direction.sqrMagnitude <= 0.0001f)
            return;

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
        currentBlockerEngagement = null;
        hasBlockerAttackReservation = false;
        currentBlockerApproachDirection = Vector2.zero;
        noReservationTimer = 0f;
        blockerAttackTimer = 0f;
        blockerRetargetTimer = Random.Range(0f, blockerRetargetInterval);
        lastKnownBlockerRegistryVersion = -1;
        hasForcedBlockerAggro = false;
        wasHandlingBlockerCombat = false;
        lastBlockerCombatForward = currentPathDirection;
        SetAttackAnimation(false);
        SetMovingAnimation(false);
    }

    void OnDisable()
    {
        SetCurrentBlocker(null);
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

        AdvancePastBehindWaypointsAlongPath();
        if (waypointIndex >= path.Length)
        {
            HandleReachedGoal();
            return;
        }

        Transform target = path[waypointIndex];
        Vector3 dir = target.position - cachedTransform.position;
        currentPathDirection = dir.sqrMagnitude > 0.0001f ? ((Vector2)dir).normalized : currentPathDirection;
        Vector2 forwardForBlocker = currentPathDirection;

        UpdateSeparationCache();

        if (TryHandleBlockerCombat(forwardForBlocker))
        {
            wasHandlingBlockerCombat = true;
            lastBlockerCombatForward = forwardForBlocker;
            return;
        }

        if (wasHandlingBlockerCombat)
        {
            AdvancePastWaypointsBehind(lastBlockerCombatForward);
            wasHandlingBlockerCombat = false;
            if (waypointIndex >= path.Length)
            {
                HandleReachedGoal();
                return;
            }

            target = path[waypointIndex];
            dir = target.position - cachedTransform.position;
            currentPathDirection = dir.sqrMagnitude > 0.0001f ? ((Vector2)dir).normalized : currentPathDirection;
        }

        Vector2 movementDirection = dir.sqrMagnitude > 0.0001f
            ? ((Vector2)dir).normalized
            : currentPathDirection;

        if (cachedSeparation.sqrMagnitude > 0.0001f)
        {
            // Separation should soften crowding, not flip marching direction.
            Vector2 separationOffset = Vector2.ClampMagnitude(cachedSeparation, 1f);
            float separationWeight = Mathf.Clamp01(separationForce) * 0.35f;
            movementDirection = (movementDirection + separationOffset * separationWeight).normalized;
        }

        Vector3 movement = (Vector3)movementDirection * speed * speedMultiplier * Time.deltaTime;

        cachedTransform.Translate(movement, Space.World);

        if (animator != null)
        {
            animator.SetFloat(MoveXHash, movementDirection.x);
            animator.SetFloat(MoveYHash, movementDirection.y);
        }
        SetAttackAnimation(false);
        SetMovingAnimation(true);

        if ((cachedTransform.position - target.position).sqrMagnitude < 0.01f)
        {
            waypointIndex++;
            if (waypointIndex >= path.Length)
            {
                HandleReachedGoal();
                return;
            }
        }
    }

    private void AdvancePastWaypointsBehind(Vector2 forward)
    {
        if (path == null || path.Length == 0 || waypointIndex >= path.Length)
            return;

        Vector2 heading = forward.sqrMagnitude > 0.0001f
            ? forward.normalized
            : currentPathDirection;

        while (waypointIndex < path.Length)
        {
            Transform waypoint = path[waypointIndex];
            if (waypoint == null)
            {
                waypointIndex++;
                continue;
            }

            Vector2 toWaypoint = (Vector2)waypoint.position - (Vector2)cachedTransform.position;
            if (toWaypoint.sqrMagnitude <= 0.01f || Vector2.Dot(toWaypoint, heading) < -0.02f)
            {
                waypointIndex++;
                continue;
            }

            break;
        }
    }

    private void AdvancePastBehindWaypointsAlongPath()
    {
        if (path == null || path.Length == 0 || waypointIndex >= path.Length)
            return;

        while (waypointIndex < path.Length)
        {
            Transform waypoint = path[waypointIndex];
            if (waypoint == null)
            {
                waypointIndex++;
                continue;
            }

            Vector2 toWaypoint = (Vector2)waypoint.position - (Vector2)cachedTransform.position;
            if (toWaypoint.sqrMagnitude <= 0.01f)
            {
                waypointIndex++;
                continue;
            }

            Vector2 segmentForward = ResolveWaypointSegmentForward(waypointIndex);
            if (segmentForward.sqrMagnitude > 0.0001f && Vector2.Dot(toWaypoint, segmentForward) < WaypointBehindDotThreshold)
            {
                waypointIndex++;
                continue;
            }

            break;
        }
    }

    private Vector2 ResolveWaypointSegmentForward(int index)
    {
        if (path == null || path.Length == 0)
            return currentPathDirection;

        if (index >= 0 && index < path.Length - 1)
        {
            Transform current = path[index];
            Transform next = path[index + 1];
            if (current != null && next != null)
            {
                Vector2 forward = (Vector2)(next.position - current.position);
                if (forward.sqrMagnitude > 0.0001f)
                    return forward.normalized;
            }
        }

        if (index > 0 && index < path.Length)
        {
            Transform previous = path[index - 1];
            Transform current = path[index];
            if (previous != null && current != null)
            {
                Vector2 forward = (Vector2)(current.position - previous.position);
                if (forward.sqrMagnitude > 0.0001f)
                    return forward.normalized;
            }
        }

        return currentPathDirection;
    }

    private void HandleReachedGoal()
    {
        EnemyRuntimeEvents.RaiseEnemyReachedGoal(unitHealth);

        if (enemyPoolMember != null && enemyPoolMember.TryDespawnToPool())
            return;

        if (destroyOnGoalReached)
            Destroy(gameObject);
        else
            gameObject.SetActive(false);
    }

    private bool TryHandleBlockerCombat(Vector2 forward)
    {
        if (!attackPathBlockers)
            return false;

        Vector2 heading = forward.sqrMagnitude > 0.0001f ? forward.normalized : currentPathDirection;
        float effectiveScanRange = Mathf.Max(blockerScanRange, MinEffectiveBlockerScanRange);
        float effectiveLaneHalfWidth = Mathf.Max(blockerLaneHalfWidth, MinEffectiveBlockerLaneHalfWidth);

        blockerAttackTimer -= Time.deltaTime;
        blockerRetargetTimer -= Time.deltaTime;

        if (currentBlocker != null)
        {
            if (!currentBlocker.IsBlocking)
            {
                SetCurrentBlocker(null);
                hasForcedBlockerAggro = false;
            }
            else if (hasForcedBlockerAggro)
            {
                Vector2 toForcedBlocker = currentBlocker.WorldPosition - (Vector2)cachedTransform.position;
                float blockerRadius = Mathf.Max(0.05f, currentBlocker.BlockRadius);
                float behindProjection = Vector2.Dot(toForcedBlocker, heading);
                float maxBehindDistance = blockerRadius + BlockerAggroBehindRange;
                float breakDistance = Mathf.Max(ForcedAggroBreakDistance, effectiveScanRange + Mathf.Max(0.05f, currentBlocker.BlockRadius) + 0.8f);
                if (toForcedBlocker.sqrMagnitude > breakDistance * breakDistance || behindProjection < -maxBehindDistance)
                {
                    SetCurrentBlocker(null);
                    hasForcedBlockerAggro = false;
                }
            }
            else if (!IsBlockerRelevant(currentBlocker, heading, effectiveScanRange, effectiveLaneHalfWidth))
            {
                Vector2 toCurrentBlocker = currentBlocker.WorldPosition - (Vector2)cachedTransform.position;
                float blockerRadius = Mathf.Max(0.05f, currentBlocker.BlockRadius);
                float baseDistance = Mathf.Max(0.1f, blockerRadius + blockerAttackRange);
                float maxQueueDepth = baseDistance
                    + BlockerQueueExtraDistance
                    + Mathf.Max(0, BlockerQueueRows - 1) * BlockerQueueRowSpacing
                    + BlockerQueueJitter;

                float keepDistance = Mathf.Max(
                    effectiveScanRange + blockerRadius,
                    maxQueueDepth + 0.35f);

                if (toCurrentBlocker.sqrMagnitude > keepDistance * keepDistance)
                    SetCurrentBlocker(null);
            }
        }

        bool blockerRegistryChanged = lastKnownBlockerRegistryVersion != EnemyPathBlockerRegistry.Version;
        if (currentBlocker == null && (blockerRetargetTimer <= 0f || blockerRegistryChanged))
        {
            float radialFallbackDistance = effectiveScanRange + 1.2f;

            EnemyPathBlockerRegistry.TryGetFirstBlockingTarget(
                cachedTransform.position,
                heading,
                effectiveScanRange,
                effectiveLaneHalfWidth,
                unitHealth,
                ignoreEngagementCapacity: false,
                out IEnemyPathBlocker preferredBlocker);

            if (preferredBlocker == null)
            {
                EnemyPathBlockerRegistry.TryGetNearestBlockingTarget(
                    cachedTransform.position,
                    radialFallbackDistance,
                    unitHealth,
                    ignoreEngagementCapacity: false,
                    out preferredBlocker);

                if (!IsFallbackBlockerUsable(preferredBlocker, heading))
                    preferredBlocker = null;
            }

            SetCurrentBlocker(preferredBlocker);

            if (currentBlocker != null)
                hasForcedBlockerAggro = false;

            blockerRetargetTimer = Mathf.Max(0.02f, blockerRetargetInterval);
            lastKnownBlockerRegistryVersion = EnemyPathBlockerRegistry.Version;
        }

        if (currentBlocker == null)
            return false;

        EnsureBlockerApproachDirection(heading);

        if (!hasBlockerAttackReservation)
            TryAcquireCurrentBlockerReservation();

        if (!hasBlockerAttackReservation)
        {
            noReservationTimer += Time.deltaTime;
            if (!hasForcedBlockerAggro
                && currentBlockerEngagement != null
                && !currentBlockerEngagement.CanAcceptBlockerAttacker(unitHealth)
                && (noReservationTimer >= BlockerRetargetWhenFullDelay || blockerRetargetTimer <= 0f))
            {
                float radialFallbackDistance = effectiveScanRange + 1.2f;
                EnemyPathBlockerRegistry.TryGetNearestBlockingTarget(
                    cachedTransform.position,
                    radialFallbackDistance,
                    unitHealth,
                    ignoreEngagementCapacity: false,
                    out IEnemyPathBlocker alternativeBlocker);

                if (alternativeBlocker == null || alternativeBlocker == currentBlocker)
                {
                    EnemyPathBlockerRegistry.TryGetFirstBlockingTarget(
                        cachedTransform.position,
                        heading,
                        effectiveScanRange,
                        effectiveLaneHalfWidth,
                        unitHealth,
                        ignoreEngagementCapacity: false,
                        out alternativeBlocker);
                }

                if (!IsFallbackBlockerUsable(alternativeBlocker, heading))
                    alternativeBlocker = null;

                if (alternativeBlocker != null && alternativeBlocker != currentBlocker)
                {
                    SetCurrentBlocker(alternativeBlocker);
                    blockerRetargetTimer = Mathf.Max(0.02f, blockerRetargetInterval);
                    lastKnownBlockerRegistryVersion = EnemyPathBlockerRegistry.Version;
                    hasForcedBlockerAggro = false;
                    EnsureBlockerApproachDirection(heading);
                    TryAcquireCurrentBlockerReservation();
                }
            }

            if (!hasBlockerAttackReservation)
            {
                // No free melee slot: keep marching to the next choke instead of idling in rear queue.
                SetCurrentBlocker(null);
                hasForcedBlockerAggro = false;
                blockerRetargetTimer = Mathf.Min(blockerRetargetTimer, 0.02f);
                return false;
            }
        }
        else
        {
            noReservationTimer = 0f;
        }

        Vector2 desiredStandPosition = ResolveBlockerStandPosition(heading, hasBlockerAttackReservation);
        Vector2 toStandPosition = desiredStandPosition - (Vector2)cachedTransform.position;
        float distanceToSlot = toStandPosition.magnitude;
        float slotTolerance = BlockerSlotTolerance;

        if (distanceToSlot > slotTolerance)
        {
            Vector2 pursuitDirection = toStandPosition.sqrMagnitude > 0.0001f ? toStandPosition.normalized : forward;

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

        Vector2 toBlocker = currentBlocker.WorldPosition - (Vector2)cachedTransform.position;
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

    private Vector2 ResolveBlockerStandPosition(Vector2 forward, bool hasReservation)
    {
        Vector2 blockerCenter = currentBlocker.WorldPosition;
        Vector2 approachDirection = currentBlockerApproachDirection;
        if (approachDirection.sqrMagnitude <= 0.0001f)
        {
            if (forward.sqrMagnitude > 0.0001f)
                approachDirection = -forward.normalized;
            else
                approachDirection = Vector2.up;
        }

        if (approachDirection.sqrMagnitude <= 0.0001f)
            approachDirection = Vector2.up;

        Vector2 lateralDirection = new Vector2(-approachDirection.y, approachDirection.x);
        float baseDistance = Mathf.Max(0.1f, currentBlocker.BlockRadius + blockerAttackRange);

        int seed = BuildStableBlockerSeed();
        if (hasReservation)
        {
            int frontSlot = Mathf.Abs(seed % 3) - 1; // -1, 0, 1
            float lateral = frontSlot * BlockerFrontLateralSpacing;
            return blockerCenter + approachDirection * baseDistance + lateralDirection * lateral;
        }

        int queueSlot = Mathf.Abs(seed % (BlockerQueueColumns * BlockerQueueRows));
        int column = queueSlot % BlockerQueueColumns;
        int row = queueSlot / BlockerQueueColumns;

        float middleColumn = (BlockerQueueColumns - 1) * 0.5f;
        float lateralOffset = (column - middleColumn) * BlockerQueueLateralSpacing;
        float jitter = HashSigned01(seed ^ 0x2C1B3C6D) * BlockerQueueJitter;
        float depth = baseDistance + BlockerQueueExtraDistance + row * BlockerQueueRowSpacing + jitter;
        return blockerCenter + approachDirection * depth + lateralDirection * lateralOffset;
    }

    private int BuildStableBlockerSeed()
    {
        int blockerId = ResolveBlockerInstanceId(currentBlocker);
        int unitId = cachedTransform != null ? cachedTransform.GetInstanceID() : GetInstanceID();
        return (unitId * 73856093) ^ (blockerId * 19349663);
    }

    private static int ResolveBlockerInstanceId(IEnemyPathBlocker blocker)
    {
        if (blocker is Component component && component != null)
            return component.GetInstanceID();

        return blocker != null ? blocker.GetHashCode() : 0;
    }

    private static float HashSigned01(int seed)
    {
        uint x = (uint)seed;
        x ^= x >> 16;
        x *= 0x7feb352d;
        x ^= x >> 15;
        x *= 0x846ca68b;
        x ^= x >> 16;
        return (x / (float)uint.MaxValue) * 2f - 1f;
    }

    private bool IsBlockerRelevant(IEnemyPathBlocker blocker, Vector2 forward, float scanRange, float laneHalfWidth)
    {
        if (blocker == null || !blocker.IsBlocking)
            return false;

        Vector2 heading = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector2.down;
        Vector2 toBlocker = blocker.WorldPosition - (Vector2)cachedTransform.position;
        float projection = Vector2.Dot(toBlocker, heading);
        float blockerRadius = Mathf.Max(0.05f, blocker.BlockRadius);

        if (projection < -(blockerRadius + BlockerMaintainBehindRange) || projection > scanRange + blockerRadius)
            return false;

        float perpendicular = Mathf.Abs(heading.x * toBlocker.y - heading.y * toBlocker.x);
        float allowedWidth = laneHalfWidth + blockerRadius;
        return perpendicular <= allowedWidth;
    }

    private bool IsFallbackBlockerUsable(IEnemyPathBlocker blocker, Vector2 heading)
    {
        if (blocker == null || !blocker.IsBlocking)
            return false;

        Vector2 toBlocker = blocker.WorldPosition - (Vector2)cachedTransform.position;
        float projection = Vector2.Dot(toBlocker, heading);
        return projection >= -0.02f;
    }

    private void SetCurrentBlocker(IEnemyPathBlocker blocker)
    {
        if (currentBlocker == blocker)
        {
            if (currentBlocker != null && !hasBlockerAttackReservation)
                TryAcquireCurrentBlockerReservation();

            return;
        }

        if (hasBlockerAttackReservation && currentBlockerEngagement != null && unitHealth != null)
            currentBlockerEngagement.ReleaseBlockerAttacker(unitHealth);

        currentBlocker = blocker;
        currentBlockerEngagement = blocker as IEnemyBlockerEngagement;
        hasBlockerAttackReservation = false;
        currentBlockerApproachDirection = Vector2.zero;
        noReservationTimer = 0f;
        TryAcquireCurrentBlockerReservation();
    }

    private void EnsureBlockerApproachDirection(Vector2 heading)
    {
        if (currentBlocker == null || currentBlockerApproachDirection.sqrMagnitude > 0.0001f)
            return;

        Vector2 preferredDirection = heading.sqrMagnitude > 0.0001f ? -heading.normalized : Vector2.zero;
        Vector2 fromBlockerToUnit = (Vector2)cachedTransform.position - currentBlocker.WorldPosition;
        if (fromBlockerToUnit.sqrMagnitude > 0.0001f)
            fromBlockerToUnit.Normalize();

        if (preferredDirection.sqrMagnitude <= 0.0001f)
        {
            currentBlockerApproachDirection = fromBlockerToUnit.sqrMagnitude > 0.0001f ? fromBlockerToUnit : Vector2.up;
            return;
        }

        float sameSide = fromBlockerToUnit.sqrMagnitude > 0.0001f ? Vector2.Dot(fromBlockerToUnit, preferredDirection) : -1f;
        currentBlockerApproachDirection = sameSide >= 0f ? fromBlockerToUnit : preferredDirection;

        if (currentBlockerApproachDirection.sqrMagnitude <= 0.0001f)
            currentBlockerApproachDirection = preferredDirection;
    }

    private void TryAcquireCurrentBlockerReservation()
    {
        if (currentBlocker == null)
        {
            hasBlockerAttackReservation = false;
            return;
        }

        if (currentBlockerEngagement == null || unitHealth == null)
        {
            hasBlockerAttackReservation = true;
            return;
        }

        hasBlockerAttackReservation = currentBlockerEngagement.TryAcquireBlockerAttacker(unitHealth);
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

    private void UpdateSeparationCache()
    {
        separationTimer -= Time.deltaTime;
        if (separationTimer > 0f)
            return;

        cachedSeparation = CalculateSeparation();
        float jitter = Random.Range(-SeparationRefreshJitter, SeparationRefreshJitter);
        separationTimer = Mathf.Max(0.02f, SeparationRefreshInterval + jitter);
    }
}
