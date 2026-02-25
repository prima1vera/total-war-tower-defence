using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(UnitHealth))]
public class UnitMovement : MonoBehaviour
{
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

    private Collider2D[] separationBuffer;

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
        int bufferSize = Mathf.Max(4, maxSeparationColliders);
        separationBuffer = new Collider2D[bufferSize];
    }

    void OnEnable()
    {
        waypointIndex = 0;
        knockbackVelocity = Vector2.zero;
        knockbackTimer = 0f;
        speedMultiplier = 1f;
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
        transform.position += new Vector3(0, yOffset, 0);
    }

    void Update()
    {
        if (path == null || path.Length == 0 || unitHealth == null) return;

        if (unitHealth.CurrentState != UnitState.Moving)
            return;

        if (knockbackTimer > 0)
        {
            transform.Translate(knockbackVelocity * Time.deltaTime, Space.World);
            knockbackTimer -= Time.deltaTime;
            return;
        }

        Transform target = path[waypointIndex];
        Vector3 dir = target.position - transform.position;

        Vector2 separation = CalculateSeparation();
        Vector3 movement = (dir.normalized + (Vector3)separation) * speed * speedMultiplier * Time.deltaTime;

        transform.Translate(movement, Space.World);

        if (animator != null)
        {
            Vector2 direction = dir.normalized;
            animator.SetFloat("moveX", direction.x);
            animator.SetFloat("moveY", direction.y);
        }

        if ((transform.position - target.position).sqrMagnitude < 0.01f)
        {
            waypointIndex++;
            if (waypointIndex >= path.Length)
            {
                Destroy(gameObject);
            }
        }
    }

    Vector2 CalculateSeparation()
    {
        if (separationBuffer == null || separationBuffer.Length == 0)
            return Vector2.zero;

        int hitCount = Physics2D.OverlapCircleNonAlloc(
            transform.position,
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

            Vector2 diff = (Vector2)(transform.position - neighbor.transform.position);
            float distSqr = diff.sqrMagnitude;
            if (distSqr <= 0.0001f)
                continue;

            separation += diff.normalized / Mathf.Sqrt(distSqr);
        }

        return separation * separationForce;
    }
}
