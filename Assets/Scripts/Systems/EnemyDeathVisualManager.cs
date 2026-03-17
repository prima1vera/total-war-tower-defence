using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EnemyDeathVisualManager : MonoBehaviour
{
    private static EnemyDeathVisualManager instance;
    private static bool missingInstanceLogged;

    [SerializeField] private int maxTrackedDeaths = 80;
    [SerializeField, Min(0.05f)] private float overflowToRemainsTransitionDuration = 0.6f;
    [SerializeField, Min(1f)] private float remainsLifetimeAfterOverflow = 60f;
    [SerializeField, Min(0.05f)] private float overflowBloodFadeDuration = 1.25f;
    [SerializeField] private List<RemainsVariant> remainsVariants = new List<RemainsVariant>(4);
    [SerializeField, Min(0)] private int prewarmCorpseCount = 32;
    [SerializeField] private GameObject bloodPoolPrewarmPrefab;
    [SerializeField, Min(0)] private int bloodPoolPrewarmCount = 24;

    [Header("Blood Variants")]
    [SerializeField] private Sprite[] bloodDecalVariants;
    [SerializeField] private Vector2 bloodDecalScaleRange = new Vector2(0.22f, 0.36f);

    [Header("Death Blood Behavior")]
    [SerializeField, Range(0f, 1f)] private float deathBloodSpawnChance = 0.35f;
    [SerializeField] private GameObject clusterBloodPrefab;
    [SerializeField, Range(0f, 1f)] private float clusterBloodChance = 0.35f;
    [SerializeField, Min(0.1f)] private float clusterWindowSeconds = 2.5f;
    [SerializeField, Min(0.1f)] private float clusterScanRadius = 1.35f;
    [SerializeField, Min(2)] private int clusterThreshold = 4;
    [SerializeField] private Vector2 clusterBloodScaleRange = new Vector2(0.28f, 0.45f);
    [SerializeField] private Vector2 clusterBloodLifetimeRange = new Vector2(20f, 38f);

    [Header("Death Blood Flow")]
    [SerializeField] private bool deathBloodFlowRightToLeft = true;
    [SerializeField] private Vector2 deathBloodFlowDistanceRange = new Vector2(0.08f, 0.18f);
    [SerializeField] private Vector2 deathBloodFlowVerticalJitterRange = new Vector2(-0.02f, 0.03f);
    [SerializeField] private Vector2 deathBloodSettleHorizontalOffsetRange = new Vector2(-0.06f, 0.02f);

    private readonly Queue<DeathVisualEntry> activeVisuals = new Queue<DeathVisualEntry>(80);
    private readonly Dictionary<Sprite, Sprite> remainsVariantLookup = new Dictionary<Sprite, Sprite>(8);
    private readonly Dictionary<GameObject, Stack<GameObject>> bloodPoolByPrefab = new Dictionary<GameObject, Stack<GameObject>>(4);
    private readonly Stack<GameObject> corpseVisualPool = new Stack<GameObject>(64);
    private readonly Queue<RecentDeathEntry> recentDeaths = new Queue<RecentDeathEntry>(64);

    private WaitForSeconds cachedRemainsLifetimeWait;
    private float cachedRemainsLifetime = -1f;

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
        RebuildRemainsVariantLookup();
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

        EnforceCap();
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

        GameObject bloodObject = null;
        bool shouldSpawnDeathBlood = bloodPoolPrefab != null && Random.value <= deathBloodSpawnChance;
        if (shouldSpawnDeathBlood)
        {
            bloodObject = AcquireBloodVisual(bloodPoolPrefab);

            Vector3 settlePosition = ResolveDeathBloodSettlePosition(bloodPosition);
            Vector3 startPosition = ResolveDeathBloodStartPosition(settlePosition);
            bloodObject.transform.SetPositionAndRotation(startPosition, Quaternion.identity);

            SpriteRenderer bloodRenderer = bloodObject.GetComponent<SpriteRenderer>();
            ApplyBloodDecalVariant(bloodObject.transform, bloodRenderer);

            float targetScale = ResolveBloodDecalScale();
            StartCoroutine(AnimateBloodPool(bloodObject.transform, bloodRenderer, targetScale, settlePosition));
        }

        TrySpawnClusterBlood(bloodPosition, bloodPoolPrefab);

        activeVisuals.Enqueue(new DeathVisualEntry(corpseObject, corpseRenderer, bloodObject, corpseSprite, corpseSortingOrder, bloodPoolPrefab));
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
        GameObject sourcePrefab = clusterBloodPrefab != null ? clusterBloodPrefab : fallbackPrefab;
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

        if (!VfxPool.TryGetInstance(out VfxPool vfxPool))
            return;

        float density01 = Mathf.Clamp01((nearbyDeaths - threshold + 1f) / Mathf.Max(1f, threshold));
        int spawnCount = Mathf.Clamp(1 + Mathf.FloorToInt((nearbyDeaths - threshold) * 0.5f), 1, 3);

        for (int i = 0; i < spawnCount; i++)
        {
            Vector2 jitter = Random.insideUnitCircle * Mathf.Lerp(0.08f, 0.35f, density01);
            Vector3 spawnPosition = new Vector3(bloodPosition.x + jitter.x, bloodPosition.y + jitter.y - 0.02f, 0f);

            float scale = ResolveRangeValue(clusterBloodScaleRange, 0.1f) * Mathf.Lerp(1f, 1.35f, density01);
            GameObject clusterBlood = vfxPool.Spawn(sourcePrefab, spawnPosition, Quaternion.identity, new Vector3(scale, scale, 1f));
            if (clusterBlood == null)
                continue;

            SpriteRenderer clusterRenderer = clusterBlood.GetComponent<SpriteRenderer>();
            ApplyBloodDecalVariant(clusterBlood.transform, clusterRenderer);
            ArmClusterBloodLifetime(clusterBlood, Mathf.Lerp(1f, 1.45f, density01));
        }
    }

    private void ArmClusterBloodLifetime(GameObject clusterBlood, float lifetimeMultiplier)
    {
        if (clusterBlood == null)
            return;

        float lifetime = ResolveRangeValue(clusterBloodLifetimeRange, 0.2f) * Mathf.Max(0.1f, lifetimeMultiplier);
        PooledTimedAutoReturn timedAutoReturn = clusterBlood.GetComponent<PooledTimedAutoReturn>();

        if (timedAutoReturn != null)
        {
            timedAutoReturn.enabled = true;
            timedAutoReturn.Arm(lifetime);
            return;
        }

        if (VfxPool.TryGetInstance(out VfxPool vfxPool))
            vfxPool.Release(clusterBlood);
        else
            Destroy(clusterBlood);
    }

    private Vector3 ResolveDeathBloodSettlePosition(Vector3 basePosition)
    {
        float minX = Mathf.Min(deathBloodSettleHorizontalOffsetRange.x, deathBloodSettleHorizontalOffsetRange.y);
        float maxX = Mathf.Max(deathBloodSettleHorizontalOffsetRange.x, deathBloodSettleHorizontalOffsetRange.y);

        float settleXOffset = Random.Range(minX, maxX);
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

    // Overflow to remains: when tracked death visuals reach cap, the oldest corpse is removed
    // from the active queue and converted into a faded remains visual (instead of instant destroy).
    private void EnforceCap()
    {
        int cap = Mathf.Max(1, maxTrackedDeaths);
        while (activeVisuals.Count >= cap)
        {
            DeathVisualEntry oldest = activeVisuals.Dequeue();
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
        StartCoroutine(TransitionOverflowCorpseToRemains(entry.CorpseObject.transform, entry.CorpseRenderer, remainsSprite, entry.SortingOrder));
    }

    private IEnumerator FadeOutAndReleaseBloodVisual(GameObject bloodObject, GameObject sourcePrefab)
    {
        if (bloodObject == null)
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

    private WaitForSeconds GetRemainsLifetimeWait()
    {
        float remainsLifetime = Mathf.Max(1f, remainsLifetimeAfterOverflow);
        if (cachedRemainsLifetimeWait == null || !Mathf.Approximately(cachedRemainsLifetime, remainsLifetime))
        {
            cachedRemainsLifetime = remainsLifetime;
            cachedRemainsLifetimeWait = new WaitForSeconds(remainsLifetime);
        }

        return cachedRemainsLifetimeWait;
    }

    private IEnumerator TransitionOverflowCorpseToRemains(Transform corpseTransform, SpriteRenderer corpseRenderer, Sprite remainsSprite, int corpseSortingOrder)
    {
        if (corpseTransform == null || corpseRenderer == null)
            yield break;

        float duration = Mathf.Max(0.05f, overflowToRemainsTransitionDuration);

        if (remainsSprite != null)
            corpseRenderer.sprite = remainsSprite;

        float t = 0f;
        while (t < duration)
        {
            if (corpseTransform == null || corpseRenderer == null)
                yield break;

            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / duration);
            k = k * k * (3f - 2f * k);

            yield return null;
        }

        if (corpseTransform == null || corpseRenderer == null)
            yield break;

        corpseRenderer.sortingOrder = corpseSortingOrder - 1;

        yield return GetRemainsLifetimeWait();

        if (corpseRenderer != null)
            ReleaseCorpseVisual(corpseRenderer.gameObject);
    }

    private void ApplyBloodDecalVariant(Transform bloodTransform, SpriteRenderer bloodRenderer)
    {
        if (bloodTransform == null || bloodRenderer == null)
            return;

        if (bloodDecalVariants != null && bloodDecalVariants.Length > 0)
        {
            Sprite variant = bloodDecalVariants[Random.Range(0, bloodDecalVariants.Length)];
            if (variant != null)
                bloodRenderer.sprite = variant;
        }

        // Decals are already authored with desired orientation.
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
        return Random.Range(min, max);
    }

    private static float ResolveSignedRange(Vector2 range)
    {
        float min = Mathf.Min(range.x, range.y);
        float max = Mathf.Max(range.x, range.y);
        return Random.Range(min, max);
    }

    private IEnumerator AnimateBloodPool(Transform blood, SpriteRenderer spriteRenderer, float targetScale, Vector3 settlePosition)
    {
        float startUniform = Random.Range(0.05f, 0.15f);

        Vector3 startScale = new Vector3(startUniform, startUniform, 1f);
        Vector3 endScale = new Vector3(targetScale, targetScale, 1f);

        float duration = Mathf.Lerp(0.25f, 0.95f, Mathf.InverseLerp(0.35f, 1.05f, targetScale));
        duration *= Random.Range(1.9f, 3.25f);

        float startAlpha = 0f;
        float endAlpha = Random.Range(0.8f, 1f);

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

    [System.Serializable]
    private struct RemainsVariant
    {
        public Sprite SourceSprite;
        public Sprite RemainsSprite;
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
