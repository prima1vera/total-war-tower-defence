using System;
using UnityEngine;

public class ArcherTower : MonoBehaviour
{
    [Header("Targeting")]
    [SerializeField, Min(0.1f)] private float range = 8f;
    [SerializeField, Min(0.02f)] private float targetRefreshInterval = 0.15f;

    [Header("Visual Shot Rhythm")]
    [SerializeField, Min(0.05f)] private float shotsPerSecond = 1.25f;
    [SerializeField] private bool autoShoot = true;
    [SerializeField] private Transform shotOrigin;

    [Header("Progress")]
    [SerializeField, Min(1)] private int visualLevel = 1;

    [Header("Authoring")]
    [SerializeField] private bool strictAuthoring = true;

    private UnitHealth currentTarget;
    private float targetRefreshTimer;
    private int lastKnownRegistryVersion = -1;
    private float shotCooldown;
    private bool isAuthoringValid;

    public event Action<Vector2> ShotFired;
    public event Action<int> VisualLevelChanged;

    public int VisualLevel => Mathf.Max(1, visualLevel);
    public float Range => Mathf.Max(0.1f, range);

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
        shotCooldown = 1f / Mathf.Max(0.05f, shotsPerSecond);
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

        ShotFired?.Invoke(direction);
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
