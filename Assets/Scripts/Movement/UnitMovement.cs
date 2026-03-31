using UnityEngine;

public class UnitMovement : MonoBehaviour
{
    private const float SeparationRefreshInterval = 0.06f;
    private const float SeparationRefreshJitter = 0.015f;

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

    public Vector2 CurrentPathDirection => currentPathDirection;

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
    }

    void Start()
    {
        unitHealth = GetComponent<UnitHealth>();
        animator = GetComponent<Animator>();

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
        if (path == null || path.Length == 0) return;

        if (unitHealth.CurrentState != UnitState.Moving)
            return;

        if (knockbackTimer > 0)
        {
            cachedTransform.Translate(knockbackVelocity * Time.deltaTime, Space.World);
            knockbackTimer -= Time.deltaTime;
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
            animator.SetFloat("moveX", direction.x);
            animator.SetFloat("moveY", direction.y);
        }

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

        if (!IsBlockerRelevant(currentBlocker, forward))
            currentBlocker = null;

        bool blockerRegistryChanged = lastKnownBlockerRegistryVersion != EnemyPathBlockerRegistry.Version;
        if (currentBlocker == null && (blockerRetargetTimer <= 0f || blockerRegistryChanged))
        {
            EnemyPathBlockerRegistry.TryGetFirstBlockingTarget(
                cachedTransform.position,
                forward,
                blockerScanRange,
                blockerLaneHalfWidth,
                out currentBlocker);

            blockerRetargetTimer = Mathf.Max(0.02f, blockerRetargetInterval);
            lastKnownBlockerRegistryVersion = EnemyPathBlockerRegistry.Version;
        }

        if (currentBlocker == null)
            return false;

        Vector2 toBlocker = currentBlocker.WorldPosition - (Vector2)cachedTransform.position;
        float distanceToCenter = toBlocker.magnitude;
        float contactDistance = Mathf.Max(0.05f, currentBlocker.BlockRadius + blockerAttackRange);

        if (distanceToCenter > contactDistance)
            return false;

        if (animator != null && toBlocker.sqrMagnitude > 0.0001f)
        {
            Vector2 face = toBlocker.normalized;
            animator.SetFloat("moveX", face.x);
            animator.SetFloat("moveY", face.y);
        }

        if (blockerAttackTimer > 0f)
            return true;

        currentBlocker.ReceiveBlockDamage(Mathf.Max(1, blockerAttackDamage), unitHealth);
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

        if (projection < -blockerRadius || projection > blockerScanRange + blockerRadius)
            return false;

        float perpendicular = Mathf.Abs(heading.x * toBlocker.y - heading.y * toBlocker.x);
        float allowedWidth = blockerLaneHalfWidth + blockerRadius;
        return perpendicular <= allowedWidth;
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
