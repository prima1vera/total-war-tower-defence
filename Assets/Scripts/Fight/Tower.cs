using UnityEngine;

public class Tower : MonoBehaviour
{
    public GameObject arrowPrefab;
    public float range = 5f;
    public float fireRate = 1f;
    public Transform firePoint;

    private Transform currentTarget;
    private float fireCountdown = 0f;
    private Animator animator;

    void Start()
    {
        animator = GetComponent<Animator>();
    }

    void Update()
    {
        fireCountdown -= Time.deltaTime;

        Transform target = FindTarget();

        if (target == null)
        {
            currentTarget = null;
            return;
        }

        currentTarget = target;

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

        GameObject arrowGO = Instantiate(
            arrowPrefab,
            firePoint.position,
            Quaternion.identity
        );

        Arrow arrow = arrowGO.GetComponent<Arrow>();
        if (arrow != null)
        {
            Vector2 randomOffset = Random.insideUnitCircle * 0.5f;
            Vector2 targetPoint = (Vector2)currentTarget.position + randomOffset;

            arrow.Launch(targetPoint);
        }
    }
}
