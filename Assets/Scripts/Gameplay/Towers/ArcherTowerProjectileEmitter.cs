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

    private int currentVisualLevel = 1;
    private bool isAuthoringValid;

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

        archerTower.ShotFired += HandleShotFired;
        archerTower.VisualLevelChanged += HandleVisualLevelChanged;

        currentVisualLevel = archerTower.VisualLevel;
    }

    private void OnDisable()
    {
        if (archerTower == null)
            return;

        archerTower.ShotFired -= HandleShotFired;
        archerTower.VisualLevelChanged -= HandleVisualLevelChanged;
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

    private void HandleShotFired(Vector2 _)
    {
        if (!archerTower.TryGetAimPoint(out Vector2 baseAimPoint))
            return;

        float arrowScale = ResolveArrowScale();

        for (int i = 0; i < firePoints.Length; i++)
        {
            Transform firePoint = firePoints[i];
            if (firePoint == null)
                continue;

            Arrow arrow = arrowPool.Spawn(firePoint.position, Quaternion.identity);
            if (arrow == null)
                continue;

            ConfigureArrow(arrow, arrowScale);

            Vector2 targetPoint = baseAimPoint;
            if (aimPointJitter > 0.001f)
                targetPoint += UnityEngine.Random.insideUnitCircle * aimPointJitter;

            arrow.Launch(targetPoint);
        }
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
}

