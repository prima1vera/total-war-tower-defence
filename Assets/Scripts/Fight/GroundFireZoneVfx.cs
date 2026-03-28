using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;
#if UNITY_EDITOR
using UnityEditor;
#endif

#pragma warning disable CS0649

[RequireComponent(typeof(SpriteRenderer))]
public class GroundFireZoneVfx : MonoBehaviour
{
    [Serializable]
    private struct FireStrip
    {
        public Texture2D stripTexture;
        [Min(1)] public int frameCount;
    }

    [Serializable]
    private struct FireVariant
    {
        public FireStrip start;
        public FireStrip loop;
        public FireStrip end;
        [Min(0f)] public float spawnWeight;
        public Vector2 sizeMultiplierRange;
    }

    private enum FirePhase
    {
        Start,
        Loop,
        End
    }

    private struct FireVariantCache
    {
        public Sprite[] startFrames;
        public Sprite[] loopFrames;
        public Sprite[] endFrames;
        public float spawnWeight;
        public Vector2 sizeRange;
        public bool IsValid => loopFrames != null && loopFrames.Length > 0;
    }

    private struct TongueState
    {
        public SpriteRenderer Renderer;
        public int VariantIndex;
        public FirePhase Phase;
        public int FrameIndex;
        public float FrameTimer;
        public int RemainingLoopCycles;
        public float Cooldown;
        public float SizeMultiplier;
        public bool Active;
    }

