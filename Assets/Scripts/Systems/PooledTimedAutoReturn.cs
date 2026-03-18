using UnityEngine;

public class PooledTimedAutoReturn : MonoBehaviour
{
    [SerializeField, Min(0.05f)] private float lifetime = 2f;

    [Header("Smooth Despawn")]
    [SerializeField] private bool smoothDisappear;
    [SerializeField, Min(0.05f)] private float disappearDuration = 1f;
    [SerializeField] private bool fadeAlpha = true;
    [SerializeField] private bool shrinkScale = true;
    [SerializeField, Range(0f, 1f)] private float endScaleMultiplier = 0.08f;

    private float timer;
    private bool armed;

    private bool despawning;
    private float despawnTimer;

    private SpriteRenderer cachedRenderer;
    private Color startColor = Color.white;
    private Vector3 startScale = Vector3.one;

    private void Awake()
    {
        cachedRenderer = GetComponent<SpriteRenderer>();
    }

    private void OnEnable()
    {
        timer = 0f;
        despawnTimer = 0f;
        despawning = false;
        armed = lifetime > 0.01f;

        startScale = transform.localScale;
        if (cachedRenderer != null)
            startColor = cachedRenderer.color;
    }

    public void Arm(float duration)
    {
        lifetime = Mathf.Max(0.05f, duration);
        timer = 0f;
        despawnTimer = 0f;
        despawning = false;
        armed = true;

        startScale = transform.localScale;
        if (cachedRenderer != null)
            startColor = cachedRenderer.color;
    }

    private void Update()
    {
        if (!armed)
            return;

        if (!despawning)
        {
            timer += Time.deltaTime;
            if (timer < lifetime)
                return;

            if (!smoothDisappear)
            {
                armed = false;
                ReleaseNow();
                return;
            }

            despawning = true;
            despawnTimer = 0f;
            startScale = transform.localScale;
            if (cachedRenderer != null)
                startColor = cachedRenderer.color;
        }

        despawnTimer += Time.deltaTime;
        float duration = Mathf.Max(0.05f, disappearDuration);
        float k = Mathf.Clamp01(despawnTimer / duration);

        if (shrinkScale)
        {
            float scaleK = Mathf.Lerp(1f, Mathf.Clamp01(endScaleMultiplier), k);
            transform.localScale = startScale * scaleK;
        }

        if (fadeAlpha && cachedRenderer != null)
        {
            Color color = startColor;
            color.a = Mathf.Lerp(startColor.a, 0f, k);
            cachedRenderer.color = color;
        }

        if (k < 1f)
            return;

        armed = false;
        despawning = false;
        ReleaseNow();
    }

    private void ReleaseNow()
    {
        if (cachedRenderer != null)
            cachedRenderer.color = startColor;

        transform.localScale = startScale;

        if (VfxPool.TryGetInstance(out VfxPool vfxPool))
            vfxPool.Release(gameObject);
        else
            Destroy(gameObject);
    }

    private void OnDisable()
    {
        armed = false;
        despawning = false;
    }
}
