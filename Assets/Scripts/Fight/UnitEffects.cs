using UnityEngine;
using Random = UnityEngine.Random;

public class UnitEffects : MonoBehaviour
{
    private static readonly int FlashColorId = Shader.PropertyToID("_FlashColor");
    private static readonly int FlashAmountId = Shader.PropertyToID("_FlashAmount");

    private MaterialPropertyBlock propertyBlock;

    [Header("Status Visuals")]
    [SerializeField] private Color fireTint = new Color(1f, 0.5f, 0.2f, 1f);
    [SerializeField] private Color freezeTint = new Color(0.4f, 0.8f, 1f, 1f);
    [SerializeField] private Color baseTint = Color.white;

    [Header("Damage Feedback")]
    [SerializeField] private Color hitFlashColor = new Color(1f, 0.95f, 0.85f, 1f);
    [SerializeField, Min(0.02f)] private float hitFlashDuration = 0.08f;
    [SerializeField, Range(0f, 1f)] private float hitFlashStrength = 0.85f;
    [SerializeField] private Color burnTickColor = new Color(1f, 0.9f, 0.35f, 1f);
    [SerializeField, Min(0.02f)] private float burnTickDuration = 0.12f;
    [SerializeField, Range(0f, 1f)] private float burnTickStrength = 0.35f;

    [Header("Linked Effects")]
    public GameObject fireEffect;
    public GameObject frostEffectPrefab;

    [Header("Burn Loop (Sprite)")]
    [SerializeField] private bool useBurnLoopSprite = true;
    [SerializeField] private Sprite[] burnLoopFrames;
    [SerializeField, Min(1f)] private float burnLoopFps = 12f;
    [SerializeField] private Vector3 burnLoopLocalOffset = new Vector3(0f, -0.06f, 0f);
    [SerializeField] private Vector2 burnLoopScaleRange = new Vector2(0.62f, 0.86f);
    [SerializeField, Range(0f, 1f)] private float burnLoopAlpha = 0.92f;
    [SerializeField] private int burnLoopSortingOffset = 1;

    private SpriteRenderer sr;
    private UnitHealth health;
    private SpriteRenderer burnLoopRenderer;
    private Sprite[] burnLoopResolvedFrames;
    private ParticleSystem[] fireParticleSystems;
    private float burnLoopFrameTimer;
    private float burnLoopFrameDuration = 0.0833f;
    private int burnLoopFrameIndex;
    private int burnLoopLastSyncedSortingOrder = int.MinValue;
    private int burnLoopLastSyncedSortingLayerId = int.MinValue;

    private bool fireActive;
    private bool freezeActive;
    private float hitFlashTimer;
    private float burnTickTimer;
    private bool needsAnimatedUpdate;
    private Color appliedColor = Color.white;

    void Awake()
    {
        propertyBlock = new MaterialPropertyBlock();
        sr = GetComponent<SpriteRenderer>();
        health = GetComponent<UnitHealth>();
        ResolveLinkedEffectsIfMissing();

        CacheFireVisualComponents();

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
        ApplyShaderFlash(0f, hitFlashColor);
    }

    void OnDisable()
    {
        if (health != null)
            health.DamageTaken -= HandleDamageTaken;

        needsAnimatedUpdate = false;
    }

    void Update()
    {
        UpdateBurnLoopAnimation();
        if (!needsAnimatedUpdate)
            return;

        float dt = Time.deltaTime;

        if (hitFlashTimer > 0f)
            hitFlashTimer = Mathf.Max(0f, hitFlashTimer - dt);

        if (burnTickTimer > 0f)
            burnTickTimer = Mathf.Max(0f, burnTickTimer - dt);

        float hitFactor = hitFlashDuration > 0f ? Mathf.Clamp01(hitFlashTimer / hitFlashDuration) : 0f;
        hitFactor = Mathf.Pow(hitFactor, 0.65f) * hitFlashStrength;

        ApplyShaderFlash(hitFactor, hitFlashColor);

        ApplyCompositeColor(false);

        if (hitFlashTimer <= 0f && burnTickTimer <= 0f)
            needsAnimatedUpdate = false;
    }

    void LateUpdate()
    {
        SyncBurnLoopSorting(false);
    }

    void OnWillRenderObject()
    {
        SyncBurnLoopSorting(false);
    }

