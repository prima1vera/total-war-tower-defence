using UnityEngine;

public class ArcherTowerProjectileEmitter : MonoBehaviour
{
    [Header("Scene Wiring")]
    [SerializeField] private ArcherTower archerTower;
    [SerializeField] private ArrowPool arrowPool;
    [SerializeField] private Transform[] firePoints;

    [Header("Projectile")]
    [SerializeField, Min(1)] private int damage = 1;
    [SerializeField] private DamageType damageType = DamageType.Normal;
    [SerializeField, Min(0f)] private float knockbackForce = 0.15f;
    [SerializeField, Min(1)] private int maxPierce = 1;
    [SerializeField, Min(0f)] private float impactRadius = 0.05f;

    [Header("Visual")]
    [SerializeField, Min(0.1f)] private float baseArrowScale = 0.48f;
    [SerializeField] private bool scaleArrowWithLevel = true;
    [SerializeField, Min(0f)] private float levelScaleStep = 0.1f;

    [Header("Spread")]
    [SerializeField, Min(0f)] private float aimPointJitter = 0.12f;

    [Header("Miss Feel")]
    [SerializeField, Range(0f, 1f)] private float intentionalMissChance = 0.2f;
    [SerializeField, Min(0f)] private float intentionalMissRadius = 0.45f;
    [SerializeField, Min(0f)] private float minDistanceForIntentionalMiss = 2f;

    [Header("Cadence")]
    [SerializeField, Min(0.02f)] private float idleRetryDelay = 0.08f;
    [SerializeField, Range(0f, 0.75f)] private float cadenceJitter = 0.18f;

    private int currentVisualLevel = 1;
    private bool isAuthoringValid;
    private float[] fireCooldowns;

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

        if (arrowPool == null)
        {
            Debug.LogError($"{name}: ArcherTowerProjectileEmitter requires ArrowPool reference.", this);
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

        Arrow arrow = arrowPool.Spawn(firePoint.position, Quaternion.identity);
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
}
