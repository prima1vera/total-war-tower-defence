using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

public class EnemyDeathVisualManager : MonoBehaviour
{
    private static EnemyDeathVisualManager instance;
    private static bool missingInstanceLogged;
    private bool isApplyingPresetInValidate;

    private enum VisualTuningPreset
    {
        Custom = 0,
        Light = 1,
        Cinematic = 2,
        Gore = 3
    }

    [Header("Preset")]
    [Tooltip("Select one of built-in tuning presets. Use Custom to keep manual values.")]
    [SerializeField] private VisualTuningPreset authoringPreset = VisualTuningPreset.Custom;
    [Tooltip("If enabled, selected preset is auto-applied in editor on every validation.")]
    [SerializeField] private bool autoApplySelectedPresetInEditor;

    [Header("Death Tracking")]
    [Tooltip("How many fresh corpse visuals can stay active before oldest deaths overflow to remains.")]
    [SerializeField, Min(1)] private int maxTrackedDeaths = 80;
    [Tooltip("How many overflow remains can stay on battlefield. Oldest remains are recycled when cap is exceeded.")]
    [SerializeField, Min(1)] private int maxTrackedRemains = 140;
    [Tooltip("Blend duration when corpse overflow converts to remains sprite.")]
    [SerializeField, Min(0.05f)] private float overflowToRemainsTransitionDuration = 0.6f;
    [Tooltip("Fade-out duration when old blood decals are recycled by cap.")]
    [SerializeField, Min(0.05f)] private float overflowBloodFadeDuration = 1.25f;
    [SerializeField] private List<RemainsVariant> remainsVariants = new List<RemainsVariant>(4);

    [Header("Prewarm")]
    [Tooltip("Corpse visual objects pre-created on Awake for stable runtime allocation.")]
    [SerializeField, Min(0)] private int prewarmCorpseCount = 32;
    [Tooltip("Blood decal prefab used for prewarm and as fallback source when enemy blood prefab is not assigned.")]
    [SerializeField] private GameObject bloodPoolPrewarmPrefab;
    [Tooltip("How many blood decal objects to prewarm on Awake.")]
    [SerializeField, Min(0)] private int bloodPoolPrewarmCount = 24;

    [Header("Blood Decals")]
    [Tooltip("Sprite variants used for all managed ground blood decals (death + cluster + direct hits).")]
    [SerializeField] private Sprite[] bloodDecalVariants;
    [Tooltip("Scale range for regular death blood decals.")]
    [SerializeField] private Vector2 bloodDecalScaleRange = new Vector2(0.22f, 0.36f);
    [Tooltip("Global cap for all persistent ground blood decals on the battlefield.")]
    [SerializeField, Min(1)] private int maxTrackedGroundBloodDecals = 220;

    [Header("Death Blood Behavior")]
    [Tooltip("Chance to spawn a death blood decal when an enemy dies (0 = never, 1 = always).")]
    [SerializeField, Range(0f, 1f)] private float deathBloodSpawnChance = 0.35f;
    [Tooltip("Chance to attempt extra cluster blood when many enemies die in one area.")]
    [SerializeField, Range(0f, 1f)] private float clusterBloodChance = 0.35f;
    [Tooltip("Time window (seconds) used to count nearby recent deaths for cluster detection.")]
    [SerializeField, Min(0.1f)] private float clusterWindowSeconds = 2.5f;
    [Tooltip("Radius around current death used to detect clustered kills.")]
    [SerializeField, Min(0.1f)] private float clusterScanRadius = 1.35f;
    [Tooltip("Minimum nearby deaths in the time window required to spawn cluster blood.")]
    [SerializeField, Min(2)] private int clusterThreshold = 4;
    [Tooltip("Scale range for additional cluster blood decals.")]
    [SerializeField] private Vector2 clusterBloodScaleRange = new Vector2(0.28f, 0.45f);

    [Header("Death Blood Flow")]
    [Tooltip("If enabled, death blood starts from enemy right side and settles to the left (screen-space feel).")]
    [SerializeField] private bool deathBloodFlowRightToLeft = true;
    [Tooltip("How far blood travels while flowing before it settles. Larger values = longer smear.")]
    [SerializeField] private Vector2 deathBloodFlowDistanceRange = new Vector2(0.08f, 0.18f);
    [Tooltip("Random vertical offset for blood flow start, to avoid identical-looking streaks.")]
    [SerializeField] private Vector2 deathBloodFlowVerticalJitterRange = new Vector2(-0.02f, 0.03f);
    [Tooltip("Final horizontal offset of the blood puddle relative to death point.")]
    [SerializeField] private Vector2 deathBloodSettleHorizontalOffsetRange = new Vector2(-0.06f, 0.02f);

    [Header("Death Blood Flow Timing")]
    [Tooltip("Multiplier for blood growth/flow duration. Higher values = slower blood spreading.")]
    [SerializeField] private Vector2 deathBloodFlowDurationMultiplierRange = new Vector2(2.4f, 4.35f);
    [Tooltip("Random delay before a blood decal starts growing. Adds natural desync.")]
    [SerializeField] private Vector2 deathBloodFlowStartDelayRange = new Vector2(0f, 0.12f);
    [Tooltip("Final alpha range after blood settles. Keep this near-opaque; use tint aging for old blood look.")]
    [SerializeField] private Vector2 deathBloodFlowEndAlphaRange = new Vector2(0.9f, 0.98f);

    [Header("Blood Aging")]
    [Tooltip("Fresh blood tint right after spawn.")]
    [SerializeField] private Color freshBloodTint = new Color(0.55f, 0.18f, 0.18f, 1f);
    [Tooltip("Dried blood tint reached after a short delay.")]
    [SerializeField] private Color driedBloodTint = new Color(0.28f, 0.08f, 0.08f, 1f);
    [Tooltip("Very old blood tint used for late lifetime readability.")]
    [SerializeField] private Color staleBloodTint = new Color(0.18f, 0.05f, 0.05f, 1f);
    [Tooltip("Time range to transition from fresh to dried tint.")]
    [SerializeField] private Vector2 bloodDryDurationRange = new Vector2(5f, 8f);
    [Tooltip("Extra time range to transition from dried to stale tint.")]
    [SerializeField] private Vector2 bloodStaleDurationRange = new Vector2(9f, 14f);
    [Tooltip("Delay range before late alpha fade starts. Keep late to avoid early transparency loss.")]
    [SerializeField] private Vector2 bloodLateFadeDelayRange = new Vector2(24f, 34f);
    [Tooltip("Duration range for very soft late alpha fade before cap cleanup.")]
    [SerializeField] private Vector2 bloodLateFadeDurationRange = new Vector2(18f, 28f);
    [Tooltip("Random late alpha multiplier. Keep close to 1 so aging is mostly tint-driven.")]
    [SerializeField] private Vector2 bloodLateFadeAlphaMultiplierRange = new Vector2(0.96f, 1f);
    [Tooltip("Brightness jitter to avoid identical blood puddles while keeping the same palette.")]
    [SerializeField] private Vector2 bloodTintBrightnessJitterRange = new Vector2(0.9f, 1.08f);

