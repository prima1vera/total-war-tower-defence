using UnityEngine;

public class Tower : MonoBehaviour
{
    public GameObject arrowPrefab;
    public int damage = 1;
    public float range = 5f;
    public float fireRate = 1f;
    public Transform firePoint;

    [Header("Targeting")]
    [SerializeField] private float targetRefreshInterval = 0.15f;
    [SerializeField, Min(0f), Tooltip("Minimum time to keep current valid target before switching to another one.")]
    private float minTargetHoldSeconds = 0.18f;
    [SerializeField, Min(0f), Tooltip("New target must be closer by at least this distance to force target switch after hold window.")]
    private float targetSwitchDistanceBias = 0.25f;
    [SerializeField] private SpriteRenderer towerSpriteRenderer;

    [Header("Directional Visual")]
    [SerializeField, Tooltip("Enable directional sprite switching based on target aim.")]
    private bool enableDirectionalVisual = false;
    [SerializeField, Tooltip("Right-facing / down-right base sprite.")]
    private Sprite towerSpriteSouthEast;
    [SerializeField, Tooltip("Right-facing / up-right sprite used when aiming upward.")]
    private Sprite towerSpriteNorthEast;
    [SerializeField, Range(0f, 1f), Tooltip("Y threshold separating up/down visual bands.")]
    private float verticalAimThreshold = 0.2f;

    [Header("Directional Animator")]
    [SerializeField, Tooltip("Enable directional animator controller switching (SE/NE/NW/SW).")]
    private bool enableDirectionalAnimator = true;
    [SerializeField, Tooltip("SE-facing animator controller (fallback/base).")]
    private RuntimeAnimatorController southEastAnimatorController;
    [SerializeField, Tooltip("NE-facing animator controller.")]
    private RuntimeAnimatorController northEastAnimatorController;
    [SerializeField, Tooltip("NW-facing animator controller.")]
    private RuntimeAnimatorController northWestAnimatorController;
    [SerializeField, Tooltip("SW-facing animator controller.")]
    private RuntimeAnimatorController southWestAnimatorController;
    [SerializeField, Range(0f, 1f), Tooltip("Normalized Y threshold with hysteresis for up/down facing split.")]
    private float directionalAnimatorVerticalThreshold = 0.12f;
    [SerializeField, Range(0f, 1f), Tooltip("Normalized X threshold with hysteresis for left/right facing split.")]
    private float directionalAnimatorHorizontalThreshold = 0.08f;

