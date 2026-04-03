using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[DisallowMultipleComponent]
public sealed class BarracksController : MonoBehaviour
{
    private const float RoadFormationSpacing = 0.44f;
    private const float RoadFormationRowDepth = 0.36f;
    private const float RoadFormationFrontOffset = 0.04f;
    private const float RoadFormationHalfWidth = 0.88f;
    private const int RoadFormationMaxPerRow = 3;
    private const int RallyCircleSegments = 40;
    private const float RallyPointAnimationFps = 10f;
    private static readonly Color RallyCircleColor = new Color(1f, 0.92f, 0.35f, 0.72f);
    private static Material rallyCircleMaterial;
#if UNITY_EDITOR
    private const string DefaultRallyPointSpritePath = "Assets/Sprites/Barracks/RallyPoint96.png";
#endif
    private static readonly string[] LegacyRallyVisualNames =
    {
        "RallyPointVisual",
        "RallyRadiusVisual",
        "RallyPoint",
        "RallyRadius"
    };

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
    [SerializeField, Min(0.05f), Tooltip("Max distance from clicked point to nearest road point when placing rally.")]
    private float rallySnapToRoadMaxDistance = 1.2f;
    [SerializeField, Min(0.01f), Tooltip("Width of rally radius line in world units.")]
    private float rallyCircleWidth = 0.04f;
    [SerializeField, Tooltip("Marker sprite shown at active rally point.")]
    private Sprite rallyPointSprite;
    [SerializeField, Min(0.05f), Tooltip("World-space marker size multiplier.")]
    private float rallyPointScale = 0.7f;
    [SerializeField, Tooltip("World-space marker offset from ground point.")]
    private Vector3 rallyPointOffset = new Vector3(0f, 0.18f, 0f);
    [SerializeField, Min(0), Tooltip("Sorting order offset applied to rally marker over barracks layer.")]
    private int rallyPointSortingOffset = 4;
    [SerializeField, Tooltip("When enabled, rally flag stays visible while this barracks is selected.")]
    private bool showRallyPointWhenSelected = true;

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
    private Vector2 roadFormationTangent;
    private bool hasManualRallyPoint;
    private Vector3 manualRallyPoint;
    private Vector2 manualRallyNormal;
    private Vector2 manualRallyTangent;
    private bool rallyPlacementPreviewActive;
    private bool isSelectedInUi;
    private LineRenderer rallyCircleRenderer;
    private SpriteRenderer rallyPointRenderer;
    private Sprite[] rallyPointAnimationFrames;
    [SerializeField, HideInInspector] private Sprite[] rallyPointAnimationFramesSerialized;
    private float rallyPointAnimationTimer;
    private int rallyPointAnimationFrameIndex;
    private bool rallyPointWasVisibleLastFrame;

    private void Awake()
    {
#if UNITY_EDITOR
        if (rallyPointSprite == null)
            rallyPointSprite = AssetDatabase.LoadAssetAtPath<Sprite>(DefaultRallyPointSpritePath);
#endif
        DisableLegacyRallyVisuals();

        isAuthoringValid = ValidateAuthoring();
        runtimeSquadSize = Mathf.Max(1, defendersPerBarracks);

        EnsureRallyCircleRenderer();
        EnsureRallyPointRenderer();
        SetRallyPlacementPreviewActive(false);

        if (!isAuthoringValid)
            enabled = false;
    }

    private void OnEnable()
    {
        if (!isAuthoringValid)
            return;

        DisableLegacyRallyVisuals();
        roadFormationCached = TryResolveRallyAnchor(out roadFormationAnchor, out roadFormationNormal, out roadFormationTangent);

        UpdateRallyPointVisual();
        EnsureRuntimeBuffers(ResolveActiveSlotCount());
        if (spawnOnEnable)
            SpawnAllMissingDefenders();
    }

    private void OnDisable()
    {
        StopAllRespawns();
        ReturnAllActiveDefendersToPool();
        SetRallyPlacementPreviewActive(false);
    }

