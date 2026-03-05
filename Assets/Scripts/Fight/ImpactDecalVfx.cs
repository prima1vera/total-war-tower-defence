using UnityEngine;

[RequireComponent(typeof(SpriteRenderer))]
public class ImpactDecalVfx : MonoBehaviour
{
    [SerializeField] private float lifetime = 0.8f;
    [SerializeField] private float startAlpha = 0.45f;
    [SerializeField] private float endAlpha = 0f;

    [SerializeField] private float startScale = 0.9f;
    [SerializeField] private float endScale = 1.1f;

    [SerializeField, Range(0.25f, 1f)]
    private float groundSquash = 0.6f;

    private float timer;
    private SpriteRenderer sr;

    private void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
    }

    private void OnEnable()
    {
        timer = 0f;
        Apply(0f);
    }

    private void Update()
    {
        timer += Time.deltaTime;
        float t = Mathf.Clamp01(timer / Mathf.Max(0.0001f, lifetime));

        Apply(t);

        if (t >= 1f)
            VfxPool.Instance.Release(gameObject);
    }

    private void Apply(float t)
    {
        float scale = Mathf.Lerp(startScale, endScale, t);
        transform.localScale = new Vector3(scale, scale * groundSquash, 1f);

        if (sr != null)
        {
            Color c = sr.color;
            c.a = Mathf.Lerp(startAlpha, endAlpha, t);
            sr.color = c;
        }
    }

    public void Configure(float scaleMultiplier, float squash, float duration, float alpha)
    {
        startScale = Mathf.Max(0.01f, scaleMultiplier * 0.9f);
        endScale = Mathf.Max(0.01f, scaleMultiplier * 1.1f);
        groundSquash = Mathf.Clamp(squash, 0.1f, 1f);
        lifetime = Mathf.Max(0.05f, duration);
        startAlpha = Mathf.Clamp01(alpha);
    }
}