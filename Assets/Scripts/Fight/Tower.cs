using UnityEngine;

public class Tower : MonoBehaviour
{
    public GameObject arrowPrefab;
    public float range = 5f;
    public float fireRate = 1f;
    public Transform firePoint;

    [SerializeField] private float targetRefreshInterval = 0.15f;
    [SerializeField] private ArrowPool arrowPool;

    private Transform currentTarget;
    private float fireCountdown = 0f;
    private float targetRefreshTimer = 0f;
    private Animator animator;

    void Start()
    {
        animator = GetComponent<Animator>();
    }

    void Update()
    {
        fireCountdown -= Time.deltaTime;
        targetRefreshTimer -= Time.deltaTime;

        Transform target = FindTarget();

        if (targetRefreshTimer <= 0f)
        {
            currentTarget = FindTarget();
            targetRefreshTimer = targetRefreshInterval;
        }

        if (currentTarget == null)
            return;

        if (fireCountdown <= 0f)
        {
            animator.SetTrigger("Shoot");
            fireCountdown = 1f / fireRate;
        }
    }

    Transform FindTarget()
    {
        var enemies = EnemyRegistry.Enemies;
        Transform nearest = null;
        float shortest = Mathf.Infinity;

        for (int i = 0; i < enemies.Count; i++)
        {
            UnitHealth health = enemies[i];
            if (health == null || health.CurrentState == UnitState.Dead)
                continue;

            float dist = Vector3.Distance(transform.position, health.transform.position);
            if (dist < shortest && dist <= range)
            {
                shortest = dist;
                nearest = health.transform;
            }
        }

        return nearest;
    }

    public void ShootArrow()
    {
        if (currentTarget == null) return;

        Arrow arrow = null;

        if (arrowPool != null)
        {
            arrow = arrowPool.Spawn(firePoint.position, Quaternion.identity);
        }
        else if (arrowPrefab != null)
        {
            GameObject arrowGO = Instantiate(
                arrowPrefab,
                firePoint.position,
                Quaternion.identity
            );

            arrow = arrowGO.GetComponent<Arrow>();
        }

        if (arrow == null)
            return;

        Vector2 randomOffset = Random.insideUnitCircle * 0.5f;
        Vector2 targetPoint = (Vector2)currentTarget.position + randomOffset;

        arrow.Launch(targetPoint);
    }
}