    private void ApplyShaderFlash(float amount, Color color)
    {
        if (sr == null)
            return;

        sr.GetPropertyBlock(propertyBlock);
        propertyBlock.SetColor(FlashColorId, color);
        propertyBlock.SetFloat(FlashAmountId, amount);
        sr.SetPropertyBlock(propertyBlock);
    }

    public void SetFireVisual(bool state)
    {
        fireActive = state;

        if (!TryApplyBurnLoopState(state) && fireEffect != null)
            fireEffect.SetActive(false);

        ApplyCompositeColor(false);
    }

    private void ResolveLinkedEffectsIfMissing()
    {
        if (fireEffect == null)
        {
            Transform fire = transform.Find("Fire");
            if (fire != null)
                fireEffect = fire.gameObject;
        }

        if (frostEffectPrefab == null)
        {
            Transform frost = transform.Find("Frost");
            if (frost != null)
                frostEffectPrefab = frost.gameObject;
        }
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
        if (damageEvent.FeedbackKind == DamageFeedbackKind.BurnTick)
        {
            TriggerBurnTickFlash();
            return;
        }

        if (damageEvent.Amount > 0)
            TriggerHitFlash();
    }

    private void TriggerHitFlash()
    {
        hitFlashTimer = hitFlashDuration;
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
        //bool nearWhiteBase = IsNearWhite(baseColor);
        //bool nearWhiteFlash = IsNearWhite(hitFlashColor);

        float burnFactor = burnTickDuration > 0f ? Mathf.Clamp01(burnTickTimer / burnTickDuration) : 0f;
        burnFactor *= burnTickStrength;

        Color composed = Color.Lerp(baseColor, burnTickColor, burnFactor);

        //float hitFactor = hitFlashDuration > 0f ? Mathf.Clamp01(hitFlashTimer / hitFlashDuration) : 0f;
        //hitFactor = Mathf.Pow(hitFactor, 0.65f) * hitFlashStrength;

        //Color resolvedHitFlashColor = hitFlashColor;
        //if (nearWhiteBase && nearWhiteFlash)
        //    resolvedHitFlashColor = Color.white;

        //composed = Color.Lerp(composed, resolvedHitFlashColor, hitFactor);
        //composed.a = 1f;

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

        return baseTint;
    }

    public Color GetCorpseTint()
    {
        Color c = baseTint;
        c.a = 1f;
        return c;
    }

    private static bool Approximately(Color a, Color b)
    {
        return Mathf.Abs(a.r - b.r) <= 0.001f
            && Mathf.Abs(a.g - b.g) <= 0.001f
            && Mathf.Abs(a.b - b.b) <= 0.001f
            && Mathf.Abs(a.a - b.a) <= 0.001f;
    }

    private static bool IsNearWhite(Color color)
    {
        return color.r >= 0.95f && color.g >= 0.95f && color.b >= 0.95f;
    }

    private void CacheFireVisualComponents()
    {
        if (fireEffect == null)
            return;

        fireParticleSystems = fireEffect.GetComponentsInChildren<ParticleSystem>(true);
        DisableLegacyFireParticles();

        burnLoopResolvedFrames = ResolveBurnLoopFrames();
        bool canUseBurnLoop = useBurnLoopSprite && burnLoopResolvedFrames != null && burnLoopResolvedFrames.Length > 0;

        if (!canUseBurnLoop)
            return;

        burnLoopRenderer = fireEffect.GetComponent<SpriteRenderer>();
        if (burnLoopRenderer == null)
            burnLoopRenderer = fireEffect.AddComponent<SpriteRenderer>();

        if (sr != null)
        {
            burnLoopRenderer.sortingLayerID = sr.sortingLayerID;
            burnLoopRenderer.sortingOrder = sr.sortingOrder + burnLoopSortingOffset;
            burnLoopRenderer.sharedMaterial = sr.sharedMaterial;
        }

        burnLoopRenderer.drawMode = SpriteDrawMode.Simple;
        burnLoopRenderer.maskInteraction = SpriteMaskInteraction.None;
        burnLoopRenderer.enabled = false;
        burnLoopRenderer.color = new Color(1f, 1f, 1f, Mathf.Clamp01(burnLoopAlpha));

        burnLoopFrameDuration = 1f / Mathf.Max(1f, burnLoopFps);
    }

