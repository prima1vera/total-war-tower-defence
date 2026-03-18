using System;
using UnityEngine;

public class ArcherTower : MonoBehaviour
{
    [Header("Targeting")]
    [Tooltip("Target acquisition radius for this archer tower.")]
    [SerializeField, Min(0.1f)] private float range = 8f;
    [Tooltip("How often tower re-evaluates best target. Lower = more responsive, higher = cheaper CPU.")]
    [SerializeField, Min(0.02f)] private float targetRefreshInterval = 0.15f;

    [Header("Visual Shot Rhythm")]
    [Tooltip("Visual shot cadence shared across emitter fire points (shots per second).")]
    [SerializeField, Min(0.05f)] private float shotsPerSecond = 1.25f;
    [Tooltip("If enabled, tower auto-fires as soon as it has a valid target and cooldown is ready.")]
    [SerializeField] private bool autoShoot = true;
    [Tooltip("Main aiming origin used for direction estimation and visual presenter.")]
    [SerializeField] private Transform shotOrigin;

    [Header("Progress")]
    [Tooltip("Current visual level used by archer presenter/emitter scaling.")]
    [SerializeField, Min(1)] private int visualLevel = 1;

    [Header("Authoring")]
    [Tooltip("If enabled, missing required references become hard errors and component disables itself.")]
    [SerializeField] private bool strictAuthoring = true;

    private UnitHealth currentTarget;
    private float targetRefreshTimer;
    private int lastKnownRegistryVersion = -1;
    private float shotCooldown;
    private bool isAuthoringValid;

    public event Action<Vector2> ShotFired;
    public event Action<Vector2, Vector2> ShotFiredFrom;
    public event Action<int> VisualLevelChanged;

    public int VisualLevel => Mathf.Max(1, visualLevel);
    public float Range => Mathf.Max(0.1f, range);
    public float ShotsPerSecond => Mathf.Max(0.05f, shotsPerSecond);

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

        shotCooldown = 0f;
        targetRefreshTimer = 0f;
        lastKnownRegistryVersion = -1;

        VisualLevelChanged?.Invoke(VisualLevel);
    }

    private void Update()
    {
        if (!isAuthoringValid)
            return;

        targetRefreshTimer -= Time.deltaTime;
        shotCooldown -= Time.deltaTime;

        bool registryChanged = lastKnownRegistryVersion != EnemyRegistry.Version;
        bool needsRetarget = targetRefreshTimer <= 0f || registryChanged || !IsCurrentTargetValid();

        if (needsRetarget)
        {
            RefreshTarget();
            targetRefreshTimer = targetRefreshInterval;
            lastKnownRegistryVersion = EnemyRegistry.Version;
        }

        if (!autoShoot || currentTarget == null || shotCooldown > 0f)
            return;

        EmitShot();
        shotCooldown = 1f / ShotsPerSecond;
    }

    public void SetVisualLevel(int level)
    {
        int next = Mathf.Max(1, level);
        if (visualLevel == next)
            return;

        visualLevel = next;
        VisualLevelChanged?.Invoke(visualLevel);
    }

    public bool TryGetAimDirection(out Vector2 direction)
    {
        if (!TryGetAimPoint(out Vector2 aimPoint))
        {
            direction = Vector2.zero;
            return false;
        }

        Vector2 origin = shotOrigin != null ? (Vector2)shotOrigin.position : (Vector2)transform.position;
        direction = aimPoint - origin;

        if (direction.sqrMagnitude <= 0.0001f)
        {
            direction = Vector2.zero;
            return false;
        }

        direction.Normalize();
        return true;
    }

    public bool TryGetAimPoint(out Vector2 aimPoint)
    {
        if (currentTarget == null || currentTarget.IsDead)
        {
            aimPoint = Vector2.zero;
            return false;
        }

        aimPoint = currentTarget.transform.position;
        return true;
    }

    public void EmitShot()
    {
        if (!TryGetAimDirection(out Vector2 direction))
            return;

        Vector2 origin = shotOrigin != null ? (Vector2)shotOrigin.position : (Vector2)transform.position;
        EmitShotFrom(origin, direction);
    }

    public void EmitShotFrom(Vector2 origin, Vector2 direction)
    {
        if (direction.sqrMagnitude <= 0.0001f)
            return;

        Vector2 normalizedDirection = direction.normalized;
        ShotFired?.Invoke(normalizedDirection);
        ShotFiredFrom?.Invoke(origin, normalizedDirection);
    }

    private bool ValidateAuthoring()
    {
        if (shotOrigin != null)
            return true;

        if (!strictAuthoring)
        {
            shotOrigin = transform;
            return true;
        }

        Debug.LogError($"{name}: ArcherTower.shotOrigin is not assigned. Assign explicit shot origin in prefab/scene.", this);
        return false;
    }

    private bool IsCurrentTargetValid()
    {
        if (currentTarget == null || currentTarget.IsDead)
            return false;

        float rangeSqr = Range * Range;
        float distSqr = (currentTarget.transform.position - transform.position).sqrMagnitude;
        return distSqr <= rangeSqr;
    }

    private void RefreshTarget()
    {
        EnemyRegistry.TryGetNearestEnemy(transform.position, Range, out currentTarget);
    }
}


