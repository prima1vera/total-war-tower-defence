using UnityEngine;

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


    public void ApplyKnockback(Vector2 direction, float force)
    {
        knockbackVelocity = direction * force;
        knockbackTimer = 0.1f;
    }

    public void SetSpeedMultiplier(float value)
    {
        speedMultiplier = value;
    }

    void Start() 
    {
        unitHealth = GetComponent<UnitHealth>();
        animator = GetComponent<Animator>();

        int pathIndex = Random.Range(0, Waypoints.AllPaths.Length);
        path = Waypoints.AllPaths[pathIndex];

        float yOffset = Random.Range(-5f, 5f);
        transform.position += new Vector3(0, yOffset, 0);
    }

    void Update()
    {
        if (path == null || path.Length == 0) return;

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

        //UPDATE PARAMS FOR BLEND TREE
        if (animator != null)
        {
            Vector2 direction = dir.normalized;
            animator.SetFloat("moveX", direction.x);
            animator.SetFloat("moveY", direction.y);
        }

        if (Vector3.Distance(transform.position, target.position) < 0.1f)
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
        Collider2D[] neighbors = Physics2D.OverlapCircleAll(transform.position, separationRadius);

        Vector2 separation = Vector2.zero;

        foreach (Collider2D neighbor in neighbors)
        {
            if (neighbor.gameObject != gameObject && neighbor.CompareTag("Enemy"))
            {
                Vector2 diff = (Vector2)(transform.position - neighbor.transform.position);
                separation += diff.normalized / diff.magnitude;
            }
        }

        return separation * separationForce;
    }
}
