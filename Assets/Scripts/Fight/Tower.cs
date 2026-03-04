using UnityEngine;

public class Tower : MonoBehaviour
{
    public GameObject arrowPrefab;
    public float range = 5f;
    public float fireRate = 1f;
    public Transform firePoint;

    [SerializeField] private float targetRefreshInterval = 0.15f;
    [SerializeField] private ArrowPool arrowPool;
    [Header("Animation Sync")]
    [SerializeField] private float minAnimationCycleSeconds = 0.08f;

    private UnitHealth currentTarget;
    private float fireCountdown = 0f;
    private float targetRefreshTimer = 0f;
    private int lastKnownEnemyRegistryVersion = -1;
    private Animator animator;
    private float fireClipLengthSeconds = 0.3f;

    void Start()
    {
        animator = GetComponent<Animator>();
        CacheFireClipLength();

        if (firePoint == null)
        {
            firePoint = transform;
            Debug.LogWarning($"{name}: firePoint is not assigned, fallback to tower transform.", this);
        }
    }

    void Update()
    {
        fireCountdown -= Time.deltaTime;
        targetRefreshTimer -= Time.deltaTime;

        bool registryChanged = lastKnownEnemyRegistryVersion != EnemyRegistry.Version;
        bool needsRetarget = targetRefreshTimer <= 0f || registryChanged || !IsCurrentTargetValid();

        if (needsRetarget)
        {
            RefreshTarget();
            targetRefreshTimer = targetRefreshInterval;
            lastKnownEnemyRegistryVersion = EnemyRegistry.Version;
        }

        if (currentTarget == null)
            return;

        SyncAnimationSpeedToFireRate();

        if (fireCountdown <= 0f)
        {
            if (animator != null)
            {
                animator.SetTrigger("Shoot");
            }

            fireCountdown = 1f / fireRate;
        }
    }

    private bool IsCurrentTargetValid()
    {
        if (currentTarget == null)
            return false;

        float rangeSqr = range * range;
        if (currentTarget.CurrentState == UnitState.Dead)
            return false;

        float distSqr = (currentTarget.transform.position - transform.position).sqrMagnitude;
        return distSqr <= rangeSqr;
    }

    private void RefreshTarget()
    {
        EnemyRegistry.TryGetNearestEnemy(transform.position, range, out currentTarget);
    }

    private void CacheFireClipLength()
    {
        if (animator == null || animator.runtimeAnimatorController == null)
            return;

        AnimationClip[] clips = animator.runtimeAnimatorController.animationClips;

        if (clips == null || clips.Length == 0)
            return;

        for (int i = 0; i < clips.Length; i++)
        {
            AnimationClip clip = clips[i];

            if (clip == null)
                continue;

            if (!clip.name.Contains("Fire"))
                continue;

            fireClipLengthSeconds = Mathf.Max(minAnimationCycleSeconds, clip.length);
            return;
        }
    }

    private void SyncAnimationSpeedToFireRate()
    {
        if (animator == null || fireRate <= 0f)
            return;

        float cooldownSeconds = Mathf.Max(0.01f, 1f / fireRate);
        float targetAnimationTime = Mathf.Max(minAnimationCycleSeconds, cooldownSeconds);
        animator.speed = fireClipLengthSeconds / targetAnimationTime;
    }

    public void ShootArrow()
    {
        if (currentTarget == null) return;

        Arrow arrow = null;

        Vector3 spawnPosition = firePoint != null ? firePoint.position : transform.position;

        if (arrowPool != null)
        {
            arrow = arrowPool.Spawn(spawnPosition, Quaternion.identity);
        }
        else if (arrowPrefab != null)
        {
            GameObject arrowGO = Instantiate(
                arrowPrefab,
                spawnPosition,
                Quaternion.identity
            );

            arrow = arrowGO.GetComponent<Arrow>();
        }

        if (arrow == null)
            return;

        Vector2 randomOffset = Random.insideUnitCircle * 0.5f;
        Vector2 targetPoint = (Vector2)currentTarget.transform.position + randomOffset;

        arrow.Launch(targetPoint);
    }
}