    private bool TryApplyBurnLoopState(bool state)
    {
        if (!useBurnLoopSprite || burnLoopRenderer == null || burnLoopResolvedFrames == null || burnLoopResolvedFrames.Length == 0 || fireEffect == null)
            return false;

        if (state)
        {
            fireEffect.SetActive(true);

            float minScale = Mathf.Max(0.05f, Mathf.Min(burnLoopScaleRange.x, burnLoopScaleRange.y));
            float maxScale = Mathf.Max(minScale, Mathf.Max(burnLoopScaleRange.x, burnLoopScaleRange.y));
            float randomScale = Random.Range(minScale, maxScale);

            fireEffect.transform.localPosition = burnLoopLocalOffset + new Vector3(
                Random.Range(-0.015f, 0.015f),
                Random.Range(-0.01f, 0.01f),
                0f);
            fireEffect.transform.localScale = new Vector3(randomScale, randomScale, 1f);

            burnLoopFrameDuration = 1f / Mathf.Max(1f, burnLoopFps);
            burnLoopFrameTimer = Random.Range(0f, burnLoopFrameDuration);
            burnLoopFrameIndex = Random.Range(0, burnLoopResolvedFrames.Length);
            burnLoopRenderer.flipX = Random.value > 0.5f;
            burnLoopRenderer.sprite = burnLoopResolvedFrames[burnLoopFrameIndex];
            burnLoopRenderer.color = new Color(1f, 1f, 1f, Mathf.Clamp01(burnLoopAlpha));
            burnLoopRenderer.enabled = true;
            SyncBurnLoopSorting(true);
        }
        else
        {
            burnLoopRenderer.enabled = false;
            fireEffect.SetActive(false);
        }

        return true;
    }

    private void UpdateBurnLoopAnimation()
    {
        if (!fireActive || burnLoopRenderer == null || !burnLoopRenderer.enabled || burnLoopResolvedFrames == null || burnLoopResolvedFrames.Length == 0)
            return;

        burnLoopFrameTimer += Time.deltaTime;
        if (burnLoopFrameTimer >= burnLoopFrameDuration)
        {
            burnLoopFrameTimer -= burnLoopFrameDuration;
            burnLoopFrameIndex = (burnLoopFrameIndex + 1) % burnLoopResolvedFrames.Length;
            burnLoopRenderer.sprite = burnLoopResolvedFrames[burnLoopFrameIndex];
        }

        SyncBurnLoopSorting(false);
    }

    private void SyncBurnLoopSorting(bool force)
    {
        if (burnLoopRenderer == null || sr == null)
            return;

        int targetLayerId = sr.sortingLayerID;
        int targetOrder = sr.sortingOrder + burnLoopSortingOffset;

        if (!force
            && burnLoopLastSyncedSortingLayerId == targetLayerId
            && burnLoopLastSyncedSortingOrder == targetOrder)
            return;

        burnLoopLastSyncedSortingLayerId = targetLayerId;
        burnLoopLastSyncedSortingOrder = targetOrder;

        burnLoopRenderer.sortingLayerID = targetLayerId;
        burnLoopRenderer.sortingOrder = targetOrder;
    }

    private void DisableLegacyFireParticles()
    {
        if (fireParticleSystems == null || fireParticleSystems.Length == 0)
            return;

        for (int i = 0; i < fireParticleSystems.Length; i++)
        {
            ParticleSystem ps = fireParticleSystems[i];
            if (ps == null)
                continue;

            var emission = ps.emission;
            emission.enabled = false;

            ParticleSystemRenderer psRenderer = ps.GetComponent<ParticleSystemRenderer>();
            if (psRenderer != null)
                psRenderer.enabled = false;

            if (ps.isPlaying)
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }

    private Sprite[] ResolveBurnLoopFrames()
    {
        if (burnLoopFrames == null || burnLoopFrames.Length == 0)
            return null;

        int validCount = 0;
        for (int i = 0; i < burnLoopFrames.Length; i++)
        {
            if (burnLoopFrames[i] != null)
                validCount++;
        }

        if (validCount == 0)
            return null;

        if (validCount == burnLoopFrames.Length)
            return burnLoopFrames;

        Sprite[] resolved = new Sprite[validCount];
        int writeIndex = 0;
        for (int i = 0; i < burnLoopFrames.Length; i++)
        {
            Sprite sprite = burnLoopFrames[i];
            if (sprite == null)
                continue;

            resolved[writeIndex++] = sprite;
        }

        return resolved;
    }
}
