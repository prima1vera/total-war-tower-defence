using UnityEngine;

public class Tower : MonoBehaviour
{
    public GameObject arrowPrefab;
    public int damage = 1;
    public float range = 5f;
    public float fireRate = 1f;
    public Transform firePoint;

    [SerializeField] private float targetRefreshInterval = 0.15f;
    [SerializeField] private SpriteRenderer towerSpriteRenderer;

    [Header("Ground Visual")]
    [SerializeField] private SpriteRenderer towerGroundRenderer;
    [SerializeField] private Sprite baseGroundSprite;
    [SerializeField] private Sprite fireGroundSprite;
    [SerializeField] private Sprite frostGroundSprite;
    [SerializeField] private Sprite ironGroundSprite;
    [SerializeField, Min(1f)] private float groundScaleMultiplier = 1.33f;

    [Header("Tower Scale")]
    [SerializeField, Min(0f)] private float levelScaleStep = 0.2f;

    [Header("Animation Sync")]
    [SerializeField] private float minAnimationCycleSeconds = 0.08f;

    private UnitHealth currentTarget;
    private float fireCountdown;
    private float targetRefreshTimer;
    private int lastKnownEnemyRegistryVersion = -1;
    private Animator animator;
    private float fireClipLengthSeconds = 0.3f;
    private bool loggedInvalidPoolReference;
    private int currentVisualLevel = 1;
    private float currentLevelScaleMultiplier = 1f;
    private Vector3 cachedBaseScale;
    private bool cachedBaseScaleInitialized;
    private TowerProjectilePoolKey currentProjectilePoolKey = TowerProjectilePoolKey.Base;
    private ArrowPool runtimeArrowPool;
    private bool isAuthoringValid;

    public int Damage => Mathf.Max(1, damage);
    public float Range => Mathf.Max(0.1f, range);
    public float FireRate => Mathf.Max(0.05f, fireRate);

    private void Awake()
    {
        EnsureAnimator();
        CacheBaseScale();

        isAuthoringValid = ValidateAuthoring();
        if (!isAuthoringValid)
            enabled = false;
    }

