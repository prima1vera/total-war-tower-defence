using UnityEngine;

public class ArcherTowerProjectileEmitter : MonoBehaviour
{
    [Header("Scene Wiring")]
    [Tooltip("Owning ArcherTower component (targeting/cadence source).")]
    [SerializeField] private ArcherTower archerTower;
    [Tooltip("Projectile pool key resolved through TowerProjectilePoolRegistry (unified pool pipeline).")]
    [SerializeField] private TowerProjectilePoolKey projectilePoolKey = TowerProjectilePoolKey.Archer;
    [Tooltip("Volley fire points (usually one per visible archer slot).")]
    [SerializeField] private Transform[] firePoints;

    [Header("Projectile")]
    [Tooltip("Direct hit damage per emitted arrow.")]
    [SerializeField, Min(1)] private int damage = 1;
    [Tooltip("Damage type propagated to Arrow (Normal/Fire/Ice).")]
    [SerializeField] private DamageType damageType = DamageType.Normal;
    [Tooltip("Knockback force applied on arrow hit.")]
    [SerializeField, Min(0f)] private float knockbackForce = 0.15f;
    [Tooltip("How many enemies one arrow can pierce before impact.")]
    [SerializeField, Min(1)] private int maxPierce = 1;
    [Tooltip("AoE radius applied by emitted arrows on impact.")]
    [SerializeField, Min(0f)] private float impactRadius = 0.05f;

    [Header("Visual")]
    [Tooltip("Base scale for archer arrows before optional level scaling.")]
    [SerializeField, Min(0.1f)] private float baseArrowScale = 0.48f;
    [Tooltip("If enabled, projectile size increases with tower visual level.")]
    [SerializeField] private bool scaleArrowWithLevel = true;
    [Tooltip("Per-level additional scale step when level scaling is enabled.")]
    [SerializeField, Min(0f)] private float levelScaleStep = 0.1f;

    [Header("Spread")]
    [Tooltip("Random offset around target point to avoid laser-perfect grouping.")]
    [SerializeField, Min(0f)] private float aimPointJitter = 0.12f;

    [Header("Miss Feel")]
    [Tooltip("Chance to intentionally miss for less robotic volleys (applied only at longer distances).")]
    [SerializeField, Range(0f, 1f)] private float intentionalMissChance = 0.2f;
    [Tooltip("Radius of intentional miss offset around the target point.")]
    [SerializeField, Min(0f)] private float intentionalMissRadius = 0.45f;
    [Tooltip("Minimum distance required before intentional miss logic can apply.")]
    [SerializeField, Min(0f)] private float minDistanceForIntentionalMiss = 2f;

    [Header("Cadence")]
    [Tooltip("Retry delay for a fire point when no target is currently available.")]
    [SerializeField, Min(0.02f)] private float idleRetryDelay = 0.08f;
    [Tooltip("Random spread around base interval so archers do not fire in perfect sync.")]
    [SerializeField, Range(0f, 0.75f)] private float cadenceJitter = 0.18f;

    private int currentVisualLevel = 1;
    private bool isAuthoringValid;
    private float[] fireCooldowns;
    private ArrowPool runtimeArrowPool;
    private bool loggedPoolResolveError;

    private void Awake()
    {
        isAuthoringValid = ValidateAuthoring();
        if (!isAuthoringValid)
            enabled = false;
    }

    private void OnEnable()
    {
        if (!isAuthoringValid)
            return;

        runtimeArrowPool = null;
        loggedPoolResolveError = false;

        archerTower.VisualLevelChanged += HandleVisualLevelChanged;
        currentVisualLevel = archerTower.VisualLevel;

        EnsureCooldownBuffer();
        SeedFireCooldowns();
    }

    private void OnDisable()
    {
        if (archerTower != null)
            archerTower.VisualLevelChanged -= HandleVisualLevelChanged;
    }

    private void Update()
    {
        if (!isAuthoringValid || firePoints == null || firePoints.Length == 0)
            return;

        EnsureCooldownBuffer();

        for (int i = 0; i < firePoints.Length; i++)
        {
            fireCooldowns[i] -= Time.deltaTime;
            if (fireCooldowns[i] > 0f)
                continue;

            TryFireFromPoint(i);
        }
    }

    private bool ValidateAuthoring()
    {
        bool valid = true;

        if (archerTower == null)
        {
            Debug.LogError($"{name}: ArcherTowerProjectileEmitter requires ArcherTower reference.", this);
            valid = false;
        }

        if (firePoints == null || firePoints.Length == 0)
        {
            Debug.LogError($"{name}: ArcherTowerProjectileEmitter requires at least one fire point.", this);
            valid = false;
        }
        else
        {
            for (int i = 0; i < firePoints.Length; i++)
            {
                if (firePoints[i] != null)
                    continue;

                Debug.LogError($"{name}: firePoints[{i}] is null. Assign all volley fire points.", this);
                valid = false;
            }
        }

        return valid;
    }

    public void SetProjectileDamage(int newDamage)
    {
        damage = Mathf.Max(1, newDamage);
    }

    public void ApplyEvolutionProfile(TowerEvolutionProfile profile)
    {
        if (profile == null)
            return;

        projectilePoolKey = profile.ProjectilePoolKey;
        runtimeArrowPool = null;
        loggedPoolResolveError = false;
    }

    private void HandleVisualLevelChanged(int level)
    {
        currentVisualLevel = Mathf.Max(1, level);
    }