    [Header("Direction Stability")]
    [SerializeField, Tooltip("Lock direction for a short time right after Shoot trigger to avoid rapid jittering turns.")]
    private bool lockDirectionDuringShot = true;
    [SerializeField, Min(0f), Tooltip("Explicit lock duration after shot trigger. If 0, lock uses Fire clip length * Shot Lock Clip Fraction.")]
    private float shotDirectionLockSeconds = 0f;
    [SerializeField, Range(0f, 1.5f), Tooltip("Auto lock fraction of current Fire clip length when explicit lock duration is 0.")]
    private float shotDirectionLockClipFraction = 0.9f;

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
    private bool isFacingUp;
    private bool isFacingLeft;
    private bool hasDirectionalState;
    private RuntimeAnimatorController activeDirectionalController;
    private float targetHoldTimer;
    private float directionLockTimer;

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
        InitializeDirectionalVisual();
        InitializeDirectionalAnimator();
        CacheFireClipLength();
    }

    private void Update()
    {
        float dt = Time.deltaTime;

        targetHoldTimer -= dt;
        directionLockTimer -= dt;

        fireCountdown -= Time.deltaTime;
        targetRefreshTimer -= Time.deltaTime;

        bool currentTargetValid = IsCurrentTargetValid();
        bool registryChanged = lastKnownEnemyRegistryVersion != EnemyRegistry.Version;
        bool needsRetarget = targetRefreshTimer <= 0f || registryChanged || !currentTargetValid;
        bool canRetarget = !IsDirectionLockActive() || !currentTargetValid;

        if (needsRetarget && canRetarget)
        {
            RefreshTarget(currentTargetValid);
            targetRefreshTimer = targetRefreshInterval;
            lastKnownEnemyRegistryVersion = EnemyRegistry.Version;
        }

        if (currentTarget == null)
            return;

        if (!IsDirectionLockActive())
        {
            if (enableDirectionalAnimator)
                UpdateDirectionalAnimatorFromTarget();
            else
                UpdateDirectionalVisualFromTarget();
        }

        SyncAnimationSpeedToFireRate();

        if (fireCountdown <= 0f)
        {
            if (animator != null)
                animator.SetTrigger("Shoot");

            StartDirectionLockAfterShot();
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

        // Do not hard-fail in early Awake order: registry may initialize after towers.
        if (TowerProjectilePoolRegistry.HasInstance &&
            TowerProjectilePoolRegistry.TryGetPool(currentProjectilePoolKey, out ArrowPool resolvedPool) &&
            resolvedPool != null)
            runtimeArrowPool = resolvedPool;

        ApplyGroundSprite(currentProjectilePoolKey);
        loggedInvalidPoolReference = false;

        if (profile.TowerSprite != null)
        {
            towerSpriteSouthEast = profile.TowerSprite;
            towerSpriteNorthEast = profile.TowerSpriteNorthEast != null
                ? profile.TowerSpriteNorthEast
                : towerSpriteSouthEast;

            hasDirectionalState = false;
            ApplyDirectionalVisual(force: true);
        }
        else if (profile.TowerSpriteNorthEast != null)
        {
            towerSpriteNorthEast = profile.TowerSpriteNorthEast;
            hasDirectionalState = false;
            ApplyDirectionalVisual(force: true);
        }

        if (profile.AnimatorController != null)
        {
            southEastAnimatorController = profile.AnimatorController;
            northEastAnimatorController = profile.AnimatorControllerNorthEast != null
                ? profile.AnimatorControllerNorthEast
                : southEastAnimatorController;
            northWestAnimatorController = profile.AnimatorControllerNorthWest != null
                ? profile.AnimatorControllerNorthWest
                : northEastAnimatorController;
            southWestAnimatorController = profile.AnimatorControllerSouthWest != null
                ? profile.AnimatorControllerSouthWest
                : southEastAnimatorController;

            EnsureAnimator();
            if (animator != null && animator.runtimeAnimatorController != profile.AnimatorController)
            {
                animator.runtimeAnimatorController = profile.AnimatorController;
                animator.Rebind();
                animator.Update(0f);
                CacheFireClipLength();
            }

            activeDirectionalController = null;
            hasDirectionalState = false;
            InitializeDirectionalAnimator();
        }

        if (towerSpriteRenderer != null)
            towerSpriteRenderer.flipX = false;
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

    private void RefreshTarget(bool currentTargetWasValid)
    {
        UnitHealth previousTarget = currentTarget;

        if (!EnemyRegistry.TryGetNearestEnemy(transform.position, Range, out UnitHealth nearestTarget) || nearestTarget == null)
        {
            if (!currentTargetWasValid)
                currentTarget = null;

            return;
        }

        if (!currentTargetWasValid || previousTarget == null || previousTarget.IsDead)
        {
            currentTarget = nearestTarget;
            targetHoldTimer = Mathf.Max(minTargetHoldSeconds, 0f);
            return;
        }

        if (nearestTarget == previousTarget)
        {
            currentTarget = previousTarget;
            targetHoldTimer = Mathf.Max(targetHoldTimer, minTargetHoldSeconds * 0.5f);
            return;
        }

        if (targetHoldTimer > 0f)
        {
            currentTarget = previousTarget;
            return;
        }

        float previousDistance = Vector2.Distance(previousTarget.transform.position, transform.position);
        float nearestDistance = Vector2.Distance(nearestTarget.transform.position, transform.position);
        float requiredBias = Mathf.Max(0f, targetSwitchDistanceBias);

        if (nearestDistance + requiredBias < previousDistance)
        {
            currentTarget = nearestTarget;
            targetHoldTimer = Mathf.Max(minTargetHoldSeconds, 0f);
            return;
        }

        currentTarget = previousTarget;
    }

    private void EnsureAnimator()
    {
        if (animator == null)
            animator = GetComponent<Animator>();
    }

    private void InitializeDirectionalAnimator()
    {
        if (!enableDirectionalAnimator)
            return;

        EnsureAnimator();
        if (animator == null)
            return;

        if (southEastAnimatorController == null)
            southEastAnimatorController = animator.runtimeAnimatorController;

        if (northEastAnimatorController == null)
            northEastAnimatorController = southEastAnimatorController;

        if (northWestAnimatorController == null)
            northWestAnimatorController = northEastAnimatorController != null ? northEastAnimatorController : southEastAnimatorController;

        if (southWestAnimatorController == null)
            southWestAnimatorController = southEastAnimatorController;

        RuntimeAnimatorController initial = ResolveDirectionalAnimatorController(isFacingUp, isFacingLeft);
        ApplyDirectionalAnimatorController(initial, force: true);
    }

    private void EnsureSpriteRenderer()
    {
        if (towerSpriteRenderer == null)
            towerSpriteRenderer = GetComponent<SpriteRenderer>();
    }

    private void InitializeDirectionalVisual()
    {
        EnsureSpriteRenderer();
        if (towerSpriteRenderer == null)
            return;

        if (towerSpriteSouthEast == null)
            towerSpriteSouthEast = towerSpriteRenderer.sprite;

        if (towerSpriteNorthEast == null)
            towerSpriteNorthEast = towerSpriteSouthEast;

        ApplyDirectionalVisual(force: true);
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

    private void StartDirectionLockAfterShot()
    {
        if (!lockDirectionDuringShot)
            return;

        float lockDuration = shotDirectionLockSeconds > 0f
            ? shotDirectionLockSeconds
            : fireClipLengthSeconds * Mathf.Max(0f, shotDirectionLockClipFraction);

        if (lockDuration <= 0f)
            return;

        directionLockTimer = Mathf.Max(directionLockTimer, lockDuration);
    }

    private bool IsDirectionLockActive()
    {
        return lockDirectionDuringShot && directionLockTimer > 0f;
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

    private void UpdateDirectionalVisualFromTarget()
    {
        if (!enableDirectionalVisual || towerSpriteRenderer == null || currentTarget == null)
            return;

        Vector2 aim = currentTarget.transform.position - transform.position;
        if (aim.sqrMagnitude <= 0.0001f)
            return;

        Vector2 aimNormalized = aim.normalized;
        bool isFirstDirectionalSample = !hasDirectionalState;

        if (isFirstDirectionalSample)
        {
            isFacingUp = aimNormalized.y >= 0f;
            hasDirectionalState = true;
        }
        else
        {
            float threshold = Mathf.Clamp01(verticalAimThreshold);
            if (aimNormalized.y >= threshold)
                isFacingUp = true;
            else if (aimNormalized.y <= -threshold)
                isFacingUp = false;
        }

        isFacingLeft = isFirstDirectionalSample
            ? aimNormalized.x < 0f
            : ResolveFacingLeft(aimNormalized.x, isFacingLeft, directionalAnimatorHorizontalThreshold);
        ApplyDirectionalVisual(force: false);
    }

    private void ApplyDirectionalVisual(bool force)
    {
        if (towerSpriteRenderer == null)
            return;

        if (!enableDirectionalVisual)
        {
            if (towerSpriteSouthEast != null && (force || towerSpriteRenderer.sprite != towerSpriteSouthEast))
                towerSpriteRenderer.sprite = towerSpriteSouthEast;

            towerSpriteRenderer.flipX = false;
            return;
        }

        Sprite southEast = towerSpriteSouthEast != null ? towerSpriteSouthEast : towerSpriteRenderer.sprite;
        Sprite northEast = towerSpriteNorthEast != null ? towerSpriteNorthEast : southEast;
        Sprite targetSprite = isFacingUp ? northEast : southEast;

        if (targetSprite != null && (force || towerSpriteRenderer.sprite != targetSprite))
            towerSpriteRenderer.sprite = targetSprite;

        towerSpriteRenderer.flipX = false;
    }

    private void UpdateDirectionalAnimatorFromTarget()
    {
        if (!enableDirectionalAnimator || animator == null || currentTarget == null)
            return;

        Vector2 aim = currentTarget.transform.position - transform.position;
        if (aim.sqrMagnitude <= 0.0001f)
            return;

        Vector2 normalized = aim.normalized;
        bool isFirstDirectionalSample = !hasDirectionalState;

        if (isFirstDirectionalSample)
        {
            isFacingUp = normalized.y >= 0f;
            hasDirectionalState = true;
        }
        else
        {
            float threshold = Mathf.Clamp01(directionalAnimatorVerticalThreshold);
            if (normalized.y >= threshold)
                isFacingUp = true;
            else if (normalized.y <= -threshold)
                isFacingUp = false;
        }

        isFacingLeft = isFirstDirectionalSample
            ? normalized.x < 0f
            : ResolveFacingLeft(normalized.x, isFacingLeft, directionalAnimatorHorizontalThreshold);

        RuntimeAnimatorController desired = ResolveDirectionalAnimatorController(isFacingUp, isFacingLeft);
        ApplyDirectionalAnimatorController(desired, force: false);
    }

    private static bool ResolveFacingLeft(float normalizedX, bool currentFacingLeft, float hysteresisThreshold)
    {
        float threshold = Mathf.Clamp01(hysteresisThreshold);

        if (normalizedX <= -threshold)
            return true;

        if (normalizedX >= threshold)
            return false;

        return currentFacingLeft;
    }

    private RuntimeAnimatorController ResolveDirectionalAnimatorController(bool facingUp, bool facingLeft)
    {
        RuntimeAnimatorController candidate;

        if (facingUp)
            candidate = facingLeft ? northWestAnimatorController : northEastAnimatorController;
        else
            candidate = facingLeft ? southWestAnimatorController : southEastAnimatorController;

        if (candidate != null)
            return candidate;

        if (southEastAnimatorController != null)
            return southEastAnimatorController;

        return animator != null ? animator.runtimeAnimatorController : null;
    }

    private void ApplyDirectionalAnimatorController(RuntimeAnimatorController controller, bool force)
    {
        if (animator == null || controller == null)
            return;

        if (!force && activeDirectionalController == controller)
            return;

        if (animator.runtimeAnimatorController == controller)
        {
            activeDirectionalController = controller;
            return;
        }

        animator.runtimeAnimatorController = controller;
        animator.Rebind();
        animator.Update(0f);
        CacheFireClipLength();
        SyncAnimationSpeedToFireRate();

        activeDirectionalController = controller;
    }
}