    private readonly Queue<DeathVisualEntry> activeDeathVisuals = new Queue<DeathVisualEntry>(80);
    private readonly Queue<RemainsVisualEntry> activeRemains = new Queue<RemainsVisualEntry>(128);
    private readonly Queue<GroundBloodEntry> activeGroundBlood = new Queue<GroundBloodEntry>(256);

    private readonly Dictionary<Sprite, Sprite> remainsVariantLookup = new Dictionary<Sprite, Sprite>(8);
    private readonly Dictionary<GameObject, Stack<GameObject>> bloodPoolByPrefab = new Dictionary<GameObject, Stack<GameObject>>(4);
    private readonly Stack<GameObject> corpseVisualPool = new Stack<GameObject>(64);
    private readonly Queue<RecentDeathEntry> recentDeaths = new Queue<RecentDeathEntry>(64);

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        missingInstanceLogged = false;
        RebuildRemainsVariantLookup();
        PrewarmPools();
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }

    private void OnValidate()
    {
        if (isApplyingPresetInValidate)
            return;

        if (autoApplySelectedPresetInEditor && authoringPreset != VisualTuningPreset.Custom)
            ApplyPreset(authoringPreset);

        RebuildRemainsVariantLookup();
        maxTrackedDeaths = Mathf.Max(1, maxTrackedDeaths);
        maxTrackedRemains = Mathf.Max(1, maxTrackedRemains);
        maxTrackedGroundBloodDecals = Mathf.Max(1, maxTrackedGroundBloodDecals);
        deathBloodFlowEndAlphaRange = SanitizeRange(deathBloodFlowEndAlphaRange, 0f, 1f);
        bloodDryDurationRange = SanitizeRange(bloodDryDurationRange, 0.1f);
        bloodStaleDurationRange = SanitizeRange(bloodStaleDurationRange, 0.1f);
        bloodLateFadeDelayRange = SanitizeRange(bloodLateFadeDelayRange, 0f);
        bloodLateFadeDurationRange = SanitizeRange(bloodLateFadeDurationRange, 0.1f);
        bloodLateFadeAlphaMultiplierRange = SanitizeRange(bloodLateFadeAlphaMultiplierRange, 0f, 1f);
        bloodTintBrightnessJitterRange = SanitizeRange(bloodTintBrightnessJitterRange, 0.4f);
    }

    [ContextMenu("Presets/Apply Selected Preset")]
    private void ApplySelectedPresetFromContextMenu()
    {
        ApplyPreset(authoringPreset);
    }

    [ContextMenu("Presets/Apply Light")]
    private void ApplyLightPresetFromContextMenu()
    {
        ApplyPreset(VisualTuningPreset.Light);
    }

    [ContextMenu("Presets/Apply Cinematic")]
    private void ApplyCinematicPresetFromContextMenu()
    {
        ApplyPreset(VisualTuningPreset.Cinematic);
    }

    [ContextMenu("Presets/Apply Gore")]
    private void ApplyGorePresetFromContextMenu()
    {
        ApplyPreset(VisualTuningPreset.Gore);
    }

    public void ApplyLightPreset()
    {
        ApplyPreset(VisualTuningPreset.Light);
    }

    public void ApplyCinematicPreset()
    {
        ApplyPreset(VisualTuningPreset.Cinematic);
    }

    public void ApplyGorePreset()
    {
        ApplyPreset(VisualTuningPreset.Gore);
    }

    private void ApplyPreset(VisualTuningPreset preset)
    {
        if (preset == VisualTuningPreset.Custom)
            return;

        isApplyingPresetInValidate = true;
        authoringPreset = preset;

        switch (preset)
        {
            case VisualTuningPreset.Light:
                maxTrackedDeaths = 70;
                maxTrackedRemains = 110;
                maxTrackedGroundBloodDecals = 110;
                overflowToRemainsTransitionDuration = 0.7f;
                overflowBloodFadeDuration = 0.85f;
                deathBloodSpawnChance = 0.5f;
                clusterBloodChance = 0.4f;
                clusterWindowSeconds = 1.2f;
                clusterScanRadius = 1.0f;
                clusterThreshold = 8;
                bloodDecalScaleRange = new Vector2(0.4f, 0.5f);
                clusterBloodScaleRange = new Vector2(0.5f, 0.6f);
                deathBloodFlowDistanceRange = new Vector2(0.04f, 0.10f);
                deathBloodFlowVerticalJitterRange = new Vector2(-0.015f, 0.02f);
                deathBloodSettleHorizontalOffsetRange = new Vector2(-0.04f, 0.015f);
                deathBloodFlowDurationMultiplierRange = new Vector2(1.6f, 2.6f);
                deathBloodFlowStartDelayRange = new Vector2(0f, 0.06f);
                deathBloodFlowEndAlphaRange = new Vector2(0.88f, 0.96f);
                freshBloodTint = new Color(0.53f, 0.18f, 0.18f, 1f);
                driedBloodTint = new Color(0.31f, 0.1f, 0.1f, 1f);
                staleBloodTint = new Color(0.21f, 0.065f, 0.065f, 1f);
                bloodDryDurationRange = new Vector2(5f, 8f);
                bloodStaleDurationRange = new Vector2(12f, 20f);
                bloodLateFadeDelayRange = new Vector2(20f, 30f);
                bloodLateFadeDurationRange = new Vector2(16f, 24f);
                bloodLateFadeAlphaMultiplierRange = new Vector2(0.94f, 1f);
                bloodTintBrightnessJitterRange = new Vector2(0.95f, 1.06f);
                deathBloodFlowRightToLeft = true;
                break;

            case VisualTuningPreset.Cinematic:
                maxTrackedDeaths = 100;
                maxTrackedRemains = 180;
                maxTrackedGroundBloodDecals = 220;
                overflowToRemainsTransitionDuration = 1f;
                overflowBloodFadeDuration = 1.25f;
                deathBloodSpawnChance = 0.85f;
                clusterBloodChance = 0.75f;
                clusterWindowSeconds = 1.8f;
                clusterScanRadius = 1.3f;
                clusterThreshold = 6;
                bloodDecalScaleRange = new Vector2(0.75f, 0.85f);
                clusterBloodScaleRange = new Vector2(0.85f, 0.95f);
                deathBloodFlowDistanceRange = new Vector2(0.08f, 0.18f);
                deathBloodFlowVerticalJitterRange = new Vector2(-0.02f, 0.03f);
                deathBloodSettleHorizontalOffsetRange = new Vector2(-0.06f, 0.02f);
                deathBloodFlowDurationMultiplierRange = new Vector2(5.8f, 8.2f);
                deathBloodFlowStartDelayRange = new Vector2(0f, 0.12f);
                deathBloodFlowEndAlphaRange = new Vector2(0.90f, 0.98f);
                freshBloodTint = new Color(0.55f, 0.18f, 0.18f, 1f);
                driedBloodTint = new Color(0.28f, 0.08f, 0.08f, 1f);
                staleBloodTint = new Color(0.18f, 0.05f, 0.05f, 1f);
                bloodDryDurationRange = new Vector2(6f, 9f);
                bloodStaleDurationRange = new Vector2(14f, 24f);
                bloodLateFadeDelayRange = new Vector2(24f, 34f);
                bloodLateFadeDurationRange = new Vector2(18f, 28f);
                bloodLateFadeAlphaMultiplierRange = new Vector2(0.96f, 1f);
                bloodTintBrightnessJitterRange = new Vector2(0.94f, 1.08f);
                deathBloodFlowRightToLeft = true;
                break;

            case VisualTuningPreset.Gore:
                maxTrackedDeaths = 130;
                maxTrackedRemains = 260;
                maxTrackedGroundBloodDecals = 420;
                overflowToRemainsTransitionDuration = 1.15f;
                overflowBloodFadeDuration = 1.8f;
                deathBloodSpawnChance = 0.95f;
                clusterBloodChance = 0.95f;
                clusterWindowSeconds = 2f;
                clusterScanRadius = 1.5f;
                clusterThreshold = 4;
                bloodDecalScaleRange = new Vector2(0.90f, 1.05f);
                clusterBloodScaleRange = new Vector2(1.05f, 1.22f);
                deathBloodFlowDistanceRange = new Vector2(0.12f, 0.26f);
                deathBloodFlowVerticalJitterRange = new Vector2(-0.03f, 0.05f);
                deathBloodSettleHorizontalOffsetRange = new Vector2(-0.1f, 0.02f);
                deathBloodFlowDurationMultiplierRange = new Vector2(8.8f, 12.8f);
                deathBloodFlowStartDelayRange = new Vector2(0f, 0.16f);
                deathBloodFlowEndAlphaRange = new Vector2(0.92f, 1f);
                freshBloodTint = new Color(0.62f, 0.20f, 0.20f, 1f);
                driedBloodTint = new Color(0.40f, 0.12f, 0.11f, 1f);
                staleBloodTint = new Color(0.28f, 0.08f, 0.075f, 1f);
                bloodDryDurationRange = new Vector2(10f, 14f);
                bloodStaleDurationRange = new Vector2(28f, 42f);
                bloodLateFadeDelayRange = new Vector2(40f, 60f);
                bloodLateFadeDurationRange = new Vector2(28f, 40f);
                bloodLateFadeAlphaMultiplierRange = new Vector2(0.98f, 1f);
                bloodTintBrightnessJitterRange = new Vector2(0.98f, 1.14f);
                deathBloodFlowRightToLeft = true;
                break;
        }

        isApplyingPresetInValidate = false;
    }

    public static EnemyDeathVisualManager Instance
    {
        get
        {
            if (instance == null && !missingInstanceLogged)
            {
                missingInstanceLogged = true;
                Debug.LogError("EnemyDeathVisualManager instance is missing. Add a scene-wired EnemyDeathManager object to the scene.");
            }

            return instance;
        }
    }

    public static bool TryGetInstance(out EnemyDeathVisualManager manager)
    {
        manager = instance;
        return manager != null;
    }

    public bool TrySpawnManagedGroundBlood(GameObject decalPrefab, Vector3 worldPosition, float uniformScale)
    {
        if (decalPrefab == null)
            return false;

        GameObject bloodObject = AcquireBloodVisual(decalPrefab);
        if (bloodObject == null)
            return false;

        SpriteRenderer bloodRenderer = bloodObject.GetComponent<SpriteRenderer>();
        ApplyBloodDecalVariant(bloodObject.transform, bloodRenderer, null);
        InitializeImmediateBloodPoolVisualState(bloodObject.transform, worldPosition, Mathf.Max(0.05f, uniformScale));

        float targetAlpha = ResolveBloodEndAlpha();
        BloodAgingVisual agingVisual = PrepareBloodAgingVisual(bloodObject, bloodRenderer, targetAlpha);
        ApplyFreshBloodVisual(agingVisual, bloodRenderer, targetAlpha, startAgingImmediately: true);

        RegisterGroundBloodDecal(bloodObject, decalPrefab);
        return true;
    }

    public void SpawnDeathVisuals(
        Sprite corpseSprite,
        bool corpseFlipX,
        Color corpseTint,
        Vector3 corpsePosition,
        Vector3 corpseScale,
        int corpseSortingOrder,
        GameObject bloodPoolPrefab,
        Vector3 bloodPosition)
    {
        if (corpseSprite == null && bloodPoolPrefab == null)
            return;

        EnforceActiveDeathCap();
        RegisterRecentDeath(bloodPosition);

        GameObject corpseObject = null;
        SpriteRenderer corpseRenderer = null;

        Color deadTint = corpseTint;
        deadTint.r *= 0.9f;
        deadTint.g *= 0.9f;
        deadTint.b *= 0.9f;
        deadTint.a = 0.9f;

        if (corpseSprite != null)
        {
            corpseObject = AcquireCorpseVisual();
            corpseObject.transform.SetPositionAndRotation(corpsePosition, Quaternion.identity);
            corpseObject.transform.localScale = corpseScale;

            corpseRenderer = corpseObject.GetComponent<SpriteRenderer>();
            corpseRenderer.color = deadTint;
            corpseRenderer.sprite = corpseSprite;
            corpseRenderer.flipX = corpseFlipX;
            corpseRenderer.sortingLayerName = "Units_Dead";
            corpseRenderer.sortingOrder = corpseSortingOrder;
            corpseObject.SetActive(true);
        }

        GameObject deathBloodObject = null;
        GameObject deathBloodSourcePrefab = null;

        bool shouldSpawnDeathBlood = bloodPoolPrefab != null && Random.value <= deathBloodSpawnChance;
        if (shouldSpawnDeathBlood)
        {
            deathBloodSourcePrefab = ResolveBloodSourcePrefab(bloodPoolPrefab);
            if (deathBloodSourcePrefab != null)
            {
                deathBloodObject = AcquireBloodVisual(deathBloodSourcePrefab);
                if (deathBloodObject != null)
                {
                    Vector3 settlePosition = ResolveDeathBloodSettlePosition(bloodPosition);
                    Vector3 startPosition = ResolveDeathBloodStartPosition(settlePosition);

                    SpriteRenderer bloodRenderer = deathBloodObject.GetComponent<SpriteRenderer>();
                    ApplyBloodDecalVariant(deathBloodObject.transform, bloodRenderer, null);

                    float targetScale = ResolveBloodDecalScale();
                    float startScale = 0.01f; // Effect of flowing blood growing from a point, visually more impactful than scaling up from a small random value.
                    float endAlpha = ResolveBloodEndAlpha();
                    BloodAgingVisual agingVisual = PrepareBloodAgingVisual(deathBloodObject, bloodRenderer, endAlpha);
                    ApplyFreshBloodVisual(agingVisual, bloodRenderer, 0f, startAgingImmediately: false);
                    InitializeBloodPoolVisualState(deathBloodObject.transform, startPosition, startScale);

                    StartCoroutine(AnimateBloodPool(
                        deathBloodObject.transform,
                        bloodRenderer,
                        agingVisual,
                        startScale,
                        targetScale,
                        settlePosition,
                        endAlpha));
                    RegisterGroundBloodDecal(deathBloodObject, deathBloodSourcePrefab);
                }
            }
        }

        TrySpawnClusterBlood(bloodPosition, bloodPoolPrefab);

        activeDeathVisuals.Enqueue(new DeathVisualEntry(
            corpseObject,
            corpseRenderer,
            deathBloodObject,
            corpseSprite,
            corpseSortingOrder,
            deathBloodSourcePrefab));
    }

    public void PrewarmCorpsePool(int count)
    {
        int targetCount = Mathf.Max(0, count);
        while (corpseVisualPool.Count < targetCount)
            corpseVisualPool.Push(CreateCorpseVisual());
    }

    public void PrewarmBloodPool(int count)
    {
        if (bloodPoolPrewarmPrefab == null)
            return;

        int targetCount = Mathf.Max(0, count);
        Stack<GameObject> pool = GetOrCreateBloodPool(bloodPoolPrewarmPrefab);

        while (pool.Count < targetCount)
        {
            GameObject created = Instantiate(bloodPoolPrewarmPrefab, transform);
            created.SetActive(false);
            pool.Push(created);
        }
    }

    private void PrewarmPools()
    {
        PrewarmCorpsePool(prewarmCorpseCount);
        PrewarmBloodPool(bloodPoolPrewarmCount);
    }

    private void RegisterRecentDeath(Vector3 position)
    {
        float now = Time.time;
        recentDeaths.Enqueue(new RecentDeathEntry(position, now));
        PruneRecentDeaths(now);
    }

    private void PruneRecentDeaths(float now)
    {
        float window = Mathf.Max(0.1f, clusterWindowSeconds);
        while (recentDeaths.Count > 0 && now - recentDeaths.Peek().Time > window)
            recentDeaths.Dequeue();

        int maxEntries = Mathf.Max(16, maxTrackedDeaths * 2);
        while (recentDeaths.Count > maxEntries)
            recentDeaths.Dequeue();
    }

    private void TrySpawnClusterBlood(Vector3 bloodPosition, GameObject fallbackPrefab)
    {
        GameObject sourcePrefab = ResolveBloodSourcePrefab(fallbackPrefab);
        if (sourcePrefab == null)
            return;

        if (Random.value > clusterBloodChance)
            return;

        float now = Time.time;
        PruneRecentDeaths(now);

        float scanRadius = Mathf.Max(0.1f, clusterScanRadius);
        float scanRadiusSq = scanRadius * scanRadius;

        int nearbyDeaths = 0;
        foreach (RecentDeathEntry entry in recentDeaths)
        {
            if ((entry.Position - bloodPosition).sqrMagnitude > scanRadiusSq)
                continue;

            nearbyDeaths++;
        }

        int threshold = Mathf.Max(2, clusterThreshold);
        if (nearbyDeaths < threshold)
            return;

        float density01 = Mathf.Clamp01((nearbyDeaths - threshold + 1f) / Mathf.Max(1f, threshold));
        int spawnCount = Mathf.Clamp(1 + Mathf.FloorToInt((nearbyDeaths - threshold) * 0.5f), 1, 3);

        for (int i = 0; i < spawnCount; i++)
        {
            Vector2 jitter = Random.insideUnitCircle * Mathf.Lerp(0.08f, 0.35f, density01);
            Vector3 spawnPosition = new Vector3(bloodPosition.x + jitter.x, bloodPosition.y + jitter.y - 0.02f, 0f);
            float scale = ResolveRangeValue(clusterBloodScaleRange, 0.1f) * Mathf.Lerp(1f, 1.35f, density01);

            GameObject clusterBlood = AcquireBloodVisual(sourcePrefab);
            if (clusterBlood == null)
                continue;

            SpriteRenderer clusterRenderer = clusterBlood.GetComponent<SpriteRenderer>();
            ApplyBloodDecalVariant(clusterBlood.transform, clusterRenderer, null);
            InitializeImmediateBloodPoolVisualState(clusterBlood.transform, spawnPosition, scale);

            float targetAlpha = ResolveBloodEndAlpha();
            BloodAgingVisual agingVisual = PrepareBloodAgingVisual(clusterBlood, clusterRenderer, targetAlpha);
            ApplyFreshBloodVisual(agingVisual, clusterRenderer, targetAlpha, startAgingImmediately: true);

            RegisterGroundBloodDecal(clusterBlood, sourcePrefab);
        }
    }

    private GameObject ResolveBloodSourcePrefab(GameObject preferredPrefab)
    {
        if (preferredPrefab != null)
            return preferredPrefab;

        return bloodPoolPrewarmPrefab;
    }

    private void RegisterGroundBloodDecal(GameObject bloodObject, GameObject sourcePrefab)
    {
        if (bloodObject == null)
            return;

        DisableTimedAutoReturn(bloodObject);

        activeGroundBlood.Enqueue(new GroundBloodEntry(bloodObject, sourcePrefab));
        EnforceGroundBloodCap();
    }

    private void EnforceGroundBloodCap()
    {
        CompactQueue(activeGroundBlood, entry => entry.BloodObject != null && entry.BloodObject.activeInHierarchy);

        int cap = Mathf.Max(1, maxTrackedGroundBloodDecals);
        while (activeGroundBlood.Count > cap)
        {
            GroundBloodEntry oldest = activeGroundBlood.Dequeue();
            if (oldest.BloodObject == null)
                continue;

            StartCoroutine(FadeOutAndReleaseBloodVisual(oldest.BloodObject, oldest.SourcePrefab));
        }
    }

    private static void DisableTimedAutoReturn(GameObject bloodObject)
    {
        if (bloodObject == null)
            return;

        PooledTimedAutoReturn timedAutoReturn = bloodObject.GetComponent<PooledTimedAutoReturn>();
        if (timedAutoReturn != null)
            timedAutoReturn.enabled = false;
    }

    private Vector3 ResolveDeathBloodSettlePosition(Vector3 basePosition)
    {
        float minX = Mathf.Min(deathBloodSettleHorizontalOffsetRange.x, deathBloodSettleHorizontalOffsetRange.y);
        float maxX = Mathf.Max(deathBloodSettleHorizontalOffsetRange.x, deathBloodSettleHorizontalOffsetRange.y);

        float settleXOffset = UnityEngine.Random.Range(minX, maxX);
        float settleYJitter = ResolveSignedRange(deathBloodFlowVerticalJitterRange) * 0.45f;

        return new Vector3(basePosition.x + settleXOffset, basePosition.y + settleYJitter, 0f);
    }

    private Vector3 ResolveDeathBloodStartPosition(Vector3 settlePosition)
    {
        float flowDistance = ResolveRangeValue(deathBloodFlowDistanceRange, 0f);
        float horizontalDirection = deathBloodFlowRightToLeft ? 1f : -1f;
        float yJitter = ResolveSignedRange(deathBloodFlowVerticalJitterRange);

        return new Vector3(
            settlePosition.x + flowDistance * horizontalDirection,
            settlePosition.y + yJitter,
            0f);
    }

    private static void InitializeBloodPoolVisualState(Transform bloodTransform, Vector3 startPosition, float startScale)
    {
        if (bloodTransform == null)
            return;

        bloodTransform.SetPositionAndRotation(startPosition, Quaternion.identity);
        bloodTransform.localScale = new Vector3(startScale, startScale, 1f);
    }

    private static void InitializeImmediateBloodPoolVisualState(Transform bloodTransform, Vector3 position, float scale)
    {
        if (bloodTransform == null)
            return;

        bloodTransform.SetPositionAndRotation(position, Quaternion.identity);
        bloodTransform.localScale = new Vector3(scale, scale, 1f);
    }

    private BloodAgingVisual PrepareBloodAgingVisual(GameObject bloodObject, SpriteRenderer bloodRenderer, float targetAlpha)
    {
        if (bloodObject == null || bloodRenderer == null)
            return null;

        BloodAgingVisual visual = bloodObject.GetComponent<BloodAgingVisual>();
        if (visual == null)
            visual = bloodObject.AddComponent<BloodAgingVisual>();

        float brightness = ResolveRangeValue(bloodTintBrightnessJitterRange, 0.1f);
        BloodAgingProfile profile = new BloodAgingProfile(
            ResolveBloodTint(freshBloodTint, brightness),
            ResolveBloodTint(driedBloodTint, brightness),
            ResolveBloodTint(staleBloodTint, brightness),
            targetAlpha,
            ResolveRangeValue(bloodDryDurationRange, 0.1f),
            ResolveRangeValue(bloodStaleDurationRange, 0.1f),
            ResolveRangeValue(bloodLateFadeDelayRange, 0f),
            ResolveRangeValue(bloodLateFadeDurationRange, 0.1f),
            ResolveRangeValue(bloodLateFadeAlphaMultiplierRange, 0f));

        visual.Configure(bloodRenderer, profile);
        return visual;
    }

    private void ApplyFreshBloodVisual(BloodAgingVisual agingVisual, SpriteRenderer bloodRenderer, float alpha, bool startAgingImmediately)
    {
        if (agingVisual != null)
        {
            agingVisual.ApplySpawnAlpha(alpha);
            if (startAgingImmediately)
                agingVisual.ResumeAging();
            else
                agingVisual.SuspendAging();

            return;
        }

        if (bloodRenderer == null)
            return;

        Color color = freshBloodTint;
        color.a = Mathf.Clamp01(alpha);
        bloodRenderer.color = color;
    }

    private float ResolveBloodEndAlpha()
    {
        return Mathf.Clamp01(ResolveRangeValue(deathBloodFlowEndAlphaRange, 0f));
    }

    private static Color ResolveBloodTint(Color source, float brightness)
    {
        float factor = Mathf.Max(0f, brightness);
        return new Color(
            Mathf.Clamp01(source.r * factor),
            Mathf.Clamp01(source.g * factor),
            Mathf.Clamp01(source.b * factor),
            1f);
    }

    private GameObject AcquireCorpseVisual()
    {
        while (corpseVisualPool.Count > 0)
        {
            GameObject pooled = corpseVisualPool.Pop();
            if (pooled != null)
                return pooled;
        }

        return CreateCorpseVisual();
    }

    private GameObject CreateCorpseVisual()
    {
        GameObject corpseObject = new GameObject("CorpseVisual");
        corpseObject.transform.SetParent(transform, false);
        corpseObject.AddComponent<SpriteRenderer>();
        corpseObject.SetActive(false);
        return corpseObject;
    }

    private void ReleaseCorpseVisual(GameObject corpseObject)
    {
        if (corpseObject == null)
            return;

        SpriteRenderer corpseRenderer = corpseObject.GetComponent<SpriteRenderer>();
        if (corpseRenderer != null)
        {
            corpseRenderer.sprite = null;
            corpseRenderer.color = Color.white;
            corpseRenderer.flipX = false;
            corpseRenderer.sortingOrder = 0;
        }

        corpseObject.SetActive(false);
        corpseObject.transform.SetParent(transform, false);
        corpseVisualPool.Push(corpseObject);
    }

    private GameObject AcquireBloodVisual(GameObject bloodPrefab)
    {
        Stack<GameObject> pool = GetOrCreateBloodPool(bloodPrefab);

        while (pool.Count > 0)
        {
            GameObject pooled = pool.Pop();
            if (pooled != null)
            {
                SpriteRenderer pooledRenderer = pooled.GetComponent<SpriteRenderer>();
                if (pooledRenderer != null)
                    pooledRenderer.color = Color.white;

                BloodAgingVisual pooledAging = pooled.GetComponent<BloodAgingVisual>();
                if (pooledAging != null)
                    pooledAging.ResetVisualState();

                pooled.SetActive(true);
                return pooled;
            }
        }

        GameObject created = Instantiate(bloodPrefab, transform);
        created.SetActive(true);
        return created;
    }

    private Stack<GameObject> GetOrCreateBloodPool(GameObject bloodPrefab)
    {
        if (bloodPoolByPrefab.TryGetValue(bloodPrefab, out Stack<GameObject> existingPool))
            return existingPool;

        Stack<GameObject> newPool = new Stack<GameObject>(16);
        bloodPoolByPrefab[bloodPrefab] = newPool;
        return newPool;
    }

    private void ReleaseBloodVisual(GameObject bloodObject, GameObject sourcePrefab)
    {
        if (bloodObject == null)
            return;

        BloodAgingVisual agingVisual = bloodObject.GetComponent<BloodAgingVisual>();
        if (agingVisual != null)
            agingVisual.ResetVisualState();

        if (sourcePrefab == null)
        {
            Destroy(bloodObject);
            return;
        }

        Stack<GameObject> pool = GetOrCreateBloodPool(sourcePrefab);

        bloodObject.SetActive(false);
        bloodObject.transform.SetParent(transform, false);
        pool.Push(bloodObject);
    }

    private void EnforceActiveDeathCap()
    {
        int cap = Mathf.Max(1, maxTrackedDeaths);
        while (activeDeathVisuals.Count >= cap)
        {
            DeathVisualEntry oldest = activeDeathVisuals.Dequeue();
            ConvertOverflowToRemains(oldest);
        }
    }

    private void ConvertOverflowToRemains(DeathVisualEntry entry)
    {
        if (entry.BloodObject != null)
            StartCoroutine(FadeOutAndReleaseBloodVisual(entry.BloodObject, entry.BloodSourcePrefab));

        if (entry.CorpseObject == null || entry.CorpseRenderer == null)
            return;

        Sprite remainsSprite = ResolveRemainsSprite(entry.SourceCorpseSprite);

        activeRemains.Enqueue(new RemainsVisualEntry(entry.CorpseObject, entry.CorpseRenderer));
        EnforceRemainsCap();

        StartCoroutine(TransitionOverflowCorpseToRemains(entry.CorpseObject.transform, entry.CorpseRenderer, remainsSprite, entry.SortingOrder));
    }

    private void EnforceRemainsCap()
    {
        CompactQueue(activeRemains, entry => entry.CorpseObject != null && entry.CorpseObject.activeInHierarchy);

        int cap = Mathf.Max(1, maxTrackedRemains);
        while (activeRemains.Count > cap)
        {
            RemainsVisualEntry oldest = activeRemains.Dequeue();
            ReleaseCorpseVisual(oldest.CorpseObject);
        }
    }

    private static void CompactQueue<T>(Queue<T> queue, Func<T, bool> keepPredicate)
    {
        int count = queue.Count;
        if (count == 0)
            return;

        for (int i = 0; i < count; i++)
        {
            T item = queue.Dequeue();
            if (keepPredicate(item))
                queue.Enqueue(item);
        }
    }

    private IEnumerator FadeOutAndReleaseBloodVisual(GameObject bloodObject, GameObject sourcePrefab)
    {
        if (bloodObject == null || !bloodObject.activeInHierarchy)
            yield break;

        Transform bloodTransform = bloodObject.transform;
        SpriteRenderer renderer = bloodObject.GetComponent<SpriteRenderer>();
        BloodAgingVisual agingVisual = bloodObject.GetComponent<BloodAgingVisual>();
        if (agingVisual != null)
            agingVisual.SuspendAging();

        Vector3 startScale = bloodTransform.localScale;
        Color startColor = Color.white;
        if (agingVisual != null)
            startColor = agingVisual.CurrentColor;
        else if (renderer != null)
            startColor = renderer.color;

        float duration = Mathf.Max(0.05f, overflowBloodFadeDuration);
        float t = 0f;

        while (t < duration)
        {
            if (bloodObject == null || !bloodObject.activeInHierarchy)
                yield break;

            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / duration);
            k = k * k * (3f - 2f * k);

            bloodTransform.localScale = Vector3.Lerp(startScale, Vector3.zero, k);

            if (renderer != null)
            {
                Color color = startColor;
                color.a = Mathf.Lerp(startColor.a, 0f, k);
                if (agingVisual != null)
                    agingVisual.ApplyOverrideColor(color);
                else
                    renderer.color = color;
            }

            yield return null;
        }

        if (bloodObject != null)
            ReleaseBloodVisual(bloodObject, sourcePrefab);
    }

    private Sprite ResolveRemainsSprite(Sprite sourceSprite)
    {
        if (sourceSprite == null)
            return null;

        if (remainsVariantLookup.TryGetValue(sourceSprite, out Sprite remainsSprite))
            return remainsSprite;

        return sourceSprite;
    }

    private void RebuildRemainsVariantLookup()
    {
        remainsVariantLookup.Clear();
        for (int i = 0; i < remainsVariants.Count; i++)
        {
            Sprite sourceSprite = remainsVariants[i].SourceSprite;
            Sprite remainsSprite = remainsVariants[i].RemainsSprite;
            if (sourceSprite == null || remainsSprite == null || remainsVariantLookup.ContainsKey(sourceSprite))
                continue;

            remainsVariantLookup.Add(sourceSprite, remainsSprite);
        }
    }

    private IEnumerator TransitionOverflowCorpseToRemains(Transform corpseTransform, SpriteRenderer corpseRenderer, Sprite remainsSprite, int corpseSortingOrder)
    {
        if (corpseTransform == null || corpseRenderer == null || !corpseRenderer.gameObject.activeInHierarchy)
            yield break;

        float duration = Mathf.Max(0.05f, overflowToRemainsTransitionDuration);

        if (remainsSprite != null)
            corpseRenderer.sprite = remainsSprite;

        float t = 0f;
        while (t < duration)
        {
            if (corpseTransform == null || corpseRenderer == null || !corpseRenderer.gameObject.activeInHierarchy)
                yield break;

            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / duration);
            k = k * k * (3f - 2f * k);

            yield return null;
        }

        if (corpseTransform == null || corpseRenderer == null || !corpseRenderer.gameObject.activeInHierarchy)
            yield break;

        corpseRenderer.sortingOrder = corpseSortingOrder - 1;
    }

    private void ApplyBloodDecalVariant(Transform bloodTransform, SpriteRenderer bloodRenderer, Sprite[] overrideVariants)
    {
        if (bloodTransform == null || bloodRenderer == null)
            return;

        Sprite[] variants = overrideVariants != null && overrideVariants.Length > 0
            ? overrideVariants
            : bloodDecalVariants;

        if (variants != null && variants.Length > 0)
        {
            Sprite variant = variants[UnityEngine.Random.Range(0, variants.Length)];
            if (variant != null)
                bloodRenderer.sprite = variant;
        }

        // Decals are authored with desired orientation.
        bloodTransform.rotation = Quaternion.identity;
    }

    private float ResolveBloodDecalScale()
    {
        return ResolveRangeValue(bloodDecalScaleRange, 0.05f);
    }

    private static float ResolveRangeValue(Vector2 range, float minClamp)
    {
        float min = Mathf.Max(minClamp, Mathf.Min(range.x, range.y));
        float max = Mathf.Max(min, Mathf.Max(range.x, range.y));
        return UnityEngine.Random.Range(min, max);
    }

    private static Vector2 SanitizeRange(Vector2 range, float minClamp)
    {
        float min = Mathf.Max(minClamp, Mathf.Min(range.x, range.y));
        float max = Mathf.Max(min, Mathf.Max(range.x, range.y));
        return new Vector2(min, max);
    }

    private static Vector2 SanitizeRange(Vector2 range, float minClamp, float maxClamp)
    {
        float clampedMax = Mathf.Max(minClamp, maxClamp);
        Vector2 sanitized = SanitizeRange(range, minClamp);
        float min = Mathf.Clamp(sanitized.x, minClamp, clampedMax);
        float max = Mathf.Clamp(sanitized.y, min, clampedMax);
        return new Vector2(min, max);
    }

    private static float ResolveSignedRange(Vector2 range)
    {
        float min = Mathf.Min(range.x, range.y);
        float max = Mathf.Max(range.x, range.y);
        return UnityEngine.Random.Range(min, max);
    }

    private IEnumerator AnimateBloodPool(
        Transform blood,
        SpriteRenderer spriteRenderer,
        BloodAgingVisual agingVisual,
        float startScaleUniform,
        float targetScale,
        Vector3 settlePosition,
        float endAlpha)
    {
        float startDelay = ResolveRangeValue(deathBloodFlowStartDelayRange, 0f);
        float delayed = 0f;
        while (delayed < startDelay)
        {
            if (blood == null)
                yield break;

            delayed += Time.deltaTime;
            yield return null;
        }

        if (blood == null)
            yield break;

        Vector3 startScale = new Vector3(startScaleUniform, startScaleUniform, 1f);
        Vector3 endScale = new Vector3(targetScale, targetScale, 1f);

        float duration = Mathf.Lerp(0.25f, 0.95f, Mathf.InverseLerp(0.35f, 1.05f, targetScale));
        duration *= ResolveRangeValue(deathBloodFlowDurationMultiplierRange, 0.1f);

        float startAlpha = 0f;
        ApplyFreshBloodVisual(agingVisual, spriteRenderer, startAlpha, startAgingImmediately: false);

        Vector3 startPosition = blood.position;
        blood.localScale = startScale;

        float t = 0f;

        float growPhase = duration * 0.75f;
        while (t < growPhase)
        {
            if (blood == null || !blood.gameObject.activeInHierarchy)
                yield break;

            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / growPhase);
            k = k * k * (3f - 2f * k);

            blood.position = Vector3.Lerp(startPosition, settlePosition, k);
            blood.localScale = Vector3.Lerp(startScale, endScale, k);

            if (spriteRenderer != null)
            {
                float alpha = Mathf.Lerp(startAlpha, endAlpha, k);
                if (agingVisual != null)
                    agingVisual.ApplySpawnAlpha(alpha);
                else
                {
                    Color color = freshBloodTint;
                    color.a = alpha;
                    spriteRenderer.color = color;
                }
            }

            yield return null;
        }

        if (blood == null || !blood.gameObject.activeInHierarchy)
            yield break;

        blood.position = settlePosition;
        blood.localScale = endScale;

        ApplyFreshBloodVisual(agingVisual, spriteRenderer, endAlpha, startAgingImmediately: true);
    }

    [Serializable]
    private struct RemainsVariant
    {
        [SerializeField] public Sprite SourceSprite;
        [SerializeField] public Sprite RemainsSprite;
    }

    internal readonly struct BloodAgingProfile
    {
        public readonly Color FreshTint;
        public readonly Color DriedTint;
        public readonly Color StaleTint;
        public readonly float MaxAlpha;
        public readonly float DryDuration;
        public readonly float StaleDuration;
        public readonly float LateFadeDelay;
        public readonly float LateFadeDuration;
        public readonly float LateFadeAlphaMultiplier;

        public BloodAgingProfile(
            Color freshTint,
            Color driedTint,
            Color staleTint,
            float maxAlpha,
            float dryDuration,
            float staleDuration,
            float lateFadeDelay,
            float lateFadeDuration,
            float lateFadeAlphaMultiplier)
        {
            FreshTint = freshTint;
            DriedTint = driedTint;
            StaleTint = staleTint;
            MaxAlpha = Mathf.Clamp01(maxAlpha);
            DryDuration = Mathf.Max(0.1f, dryDuration);
            StaleDuration = Mathf.Max(0.1f, staleDuration);
            LateFadeDelay = Mathf.Max(0f, lateFadeDelay);
            LateFadeDuration = Mathf.Max(0.1f, lateFadeDuration);
            LateFadeAlphaMultiplier = Mathf.Clamp01(lateFadeAlphaMultiplier);
        }
    }

    private readonly struct DeathVisualEntry
    {
        public readonly GameObject CorpseObject;
        public readonly SpriteRenderer CorpseRenderer;
        public readonly GameObject BloodObject;
        public readonly Sprite SourceCorpseSprite;
        public readonly int SortingOrder;
        public readonly GameObject BloodSourcePrefab;

        public DeathVisualEntry(GameObject corpseObject, SpriteRenderer corpseRenderer, GameObject bloodObject, Sprite sourceCorpseSprite, int sortingOrder, GameObject bloodSourcePrefab)
        {
            CorpseObject = corpseObject;
            CorpseRenderer = corpseRenderer;
            BloodObject = bloodObject;
            SourceCorpseSprite = sourceCorpseSprite;
            SortingOrder = sortingOrder;
            BloodSourcePrefab = bloodSourcePrefab;
        }
    }

    private readonly struct RemainsVisualEntry
    {
        public readonly GameObject CorpseObject;
        public readonly SpriteRenderer CorpseRenderer;

        public RemainsVisualEntry(GameObject corpseObject, SpriteRenderer corpseRenderer)
        {
            CorpseObject = corpseObject;
            CorpseRenderer = corpseRenderer;
        }
    }

    private readonly struct GroundBloodEntry
    {
        public readonly GameObject BloodObject;
        public readonly GameObject SourcePrefab;

        public GroundBloodEntry(GameObject bloodObject, GameObject sourcePrefab)
        {
            BloodObject = bloodObject;
            SourcePrefab = sourcePrefab;
        }
    }

    private readonly struct RecentDeathEntry
    {
        public readonly Vector3 Position;
        public readonly float Time;

        public RecentDeathEntry(Vector3 position, float time)
        {
            Position = position;
            Time = time;
        }
    }
}

