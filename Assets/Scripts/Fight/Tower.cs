using UnityEngine;

public class Tower : MonoBehaviour
{
    public GameObject arrowPrefab;
    public int damage = 1;
    public float range = 5f;
    public float fireRate = 1f;
    public Transform firePoint;

    [SerializeField] private float targetRefreshInterval = 0.15f;
    [SerializeField] private ArrowPool arrowPool;
    [SerializeField] private SpriteRenderer towerSpriteRenderer;

    [Header("Animation Sync")]
    [SerializeField] private float minAnimationCycleSeconds = 0.08f;

    private UnitHealth currentTarget;
    private float fireCountdown;
    private float targetRefreshTimer;
    private int lastKnownEnemyRegistryVersion = -1;
    private Animator animator;
    private float fireClipLengthSeconds = 0.3f;
    private bool loggedInvalidPoolReference;

    public int Damage => Mathf.Max(1, damage);
    public float Range => Mathf.Max(0.1f, range);
    public float FireRate => Mathf.Max(0.05f, fireRate);

    private void Start()
    {
        EnsureAnimator();
        EnsureSpriteRenderer();
        CacheFireClipLength();

        if (firePoint == null)
        {
            firePoint = transform;
            Debug.LogWarning($"{name}: firePoint is not assigned, fallback to tower transform.", this);
        }
    }

    private void Update()
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
                animator.SetTrigger("Shoot");

            fireCountdown = 1f / FireRate;
        }
    }

    public void SetCombatStats(int newDamage, float newRange, float newFireRate)
    {
        damage = Mathf.Max(1, newDamage);
        range = Mathf.Max(0.1f, newRange);
        fireRate = Mathf.Max(0.05f, newFireRate);

        float cooldown = 1f / FireRate;
        fireCountdown = Mathf.Min(fireCountdown, cooldown);
    }

    public void ApplyEvolutionProfile(TowerEvolutionProfile profile)
    {
        if (profile == null)
            return;

        if (profile.ArrowPrefab != null)
            arrowPrefab = profile.ArrowPrefab;

        if (TowerProjectilePoolRegistry.TryGetPool(profile.ProjectilePoolKey, out ArrowPool resolvedPool) && resolvedPool != null)
            arrowPool = resolvedPool;

        loggedInvalidPoolReference = false;

        if (profile.TowerSprite != null)
        {
            EnsureSpriteRenderer();
            if (towerSpriteRenderer != null)
                towerSpriteRenderer.sprite = profile.TowerSprite;
        }

        if (profile.AnimatorController != null)
        {
            EnsureAnimator();
            if (animator != null && animator.runtimeAnimatorController != profile.AnimatorController)
            {
                animator.runtimeAnimatorController = profile.AnimatorController;
                animator.Rebind();
                animator.Update(0f);
                CacheFireClipLength();
            }
        }
    }

    private bool IsCurrentTargetValid()
    {
        if (currentTarget == null)
            return false;

        float rangeSqr = Range * Range;
        if (currentTarget.CurrentState == UnitState.Dead)
            return false;

        float distSqr = (currentTarget.transform.position - transform.position).sqrMagnitude;
        return distSqr <= rangeSqr;
    }

    private void RefreshTarget()
    {
        EnemyRegistry.TryGetNearestEnemy(transform.position, Range, out currentTarget);
    }

    private void EnsureAnimator()
    {
        if (animator == null)
            animator = GetComponent<Animator>();
    }

    private void EnsureSpriteRenderer()
    {
        if (towerSpriteRenderer == null)
            towerSpriteRenderer = GetComponent<SpriteRenderer>();
    }

    private void CacheFireClipLength()
    {
        fireClipLengthSeconds = minAnimationCycleSeconds;

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
        if (animator == null || FireRate <= 0f)
            return;

        float cooldownSeconds = Mathf.Max(0.01f, 1f / FireRate);
        float targetAnimationTime = Mathf.Max(minAnimationCycleSeconds, cooldownSeconds);
        animator.speed = fireClipLengthSeconds / targetAnimationTime;
    }

    public void ShootArrow()
    {
        if (currentTarget == null)
            return;

        Arrow arrow = null;
        Vector3 spawnPosition = firePoint != null ? firePoint.position : transform.position;
        ArrowPool activePool = GetValidArrowPool();

        if (activePool != null)
        {
            arrow = activePool.Spawn(spawnPosition, Quaternion.identity);
        }
        else if (arrowPrefab != null)
        {
            GameObject arrowGO = Instantiate(arrowPrefab, spawnPosition, Quaternion.identity);
            arrow = arrowGO.GetComponent<Arrow>();
        }

        if (arrow == null)
            return;

        arrow.damage = Damage;

        Vector2 randomOffset = Random.insideUnitCircle * 0.5f;
        Vector2 targetPoint = (Vector2)currentTarget.transform.position + randomOffset;

        arrow.Launch(targetPoint);
    }

    private ArrowPool GetValidArrowPool()
    {
        if (arrowPool == null)
            return null;

        if (arrowPool.gameObject.scene.IsValid())
            return arrowPool;

        if (!loggedInvalidPoolReference)
        {
            Debug.LogWarning($"{name}: ArrowPool reference points to a prefab asset. Falling back to instantiate until a scene pool is resolved.", this);
            loggedInvalidPoolReference = true;
        }

        arrowPool = null;
        return null;
    }
}
