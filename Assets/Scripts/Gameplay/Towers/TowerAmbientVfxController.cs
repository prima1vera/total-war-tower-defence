using System;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Tower))]
public sealed class TowerAmbientVfxController : MonoBehaviour
{
    [Serializable]
    private struct AmbientStyle
    {
        [Tooltip("Base color used by ambient particles/glow for this tower family.")]
        public Color color;
        [Min(0f), Tooltip("Base particles per second.")]
        public float emissionRate;
        [Min(0f), Tooltip("Particle lifetime speed.")]
        public float particleSpeed;
        [Min(0f), Tooltip("Min particle size.")]
        public float minSize;
        [Min(0f), Tooltip("Max particle size.")]
        public float maxSize;
        [Range(0f, 1f), Tooltip("Base glow alpha.")]
        public float glowBaseAlpha;
        [Range(0f, 1f), Tooltip("Glow pulse amplitude.")]
        public float glowPulseAmplitude;
        [Min(0f), Tooltip("Glow pulse speed.")]
        public float glowPulseSpeed;
    }

    [Header("Scene Wiring")]
    [SerializeField] private Tower tower;
    [SerializeField, Tooltip("Small orbiting sparks.")]
    private ParticleSystem orbitParticles;
    [SerializeField, Tooltip("Soft aura around tower.")]
    private ParticleSystem auraParticles;
    [SerializeField, Tooltip("Optional additive sprite glow (can stay null).")]
    private SpriteRenderer glowSprite;

    [Header("Level Scaling")]
    [SerializeField, Min(0f), Tooltip("Added emission per tower level.")]
    private float emissionPerLevel = 1.2f;
    [SerializeField, Min(0f), Tooltip("Added particle size per tower level.")]
    private float sizePerLevel = 0.008f;

    [Header("Style Presets")]
    [SerializeField] private AmbientStyle baseStyle = new AmbientStyle
    {
        color = new Color(1f, 0.93f, 0.75f, 0.95f),
        emissionRate = 6f,
        particleSpeed = 0.08f,
        minSize = 0.025f,
        maxSize = 0.05f,
        glowBaseAlpha = 0.12f,
        glowPulseAmplitude = 0.04f,
        glowPulseSpeed = 2.1f
    };

    [SerializeField] private AmbientStyle fireStyle = new AmbientStyle
    {
        color = new Color(1f, 0.45f, 0.15f, 1f),
        emissionRate = 11f,
        particleSpeed = 0.14f,
        minSize = 0.03f,
        maxSize = 0.06f,
        glowBaseAlpha = 0.22f,
        glowPulseAmplitude = 0.08f,
        glowPulseSpeed = 3.2f
    };

    [SerializeField] private AmbientStyle frostStyle = new AmbientStyle
    {
        color = new Color(0.45f, 0.84f, 1f, 1f),
        emissionRate = 9f,
        particleSpeed = 0.1f,
        minSize = 0.028f,
        maxSize = 0.055f,
        glowBaseAlpha = 0.2f,
        glowPulseAmplitude = 0.07f,
        glowPulseSpeed = 2.6f
    };

    [SerializeField] private AmbientStyle ironStyle = new AmbientStyle
    {
        color = new Color(0.9f, 0.82f, 0.72f, 0.95f),
        emissionRate = 7f,
        particleSpeed = 0.09f,
        minSize = 0.027f,
        maxSize = 0.052f,
        glowBaseAlpha = 0.16f,
        glowPulseAmplitude = 0.05f,
        glowPulseSpeed = 2.2f
    };

    private ParticleSystemRenderer orbitRenderer;
    private ParticleSystemRenderer auraRenderer;
    private AmbientStyle activeStyle;
    private TowerProjectilePoolKey cachedKey;
    private int cachedLevel;
    private bool hasCachedState;

    private const int OrbitSortingOffset = 1;
    private const int AuraSortingOffset = 0;
    private const int GlowSortingOffset = 0;

    private void Reset()
    {
        tower = GetComponent<Tower>();
    }

    private void Awake()
    {
        if (tower == null)
            tower = GetComponent<Tower>();

        CacheRenderers();
    }

    private void OnEnable()
    {
        if (tower == null)
            return;

        tower.VisualStateChanged += HandleVisualStateChanged;
        RefreshStyle(force: true);
    }

    private void OnDisable()
    {
        if (tower != null)
            tower.VisualStateChanged -= HandleVisualStateChanged;
    }