    private void Update()
    {
        AnimateRallyPointMarker();
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
        if (!TryFindClosestRoadPoint(clamped, out Vector3 snappedPoint, out Vector2 tangent, out float sqrRoadDistance))
            return false;

        float maxRoadDistance = Mathf.Max(0.05f, rallySnapToRoadMaxDistance);
        if (sqrRoadDistance > maxRoadDistance * maxRoadDistance)
            return false;

        Vector2 normal = tangent.sqrMagnitude > 0.0001f
            ? new Vector2(-tangent.y, tangent.x).normalized
            : (roadFormationNormal.sqrMagnitude > 0.0001f ? roadFormationNormal : Vector2.right);

        hasManualRallyPoint = true;
        manualRallyPoint = snappedPoint;
        manualRallyNormal = normal;
        manualRallyTangent = tangent.sqrMagnitude > 0.0001f ? tangent.normalized : Vector2.down;

        roadFormationCached = true;
        roadFormationAnchor = snappedPoint;
        roadFormationNormal = normal;
        roadFormationTangent = manualRallyTangent;
        UpdateRallyPointVisual();
        ReassignDefenderGuardPoints(resetTargets: true);
        return true;
    }

    public void SetRallyPlacementPreviewActive(bool active)
    {
        rallyPlacementPreviewActive = active;
        UpdateRallyPointVisual();
    }

    public void SetSelectedInUi(bool selected)
    {
        isSelectedInUi = selected;
        UpdateRallyPointVisual();
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

        if (!roadFormationCached)
            roadFormationCached = TryResolveRallyAnchor(out roadFormationAnchor, out roadFormationNormal, out roadFormationTangent);

        if (!roadFormationCached)
            return transform.position;

        int slotCount = Mathf.Max(1, totalSlots);
        int defendersPerRow = Mathf.Clamp(RoadFormationMaxPerRow, 1, slotCount);
        int rowIndex = slotIndex / defendersPerRow;
        int slotIndexInRow = slotIndex % defendersPerRow;
        int rowStart = rowIndex * defendersPerRow;
        int countInRow = Mathf.Min(defendersPerRow, slotCount - rowStart);

        float rowCenter = (countInRow - 1) * 0.5f;
        float sideOffset = (slotIndexInRow - rowCenter) * RoadFormationSpacing;
        float sideClamp = Mathf.Max(RoadFormationSpacing * 0.5f, RoadFormationHalfWidth);
        sideOffset = Mathf.Clamp(sideOffset, -sideClamp, sideClamp);

        float rowDepth = RoadFormationFrontOffset + rowIndex * RoadFormationRowDepth;
        Vector3 lateral = (Vector3)(roadFormationNormal * sideOffset);
        Vector3 longitudinal = (Vector3)(roadFormationTangent * rowDepth);
        return roadFormationAnchor + lateral + longitudinal;
    }

    private bool TryResolveRallyAnchor(out Vector3 anchor, out Vector2 normal, out Vector2 tangent)
    {
        if (hasManualRallyPoint)
        {
            anchor = manualRallyPoint;
            normal = manualRallyNormal.sqrMagnitude > 0.0001f ? manualRallyNormal : Vector2.right;
            tangent = manualRallyTangent.sqrMagnitude > 0.0001f ? manualRallyTangent : Vector2.down;
            return true;
        }

        return TryComputeRoadFormationAnchor(out anchor, out normal, out tangent);
    }