    private void Start()
    {
        if (!isAuthoringValid)
            return;

        ApplyLevelScale(currentVisualLevel);
        ApplyGroundScale();
        ApplyGroundSprite(currentProjectilePoolKey);
        CacheFireClipLength();
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

    public void SetVisualLevel(int level)
    {
        currentVisualLevel = Mathf.Max(1, level);
        ApplyLevelScale(currentVisualLevel);
        ApplyGroundScale();
    }

    public void ApplyEvolutionProfile(TowerEvolutionProfile profile)
    {
        if (profile == null)
            return;

        if (profile.ArrowPrefab != null)
            arrowPrefab = profile.ArrowPrefab;

        currentProjectilePoolKey = profile.ProjectilePoolKey;
        runtimeArrowPool = null;

        if (TowerProjectilePoolRegistry.TryGetPool(currentProjectilePoolKey, out ArrowPool resolvedPool) && resolvedPool != null)
            runtimeArrowPool = resolvedPool;

        ApplyGroundSprite(currentProjectilePoolKey);
        loggedInvalidPoolReference = false;

        if (profile.TowerSprite != null)
        {
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
        if (currentTarget.IsDead)
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

    private bool ValidateAuthoring()
    {
        bool valid = true;

        if (firePoint == null)
        {
            Debug.LogError($"{name}: firePoint is not assigned. Strict authoring requires explicit FirePoint wiring.", this);
            valid = false;
        }

        EnsureSpriteRenderer();
        if (towerSpriteRenderer == null)
        {
            Debug.LogError($"{name}: towerSpriteRenderer is missing. Assign it explicitly (or keep SpriteRenderer on Tower).", this);
            valid = false;
        }

        if (towerGroundRenderer == null)
        {
            Debug.LogError($"{name}: towerGroundRenderer is not assigned. Strict authoring disables runtime transform.Find fallback.", this);
            valid = false;
        }

        if (animator == null)
        {
            Debug.LogError($"{name}: Animator is missing. Tower cannot trigger Shoot animation event.", this);
            valid = false;
        }

        return valid;
    }

    private void ApplyGroundSprite(TowerProjectilePoolKey key)
    {
        if (towerGroundRenderer == null)
            return;

        Sprite targetGround = ResolveGroundSprite(key);
        if (targetGround == null)
            targetGround = baseGroundSprite;

        if (targetGround != null)
            towerGroundRenderer.sprite = targetGround;
    }

    private Sprite ResolveGroundSprite(TowerProjectilePoolKey key)
    {
        switch (key)
        {
            case TowerProjectilePoolKey.Fire:
                return fireGroundSprite;
            case TowerProjectilePoolKey.Frost:
                return frostGroundSprite;
            case TowerProjectilePoolKey.Iron:
                return ironGroundSprite;
            default:
                return baseGroundSprite;
        }
    }

    private void ApplyGroundScale()
    {
        if (towerGroundRenderer == null)
            return;

        float scale = Mathf.Max(1f, groundScaleMultiplier);
        towerGroundRenderer.transform.localScale = new Vector3(scale, scale, 1f);
    }

    private void CacheBaseScale()
    {
        if (cachedBaseScaleInitialized)
            return;

        cachedBaseScale = transform.localScale;
        cachedBaseScaleInitialized = true;
    }

    private void ApplyLevelScale(int level)
    {
        CacheBaseScale();

        float multiplier = 1f + Mathf.Max(0f, levelScaleStep) * Mathf.Max(0, level - 1);
        currentLevelScaleMultiplier = multiplier;
        transform.localScale = cachedBaseScale * multiplier;
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

        ArrowPool activePool = GetValidArrowPool();
        if (activePool == null)
            return;

        Vector3 spawnPosition = firePoint.position;
        Arrow arrow = activePool.Spawn(spawnPosition, Quaternion.identity);
        if (arrow == null)
            return;

        arrow.damage = Damage;
        arrow.SetVisualScale(currentLevelScaleMultiplier);

        Vector2 baseTargetPoint = currentTarget.transform.position;
        float distanceToTarget = Vector2.Distance(spawnPosition, baseTargetPoint);
        float maxOffset = Mathf.Min(0.5f, distanceToTarget * 0.35f);

        Vector2 targetPoint = baseTargetPoint;
        if (maxOffset > 0.01f)
            targetPoint += UnityEngine.Random.insideUnitCircle * maxOffset;

        if (((Vector2)spawnPosition - targetPoint).sqrMagnitude < 0.0025f)
            targetPoint = baseTargetPoint;

        arrow.Launch(targetPoint);
    }

    private ArrowPool GetValidArrowPool()
    {
        if (runtimeArrowPool == null)
        {
            if (!TowerProjectilePoolRegistry.TryGetPool(currentProjectilePoolKey, out ArrowPool resolvedPool) || resolvedPool == null)
            {
                if (!loggedInvalidPoolReference)
                {
                    Debug.LogError($"{name}: Projectile pool for key {currentProjectilePoolKey} is not resolved. Strict authoring forbids instantiate fallback.", this);
                    loggedInvalidPoolReference = true;
                }

                return null;
            }

            runtimeArrowPool = resolvedPool;
        }

        if (runtimeArrowPool.gameObject.scene.IsValid())
        {
            loggedInvalidPoolReference = false;
            return runtimeArrowPool;
        }

        if (!loggedInvalidPoolReference)
        {
            Debug.LogError($"{name}: Resolved ArrowPool points to a prefab asset. Assign a scene instance in TowerProjectilePoolRegistry.", this);
            loggedInvalidPoolReference = true;
        }

        runtimeArrowPool = null;
        return null;
    }
}