internal sealed class BloodAgingVisual : MonoBehaviour
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    private SpriteRenderer spriteRenderer;
    private MaterialPropertyBlock propertyBlock;
    private bool canUsePropertyBlock;
    private bool hasBaseColorProperty;
    private bool hasColorProperty;

    private Color freshTint = Color.white;
    private Color driedTint = Color.white;
    private Color staleTint = Color.white;
    private float maxAlpha = 1f;
    private float dryDuration = 6f;
    private float staleDuration = 10f;
    private float lateFadeDelay = 12f;
    private float lateFadeDuration = 8f;
    private float lateFadeAlphaMultiplier = 0.6f;

    private float ageTimer;
    private bool agingActive;
    private bool suspended;
    private Color currentColor = Color.white;

    public Color CurrentColor => currentColor;

    public void Configure(SpriteRenderer targetRenderer, EnemyDeathVisualManager.BloodAgingProfile profile)
    {
        spriteRenderer = targetRenderer;
        EnsureRendererCompatibility();

        freshTint = profile.FreshTint;
        driedTint = profile.DriedTint;
        staleTint = profile.StaleTint;
        maxAlpha = Mathf.Clamp01(profile.MaxAlpha);
        dryDuration = Mathf.Max(0.1f, profile.DryDuration);
        staleDuration = Mathf.Max(0.1f, profile.StaleDuration);
        lateFadeDelay = Mathf.Max(0f, profile.LateFadeDelay);
        lateFadeDuration = Mathf.Max(0.1f, profile.LateFadeDuration);
        lateFadeAlphaMultiplier = Mathf.Clamp01(profile.LateFadeAlphaMultiplier);

        ageTimer = 0f;
        agingActive = false;
        suspended = false;
        ApplyColor(new Color(freshTint.r, freshTint.g, freshTint.b, 0f));
    }

    public void ApplySpawnAlpha(float alpha)
    {
        Color c = freshTint;
        c.a = Mathf.Clamp01(alpha);
        ApplyColor(c);
    }

    public void ResumeAging()
    {
        agingActive = true;
        suspended = false;
    }

    public void SuspendAging()
    {
        suspended = true;
    }

    public void ApplyOverrideColor(Color color)
    {
        suspended = true;
        ApplyColor(color);
    }

    public void ResetVisualState()
    {
        agingActive = false;
        suspended = false;
        ageTimer = 0f;
        currentColor = Color.white;

        if (spriteRenderer == null)
            return;

        if (canUsePropertyBlock)
        {
            propertyBlock ??= new MaterialPropertyBlock();
            propertyBlock.Clear();
            spriteRenderer.SetPropertyBlock(propertyBlock);
        }

        spriteRenderer.color = Color.white;
    }

    private void Update()
    {
        if (!agingActive || suspended || spriteRenderer == null || !spriteRenderer.enabled || !spriteRenderer.gameObject.activeInHierarchy)
            return;

        ageTimer += Time.deltaTime;

        Color tint;
        if (ageTimer <= dryDuration)
        {
            float dryT = Mathf.Clamp01(ageTimer / dryDuration);
            tint = Color.Lerp(freshTint, driedTint, dryT);
        }
        else
        {
            float staleT = Mathf.Clamp01((ageTimer - dryDuration) / staleDuration);
            tint = Color.Lerp(driedTint, staleTint, staleT);
        }

        float alpha = maxAlpha;
        if (ageTimer > lateFadeDelay)
        {
            float fadeT = Mathf.Clamp01((ageTimer - lateFadeDelay) / lateFadeDuration);
            alpha = Mathf.Lerp(maxAlpha, maxAlpha * lateFadeAlphaMultiplier, fadeT);
        }

        tint.a = alpha;
        ApplyColor(tint);
    }

    private void EnsureRendererCompatibility()
    {
        if (spriteRenderer == null)
        {
            canUsePropertyBlock = false;
            hasBaseColorProperty = false;
            hasColorProperty = false;
            return;
        }

        Material sharedMaterial = spriteRenderer.sharedMaterial;
        hasBaseColorProperty = sharedMaterial != null && sharedMaterial.HasProperty(BaseColorId);
        hasColorProperty = sharedMaterial != null && sharedMaterial.HasProperty(ColorId);
        canUsePropertyBlock = hasBaseColorProperty || hasColorProperty;

        if (!canUsePropertyBlock)
            return;

        propertyBlock ??= new MaterialPropertyBlock();
    }

    private void ApplyColor(Color color)
    {
        currentColor = color;

        if (spriteRenderer == null)
            return;

        if (canUsePropertyBlock)
        {
            spriteRenderer.GetPropertyBlock(propertyBlock);
            if (hasBaseColorProperty)
                propertyBlock.SetColor(BaseColorId, color);
            if (hasColorProperty)
                propertyBlock.SetColor(ColorId, color);
            spriteRenderer.SetPropertyBlock(propertyBlock);
        }
        else
        {
            spriteRenderer.color = color;
        }
    }
}





