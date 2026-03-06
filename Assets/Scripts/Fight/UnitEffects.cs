using UnityEngine;

public class UnitEffects : MonoBehaviour
{
    [Header("Status Visuals")]
    [SerializeField] private Color fireTint = new Color(1f, 0.5f, 0.2f, 1f);
    [SerializeField] private Color freezeTint = new Color(0.4f, 0.8f, 1f, 1f);

    [Header("Damage Feedback")]
    [SerializeField] private Color hitFlashColor = Color.white;
    [SerializeField, Min(0.02f)] private float hitFlashDuration = 0.08f;
    [SerializeField, Range(0f, 1f)] private float hitFlashStrength = 0.85f;
    [SerializeField] private Color burnTickColor = new Color(1f, 0.9f, 0.35f, 1f);
    [SerializeField, Min(0.02f)] private float burnTickDuration = 0.12f;
    [SerializeField, Range(0f, 1f)] private float burnTickStrength = 0.35f;

    [Header("Linked Effects")]
    public GameObject fireEffect;
    public GameObject frostEffectPrefab;

    private SpriteRenderer sr;
    private UnitHealth health;

    private bool fireActive;
    private bool freezeActive;
    private float hitFlashTimer;
    private float burnTickTimer;
    private bool needsAnimatedUpdate;
    private Color appliedColor = Color.white;

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        health = GetComponent<UnitHealth>();

        if (fireEffect != null)
            fireEffect.SetActive(false);

        if (frostEffectPrefab != null)
            frostEffectPrefab.SetActive(false);
    }

    void OnEnable()
    {
        if (health != null)
            health.DamageTaken += HandleDamageTaken;

        hitFlashTimer = 0f;
        burnTickTimer = 0f;
        needsAnimatedUpdate = false;

        ApplyCompositeColor(true);
    }

    void OnDisable()
    {
        if (health != null)
            health.DamageTaken -= HandleDamageTaken;

        needsAnimatedUpdate = false;
    }

    void Update()
    {
        if (!needsAnimatedUpdate)
            return;

        float dt = Time.deltaTime;

        if (hitFlashTimer > 0f)
            hitFlashTimer = Mathf.Max(0f, hitFlashTimer - dt);

        if (burnTickTimer > 0f)
            burnTickTimer = Mathf.Max(0f, burnTickTimer - dt);

        ApplyCompositeColor(false);

        if (hitFlashTimer <= 0f && burnTickTimer <= 0f)
            needsAnimatedUpdate = false;
    }

    public void SetFireVisual(bool state)
    {
        fireActive = state;

        if (fireEffect != null)
            fireEffect.SetActive(state);

        ApplyCompositeColor(false);
    }

    public void SetFreezeVisual(bool state)
    {
        freezeActive = state;

        if (frostEffectPrefab != null)
            frostEffectPrefab.SetActive(state);

        ApplyCompositeColor(false);
    }

    private void HandleDamageTaken(DamageFeedbackEvent damageEvent)
    {
        switch (damageEvent.FeedbackKind)
        {
            case DamageFeedbackKind.DirectHit:
                TriggerHitFlash();
                break;

            case DamageFeedbackKind.BurnTick:
                TriggerBurnTickFlash();
                break;
        }
    }

    private void TriggerHitFlash()
    {
        hitFlashTimer = Mathf.Max(hitFlashTimer, hitFlashDuration);
        needsAnimatedUpdate = true;
        ApplyCompositeColor(false);
    }

    private void TriggerBurnTickFlash()
    {
        burnTickTimer = Mathf.Max(burnTickTimer, burnTickDuration);
        needsAnimatedUpdate = true;
        ApplyCompositeColor(false);
    }

    private void ApplyCompositeColor(bool force)
    {
        if (sr == null)
            return;

        Color baseColor = ResolveStatusColor();

        float burnFactor = burnTickDuration > 0f ? Mathf.Clamp01(burnTickTimer / burnTickDuration) : 0f;
        burnFactor *= burnTickStrength;

        Color composed = Color.Lerp(baseColor, burnTickColor, burnFactor);

        float hitFactor = hitFlashDuration > 0f ? Mathf.Clamp01(hitFlashTimer / hitFlashDuration) : 0f;
        hitFactor *= hitFlashStrength;

        composed = Color.Lerp(composed, hitFlashColor, hitFactor);
        composed.a = 1f;

        if (force || !Approximately(appliedColor, composed))
        {
            sr.color = composed;
            appliedColor = composed;
        }
    }

    private Color ResolveStatusColor()
    {
        if (fireActive && freezeActive)
            return Color.Lerp(freezeTint, fireTint, 0.5f);

        if (fireActive)
            return fireTint;

        if (freezeActive)
            return freezeTint;

        return Color.white;
    }

    private static bool Approximately(Color a, Color b)
    {
        return Mathf.Abs(a.r - b.r) <= 0.001f
            && Mathf.Abs(a.g - b.g) <= 0.001f
            && Mathf.Abs(a.b - b.b) <= 0.001f
            && Mathf.Abs(a.a - b.a) <= 0.001f;
    }
}
