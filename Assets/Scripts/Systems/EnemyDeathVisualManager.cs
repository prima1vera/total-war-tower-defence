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
    [SerializeField, Range(0.05f, 1f)] private float remainsAlpha = 0.3f;
    [SerializeField, Range(0.3f, 1.2f)] private float remainsScaleMultiplier = 0.75f;
    [SerializeField] private List<RemainsVariant> remainsVariants = new List<RemainsVariant>(4);

    private readonly Queue<DeathVisualEntry> activeVisuals = new Queue<DeathVisualEntry>(80);

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
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
            corpseObject = new GameObject("CorpseVisual");
            corpseObject.transform.SetPositionAndRotation(corpsePosition, Quaternion.identity);
            corpseObject.transform.localScale = corpseScale;

            corpseRenderer = corpseObject.AddComponent<SpriteRenderer>();
            corpseRenderer.sprite = corpseSprite;
            corpseRenderer.flipX = corpseFlipX;
            corpseRenderer.sortingLayerName = "Units_Dead";
            corpseRenderer.sortingOrder = corpseSortingOrder;
        }

        GameObject bloodObject = null;
        if (bloodPoolPrefab != null)
        {
            bloodObject = Instantiate(bloodPoolPrefab, bloodPosition, Quaternion.identity);
            SpriteRenderer bloodRenderer = bloodObject.GetComponent<SpriteRenderer>();
            float targetScale = Random.Range(0.35f, 1.05f);
            StartCoroutine(AnimateBloodPool(bloodObject.transform, bloodRenderer, targetScale));
        }

        activeVisuals.Enqueue(new DeathVisualEntry(corpseObject, corpseRenderer, bloodObject, corpseSprite, corpseSortingOrder));
    }

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
            Destroy(entry.BloodObject);

        if (entry.CorpseObject == null || entry.CorpseRenderer == null)
            return;

        Sprite remainsSprite = ResolveRemainsSprite(entry.SourceCorpseSprite);
        StartCoroutine(TransitionOverflowCorpseToRemains(entry.CorpseObject.transform, entry.CorpseRenderer, remainsSprite, entry.SortingOrder));
    }

    private Sprite ResolveRemainsSprite(Sprite sourceSprite)
    {
        if (sourceSprite == null)
            return null;

        for (int i = 0; i < remainsVariants.Count; i++)
        {
            if (remainsVariants[i].SourceSprite == sourceSprite && remainsVariants[i].RemainsSprite != null)
                return remainsVariants[i].RemainsSprite;
        }

        return sourceSprite;
    }

    private IEnumerator TransitionOverflowCorpseToRemains(Transform corpseTransform, SpriteRenderer corpseRenderer, Sprite remainsSprite, int corpseSortingOrder)
    {
        if (corpseTransform == null || corpseRenderer == null)
            yield break;

        float duration = Mathf.Max(0.05f, overflowToRemainsTransitionDuration);

        Vector3 startScale = corpseTransform.localScale;
        Vector3 endScale = startScale * Mathf.Max(0.01f, remainsScaleMultiplier);

        Color startColor = corpseRenderer.color;
        Color endColor = startColor;
        endColor.a = Mathf.Clamp01(remainsAlpha);

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

            corpseTransform.localScale = Vector3.Lerp(startScale, endScale, k);
            corpseRenderer.color = Color.Lerp(startColor, endColor, k);

            yield return null;
        }

        if (corpseTransform == null || corpseRenderer == null)
            yield break;

        corpseTransform.localScale = endScale;
        corpseRenderer.color = endColor;
        corpseRenderer.sortingOrder = corpseSortingOrder - 1;

        float remainsLifetime = Mathf.Max(1f, remainsLifetimeAfterOverflow);
        yield return new WaitForSeconds(remainsLifetime);

        if (corpseRenderer != null)
            Destroy(corpseRenderer.gameObject);
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

        public DeathVisualEntry(GameObject corpseObject, SpriteRenderer corpseRenderer, GameObject bloodObject, Sprite sourceCorpseSprite, int sortingOrder)
        {
            CorpseObject = corpseObject;
            CorpseRenderer = corpseRenderer;
            BloodObject = bloodObject;
            SourceCorpseSprite = sourceCorpseSprite;
            SortingOrder = sortingOrder;
        }
    }
}