    private bool TryComputeRoadFormationAnchor(out Vector3 anchor, out Vector2 normal, out Vector2 tangent)
    {
        anchor = transform.position;
        normal = Vector2.right;
        tangent = Vector2.down;

        if (!TryFindClosestRoadPoint(transform.position, out Vector3 closestPoint, out Vector2 closestTangent, out float _))
            return false;

        anchor = closestPoint;
        if (closestTangent.sqrMagnitude < 0.0001f)
            closestTangent = Vector2.down;

        tangent = closestTangent.normalized;
        normal = new Vector2(-tangent.y, tangent.x).normalized;
        return true;
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

    private bool TryFindClosestRoadPoint(Vector2 worldPosition, out Vector3 closestPoint, out Vector2 tangent, out float sqrDistance)
    {
        closestPoint = transform.position;
        tangent = Vector2.down;
        sqrDistance = float.PositiveInfinity;

        Transform[][] allPaths = Waypoints.AllPaths;
        if (allPaths == null || allPaths.Length == 0)
            return false;

        bool found = false;

        for (int pathIndex = 0; pathIndex < allPaths.Length; pathIndex++)
        {
            Transform[] path = allPaths[pathIndex];
            if (path == null || path.Length == 0)
                continue;

            for (int i = 0; i < path.Length - 1; i++)
            {
                Transform from = path[i];
                Transform to = path[i + 1];
                if (from == null || to == null)
                    continue;

                Vector2 a = from.position;
                Vector2 b = to.position;
                Vector2 segment = b - a;
                float segmentLengthSqr = segment.sqrMagnitude;
                if (segmentLengthSqr < 0.0001f)
                    continue;

                float t = Mathf.Clamp01(Vector2.Dot(worldPosition - a, segment) / segmentLengthSqr);
                Vector2 projected = a + segment * t;
                float projectedSqrDistance = (projected - worldPosition).sqrMagnitude;
                if (projectedSqrDistance >= sqrDistance)
                    continue;

                sqrDistance = projectedSqrDistance;
                closestPoint = new Vector3(projected.x, projected.y, transform.position.z);
                tangent = segment.normalized;
                found = true;
            }

            if (path.Length == 1 && path[0] != null)
            {
                Vector2 p = path[0].position;
                float pointSqrDistance = (p - worldPosition).sqrMagnitude;
                if (pointSqrDistance >= sqrDistance)
                    continue;

                sqrDistance = pointSqrDistance;
                closestPoint = new Vector3(p.x, p.y, transform.position.z);
                tangent = Vector2.down;
                found = true;
            }
        }

        return found;
    }

    private void ReassignDefenderGuardPoints(bool resetTargets)
    {
        int activeSlotCount = ResolveActiveSlotCount();
        for (int i = 0; i < activeDefenders.Count; i++)
        {
            DefenderUnit defender = activeDefenders[i];
            if (defender == null || !defender.IsAlive || i >= activeSlotCount)
                continue;

            if (defender.HasEngagingAttackers)
                continue;

            Vector3 guardPosition = ResolveDefensePoint(i, activeSlotCount);
            defender.UpdateGuardPoint(guardPosition, resetTargets);
        }
    }

    private void UpdateRallyPointVisual()
    {
        EnsureRallyCircleRenderer();
        EnsureRallyPointRenderer();

        if (rallyCircleRenderer == null)
            return;

        bool hasAnchor = TryResolveRallyAnchor(out Vector3 rallyAnchor, out Vector2 unusedNormal, out Vector2 unusedTangent);
        bool shouldShowCircle = rallyPlacementPreviewActive;
        rallyCircleRenderer.enabled = shouldShowCircle;

        bool shouldShowMarker = hasAnchor && (rallyPlacementPreviewActive || (showRallyPointWhenSelected && isSelectedInUi));

        if (rallyPointRenderer != null)
        {
            rallyPointRenderer.enabled = shouldShowMarker && rallyPointSprite != null;
            if (rallyPointRenderer.enabled)
            {
                rallyPointRenderer.transform.position = rallyAnchor + rallyPointOffset;
                rallyPointRenderer.color = Color.white;
            }
        }

        if (!shouldShowCircle)
            return;

        float holdRadius = Mathf.Max(0.5f, rallyMaxDistanceFromBarracks);
        Vector3 circleAnchor = transform.position;

        float z = transform.position.z - 0.01f;
        for (int i = 0; i < RallyCircleSegments; i++)
        {
            float t = (float)i / RallyCircleSegments;
            float angle = t * Mathf.PI * 2f;
            Vector3 point = new Vector3(
                circleAnchor.x + Mathf.Cos(angle) * holdRadius,
                circleAnchor.y + Mathf.Sin(angle) * holdRadius,
                z);
            rallyCircleRenderer.SetPosition(i, point);
        }
    }

    private void EnsureRallyCircleRenderer()
    {
        if (rallyCircleRenderer != null)
            return;

        rallyCircleRenderer = GetComponent<LineRenderer>();
        if (rallyCircleRenderer == null)
            rallyCircleRenderer = gameObject.AddComponent<LineRenderer>();

        if (rallyCircleMaterial == null)
            rallyCircleMaterial = new Material(Shader.Find("Sprites/Default"));

        rallyCircleRenderer.useWorldSpace = true;
        rallyCircleRenderer.loop = true;
        rallyCircleRenderer.positionCount = RallyCircleSegments;
        rallyCircleRenderer.widthMultiplier = Mathf.Max(0.01f, rallyCircleWidth);
        rallyCircleRenderer.alignment = LineAlignment.View;
        rallyCircleRenderer.textureMode = LineTextureMode.Stretch;
        rallyCircleRenderer.numCapVertices = 0;
        rallyCircleRenderer.numCornerVertices = 0;
        rallyCircleRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rallyCircleRenderer.receiveShadows = false;
        rallyCircleRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        rallyCircleRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        rallyCircleRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        rallyCircleRenderer.material = rallyCircleMaterial;
        rallyCircleRenderer.startColor = RallyCircleColor;
        rallyCircleRenderer.endColor = RallyCircleColor;
        rallyCircleRenderer.enabled = false;

        SpriteRenderer barracksSprite = GetComponent<SpriteRenderer>();
        if (barracksSprite != null)
        {
            rallyCircleRenderer.sortingLayerID = barracksSprite.sortingLayerID;
            rallyCircleRenderer.sortingOrder = barracksSprite.sortingOrder + 3;
        }
        else
        {
            rallyCircleRenderer.sortingOrder = 500;
        }
    }

    private void EnsureRallyPointRenderer()
    {
        if (rallyPointRenderer == null)
        {
            Transform markerTransform = transform.Find("__RallyPointMarker");
            if (markerTransform == null)
            {
                GameObject marker = new GameObject("__RallyPointMarker");
                markerTransform = marker.transform;
                markerTransform.SetParent(transform, false);
            }

            rallyPointRenderer = markerTransform.GetComponent<SpriteRenderer>();
            if (rallyPointRenderer == null)
                rallyPointRenderer = markerTransform.gameObject.AddComponent<SpriteRenderer>();
        }

        if (rallyPointRenderer == null)
            return;

        rallyPointRenderer.sprite = rallyPointSprite;
        rallyPointRenderer.enabled = false;
        float scale = Mathf.Max(0.05f, rallyPointScale);
        rallyPointRenderer.transform.localScale = new Vector3(scale, scale, 1f);

        SpriteRenderer barracksSprite = GetComponent<SpriteRenderer>();
        if (barracksSprite != null)
        {
            rallyPointRenderer.sortingLayerID = barracksSprite.sortingLayerID;
            rallyPointRenderer.sortingOrder = barracksSprite.sortingOrder + Mathf.Max(0, rallyPointSortingOffset);
        }

        EnsureRallyPointAnimationFrames();
    }

    private void AnimateRallyPointMarker()
    {
        if (rallyPointRenderer == null)
            return;

        bool visible = rallyPointRenderer.enabled;
        if (!visible)
        {
            rallyPointWasVisibleLastFrame = false;
            return;
        }

        if (!rallyPointWasVisibleLastFrame)
        {
            rallyPointAnimationTimer = 0f;
            rallyPointAnimationFrameIndex = 0;
            rallyPointWasVisibleLastFrame = true;
            ApplyCurrentRallyFrame();
        }

        if (rallyPointAnimationFrames == null || rallyPointAnimationFrames.Length <= 1)
            return;

        float frameDuration = 1f / Mathf.Max(1f, RallyPointAnimationFps);
        rallyPointAnimationTimer += Time.deltaTime;
        while (rallyPointAnimationTimer >= frameDuration)
        {
            rallyPointAnimationTimer -= frameDuration;
            rallyPointAnimationFrameIndex = (rallyPointAnimationFrameIndex + 1) % rallyPointAnimationFrames.Length;
            ApplyCurrentRallyFrame();
        }
    }

    private void EnsureRallyPointAnimationFrames()
    {
#if UNITY_EDITOR
        if (rallyPointSprite == null)
        {
            rallyPointAnimationFrames = null;
            return;
        }

        string atlasPath = AssetDatabase.GetAssetPath(rallyPointSprite.texture);
        if (string.IsNullOrWhiteSpace(atlasPath))
        {
            rallyPointAnimationFrames = new[] { rallyPointSprite };
            return;
        }

        Object[] allAssets = AssetDatabase.LoadAllAssetsAtPath(atlasPath);
        if (allAssets == null || allAssets.Length == 0)
        {
            rallyPointAnimationFrames = new[] { rallyPointSprite };
            return;
        }

        string prefix = GetRallySpritePrefix(rallyPointSprite.name);
        List<Sprite> frames = new List<Sprite>(allAssets.Length);
        for (int i = 0; i < allAssets.Length; i++)
        {
            if (allAssets[i] is not Sprite sprite)
                continue;

            if (!sprite.name.StartsWith(prefix))
                continue;

            frames.Add(sprite);
        }

        if (frames.Count == 0)
        {
            rallyPointAnimationFrames = new[] { rallyPointSprite };
            return;
        }

        rallyPointAnimationFrames = frames
            .OrderBy(sprite => sprite.name, System.StringComparer.Ordinal)
            .ToArray();
#else
        rallyPointAnimationFrames = (rallyPointAnimationFramesSerialized != null && rallyPointAnimationFramesSerialized.Length > 0)
            ? rallyPointAnimationFramesSerialized
            : (rallyPointSprite != null ? new[] { rallyPointSprite } : null);
#endif
    }

    private void ApplyCurrentRallyFrame()
    {
        if (rallyPointRenderer == null)
            return;

        if (rallyPointAnimationFrames == null || rallyPointAnimationFrames.Length == 0)
        {
            rallyPointRenderer.sprite = rallyPointSprite;
            return;
        }

        int clampedIndex = Mathf.Clamp(rallyPointAnimationFrameIndex, 0, rallyPointAnimationFrames.Length - 1);
        rallyPointRenderer.sprite = rallyPointAnimationFrames[clampedIndex];
    }

    private static string GetRallySpritePrefix(string spriteName)
    {
        if (string.IsNullOrWhiteSpace(spriteName))
            return string.Empty;

        int lastUnderscore = spriteName.LastIndexOf('_');
        if (lastUnderscore <= 0)
            return spriteName;

        bool hasNumericSuffix = true;
        for (int i = lastUnderscore + 1; i < spriteName.Length; i++)
        {
            if (!char.IsDigit(spriteName[i]))
            {
                hasNumericSuffix = false;
                break;
            }
        }

        return hasNumericSuffix ? spriteName.Substring(0, lastUnderscore + 1) : spriteName;
    }

    private void DisableLegacyRallyVisuals()
    {
        for (int i = 0; i < LegacyRallyVisualNames.Length; i++)
            DisableLegacyVisualObject(LegacyRallyVisualNames[i]);

        // Defensive cleanup for old authoring children renamed manually.
        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);
            if (child == null || string.IsNullOrWhiteSpace(child.name))
                continue;

            string lowerName = child.name.ToLowerInvariant();
            if (!lowerName.Contains("rally"))
                continue;

            if (child.name == "__RallyPointMarker")
                continue;

            DisableLegacyVisualObject(child.name);
        }
    }

    private void DisableLegacyVisualObject(string objectName)
    {
        if (string.IsNullOrWhiteSpace(objectName))
            return;

        Transform legacyTransform = transform.Find(objectName);
        if (legacyTransform == null)
            return;

        SpriteRenderer spriteRenderer = legacyTransform.GetComponent<SpriteRenderer>();
        if (spriteRenderer != null)
            spriteRenderer.enabled = false;

        LineRenderer lineRenderer = legacyTransform.GetComponent<LineRenderer>();
        if (lineRenderer != null)
            lineRenderer.enabled = false;

        legacyTransform.gameObject.SetActive(false);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (rallyPointSprite == null)
            rallyPointSprite = AssetDatabase.LoadAssetAtPath<Sprite>(DefaultRallyPointSpritePath);

        if (rallyPointRenderer != null)
            rallyPointRenderer.sprite = rallyPointSprite;

        EnsureRallyPointAnimationFrames();
        rallyPointAnimationFramesSerialized = rallyPointAnimationFrames;
    }
#endif
}
