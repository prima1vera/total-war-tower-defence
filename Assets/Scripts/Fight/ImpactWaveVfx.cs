using UnityEngine;

[RequireComponent(typeof(SpriteRenderer))]
public class ImpactWaveVfx : MonoBehaviour
{
    [Header("Timing")]
    [SerializeField, Tooltip("How long the wave plays (seconds). Higher = longer visible wave.")]
    private float duration = 0.25f;

    [Header("Scale")]
    [SerializeField, Tooltip("Starting radius scale (in localScale units). Usually very small.")]
    private float startRadius = 0.01f;

    [SerializeField, Tooltip("Ending radius scale (in localScale units). This script treats it as 'radius scale', not world units.")]
    private float endRadius = 1.5f;

    [Header("Fade")]
    [SerializeField, Range(0f, 1f), Tooltip("Alpha at the beginning.")]
    private float startAlpha = 1f;

    [SerializeField, Range(0f, 1f), Tooltip("Alpha at the end.")]
    private float endAlpha = 0f;

    [Header("Optional curve")]
    [SerializeField, Tooltip("Optional easing curve for scale & fade (0..1).")]
    private AnimationCurve ease = AnimationCurve.EaseInOut(0, 0, 1, 1);

    private float t;
    private SpriteRenderer sr;

    private void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
    }

    private void OnEnable()
    {
        t = 0f;
        Apply(0f);
    }

    private void Update()
    {
        t += Time.deltaTime;

        float d = Mathf.Max(0.0001f, duration);
        float k01 = Mathf.Clamp01(t / d);

        Apply(k01);

        if (k01 >= 1f)
            VfxPool.Instance.Release(gameObject);
    }

    private float squashY = 1f;

    private void Apply(float k01)
    {
        float k = ease != null ? ease.Evaluate(k01) : k01;

        float r = Mathf.Lerp(startRadius, endRadius, k);
        transform.localScale = new Vector3(r, r * squashY, 1f);

        if (sr != null)
        {
            var c = sr.color;
            c.a = Mathf.Lerp(startAlpha, endAlpha, k);
            sr.color = c;
        }
    }

    public void Configure(float radius, float waveDuration, float groundSquash = 0.6f)
    {
        endRadius = Mathf.Max(0.01f, radius * 2f);
        duration = Mathf.Max(0.03f, waveDuration);
        squashY = Mathf.Clamp(groundSquash, 0.1f, 1f);
    }
}