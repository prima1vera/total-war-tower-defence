using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EnemyDeathVisualManager : MonoBehaviour
{
    private const string RuntimeObjectName = "[EnemyDeathVisualManager]";

    private static EnemyDeathVisualManager instance;

    [SerializeField] private int maxTrackedDeaths = 80;

    private readonly Queue<DeathVisualEntry> activeVisuals = new Queue<DeathVisualEntry>(80);

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
        if (corpseSprite != null)
        {
            corpseObject = new GameObject("CorpseVisual");
            corpseObject.transform.SetPositionAndRotation(corpsePosition, Quaternion.identity);
            corpseObject.transform.localScale = corpseScale;

            SpriteRenderer corpseRenderer = corpseObject.AddComponent<SpriteRenderer>();
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

        activeVisuals.Enqueue(new DeathVisualEntry(corpseObject, bloodObject));
    }

    private void EnforceCap()
    {
        int cap = Mathf.Max(1, maxTrackedDeaths);
        while (activeVisuals.Count >= cap)
        {
            DeathVisualEntry oldest = activeVisuals.Dequeue();
            if (oldest.CorpseObject != null)
                Destroy(oldest.CorpseObject);

            if (oldest.BloodObject != null)
                Destroy(oldest.BloodObject);
        }
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

    private readonly struct DeathVisualEntry
    {
        public readonly GameObject CorpseObject;
        public readonly GameObject BloodObject;

        public DeathVisualEntry(GameObject corpseObject, GameObject bloodObject)
        {
            CorpseObject = corpseObject;
            BloodObject = bloodObject;
        }
    }
}