    private void LateUpdate()
    {
        UpdateGlowPulse();
    }

    private void HandleVisualStateChanged()
    {
        RefreshStyle(force: false);
    }

    private void RefreshStyle(bool force)
    {
        if (tower == null)
            return;

        TowerProjectilePoolKey key = tower.CurrentProjectilePoolKey;
        int level = Mathf.Max(1, tower.CurrentVisualLevel);

        if (!force && hasCachedState && key == cachedKey && level == cachedLevel)
            return;

        hasCachedState = true;
        cachedKey = key;
        cachedLevel = level;
        activeStyle = ResolveStyle(key);

        ApplyParticleStyle(orbitParticles, activeStyle, level, 1f);
        ApplyParticleStyle(auraParticles, activeStyle, level, 1.45f);
        ApplyGlowImmediate();
        SyncSorting();
    }

    private AmbientStyle ResolveStyle(TowerProjectilePoolKey key)
    {
        switch (key)
        {
            case TowerProjectilePoolKey.Fire:
                return fireStyle;
            case TowerProjectilePoolKey.Frost:
                return frostStyle;
            case TowerProjectilePoolKey.Iron:
                return ironStyle;
            default:
                return baseStyle;
        }
    }

    private void ApplyParticleStyle(ParticleSystem particleSystem, AmbientStyle style, int level, float sizeMultiplier)
    {
        if (particleSystem == null)
            return;

        float levelFactor = Mathf.Max(0, level - 1);
        float minSize = Mathf.Max(0f, (style.minSize + sizePerLevel * levelFactor) * sizeMultiplier);
        float maxSize = Mathf.Max(minSize, (style.maxSize + sizePerLevel * levelFactor) * sizeMultiplier);
        float emissionRate = Mathf.Max(0f, style.emissionRate + emissionPerLevel * levelFactor);

        ParticleSystem.MainModule main = particleSystem.main;
        main.startColor = style.color;
        main.startSpeed = Mathf.Max(0f, style.particleSpeed);
        main.startSize = new ParticleSystem.MinMaxCurve(minSize, maxSize);

        ParticleSystem.EmissionModule emission = particleSystem.emission;
        emission.enabled = emissionRate > 0.001f;
        emission.rateOverTime = emissionRate;

        if (emissionRate > 0.001f && !particleSystem.isPlaying)
            particleSystem.Play(true);
    }

    private void UpdateGlowPulse()
    {
        if (glowSprite == null)
            return;

        float baseAlpha = Mathf.Clamp01(activeStyle.glowBaseAlpha);
        float pulseAmplitude = Mathf.Clamp01(activeStyle.glowPulseAmplitude);
        float pulseSpeed = Mathf.Max(0f, activeStyle.glowPulseSpeed);
        float pulse = pulseSpeed <= 0.001f ? 0f : Mathf.Sin(Time.time * pulseSpeed) * pulseAmplitude;

        Color color = activeStyle.color;
        color.a = Mathf.Clamp01(baseAlpha + pulse);
        glowSprite.color = color;
    }

    private void ApplyGlowImmediate()
    {
        if (glowSprite == null)
            return;

        Color color = activeStyle.color;
        color.a = Mathf.Clamp01(activeStyle.glowBaseAlpha);
        glowSprite.color = color;
    }

    private void CacheRenderers()
    {
        orbitRenderer = orbitParticles != null ? orbitParticles.GetComponent<ParticleSystemRenderer>() : null;
        auraRenderer = auraParticles != null ? auraParticles.GetComponent<ParticleSystemRenderer>() : null;
    }

    private void SyncSorting()
    {
        if (tower == null || tower.TowerSpriteRenderer == null)
            return;

        int sortingLayerId = tower.TowerSpriteRenderer.sortingLayerID;
        int baseOrder = tower.TowerSpriteRenderer.sortingOrder;

        if (orbitRenderer != null)
        {
            orbitRenderer.sortingLayerID = sortingLayerId;
            orbitRenderer.sortingOrder = baseOrder + OrbitSortingOffset;
        }

        if (auraRenderer != null)
        {
            auraRenderer.sortingLayerID = sortingLayerId;
            auraRenderer.sortingOrder = baseOrder + AuraSortingOffset;
        }

        if (glowSprite != null)
        {
            glowSprite.sortingLayerID = sortingLayerId;
            glowSprite.sortingOrder = baseOrder + GlowSortingOffset;
        }
    }
}
