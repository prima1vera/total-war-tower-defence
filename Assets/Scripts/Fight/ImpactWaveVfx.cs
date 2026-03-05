using UnityEngine;

public class ImpactWaveVfx : MonoBehaviour
{
    [Header("Timing")]
    [SerializeField] private float duration = 5f;

    [Header("Scale")]
    [SerializeField] private float startRadius = 0.01f;
    [SerializeField] private float endRadius = 1.5f;

    [Header("Fade")]
    [SerializeField, Range(0f, 1f)] private float startAlpha = 1f;
    [SerializeField, Range(0f, 1f)] private float endAlpha = 0f;

    [Header("Optional curve")]
    [SerializeField] private AnimationCurve ease = AnimationCurve.EaseInOut(0, 0, 1, 1);

    private float t;
    private SpriteRenderer sr;

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
    }

    void OnEnable()
    {
        t = 0f;
        Apply(0f);
    }

    void Update()
    {
        t += Time.deltaTime;
        float k = duration <= 0.0001f ? 1f : Mathf.Clamp01(t / duration);
        Apply(k);

        if (k >= 1f)
            VfxPool.Instance.Release(gameObject);
    }

    private void Apply(float k01)
    {
        float k = ease != null ? ease.Evaluate(k01) : k01;

        float r = Mathf.Lerp(startRadius, endRadius, k);
        transform.localScale = new Vector3(r, r, 1f);

        if (sr != null)
        {
            var c = sr.color;
            c.a = Mathf.Lerp(startAlpha, endAlpha, k);
            sr.color = c;
        }
    }

    // чтобы Arrow мог задать радиус под свой impactRadius
    public void Configure(float radius, float waveDuration)
    {
        endRadius = Mathf.Max(0.01f, radius * 2f); // scale = diameter-ish дл€ большинства ring sprite
        duration = Mathf.Max(0.03f, waveDuration);
    }
}