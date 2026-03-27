using UnityEngine;

[RequireComponent(typeof(SpriteRenderer))]
public class ImpactSpriteSheetVfx : MonoBehaviour
{
    [Header("Sprite Sheet")]
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private Sprite[] frames;
    [SerializeField, Min(1f)] private float framesPerSecond = 24f;
    [SerializeField, Min(0f)] private float holdDuration = 0.02f;

    [Header("Fade")]
    [SerializeField, Range(0f, 1f)] private float startAlpha = 1f;
    [SerializeField, Range(0f, 1f)] private float endAlpha = 0f;
    [SerializeField, Min(0.01f)] private float fadeDuration = 0.16f;

    [Header("Scale")]
    [SerializeField] private float startScale = 0.9f;
    [SerializeField] private float endScale = 1.2f;
    [SerializeField, Range(0.1f, 1f)] private float groundSquash = 0.62f;

    private int currentFrameIndex;
    private float timer;
    private float frameDuration;
    private float playbackDuration;
    private float totalDuration;
    private float spawnScaleMultiplier = 1f;
    private Color initialColor = Color.white;
    private bool hasFrames;

    private void Awake()
    {
        if (spriteRenderer == null)
            spriteRenderer = GetComponent<SpriteRenderer>();

        if (spriteRenderer != null)
            initialColor = spriteRenderer.color;

        RecalculateTimings();
    }

    private void OnEnable()
    {
        if (spriteRenderer == null)
            spriteRenderer = GetComponent<SpriteRenderer>();

        RecalculateTimings();

        timer = 0f;
        currentFrameIndex = -1;
        hasFrames = frames != null && frames.Length > 0;
        spawnScaleMultiplier = Mathf.Max(0.01f, transform.localScale.x);

        if (spriteRenderer != null)
            spriteRenderer.color = initialColor;

        if (hasFrames)
            SetFrame(0);

        ApplyFadeAndScale(0f);
    }

    private void OnDisable()
    {
        if (spriteRenderer != null)
            spriteRenderer.color = initialColor;
    }

    private void Update()
    {
        timer += Time.deltaTime;

        if (hasFrames)
        {
            int nextFrame = Mathf.Clamp(Mathf.FloorToInt(timer / frameDuration), 0, frames.Length - 1);
            if (nextFrame != currentFrameIndex)
                SetFrame(nextFrame);
        }

        float fadeStartTime = playbackDuration + Mathf.Max(0f, holdDuration);
        float fadeT = timer <= fadeStartTime
            ? 0f
            : Mathf.Clamp01((timer - fadeStartTime) / Mathf.Max(0.01f, fadeDuration));

        ApplyFadeAndScale(fadeT);

        if (timer >= totalDuration)
            ReleaseSelf();
    }

    private void RecalculateTimings()
    {
        framesPerSecond = Mathf.Max(1f, framesPerSecond);
        fadeDuration = Mathf.Max(0.01f, fadeDuration);
        frameDuration = 1f / framesPerSecond;

        int frameCount = frames != null ? frames.Length : 0;
        playbackDuration = frameCount > 0 ? frameCount * frameDuration : 0f;
        totalDuration = playbackDuration + Mathf.Max(0f, holdDuration) + fadeDuration;
    }

    private void SetFrame(int index)
    {
        currentFrameIndex = index;

        if (spriteRenderer == null || frames == null || index < 0 || index >= frames.Length)
            return;

        spriteRenderer.sprite = frames[index];
    }

    private void ApplyFadeAndScale(float t)
    {
        float currentScale = Mathf.Lerp(startScale, endScale, t) * spawnScaleMultiplier;
        transform.localScale = new Vector3(currentScale, currentScale * groundSquash, 1f);

        if (spriteRenderer == null)
            return;

        Color color = initialColor;
        color.a = Mathf.Lerp(startAlpha, endAlpha, t) * initialColor.a;
        spriteRenderer.color = color;
    }

    private void ReleaseSelf()
    {
        if (VfxPool.TryGetInstance(out VfxPool vfxPool))
            vfxPool.Release(gameObject);
        else
            Destroy(gameObject);
    }
}
