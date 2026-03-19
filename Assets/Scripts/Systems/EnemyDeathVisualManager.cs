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
    [Tooltip("Final alpha range of spawned blood decals (lower = more transparent).")]
    [SerializeField] private Vector2 deathBloodFlowEndAlphaRange = new Vector2(0.78f, 0.96f);

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
                deathBloodSpawnChance = 0.45f;
                clusterBloodChance = 0.2f;
                clusterWindowSeconds = 1.2f;
                clusterScanRadius = 1.0f;
                clusterThreshold = 6;
                bloodDecalScaleRange = new Vector2(0.16f, 0.25f);
                clusterBloodScaleRange = new Vector2(0.2f, 0.32f);
                deathBloodFlowDistanceRange = new Vector2(0.04f, 0.10f);
                deathBloodFlowVerticalJitterRange = new Vector2(-0.015f, 0.02f);
                deathBloodSettleHorizontalOffsetRange = new Vector2(-0.04f, 0.015f);
                deathBloodFlowDurationMultiplierRange = new Vector2(1.6f, 2.6f);
                deathBloodFlowStartDelayRange = new Vector2(0f, 0.06f);
                deathBloodFlowEndAlphaRange = new Vector2(0.55f, 0.75f);
                deathBloodFlowRightToLeft = true;
                break;

            case VisualTuningPreset.Cinematic:
                maxTrackedDeaths = 100;
                maxTrackedRemains = 180;
                maxTrackedGroundBloodDecals = 220;
                overflowToRemainsTransitionDuration = 1f;
                overflowBloodFadeDuration = 1.25f;
                deathBloodSpawnChance = 0.75f;
                clusterBloodChance = 0.6f;
                clusterWindowSeconds = 1.8f;
                clusterScanRadius = 1.3f;
                clusterThreshold = 5;
                bloodDecalScaleRange = new Vector2(0.2f, 0.32f);
                clusterBloodScaleRange = new Vector2(0.25f, 0.45f);
                deathBloodFlowDistanceRange = new Vector2(0.08f, 0.18f);
                deathBloodFlowVerticalJitterRange = new Vector2(-0.02f, 0.03f);
                deathBloodSettleHorizontalOffsetRange = new Vector2(-0.06f, 0.02f);
                deathBloodFlowDurationMultiplierRange = new Vector2(2.2f, 3.8f);
                deathBloodFlowStartDelayRange = new Vector2(0f, 0.12f);
                deathBloodFlowEndAlphaRange = new Vector2(0.72f, 0.9f);
                deathBloodFlowRightToLeft = true;
                break;

            case VisualTuningPreset.Gore:
                maxTrackedDeaths = 130;
                maxTrackedRemains = 260;
                maxTrackedGroundBloodDecals = 380;
                overflowToRemainsTransitionDuration = 1.15f;
                overflowBloodFadeDuration = 1.8f;
                deathBloodSpawnChance = 1f;
                clusterBloodChance = 1f;
                clusterWindowSeconds = 2.8f;
                clusterScanRadius = 1.75f;
                clusterThreshold = 3;
                bloodDecalScaleRange = new Vector2(0.24f, 0.38f);
                clusterBloodScaleRange = new Vector2(0.32f, 0.6f);
                deathBloodFlowDistanceRange = new Vector2(0.12f, 0.26f);
                deathBloodFlowVerticalJitterRange = new Vector2(-0.03f, 0.05f);
                deathBloodSettleHorizontalOffsetRange = new Vector2(-0.1f, 0.02f);
                deathBloodFlowDurationMultiplierRange = new Vector2(2.8f, 5.2f);
                deathBloodFlowStartDelayRange = new Vector2(0f, 0.16f);
                deathBloodFlowEndAlphaRange = new Vector2(0.85f, 1f);
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
        InitializeImmediateBloodPoolVisualState(bloodObject.transform, bloodRenderer, worldPosition, Mathf.Max(0.05f, uniformScale));

        RegisterGroundBloodDecal(bloodObject, decalPrefab);
        return true;
    }

    public void SpawnDeathVisuals(
        Sprite corpseSprite,
        bool corpseFlipX,
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

        if (corpseSprite != null)
        {
            corpseObject = AcquireCorpseVisual();
            corpseObject.transform.SetPositionAndRotation(corpsePosition, Quaternion.identity);
            corpseObject.transform.localScale = corpseScale;

            corpseRenderer = corpseObject.GetComponent<SpriteRenderer>();
            corpseRenderer.color = Color.white;
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
                    float startScale = ResolveDeathBloodStartScale(targetScale);
                    InitializeBloodPoolVisualState(deathBloodObject.transform, bloodRenderer, startPosition, startScale);

                    StartCoroutine(AnimateBloodPool(deathBloodObject.transform, bloodRenderer, startScale, targetScale, settlePosition));
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
            InitializeImmediateBloodPoolVisualState(clusterBlood.transform, clusterRenderer, spawnPosition, scale);

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

    private float ResolveDeathBloodStartScale(float targetScale)
    {
        float minScale = Mathf.Min(0.15f, targetScale * 0.35f);
        float maxScale = Mathf.Min(targetScale, Mathf.Max(minScale, 0.2f));
        return UnityEngine.Random.Range(minScale, maxScale);
    }

    private static void InitializeBloodPoolVisualState(Transform bloodTransform, SpriteRenderer bloodRenderer, Vector3 startPosition, float startScale)
    {
        if (bloodTransform == null)
            return;

        bloodTransform.SetPositionAndRotation(startPosition, Quaternion.identity);
        bloodTransform.localScale = new Vector3(startScale, startScale, 1f);

        if (bloodRenderer == null)
            return;

        Color color = bloodRenderer.color;
        color.a = 0f;
        bloodRenderer.color = color;
    }

    private void InitializeImmediateBloodPoolVisualState(Transform bloodTransform, SpriteRenderer bloodRenderer, Vector3 position, float scale)
    {
        if (bloodTransform == null)
            return;

        bloodTransform.SetPositionAndRotation(position, Quaternion.identity);
        bloodTransform.localScale = new Vector3(scale, scale, 1f);

        if (bloodRenderer == null)
            return;

        Color color = bloodRenderer.color;
        color.a = Mathf.Clamp01(ResolveRangeValue(deathBloodFlowEndAlphaRange, 0f));
        color.r *= 0.9f;
        color.g *= 0.85f;
        color.b *= 0.85f;
        bloodRenderer.color = color;
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

        Vector3 startScale = bloodTransform.localScale;
        Color startColor = renderer != null ? renderer.color : Color.white;

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

    private static float ResolveSignedRange(Vector2 range)
    {
        float min = Mathf.Min(range.x, range.y);
        float max = Mathf.Max(range.x, range.y);
        return UnityEngine.Random.Range(min, max);
    }

    private IEnumerator AnimateBloodPool(Transform blood, SpriteRenderer spriteRenderer, float startScaleUniform, float targetScale, Vector3 settlePosition)
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
        float endAlpha = Mathf.Clamp01(ResolveRangeValue(deathBloodFlowEndAlphaRange, 0f));

        if (spriteRenderer != null)
        {
            Color color = spriteRenderer.color;
            color.a = startAlpha;
            spriteRenderer.color = color;
        }

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
                Color color = spriteRenderer.color;
                color.a = Mathf.Lerp(startAlpha, endAlpha, k);
                spriteRenderer.color = color;
            }

            yield return null;
        }

        if (blood == null || !blood.gameObject.activeInHierarchy)
            yield break;

        blood.position = settlePosition;
        blood.localScale = endScale;

        if (spriteRenderer != null)
        {
            Color color = spriteRenderer.color;
            color.r *= 0.9f;
            color.g *= 0.85f;
            color.b *= 0.85f;
            spriteRenderer.color = color;
        }
    }

    [Serializable]
    private struct RemainsVariant
    {
        [SerializeField] public Sprite SourceSprite;
        [SerializeField] public Sprite RemainsSprite;
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





