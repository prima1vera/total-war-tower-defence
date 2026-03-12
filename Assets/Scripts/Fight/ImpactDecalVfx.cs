using UnityEngine;

[RequireComponent(typeof(SpriteRenderer))]
public class ImpactDecalVfx : MonoBehaviour
{
    [Header("Lifetime")]
    [SerializeField] private float lifetime = 12f;

    [Header("Alpha")]
    [SerializeField, Range(0f, 1f)] private float startAlpha = 0.45f;
    [SerializeField, Range(0f, 1f)] private float endAlpha = 0f;

    [Header("Scale")]
    [SerializeField] private float startScale = 1f;
    [SerializeField] private float endScale = 0.1f;

    [Header("Ground Shape")]
    [SerializeField, Range(0.1f, 1f)] private float groundSquash = 0.6f;

    private float timer;
    private SpriteRenderer sr;
    private Color initialColor;
    private float spawnScaleMultiplier = 1f;

    private void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        initialColor = sr.color;
    }

    private void OnEnable()
    {
        timer = 0f;
        spawnScaleMultiplier = Mathf.Max(0.01f, transform.localScale.x);
        Apply(0f);
    }

    private void Update()
    {
        timer += Time.deltaTime;
        float t = Mathf.Clamp01(timer / Mathf.Max(0.0001f, lifetime));

        Apply(t);

        if (t >= 1f)
        {
            if (VfxPool.TryGetInstance(out VfxPool vfxPool))
                vfxPool.Release(gameObject);
            else
                Destroy(gameObject);
        }
    }

    private void Apply(float t)
    {
        float currentScale = Mathf.Lerp(startScale, endScale, t) * spawnScaleMultiplier;
        transform.localScale = new Vector3(currentScale, currentScale * groundSquash, 1f);

        Color c = initialColor;
        c.a *= Mathf.Lerp(startAlpha, endAlpha, t);
        sr.color = c;
    }
}
