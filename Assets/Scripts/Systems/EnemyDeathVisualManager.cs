using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EnemyDeathVisualManager : MonoBehaviour
{
    private const string RuntimeObjectName = "[EnemyDeathVisualManager]";

    private static EnemyDeathVisualManager instance;

    [SerializeField] private int maxTrackedDeaths = 80;
    [SerializeField, Min(0.05f)] private float overflowToRemainsTransitionDuration = 0.6f;
    [SerializeField, Min(1f)] private float remainsLifetimeAfterOverflow = 60f;
    [SerializeField, Range(0f, 1f)] private float remainsAlpha = 0.8f;
    [SerializeField, Min(0.1f)] private float remainsScaleMultiplier = 1f;
    [SerializeField, Range(0.4f, 1.2f)] private float remainsBrightness = 0.9f;
    [SerializeField] private List<RemainsVariant> remainsVariants = new List<RemainsVariant>(4);

    private readonly Queue<DeathVisualEntry> activeVisuals = new Queue<DeathVisualEntry>(80);
    private readonly Dictionary<Sprite, Sprite> remainsVariantLookup = new Dictionary<Sprite, Sprite>(8);
    private readonly Dictionary<GameObject, Stack<GameObject>> bloodPoolByPrefab = new Dictionary<GameObject, Stack<GameObject>>(4);
    private readonly Stack<GameObject> corpseVisualPool = new Stack<GameObject>(64);

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
        DontDestroyOnLoad(gameObject);
        RebuildRemainsVariantLookup();
    }

    private void OnValidate()
    {
        RebuildRemainsVariantLookup();
    }

    public static EnemyDeathVisualManager Instance
    {
        get
        {
            if (instance != null)
                return instance;

            instance = FindObjectOfType<EnemyDeathVisualManager>();
            if (instance != null)
                return instance;

            GameObject runtimeObject = new GameObject(RuntimeObjectName);
            instance = runtimeObject.AddComponent<EnemyDeathVisualManager>();
            DontDestroyOnLoad(runtimeObject);
            return instance;
        }
    }

    public void SpawnDeathVisuals(Sprite corpseSprite, bool corpseFlipX, Vector3 corpsePosition, Vector3 corpseScale, int corpseSortingOrder, GameObject bloodPoolPrefab, Vector3 bloodPosition)
    {
        if (corpseSprite == null && bloodPoolPrefab == null)
            return;

        EnforceCap();

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
        if (bloodPoolPrefab != null)
        {
            bloodObject = AcquireBloodVisual(bloodPoolPrefab);
            bloodObject.transform.SetPositionAndRotation(bloodPosition, Quaternion.identity);

            SpriteRenderer bloodRenderer = bloodObject.GetComponent<SpriteRenderer>();
            float targetScale = Random.Range(0.4f, 1.1f);
            StartCoroutine(AnimateBloodPool(bloodObject.transform, bloodRenderer, targetScale));
        }

        activeVisuals.Enqueue(new DeathVisualEntry(corpseObject, corpseRenderer, bloodObject, corpseSprite, corpseSortingOrder, bloodPoolPrefab));
    }

    private GameObject AcquireCorpseVisual()
    {
        if (corpseVisualPool.Count > 0)
        {
            GameObject pooled = corpseVisualPool.Pop();
            if (pooled != null)
                return pooled;
        }

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
        if (!bloodPoolByPrefab.TryGetValue(bloodPrefab, out Stack<GameObject> pool))
        {
            pool = new Stack<GameObject>(16);
            bloodPoolByPrefab[bloodPrefab] = pool;
        }

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

    private void ReleaseBloodVisual(GameObject bloodObject, GameObject sourcePrefab)
    {
        if (bloodObject == null)
            return;

        if (sourcePrefab == null)
        {
            Destroy(bloodObject);
            return;
        }

        if (!bloodPoolByPrefab.TryGetValue(sourcePrefab, out Stack<GameObject> pool))
        {
            pool = new Stack<GameObject>(16);
            bloodPoolByPrefab[sourcePrefab] = pool;
        }

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
            ReleaseBloodVisual(entry.BloodObject, entry.BloodSourcePrefab);

        if (entry.CorpseObject == null || entry.CorpseRenderer == null)
            return;

        Sprite remainsSprite = ResolveRemainsSprite(entry.SourceCorpseSprite);
        StartCoroutine(TransitionOverflowCorpseToRemains(entry.CorpseObject.transform, entry.CorpseRenderer, remainsSprite, entry.SortingOrder));
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

        Vector3 scaled = corpseTransform.localScale * Mathf.Max(0.1f, remainsScaleMultiplier);
        corpseTransform.localScale = scaled;

        Color color = corpseRenderer.color;
        float brightness = Mathf.Max(0.4f, remainsBrightness);
        color.r *= brightness;
        color.g *= brightness;
        color.b *= brightness;
        color.a = Mathf.Clamp01(remainsAlpha);
        corpseRenderer.color = color;

        yield return GetRemainsLifetimeWait();

        if (corpseRenderer != null)
            ReleaseCorpseVisual(corpseRenderer.gameObject);
    }

    private IEnumerator AnimateBloodPool(Transform blood, SpriteRenderer spriteRenderer, float targetScale)
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

        blood.localScale = startScale;

        float t = 0f;

        float growPhase = duration * 0.75f;
        while (t < growPhase)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / growPhase);
            k = k * k * (3f - 2f * k);

            blood.localScale = Vector3.Lerp(startScale, endScale, k);

            if (spriteRenderer != null)
            {
                Color color = spriteRenderer.color;
                color.a = Mathf.Lerp(startAlpha, endAlpha, k);
                spriteRenderer.color = color;
            }

            yield return null;
        }

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
}