    private void TryFireFromPoint(int firePointIndex)
    {
        Transform firePoint = firePoints[firePointIndex];
        if (firePoint == null)
        {
            fireCooldowns[firePointIndex] = 0.25f;
            return;
        }

        if (!EnemyRegistry.TryGetNearestEnemy(firePoint.position, archerTower.Range, out UnitHealth target) || target == null || target.IsDead)
        {
            fireCooldowns[firePointIndex] = Mathf.Max(0.02f, idleRetryDelay);
            return;
        }

        Vector2 baseAimPoint = target.transform.position;
        Vector2 targetPoint = ResolveTargetPoint(baseAimPoint, firePoint.position);

        ArrowPool pool = GetValidArrowPool();
        if (pool == null)
        {
            fireCooldowns[firePointIndex] = 0.15f;
            return;
        }

        Arrow arrow = pool.Spawn(firePoint.position, Quaternion.identity);
        if (arrow == null)
        {
            fireCooldowns[firePointIndex] = 0.1f;
            return;
        }

        float arrowScale = ResolveArrowScale();
        ConfigureArrow(arrow, arrowScale);
        arrow.Launch(targetPoint);

        Vector2 shotDirection = targetPoint - (Vector2)firePoint.position;
        archerTower.EmitShotFrom(firePoint.position, shotDirection);

        fireCooldowns[firePointIndex] = ResolveShotIntervalWithJitter();
    }

    private void ConfigureArrow(Arrow arrow, float scale)
    {
        arrow.damage = Mathf.Max(1, damage);
        arrow.damageType = damageType;
        arrow.knockbackForce = Mathf.Max(0f, knockbackForce);
        arrow.maxPierce = Mathf.Max(1, maxPierce);
        arrow.impactRadius = Mathf.Max(0f, impactRadius);
        arrow.SetVisualScale(scale);
    }

    private float ResolveArrowScale()
    {
        float scale = Mathf.Max(0.1f, baseArrowScale);

        if (!scaleArrowWithLevel)
            return scale;

        float levelMultiplier = 1f + Mathf.Max(0f, levelScaleStep) * Mathf.Max(0, currentVisualLevel - 1);
        return scale * levelMultiplier;
    }

    private Vector2 ResolveTargetPoint(Vector2 baseAimPoint, Vector2 firePointPosition)
    {
        Vector2 targetPoint = baseAimPoint;

        if (aimPointJitter > 0.001f)
            targetPoint += Random.insideUnitCircle * aimPointJitter;

        if (intentionalMissChance <= 0.001f || intentionalMissRadius <= 0.001f)
            return targetPoint;

        float distanceToTarget = Vector2.Distance(firePointPosition, baseAimPoint);
        if (distanceToTarget < minDistanceForIntentionalMiss)
            return targetPoint;

        if (Random.value > intentionalMissChance)
            return targetPoint;

        Vector2 missOffsetDir = Random.insideUnitCircle;
        if (missOffsetDir.sqrMagnitude <= 0.0001f)
            missOffsetDir = Vector2.right;
        else
            missOffsetDir.Normalize();

        float missOffsetDistance = Random.Range(intentionalMissRadius * 0.35f, intentionalMissRadius);
        return targetPoint + missOffsetDir * missOffsetDistance;
    }

    private void EnsureCooldownBuffer()
    {
        if (firePoints == null || firePoints.Length == 0)
        {
            fireCooldowns = null;
            return;
        }

        if (fireCooldowns != null && fireCooldowns.Length == firePoints.Length)
            return;

        fireCooldowns = new float[firePoints.Length];
    }

    private void SeedFireCooldowns()
    {
        if (fireCooldowns == null)
            return;

        float baseInterval = ResolveBaseShotInterval();
        for (int i = 0; i < fireCooldowns.Length; i++)
            fireCooldowns[i] = Random.Range(0f, baseInterval);
    }

    private float ResolveBaseShotInterval()
    {
        return 1f / Mathf.Max(0.05f, archerTower != null ? archerTower.ShotsPerSecond : 1f);
    }

    private float ResolveShotIntervalWithJitter()
    {
        float baseInterval = ResolveBaseShotInterval();
        float jitter = Mathf.Clamp(cadenceJitter, 0f, 0.75f);

        if (jitter <= 0.001f)
            return baseInterval;

        return baseInterval * Random.Range(1f - jitter, 1f + jitter);
    }

    private ArrowPool GetValidArrowPool()
    {
        if (runtimeArrowPool == null)
        {
            if (!TowerProjectilePoolRegistry.TryGetPool(projectilePoolKey, out ArrowPool resolvedPool) || resolvedPool == null)
            {
                if (!loggedPoolResolveError)
                {
                    Debug.LogError($"{name}: Projectile pool for key {projectilePoolKey} is not resolved. Add/wire it in TowerProjectilePoolRegistry.", this);
                    loggedPoolResolveError = true;
                }

                return null;
            }

            runtimeArrowPool = resolvedPool;
        }

        if (runtimeArrowPool.gameObject.scene.IsValid())
        {
            loggedPoolResolveError = false;
            return runtimeArrowPool;
        }

        if (!loggedPoolResolveError)
        {
            Debug.LogError($"{name}: Resolved ArrowPool for key {projectilePoolKey} points to prefab asset. Assign scene instance in TowerProjectilePoolRegistry.", this);
            loggedPoolResolveError = true;
        }

        runtimeArrowPool = null;
        return null;
    }
}

