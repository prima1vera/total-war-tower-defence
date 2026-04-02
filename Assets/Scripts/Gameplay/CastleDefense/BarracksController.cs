using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class BarracksController : MonoBehaviour
{
    private const float RoadFormationSpacing = 0.42f;
    private const float RoadFormationForwardOffset = -0.18f;
    private const int RoadFormationWaypointBiasTowardsCastle = 2;

    [Header("Defender Prefab")]
    [SerializeField] private DefenderUnit defenderPrefab;
    [SerializeField] private Transform defendersRoot;

    [Header("Slots")]
    [SerializeField, Tooltip("Spawn point per defender slot. If empty, barracks transform is used.")]
    private Transform[] spawnPoints;
    [SerializeField, Tooltip("Optional explicit guard points. Ignored when 'Prefer Road Blocking Formation' is enabled.")]
    private Transform[] defensePoints;
    [SerializeField, Tooltip("When enabled, defenders always form a line directly on the nearest road/path segment.")]
    private bool preferRoadBlockingFormation = true;
    [SerializeField, Min(0.5f), Tooltip("Max distance from barracks where rally point can be placed.")]
    private float rallyMaxDistanceFromBarracks = 3.8f;
    [SerializeField, Tooltip("Optional visual marker for rally point.")]
    private Transform rallyPointVisual;

    [Header("Respawn")]
    [SerializeField, Min(0.1f)] private float respawnCooldown = 4f;
    [SerializeField] private bool spawnOnEnable = true;
    [SerializeField, Min(1), Tooltip("How many defender slots are active simultaneously.")]
    private int defendersPerBarracks = 4;
    [SerializeField, Tooltip("If enabled, barracks uses upgrade level as squad size (clamped by Max Defenders From Upgrade).")]
    private bool useUpgradeLevelAsSquadSize;
    [SerializeField, Min(1)] private int maxDefendersFromUpgrade = 6;

    private readonly Queue<DefenderUnit> pooledDefenders = new Queue<DefenderUnit>(8);
    private readonly List<Coroutine> respawnCoroutines = new List<Coroutine>(8);
    private readonly List<DefenderUnit> activeDefenders = new List<DefenderUnit>(8);
    private bool isAuthoringValid;
    private int runtimeSquadSize;
    private bool roadFormationCached;
    private Vector3 roadFormationAnchor;
    private Vector2 roadFormationNormal;
    private bool hasManualRallyPoint;
    private Vector3 manualRallyPoint;
    private Vector2 manualRallyNormal;

    private void Awake()
    {
        isAuthoringValid = ValidateAuthoring();
        runtimeSquadSize = Mathf.Max(1, defendersPerBarracks);
        if (!isAuthoringValid)
            enabled = false;
    }

    private void OnEnable()
    {
        if (!isAuthoringValid)
            return;

        roadFormationCached = hasManualRallyPoint;
        if (hasManualRallyPoint)
        {
            roadFormationAnchor = manualRallyPoint;
            roadFormationNormal = manualRallyNormal;
        }

        UpdateRallyPointVisual();
        EnsureRuntimeBuffers(ResolveActiveSlotCount());
        if (spawnOnEnable)
            SpawnAllMissingDefenders();
    }

    private void OnDisable()
    {
        StopAllRespawns();
        ReturnAllActiveDefendersToPool();
        if (rallyPointVisual != null)
            rallyPointVisual.gameObject.SetActive(false);
    }

    public void SpawnAllMissingDefenders()
    {
        if (!isAuthoringValid)
            return;

        int activeSlotCount = ResolveActiveSlotCount();
        EnsureRuntimeBuffers(activeSlotCount);

        for (int i = 0; i < activeDefenders.Count; i++)
        {
            if (i >= activeSlotCount)
            {
                ClearSlotAndCancelRespawn(i);
                continue;
            }

            if (activeDefenders[i] != null && activeDefenders[i].IsAlive)
                continue;

            SpawnDefenderForSlot(i);
        }
    }

    public void ApplyUpgradeLevel(int level)
    {
        if (useUpgradeLevelAsSquadSize)
            runtimeSquadSize = Mathf.Clamp(level, 1, Mathf.Max(1, maxDefendersFromUpgrade));
        else
            runtimeSquadSize = Mathf.Max(1, defendersPerBarracks);

        roadFormationCached = false;
        if (isActiveAndEnabled)
            SpawnAllMissingDefenders();
    }

    public bool TrySetRallyPoint(Vector3 worldPosition)
    {
        if (!isAuthoringValid || !preferRoadBlockingFormation)
            return false;

        Vector3 clamped = ClampRallyPoint(worldPosition);
        Vector2 fallbackNormal = roadFormationNormal.sqrMagnitude > 0.0001f ? roadFormationNormal : Vector2.right;
        Vector2 normal = ResolveRoadNormalForPosition(clamped, fallbackNormal);

        hasManualRallyPoint = true;
        manualRallyPoint = clamped;
        manualRallyNormal = normal;

        roadFormationCached = true;
        roadFormationAnchor = clamped;
        roadFormationNormal = normal;

        UpdateRallyPointVisual();
        ReassignDefenderGuardPoints(resetTargets: true);
        return true;
    }

    private bool ValidateAuthoring()
    {
        if (defenderPrefab == null)
        {
            Debug.LogError($"{name}: BarracksController requires defenderPrefab.", this);
            return false;
        }

        if (!preferRoadBlockingFormation && (defensePoints == null || defensePoints.Length == 0))
        {
            Debug.LogError($"{name}: BarracksController requires at least one defense point when road formation is disabled.", this);
            return false;
        }

        if (!preferRoadBlockingFormation)
        {
            for (int i = 0; i < defensePoints.Length; i++)
            {
                if (defensePoints[i] != null)
                    continue;

                Debug.LogError($"{name}: defensePoints[{i}] is null.", this);
                return false;
            }
        }

        return true;
    }

    private void EnsureRuntimeBuffers(int requiredSlots)
    {
        while (activeDefenders.Count < requiredSlots)
            activeDefenders.Add(null);

        while (respawnCoroutines.Count < requiredSlots)
            respawnCoroutines.Add(null);
    }

    private int ResolveActiveSlotCount()
    {
        int target = Mathf.Max(1, runtimeSquadSize);

        if (preferRoadBlockingFormation)
            return target;

        int availablePoints = defensePoints != null ? defensePoints.Length : 0;
        if (availablePoints <= 0)
            return 0;

        return Mathf.Clamp(target, 0, availablePoints);
    }

    private void SpawnDefenderForSlot(int slotIndex)
    {
        DefenderUnit defender = GetOrCreateDefender();
        if (defender == null)
            return;

        Transform spawn = ResolveSpawnPoint(slotIndex);
        if (spawn != null)
            defender.transform.position = spawn.position;
        else
            defender.transform.position = transform.position;

        defender.transform.rotation = Quaternion.identity;
        defender.Died -= HandleDefenderDied;
        defender.Died += HandleDefenderDied;
        defender.ActivateAt(ResolveDefensePoint(slotIndex, ResolveActiveSlotCount()));

        activeDefenders[slotIndex] = defender;
    }

    private DefenderUnit GetOrCreateDefender()
    {
        while (pooledDefenders.Count > 0)
        {
            DefenderUnit pooled = pooledDefenders.Dequeue();
            if (pooled == null)
                continue;

            return pooled;
        }

        Transform parent = defendersRoot != null ? defendersRoot : transform;
        return Instantiate(defenderPrefab, transform.position, Quaternion.identity, parent);
    }

    private Transform ResolveSpawnPoint(int slotIndex)
    {
        if (spawnPoints == null || spawnPoints.Length == 0)
            return null;

        int clamped = Mathf.Clamp(slotIndex, 0, spawnPoints.Length - 1);
        Transform spawn = spawnPoints[clamped];
        return spawn != null ? spawn : null;
    }

    private Vector3 ResolveDefensePoint(int slotIndex, int totalSlots)
    {
        if (!preferRoadBlockingFormation && defensePoints != null && defensePoints.Length > 0)
        {
            int clamped = Mathf.Clamp(slotIndex, 0, defensePoints.Length - 1);
            Transform explicitPoint = defensePoints[clamped];
            if (explicitPoint != null)
                return explicitPoint.position;
        }

        if (hasManualRallyPoint)
        {
            roadFormationCached = true;
            roadFormationAnchor = manualRallyPoint;
            roadFormationNormal = manualRallyNormal;
        }

        if (!roadFormationCached)
            roadFormationCached = TryComputeRoadFormationAnchor(out roadFormationAnchor, out roadFormationNormal);

        if (!roadFormationCached)
            return transform.position;

        int slotCount = Mathf.Max(1, totalSlots);
        float center = (slotCount - 1) * 0.5f;
        float sideOffset = (slotIndex - center) * RoadFormationSpacing;
        Vector3 lateral = (Vector3)(roadFormationNormal * sideOffset);
        Vector3 forward = new Vector3(0f, RoadFormationForwardOffset, 0f);
        return roadFormationAnchor + lateral + forward;
    }

    private bool TryComputeRoadFormationAnchor(out Vector3 anchor, out Vector2 normal)
    {
        anchor = transform.position;
        normal = Vector2.right;

        Transform[][] allPaths = Waypoints.AllPaths;
        if (allPaths == null || allPaths.Length == 0)
            return false;

        float bestSqrDistance = float.PositiveInfinity;
        Vector2 bestTangent = Vector2.down;
        Vector2 source = transform.position;
        Transform[] bestPath = null;
        int bestWaypointIndex = -1;

        for (int pathIndex = 0; pathIndex < allPaths.Length; pathIndex++)
        {
            Transform[] path = allPaths[pathIndex];
            if (path == null || path.Length == 0)
                continue;

            for (int waypointIndex = 0; waypointIndex < path.Length; waypointIndex++)
            {
                Transform waypoint = path[waypointIndex];
                if (waypoint == null)
                    continue;

                Vector2 waypointPosition = waypoint.position;
                float sqrDistance = (waypointPosition - source).sqrMagnitude;
                if (sqrDistance >= bestSqrDistance)
                    continue;

                bestSqrDistance = sqrDistance;
                bestPath = path;
                bestWaypointIndex = waypointIndex;
            }
        }

        if (float.IsPositiveInfinity(bestSqrDistance) || bestPath == null || bestWaypointIndex < 0)
            return false;

        // Bias defenders a few waypoints toward the castle so they don't over-commit near the spawn edge.
        int biasedIndex = Mathf.Clamp(
            bestWaypointIndex + RoadFormationWaypointBiasTowardsCastle,
            0,
            bestPath.Length - 1);

        Transform biasedWaypoint = bestPath[biasedIndex];
        if (biasedWaypoint == null)
            return false;

        anchor = biasedWaypoint.position;
        bestTangent = ResolvePathTangent(bestPath, biasedIndex);

        if (bestTangent.sqrMagnitude < 0.0001f)
            bestTangent = Vector2.down;

        bestTangent.Normalize();
        normal = new Vector2(-bestTangent.y, bestTangent.x);
        return true;
    }

    private static Vector2 ResolvePathTangent(Transform[] path, int waypointIndex)
    {
        Transform current = path[waypointIndex];
        if (current == null)
            return Vector2.down;

        Transform previous = waypointIndex > 0 ? path[waypointIndex - 1] : null;
        Transform next = waypointIndex < path.Length - 1 ? path[waypointIndex + 1] : null;

        Vector2 tangent;
        if (previous != null && next != null)
            tangent = (Vector2)(next.position - previous.position);
        else if (next != null)
            tangent = (Vector2)(next.position - current.position);
        else if (previous != null)
            tangent = (Vector2)(current.position - previous.position);
        else
            tangent = Vector2.down;

        return tangent.sqrMagnitude > 0.0001f ? tangent.normalized : Vector2.down;
    }

    private void HandleDefenderDied(DefenderUnit defender)
    {
        int slotIndex = FindSlotOfDefender(defender);
        if (slotIndex < 0)
        {
            if (defender != null)
                ReturnDefenderToPool(defender);
            return;
        }

        activeDefenders[slotIndex] = null;
        ReturnDefenderToPool(defender);
        ScheduleRespawn(slotIndex);
    }

    private int FindSlotOfDefender(DefenderUnit defender)
    {
        if (defender == null)
            return -1;

        for (int i = 0; i < activeDefenders.Count; i++)
        {
            if (activeDefenders[i] == defender)
                return i;
        }

        return -1;
    }

    private void ScheduleRespawn(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= respawnCoroutines.Count)
            return;

        if (slotIndex >= ResolveActiveSlotCount())
            return;

        if (respawnCoroutines[slotIndex] != null)
            StopCoroutine(respawnCoroutines[slotIndex]);

        respawnCoroutines[slotIndex] = StartCoroutine(RespawnAfterCooldown(slotIndex));
    }

    private IEnumerator RespawnAfterCooldown(int slotIndex)
    {
        float cooldown = Mathf.Max(0.1f, respawnCooldown);
        yield return new WaitForSeconds(cooldown);

        respawnCoroutines[slotIndex] = null;
        if (!isActiveAndEnabled || !isAuthoringValid)
            yield break;

        SpawnDefenderForSlot(slotIndex);
    }

    private void StopAllRespawns()
    {
        for (int i = 0; i < respawnCoroutines.Count; i++)
        {
            if (respawnCoroutines[i] == null)
                continue;

            StopCoroutine(respawnCoroutines[i]);
            respawnCoroutines[i] = null;
        }
    }

    private void ReturnAllActiveDefendersToPool()
    {
        for (int i = 0; i < activeDefenders.Count; i++)
        {
            DefenderUnit defender = activeDefenders[i];
            activeDefenders[i] = null;
            if (defender == null)
                continue;

            ReturnDefenderToPool(defender);
        }
    }

    private void ReturnDefenderToPool(DefenderUnit defender)
    {
        if (defender == null)
            return;

        defender.Died -= HandleDefenderDied;
        if (defender.gameObject.activeSelf)
            defender.gameObject.SetActive(false);

        pooledDefenders.Enqueue(defender);
    }

    private void ClearSlotAndCancelRespawn(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= activeDefenders.Count)
            return;

        if (slotIndex < respawnCoroutines.Count && respawnCoroutines[slotIndex] != null)
        {
            StopCoroutine(respawnCoroutines[slotIndex]);
            respawnCoroutines[slotIndex] = null;
        }

        DefenderUnit defender = activeDefenders[slotIndex];
        activeDefenders[slotIndex] = null;
        if (defender != null)
            ReturnDefenderToPool(defender);
    }

    private Vector3 ClampRallyPoint(Vector3 worldPosition)
    {
        Vector2 fromBarracks = (Vector2)(worldPosition - transform.position);
        float maxDistance = Mathf.Max(0.5f, rallyMaxDistanceFromBarracks);
        if (fromBarracks.sqrMagnitude > maxDistance * maxDistance)
            worldPosition = transform.position + (Vector3)(fromBarracks.normalized * maxDistance);

        worldPosition.z = transform.position.z;
        return worldPosition;
    }

    private Vector2 ResolveRoadNormalForPosition(Vector3 worldPosition, Vector2 fallbackNormal)
    {
        Transform[][] allPaths = Waypoints.AllPaths;
        if (allPaths == null || allPaths.Length == 0)
            return fallbackNormal;

        float bestSqrDistance = float.PositiveInfinity;
        Transform[] bestPath = null;
        int bestWaypointIndex = -1;
        Vector2 source = worldPosition;

        for (int pathIndex = 0; pathIndex < allPaths.Length; pathIndex++)
        {
            Transform[] path = allPaths[pathIndex];
            if (path == null || path.Length == 0)
                continue;

            for (int waypointIndex = 0; waypointIndex < path.Length; waypointIndex++)
            {
                Transform waypoint = path[waypointIndex];
                if (waypoint == null)
                    continue;

                Vector2 waypointPosition = waypoint.position;
                float sqrDistance = (waypointPosition - source).sqrMagnitude;
                if (sqrDistance >= bestSqrDistance)
                    continue;

                bestSqrDistance = sqrDistance;
                bestPath = path;
                bestWaypointIndex = waypointIndex;
            }
        }

        if (bestPath == null || bestWaypointIndex < 0)
            return fallbackNormal;

        Vector2 tangent = ResolvePathTangent(bestPath, bestWaypointIndex);
        if (tangent.sqrMagnitude < 0.0001f)
            tangent = fallbackNormal.sqrMagnitude > 0.0001f ? fallbackNormal : Vector2.down;

        tangent.Normalize();
        return new Vector2(-tangent.y, tangent.x).normalized;
    }

    private void ReassignDefenderGuardPoints(bool resetTargets)
    {
        int activeSlotCount = ResolveActiveSlotCount();
        for (int i = 0; i < activeDefenders.Count; i++)
        {
            DefenderUnit defender = activeDefenders[i];
            if (defender == null || !defender.IsAlive || i >= activeSlotCount)
                continue;

            Vector3 guardPosition = ResolveDefensePoint(i, activeSlotCount);
            defender.UpdateGuardPoint(guardPosition, resetTargets);
        }
    }

    private void UpdateRallyPointVisual()
    {
        if (rallyPointVisual == null)
            return;

        bool shouldShow = hasManualRallyPoint;
        if (rallyPointVisual.gameObject.activeSelf != shouldShow)
            rallyPointVisual.gameObject.SetActive(shouldShow);

        if (!shouldShow)
            return;

        rallyPointVisual.position = manualRallyPoint;
    }
}
