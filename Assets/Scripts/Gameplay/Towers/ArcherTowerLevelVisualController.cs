using System;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class ArcherTowerLevelVisualController : MonoBehaviour
{
    [Serializable]
    private sealed class LevelVisual
    {
        [Min(1)] public int Level = 1;
        public Sprite[] IdleFrames = Array.Empty<Sprite>();
        [Min(1f)] public float IdleFps = 4f;
        [Min(1)] public int ActiveArcherSlots = 1;
    }

    [Header("Scene Wiring")]
    [Tooltip("Owning ArcherTower that provides current visual level.")]
    [SerializeField] private ArcherTower archerTower;
    [Tooltip("Tower body renderer that swaps/animates level idle frames.")]
    [SerializeField] private SpriteRenderer towerBodyRenderer;
    [Tooltip("Optional emitter. Receives active archer slots count as fire-point limit.")]
    [SerializeField] private ArcherTowerProjectileEmitter projectileEmitter;
    [Tooltip("Archer slot roots ordered from top to bottom. Level controls how many are active.")]
    [SerializeField] private Transform[] archerSlots = Array.Empty<Transform>();

    [Header("Per-Level Body Visuals")]
    [Tooltip("Body idle animation + active archer slot count for each tower level.")]
    [SerializeField] private LevelVisual[] levelVisuals = Array.Empty<LevelVisual>();

    [Header("Authoring")]
    [Tooltip("If enabled, missing required references disable this component with explicit error.")]
    [SerializeField] private bool strictAuthoring = true;

    private bool isWired;
    private Sprite[] activeFrames = Array.Empty<Sprite>();
    private float activeFps = 4f;
    private int frameIndex;
    private float frameTimer;
    private int currentAppliedSlotCount = 1;
    private bool sortingOffsetsCached;
    private int bodySortingOffset = 0;
    private int[] archerSortingOffsets = Array.Empty<int>();

    private const float TopDownSortingPrecision = 100f;
    private const string SortingLayerUnitsAlive = "Units_Alive";

    private void Awake()
    {
        isWired = ValidateWiring();
        if (!isWired)
            enabled = false;
    }

    private void OnEnable()
    {
        if (!isWired)
            return;

        archerTower.VisualLevelChanged += HandleVisualLevelChanged;
        CacheSortingOffsets();
        ApplyLevel(archerTower.VisualLevel);
        SyncSortingOrders();
    }

    private void OnDisable()
    {
        if (archerTower != null)
            archerTower.VisualLevelChanged -= HandleVisualLevelChanged;
    }

    private void Update()
    {
        if (!isWired)
            return;

        AdvanceBodyAnimation(Time.deltaTime);
    }

    private void LateUpdate()
    {
        if (!isWired)
            return;

        SyncSortingOrders();
    }

    private bool ValidateWiring()
    {
        bool valid = true;

        if (archerTower == null)
        {
            if (strictAuthoring)
            {
                Debug.LogError($"{name}: ArcherTowerLevelVisualController requires ArcherTower reference.", this);
                valid = false;
            }
            else
            {
                archerTower = GetComponent<ArcherTower>();
                valid = archerTower != null;
            }
        }

        if (towerBodyRenderer == null)
        {
            if (strictAuthoring)
            {
                Debug.LogError($"{name}: ArcherTowerLevelVisualController requires tower body SpriteRenderer.", this);
                valid = false;
            }
            else
            {
                towerBodyRenderer = GetComponent<SpriteRenderer>();
                valid &= towerBodyRenderer != null;
            }
        }

        return valid;
    }

    private void HandleVisualLevelChanged(int level)
    {
        ApplyLevel(level);
    }

    private void ApplyLevel(int level)
    {
        if (TryResolveLevelVisual(level, out LevelVisual resolved))
        {
            activeFrames = resolved.IdleFrames ?? Array.Empty<Sprite>();
            activeFps = Mathf.Max(1f, resolved.IdleFps);
            currentAppliedSlotCount = Mathf.Max(1, resolved.ActiveArcherSlots);
        }
        else
        {
            activeFrames = Array.Empty<Sprite>();
            activeFps = 4f;
            currentAppliedSlotCount = Mathf.Max(1, level);
        }

        ApplyActiveArcherSlots(currentAppliedSlotCount);
        ResetBodyAnimation();
    }

    private bool TryResolveLevelVisual(int requestedLevel, out LevelVisual resolved)
    {
        resolved = default;
        if (levelVisuals == null || levelVisuals.Length == 0)
            return false;

        int target = Mathf.Max(1, requestedLevel);
        bool foundExact = false;
        bool foundFallback = false;
        int fallbackLevel = int.MinValue;

        for (int i = 0; i < levelVisuals.Length; i++)
        {
            LevelVisual current = levelVisuals[i];
            int currentLevel = Mathf.Max(1, current.Level);

            if (currentLevel == target)
            {
                resolved = current;
                foundExact = true;
                break;
            }

            if (currentLevel <= target && (!foundFallback || currentLevel > fallbackLevel))
            {
                fallbackLevel = currentLevel;
                resolved = current;
                foundFallback = true;
            }
        }

        if (foundExact || foundFallback)
            return true;

        resolved = levelVisuals[0];
        return true;
    }

    private void ApplyActiveArcherSlots(int requestedCount)
    {
        int totalSlots = archerSlots == null ? 0 : archerSlots.Length;
        int clampedCount = totalSlots <= 0
            ? Mathf.Max(1, requestedCount)
            : Mathf.Clamp(requestedCount, 1, totalSlots);

        if (projectileEmitter != null)
            projectileEmitter.SetActiveFirePointLimit(clampedCount);

        if (totalSlots <= 0)
            return;

        for (int i = 0; i < archerSlots.Length; i++)
        {
            Transform slot = archerSlots[i];
            if (slot == null)
                continue;

            bool shouldBeActive = i < clampedCount;
            if (slot.gameObject.activeSelf != shouldBeActive)
                slot.gameObject.SetActive(shouldBeActive);
        }
    }

    private void ResetBodyAnimation()
    {
        frameIndex = 0;
        frameTimer = 0f;
        ApplyCurrentBodyFrame();
    }

    private void AdvanceBodyAnimation(float deltaTime)
    {
        if (activeFrames == null || activeFrames.Length <= 1)
            return;

        float frameDuration = 1f / Mathf.Max(1f, activeFps);
        frameTimer += deltaTime;

        while (frameTimer >= frameDuration)
        {
            frameTimer -= frameDuration;
            frameIndex = (frameIndex + 1) % activeFrames.Length;
            ApplyCurrentBodyFrame();
        }
    }

    private void ApplyCurrentBodyFrame()
    {
        if (towerBodyRenderer == null)
            return;

        if (activeFrames == null || activeFrames.Length == 0)
            return;

        int safeIndex = Mathf.Clamp(frameIndex, 0, activeFrames.Length - 1);
        towerBodyRenderer.sprite = activeFrames[safeIndex];
    }

    private void CacheSortingOffsets()
    {
        if (sortingOffsetsCached)
            return;

        if (towerBodyRenderer != null)
            bodySortingOffset = towerBodyRenderer.sortingOrder;

        int slotCount = archerSlots == null ? 0 : archerSlots.Length;
        if (slotCount > 0)
        {
            archerSortingOffsets = new int[slotCount];
            for (int i = 0; i < archerSlots.Length; i++)
            {
                if (archerSlots[i] == null)
                {
                    archerSortingOffsets[i] = i + 1;
                    continue;
                }

                SpriteRenderer archerRenderer = archerSlots[i].GetComponent<SpriteRenderer>();
                archerSortingOffsets[i] = archerRenderer != null ? archerRenderer.sortingOrder : i + 1;
            }
        }

        sortingOffsetsCached = true;
    }

    private void SyncSortingOrders()
    {
        if (towerBodyRenderer == null)
            return;

        CacheSortingOffsets();

        float pivotY = towerBodyRenderer.bounds.min.y;
        int baseOrder = Mathf.RoundToInt(-pivotY * TopDownSortingPrecision);

        towerBodyRenderer.sortingLayerName = SortingLayerUnitsAlive;
        towerBodyRenderer.sortingOrder = baseOrder + bodySortingOffset;

        if (archerSlots == null || archerSlots.Length == 0)
            return;

        for (int i = 0; i < archerSlots.Length; i++)
        {
            Transform slot = archerSlots[i];
            if (slot == null)
                continue;

            SpriteRenderer archerRenderer = slot.GetComponent<SpriteRenderer>();
            if (archerRenderer == null)
                continue;

            int offset = i < archerSortingOffsets.Length ? archerSortingOffsets[i] : (i + 1);
            archerRenderer.sortingLayerName = SortingLayerUnitsAlive;
            archerRenderer.sortingOrder = baseOrder + offset;
        }
    }
}