    [Header("Root")]
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField, Min(1f)] private float pixelsPerUnit = 100f;
    [SerializeField] private FireVariant[] fireVariants = Array.Empty<FireVariant>();
    [SerializeField] private Sprite[] loopFramesLegacy = Array.Empty<Sprite>();

    private enum FireVisualPreset
    {
        Custom,
        Compact,
        Chaotic
    }

    [Header("Preset")]
    [Tooltip("Quick visual tuning preset for this fire zone.")]
    [SerializeField] private FireVisualPreset authoringPreset = FireVisualPreset.Custom;
    [Tooltip("Auto-apply selected preset in Editor on value changes.")]
    [SerializeField] private bool autoApplySelectedPresetInEditor;

    [Header("Tongues")]
    [SerializeField, Min(1)] private int tongueCount = 14;
    [SerializeField, Min(0.1f)] private float zoneRadius = 1.05f;
    [SerializeField, Range(0.1f, 1f)] private float verticalSpread = 0.62f;
    [SerializeField] private Vector2 tongueRespawnDelayRange = new Vector2(0.03f, 0.18f);
    [SerializeField, Min(1)] private int loopCyclesMin = 1;
    [SerializeField, Min(1)] private int loopCyclesMax = 3;
    [SerializeField, Min(1f)] private float startFps = 14f;
    [SerializeField, Min(1f)] private float loopFps = 11f;
    [SerializeField, Min(1f)] private float endFps = 12f;

    [Header("Lifetime")]
    [SerializeField, Min(0.1f)] private float lifetime = 2.6f;
    [SerializeField, Range(0f, 1f)] private float startAlpha = 0.88f;
    [SerializeField, Range(0f, 1f)] private float endAlpha = 0.03f;
    [SerializeField] private float startScale = 1f;
    [SerializeField] private float endScale = 1.2f;
    [SerializeField, Range(0.1f, 1f)] private float groundSquash = 0.56f;

    [Header("Burn Area")]
    [SerializeField] private LayerMask unitLayer;
    [SerializeField, Min(0.05f)] private float affectRadius = 1.15f;
    [SerializeField, Min(0.05f)] private float tickRate = 0.35f;
    [SerializeField, Min(0)] private int directDamagePerTick = 0;
    [SerializeField, Min(0f)] private float burnDuration = 1.75f;
    [SerializeField, Min(0)] private int burnTickDamage = 1;
    [SerializeField, Min(0.05f)] private float burnTickRate = 0.5f;
    [SerializeField, Min(8)] private int maxHitColliders = 24;

    private const int OverlapSortingVariants = 5;
    private static int overlapSortingSeed;
    private static readonly Dictionary<int, Sprite[]> StripFrameCache = new Dictionary<int, Sprite[]>(64);

    private float lifetimeTimer;
    private float tickTimer;
    private float spawnScaleMultiplier = 1f;
    private int zoneSortingOffset;
    private int defaultSortingLayerId;
    private int defaultSortingOrder;
    private Color initialColor = Color.white;
    private Collider2D[] hitBuffer;
    private FireVariantCache[] variantCaches = Array.Empty<FireVariantCache>();
    private TongueState[] tongues = Array.Empty<TongueState>();
    private bool isApplyingPresetInValidate;

    private void Awake()
    {
        if (spriteRenderer == null)
            spriteRenderer = GetComponent<SpriteRenderer>();

        if (spriteRenderer != null)
        {
            initialColor = spriteRenderer.color;
            defaultSortingLayerId = spriteRenderer.sortingLayerID;
            defaultSortingOrder = spriteRenderer.sortingOrder;
            spriteRenderer.enabled = false;
        }

        hitBuffer = new Collider2D[Mathf.Max(8, maxHitColliders)];
        BuildVariantCache();
        EnsureTongueRenderers();
    }

    private void OnEnable()
    {
        lifetimeTimer = 0f;
        tickTimer = 0f;
        spawnScaleMultiplier = Mathf.Max(0.01f, transform.localScale.x);
        zoneSortingOffset = overlapSortingSeed;
        overlapSortingSeed = (overlapSortingSeed + 1) % OverlapSortingVariants;

        BuildVariantCache();
        EnsureTongueRenderers();

        for (int i = 0; i < tongues.Length; i++)
            ResetTongue(i, Random.Range(0f, 0.2f));

        ApplyRootVisual(0f);
    }

    private void OnDisable()
    {
        for (int i = 0; i < tongues.Length; i++)
        {
            TongueState tongue = tongues[i];
            if (tongue.Renderer != null)
            {
                tongue.Renderer.enabled = false;
                tongue.Renderer.color = initialColor;
            }
        }
    }

    private void OnValidate()
    {
        if (isApplyingPresetInValidate)
            return;

        if (autoApplySelectedPresetInEditor && authoringPreset != FireVisualPreset.Custom)
            ApplyPreset(authoringPreset);

        loopCyclesMin = Mathf.Max(1, loopCyclesMin);
        loopCyclesMax = Mathf.Max(loopCyclesMin, loopCyclesMax);
        tongueCount = Mathf.Max(1, tongueCount);
        zoneRadius = Mathf.Max(0.1f, zoneRadius);
        verticalSpread = Mathf.Clamp(verticalSpread, 0.1f, 1f);
    }

    [ContextMenu("Presets/Apply Compact")]
    private void ApplyCompactFromContextMenu() => ApplyPreset(FireVisualPreset.Compact);

    [ContextMenu("Presets/Apply Chaotic")]
    private void ApplyChaoticFromContextMenu() => ApplyPreset(FireVisualPreset.Chaotic);

    [ContextMenu("Presets/Apply Selected")]
    private void ApplySelectedFromContextMenu() => ApplyPreset(authoringPreset);

    public void ApplyCompactPreset() => ApplyPreset(FireVisualPreset.Compact);
    public void ApplyChaoticPreset() => ApplyPreset(FireVisualPreset.Chaotic);

    private void ApplyPreset(FireVisualPreset preset)
    {
        if (preset == FireVisualPreset.Custom)
            return;

        isApplyingPresetInValidate = true;
        authoringPreset = preset;

        switch (preset)
        {
            case FireVisualPreset.Compact:
                tongueCount = 12;
                zoneRadius = 0.92f;
                verticalSpread = 0.9f;
                tongueRespawnDelayRange = new Vector2(0.08f, 0.22f);
                loopCyclesMin = 1;
                loopCyclesMax = 2;
                startFps = 13f;
                loopFps = 10f;
                endFps = 12f;
                lifetime = 2.25f;
                startAlpha = 0.82f;
                endAlpha = 0.02f;
                startScale = 1f;
                endScale = 1.08f;
                groundSquash = 0.84f;
                affectRadius = 1.05f;
                break;

            case FireVisualPreset.Chaotic:
                tongueCount = 28;
                zoneRadius = 1.35f;
                verticalSpread = 1f;
                tongueRespawnDelayRange = new Vector2(0.02f, 0.12f);
                loopCyclesMin = 2;
                loopCyclesMax = 4;
                startFps = 16f;
                loopFps = 13f;
                endFps = 14f;
                lifetime = 3.2f;
                startAlpha = 0.95f;
                endAlpha = 0.03f;
                startScale = 1f;
                endScale = 1.24f;
                groundSquash = 0.88f;
                affectRadius = 1.28f;
                break;
        }

#if UNITY_EDITOR
        EditorUtility.SetDirty(this);
#endif
        isApplyingPresetInValidate = false;
    }

    private void Update()
    {
        float dt = Time.deltaTime;

        lifetimeTimer += dt;
        tickTimer += dt;

        float t = Mathf.Clamp01(lifetimeTimer / Mathf.Max(0.01f, lifetime));
        ApplyRootVisual(t);
        UpdateTongues(dt, Mathf.Lerp(startAlpha, endAlpha, t));

        float safeTickRate = Mathf.Max(0.05f, tickRate);
        while (tickTimer >= safeTickRate)
        {
            tickTimer -= safeTickRate;
            ApplyBurnTick();
        }

        if (t >= 1f)
            ReleaseSelf();
    }

    private void UpdateTongues(float dt, float globalAlpha)
    {
        for (int i = 0; i < tongues.Length; i++)
        {
            TongueState tongue = tongues[i];
            if (tongue.Renderer == null)
                continue;

            if (!tongue.Active)
            {
                tongue.Cooldown -= dt;
                if (tongue.Cooldown <= 0f)
                    SpawnTongue(ref tongue, i);

                tongues[i] = tongue;
                continue;
            }

            Sprite[] currentFrames = GetPhaseFrames(tongue);
            if (currentFrames == null || currentFrames.Length == 0)
            {
                EndTongue(ref tongue);
                tongues[i] = tongue;
                continue;
            }

            float phaseFps = GetPhaseFps(tongue.Phase);
            float frameDuration = 1f / Mathf.Max(1f, phaseFps);
            tongue.FrameTimer += dt;

            while (tongue.FrameTimer >= frameDuration)
            {
                tongue.FrameTimer -= frameDuration;
                tongue.FrameIndex++;

                if (tongue.FrameIndex < currentFrames.Length)
                    continue;

                if (tongue.Phase == FirePhase.Start)
                {
                    tongue.Phase = FirePhase.Loop;
                    tongue.FrameIndex = 0;
                    currentFrames = GetPhaseFrames(tongue);
                    continue;
                }

                if (tongue.Phase == FirePhase.Loop)
                {
                    tongue.RemainingLoopCycles--;
                    if (tongue.RemainingLoopCycles > 0)
                    {
                        tongue.FrameIndex = 0;
                        continue;
                    }

                    FireVariantCache variant = variantCaches[tongue.VariantIndex];
                    if (variant.endFrames != null && variant.endFrames.Length > 0)
                    {
                        tongue.Phase = FirePhase.End;
                        tongue.FrameIndex = 0;
                        currentFrames = variant.endFrames;
                        continue;
                    }

                    EndTongue(ref tongue);
                    currentFrames = null;
                    break;
                }

                EndTongue(ref tongue);
                currentFrames = null;
                break;
            }

            if (!tongue.Active || currentFrames == null || currentFrames.Length == 0)
            {
                tongues[i] = tongue;
                continue;
            }

            int safeFrameIndex = Mathf.Clamp(tongue.FrameIndex, 0, currentFrames.Length - 1);
            tongue.Renderer.sprite = currentFrames[safeFrameIndex];

            Color color = initialColor;
            color.a = globalAlpha * initialColor.a;
            tongue.Renderer.color = color;
            tongue.Renderer.enabled = true;

            tongues[i] = tongue;
        }
    }

    private void SpawnTongue(ref TongueState tongue, int index)
    {
        int variantIndex = ChooseVariantIndex();
        if (variantIndex < 0 || variantIndex >= variantCaches.Length || !variantCaches[variantIndex].IsValid)
        {
            EndTongue(ref tongue);
            return;
        }

        FireVariantCache variant = variantCaches[variantIndex];
        tongue.Active = true;
        tongue.VariantIndex = variantIndex;
        tongue.Phase = variant.startFrames != null && variant.startFrames.Length > 0 ? FirePhase.Start : FirePhase.Loop;
        tongue.FrameIndex = 0;
        tongue.FrameTimer = 0f;
        tongue.RemainingLoopCycles = Random.Range(
            Mathf.Max(1, Mathf.Min(loopCyclesMin, loopCyclesMax)),
            Mathf.Max(1, Mathf.Max(loopCyclesMin, loopCyclesMax)) + 1);

        Vector2 pos = Random.insideUnitCircle * Mathf.Max(0.05f, zoneRadius);
        pos.y *= Mathf.Clamp(verticalSpread, 0.1f, 1f);

        Vector2 sizeRange = NormalizeRange(variant.sizeRange, 0.8f, 1.15f);
        tongue.SizeMultiplier = Random.Range(sizeRange.x, sizeRange.y);

        Transform rendererTransform = tongue.Renderer.transform;
        rendererTransform.localPosition = new Vector3(pos.x, pos.y, 0f);
        rendererTransform.localScale = new Vector3(tongue.SizeMultiplier, tongue.SizeMultiplier, 1f);
        tongue.Renderer.flipX = Random.value > 0.5f;
        tongue.Renderer.sortingLayerID = defaultSortingLayerId;
        tongue.Renderer.sortingOrder = defaultSortingOrder + zoneSortingOffset + (index % 3);
        tongue.Renderer.enabled = true;
    }

    private void EndTongue(ref TongueState tongue)
    {
        tongue.Active = false;
        tongue.Cooldown = Random.Range(
            Mathf.Min(tongueRespawnDelayRange.x, tongueRespawnDelayRange.y),
            Mathf.Max(tongueRespawnDelayRange.x, tongueRespawnDelayRange.y));
        tongue.Phase = FirePhase.Loop;
        tongue.FrameIndex = 0;
        tongue.FrameTimer = 0f;
        if (tongue.Renderer != null)
            tongue.Renderer.enabled = false;
    }

    private void ResetTongue(int index, float initialDelay)
    {
        TongueState tongue = tongues[index];
        tongue.Active = false;
        tongue.Cooldown = Mathf.Max(0f, initialDelay);
        tongue.Phase = FirePhase.Loop;
        tongue.FrameIndex = 0;
        tongue.FrameTimer = 0f;
        if (tongue.Renderer != null)
        {
            tongue.Renderer.enabled = false;
            tongue.Renderer.sortingLayerID = defaultSortingLayerId;
            tongue.Renderer.sortingOrder = defaultSortingOrder + zoneSortingOffset + (index % 3);
        }

        tongues[index] = tongue;
    }

    private Sprite[] GetPhaseFrames(TongueState tongue)
    {
        if (tongue.VariantIndex < 0 || tongue.VariantIndex >= variantCaches.Length)
            return null;

        FireVariantCache variant = variantCaches[tongue.VariantIndex];
        switch (tongue.Phase)
        {
            case FirePhase.Start:
                return variant.startFrames != null && variant.startFrames.Length > 0
                    ? variant.startFrames
                    : variant.loopFrames;
            case FirePhase.End:
                return variant.endFrames != null && variant.endFrames.Length > 0
                    ? variant.endFrames
                    : variant.loopFrames;
            default:
                return variant.loopFrames;
        }
    }

    private float GetPhaseFps(FirePhase phase)
    {
        switch (phase)
        {
            case FirePhase.Start:
                return Mathf.Max(1f, startFps);
            case FirePhase.End:
                return Mathf.Max(1f, endFps);
            default:
                return Mathf.Max(1f, loopFps);
        }
    }

    private int ChooseVariantIndex()
    {
        if (variantCaches == null || variantCaches.Length == 0)
            return -1;

        float totalWeight = 0f;
        for (int i = 0; i < variantCaches.Length; i++)
        {
            if (!variantCaches[i].IsValid)
                continue;

            totalWeight += Mathf.Max(0.001f, variantCaches[i].spawnWeight);
        }

        if (totalWeight <= 0.0001f)
        {
            for (int i = 0; i < variantCaches.Length; i++)
            {
                if (variantCaches[i].IsValid)
                    return i;
            }

            return -1;
        }

        float pick = Random.value * totalWeight;
        float cursor = 0f;
        for (int i = 0; i < variantCaches.Length; i++)
        {
            if (!variantCaches[i].IsValid)
                continue;

            cursor += Mathf.Max(0.001f, variantCaches[i].spawnWeight);
            if (pick <= cursor)
                return i;
        }

        for (int i = variantCaches.Length - 1; i >= 0; i--)
        {
            if (variantCaches[i].IsValid)
                return i;
        }

        return -1;
    }

    private void BuildVariantCache()
    {
        if (fireVariants != null && fireVariants.Length > 0)
        {
            variantCaches = new FireVariantCache[fireVariants.Length];
            for (int i = 0; i < fireVariants.Length; i++)
            {
                FireVariant source = fireVariants[i];
                FireVariantCache cache = new FireVariantCache
                {
                    startFrames = BuildFrames(source.start.stripTexture, source.start.frameCount),
                    loopFrames = BuildFrames(source.loop.stripTexture, source.loop.frameCount),
                    endFrames = BuildFrames(source.end.stripTexture, source.end.frameCount),
                    spawnWeight = Mathf.Max(0.001f, source.spawnWeight),
                    sizeRange = NormalizeRange(source.sizeMultiplierRange, 0.8f, 1.15f)
                };

                variantCaches[i] = cache;
            }
        }
        else
        {
            FireVariantCache fallback = new FireVariantCache
            {
                startFrames = null,
                loopFrames = loopFramesLegacy != null && loopFramesLegacy.Length > 0 ? loopFramesLegacy : null,
                endFrames = null,
                spawnWeight = 1f,
                sizeRange = new Vector2(1f, 1f)
            };

            variantCaches = new[] { fallback };
        }
    }

    private static Vector2 NormalizeRange(Vector2 input, float fallbackMin, float fallbackMax)
    {
        float min = input.x;
        float max = input.y;

        if (min <= 0f || max <= 0f)
        {
            min = fallbackMin;
            max = fallbackMax;
        }

        if (max < min)
        {
            float temp = min;
            min = max;
            max = temp;
        }

        min = Mathf.Max(0.05f, min);
        max = Mathf.Max(min, max);
        return new Vector2(min, max);
    }

    private Sprite[] BuildFrames(Texture2D strip, int frameCount)
    {
        if (strip == null)
            return null;

        int safeCount = Mathf.Max(1, frameCount);
        float safePpu = Mathf.Max(1f, pixelsPerUnit);
        int cacheKey = ComputeStripCacheKey(strip, safeCount, safePpu);

        if (StripFrameCache.TryGetValue(cacheKey, out Sprite[] cachedFrames))
            return cachedFrames;

        int frameWidth = strip.width / safeCount;
        if (frameWidth <= 0)
            return null;

        int realCount = Mathf.Max(1, strip.width / frameWidth);
        Sprite[] builtFrames = new Sprite[realCount];
        for (int i = 0; i < realCount; i++)
        {
            Rect frameRect = new Rect(i * frameWidth, 0f, frameWidth, strip.height);
            Sprite frame = Sprite.Create(
                strip,
                frameRect,
                new Vector2(0.5f, 0.5f),
                safePpu,
                0u,
                SpriteMeshType.FullRect);
            frame.name = $"{strip.name}_runtime_{i}";
            builtFrames[i] = frame;
        }

        StripFrameCache[cacheKey] = builtFrames;
        return builtFrames;
    }

    private static int ComputeStripCacheKey(Texture2D strip, int frameCount, float ppu)
    {
        unchecked
        {
            int key = 17;
            key = key * 31 + strip.GetInstanceID();
            key = key * 31 + frameCount;
            key = key * 31 + Mathf.RoundToInt(ppu * 100f);
            return key;
        }
    }

    private void EnsureTongueRenderers()
    {
        int safeCount = Mathf.Max(1, tongueCount);
        if (tongues != null && tongues.Length == safeCount && AllTonguesReady())
            return;

        var existingChildren = new List<Transform>();
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform child = transform.GetChild(i);
            if (child.name.StartsWith("FireTongue_", StringComparison.Ordinal))
                existingChildren.Add(child);
        }

        tongues = new TongueState[safeCount];
        for (int i = 0; i < safeCount; i++)
        {
            SpriteRenderer tongueRenderer = null;
            string name = $"FireTongue_{i:00}";

            for (int childIndex = existingChildren.Count - 1; childIndex >= 0; childIndex--)
            {
                Transform existing = existingChildren[childIndex];
                if (existing == null || existing.name != name)
                    continue;

                tongueRenderer = existing.GetComponent<SpriteRenderer>();
                existingChildren.RemoveAt(childIndex);
                break;
            }

            if (tongueRenderer == null)
            {
                var go = new GameObject(name);
                go.layer = gameObject.layer;
                Transform childTransform = go.transform;
                childTransform.SetParent(transform, false);
                tongueRenderer = go.AddComponent<SpriteRenderer>();
            }

            if (spriteRenderer != null)
            {
                tongueRenderer.sortingLayerID = defaultSortingLayerId;
                tongueRenderer.sortingOrder = defaultSortingOrder + (i % 3);
                tongueRenderer.sharedMaterial = spriteRenderer.sharedMaterial;
            }

            tongueRenderer.enabled = false;
            tongues[i] = new TongueState { Renderer = tongueRenderer };
        }

        for (int i = 0; i < existingChildren.Count; i++)
        {
            Transform unused = existingChildren[i];
            if (unused != null)
                Destroy(unused.gameObject);
        }
    }

    private bool AllTonguesReady()
    {
        if (tongues == null || tongues.Length == 0)
            return false;

        for (int i = 0; i < tongues.Length; i++)
        {
            if (tongues[i].Renderer == null)
                return false;
        }

        return true;
    }

    private void ApplyRootVisual(float t)
    {
        float currentScale = Mathf.Lerp(startScale, endScale, t) * spawnScaleMultiplier;
        transform.localScale = new Vector3(currentScale, currentScale * groundSquash, 1f);
    }

    private void ApplyBurnTick()
    {
        if (hitBuffer == null || hitBuffer.Length == 0)
            return;

        float radius = Mathf.Max(0.05f, affectRadius * spawnScaleMultiplier);
        int hitCount = Physics2D.OverlapCircleNonAlloc(transform.position, radius, hitBuffer, unitLayer);

        for (int i = 0; i < hitCount; i++)
        {
            Collider2D hit = hitBuffer[i];
            if (hit == null)
                continue;

            UnitHealth health = UnitHealthLookupCache.Resolve(hit);
            if (health == null || health.IsDead)
                continue;

            StatusEffectHandler status = health.StatusEffectHandler;
            if (status != null)
                status.ApplyBurn(burnDuration, Mathf.Max(0, burnTickDamage), Mathf.Max(0.05f, burnTickRate));

            if (directDamagePerTick > 0)
                health.TakePureDamage(directDamagePerTick, DamageType.Fire, DamageFeedbackKind.BurnTick);
        }
    }

    private void ReleaseSelf()
    {
        if (VfxPool.TryGetInstance(out VfxPool vfxPool))
            vfxPool.Release(gameObject);
        else
            Destroy(gameObject);
    }
}

#pragma warning restore CS0649
