using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class DamageNumberSystem : MonoBehaviour
{
    private static DamageNumberSystem instance;
    private static bool missingInstanceLogged;

    [Header("Scene Wiring")]
    [SerializeField] private Camera worldCamera;
    [SerializeField] private RectTransform numbersRoot;
    [SerializeField] private DamageNumberView numberViewPrefab;

    [Header("Pooling")]
    [SerializeField, Min(1)] private int prewarmCount = 32;
    [SerializeField, Min(1)] private int maxActiveNumbers = 160;

    [Header("Pixel Crispness")]
    [SerializeField, Tooltip("Snap anchored position to whole pixels to avoid sub-pixel blur.")]
    private bool snapToWholePixels = true;
    [SerializeField, Min(1f), Tooltip("Pixel snap step in UI units (usually 1).")]
    private float positionSnapStep = 1f;

    [Header("World Placement")]
    [SerializeField] private float worldVerticalOffset = 0.25f;
    [SerializeField] private Vector2 worldHorizontalJitterRange = new Vector2(-0.12f, 0.12f);
    [SerializeField] private Vector2 worldVerticalJitterRange = new Vector2(0f, 0.08f);

    [Header("Motion")]
    [SerializeField] private Vector2 travelDurationRange = new Vector2(0.45f, 0.78f);
    [SerializeField] private Vector2 riseDistanceRange = new Vector2(0.42f, 0.86f);
    [SerializeField] private Vector2 driftXRange = new Vector2(-0.08f, 0.08f);
    [SerializeField, Range(0f, 1f)] private float fadeStartsAt = 0.56f;
    [SerializeField, Min(0f)] private float popScaleMultiplier = 0.24f;

    [Header("Size Rules")]
    [SerializeField] private Vector2 baseScaleRange = new Vector2(0.85f, 1.35f);
    [SerializeField, Min(0f)] private float randomScaleJitter = 0.08f;
    [SerializeField, Min(0f)] private float dotScaleMultiplier = 0.85f;
    [SerializeField, Min(0f)] private float fatalScaleMultiplier = 1.2f;
    [SerializeField, Range(0f, 2f)] private float mediumDamageRatio = 0.35f;
    [SerializeField, Range(0f, 2f)] private float heavyDamageRatio = 0.7f;
    [SerializeField, Min(0f)] private float mediumDamageScaleBonus = 0.08f;
    [SerializeField, Min(0f)] private float heavyDamageScaleBonus = 0.14f;

    [Header("Color Rules")]
    [SerializeField] private Color normalDirectColor = new Color(1f, 1f, 1f, 1f);
    [SerializeField] private Color fireDirectColor = new Color(1f, 0.56f, 0.2f, 1f);
    [SerializeField] private Color iceDirectColor = new Color(0.38f, 0.82f, 1f, 1f);
    [SerializeField] private Color dotTickColor = new Color(1f, 0.9f, 0.34f, 1f);
    [SerializeField] private Color burnTickColor = new Color(1f, 0.68f, 0.2f, 1f);
    [SerializeField] private Color fatalColor = new Color(1f, 0.26f, 0.26f, 1f);

    [ContextMenu("Apply Pixel Combat Preset")]
    private void ApplyPixelCombatPreset()
    {
        prewarmCount = 48;
        maxActiveNumbers = 180;

        snapToWholePixels = true;
        positionSnapStep = 1f;

        worldVerticalOffset = 0.33f;
        worldHorizontalJitterRange = new Vector2(-0.1f, 0.1f);
        worldVerticalJitterRange = new Vector2(0f, 0.05f);

        travelDurationRange = new Vector2(0.42f, 0.62f);
        riseDistanceRange = new Vector2(0.38f, 0.62f);
        driftXRange = new Vector2(-0.06f, 0.06f);
        fadeStartsAt = 0.46f;
        popScaleMultiplier = 0.08f;

        baseScaleRange = new Vector2(1.06f, 1.68f);
        randomScaleJitter = 0.04f;
        dotScaleMultiplier = 0.74f;
        fatalScaleMultiplier = 1.28f;
        mediumDamageRatio = 0.22f;
        heavyDamageRatio = 0.52f;
        mediumDamageScaleBonus = 0.1f;
        heavyDamageScaleBonus = 0.18f;

        normalDirectColor = new Color(1f, 1f, 1f, 1f);
        fireDirectColor = new Color(1f, 0.42f, 0.1f, 1f);
        iceDirectColor = new Color(0.22f, 0.9f, 1f, 1f);
        dotTickColor = new Color(1f, 0.88f, 0.16f, 1f);
        burnTickColor = new Color(1f, 0.62f, 0.12f, 1f);
        fatalColor = new Color(1f, 0.12f, 0.12f, 1f);
    }

    private readonly Stack<NumberView> pooledViews = new Stack<NumberView>(96);
    private readonly List<ActiveNumber> activeNumbers = new List<ActiveNumber>(192);

    private Camera cachedWorldCamera;
    private Camera uiProjectionCamera;
    private bool isWired;
    private bool loggedMissingWorldCamera;

    public static DamageNumberSystem Instance
    {
        get
        {
            if (instance == null && !missingInstanceLogged)
            {
                missingInstanceLogged = true;
                Debug.LogError("DamageNumberSystem instance is missing. Add and wire a scene DamageNumberSystem object.");
            }

            return instance;
        }
    }

    public static bool TryGetInstance(out DamageNumberSystem system)
    {
        system = instance;
        return system != null;
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        missingInstanceLogged = false;

        isWired = ValidateSceneWiring();
        if (!isWired)
        {
            enabled = false;
            return;
        }

        cachedWorldCamera = worldCamera;
        Prewarm(prewarmCount);
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }

    private void OnEnable()
    {
        if (!isWired)
            return;

        UnitHealth.GlobalDamageTaken += HandleGlobalDamageTaken;
    }

    private void OnDisable()
    {
        UnitHealth.GlobalDamageTaken -= HandleGlobalDamageTaken;

        if (isWired)
            ReleaseAll();
    }

    private void LateUpdate()
    {
        if (!isWired || activeNumbers.Count == 0)
            return;

        if (!EnsureWorldCamera())
            return;

        float now = Time.time;

        for (int i = activeNumbers.Count - 1; i >= 0; i--)
        {
            ActiveNumber active = activeNumbers[i];
            float normalized = (now - active.StartTime) / active.Lifetime;
            if (normalized >= 1f)
            {
                ReleaseAt(i);
                continue;
            }

            UpdateActiveView(ref active, normalized);
            activeNumbers[i] = active;
        }
    }

    private void HandleGlobalDamageTaken(DamageFeedbackEvent damageEvent)
    {
        if (damageEvent.Target == null || damageEvent.Amount <= 0)
            return;

        if (activeNumbers.Count >= Mathf.Max(1, maxActiveNumbers))
            ReleaseAt(0);

        NumberView view = AcquireView();
        if (!view.IsValid)
            return;

        Color color = ResolveColor(damageEvent);
        float scale = ResolveScale(damageEvent);
        Vector3 startWorldPos = ResolveWorldSpawnPosition(damageEvent.Target);

        view.Text.color = color;
        view.Text.SetText("{0}", damageEvent.Amount);
        view.Root.localScale = Vector3.one * scale;
        view.CanvasGroup.alpha = 1f;
        view.Root.gameObject.SetActive(true);

        float lifetime = SamplePositiveRange(travelDurationRange, 0.58f);
        float rise = SamplePositiveRange(riseDistanceRange, 0.52f);
        Vector2 drift = new Vector2(Random.Range(driftXRange.x, driftXRange.y), 0f);

        ActiveNumber active = new ActiveNumber
        {
            View = view,
            StartWorld = startWorldPos,
            Drift = drift,
            RiseDistance = rise,
            StartTime = Time.time,
            Lifetime = lifetime,
            BaseScale = scale
        };

        UpdateActiveView(ref active, 0f);
        activeNumbers.Add(active);
    }

    private Vector3 ResolveWorldSpawnPosition(UnitHealth target)
    {
        Vector3 worldPos;
        Collider2D col = target.CachedCollider;

        if (col != null)
        {
            Bounds bounds = col.bounds;
            worldPos = new Vector3(bounds.center.x, bounds.max.y, 0f);
        }
        else
        {
            worldPos = target.transform.position;
        }

        worldPos.y += worldVerticalOffset;
        worldPos.x += Random.Range(worldHorizontalJitterRange.x, worldHorizontalJitterRange.y);
        worldPos.y += Random.Range(worldVerticalJitterRange.x, worldVerticalJitterRange.y);
        worldPos.z = 0f;

        return worldPos;
    }

    private void UpdateActiveView(ref ActiveNumber active, float normalized)
    {
        float eased = EaseOutQuadratic(normalized);
        Vector3 worldPos = active.StartWorld;
        worldPos.x += active.Drift.x * normalized;
        worldPos.y += active.RiseDistance * eased;

        Vector3 screenPos = cachedWorldCamera.WorldToScreenPoint(worldPos);
        if (screenPos.z <= 0f)
        {
            if (active.View.Root.gameObject.activeSelf)
                active.View.Root.gameObject.SetActive(false);

            return;
        }

        if (!active.View.Root.gameObject.activeSelf)
            active.View.Root.gameObject.SetActive(true);

        RectTransformUtility.ScreenPointToLocalPointInRectangle(numbersRoot, screenPos, uiProjectionCamera, out Vector2 localPos);

        if (snapToWholePixels)
        {
            float snap = Mathf.Max(1f, positionSnapStep);
            localPos.x = Mathf.Round(localPos.x / snap) * snap;
            localPos.y = Mathf.Round(localPos.y / snap) * snap;
        }

        active.View.Root.anchoredPosition = localPos;

        float pop = 1f + popScaleMultiplier * (1f - normalized);
        active.View.Root.localScale = Vector3.one * (active.BaseScale * pop);
        active.View.CanvasGroup.alpha = ResolveAlpha(normalized);
    }

    private float ResolveAlpha(float normalized)
    {
        float fadeStart = Mathf.Clamp01(fadeStartsAt);
        if (normalized <= fadeStart)
            return 1f;

        float fadeProgress = (normalized - fadeStart) / Mathf.Max(0.001f, 1f - fadeStart);
        return 1f - Mathf.Clamp01(fadeProgress);
    }

    private Color ResolveColor(DamageFeedbackEvent damageEvent)
    {
        if (damageEvent.IsFatal)
            return fatalColor;

        if (damageEvent.FeedbackKind == DamageFeedbackKind.BurnTick)
            return burnTickColor;

        if (damageEvent.FeedbackKind == DamageFeedbackKind.DotTick)
            return dotTickColor;

        switch (damageEvent.DamageType)
        {
            case DamageType.Fire:
                return fireDirectColor;
            case DamageType.Ice:
                return iceDirectColor;
            default:
                return normalDirectColor;
        }
    }

    private float ResolveScale(DamageFeedbackEvent damageEvent)
    {
        float damageRatio = Mathf.Clamp01((float)damageEvent.Amount / Mathf.Max(1, damageEvent.MaxHealth));
        float scale = Mathf.Lerp(baseScaleRange.x, baseScaleRange.y, damageRatio);
        scale += Random.Range(-randomScaleJitter, randomScaleJitter);

        if (damageEvent.FeedbackKind != DamageFeedbackKind.DirectHit)
            scale *= dotScaleMultiplier;

        if (damageRatio >= mediumDamageRatio)
            scale += mediumDamageScaleBonus;

        if (damageRatio >= heavyDamageRatio)
            scale += heavyDamageScaleBonus;

        if (damageEvent.IsFatal)
            scale *= fatalScaleMultiplier;

        return Mathf.Max(0.05f, scale);
    }

    private NumberView AcquireView()
    {
        while (pooledViews.Count > 0)
        {
            NumberView pooled = pooledViews.Pop();
            if (!pooled.IsValid)
                continue;

            pooled.Root.gameObject.SetActive(true);
            pooled.Root.SetParent(numbersRoot, false);
            return pooled;
        }

        NumberView created = CreateView();
        if (!created.IsValid)
            return default;

        created.Root.gameObject.SetActive(true);
        created.Root.SetParent(numbersRoot, false);
        return created;
    }

    private void ReleaseView(NumberView view)
    {
        if (!view.IsValid)
            return;

        view.Root.gameObject.SetActive(false);
        view.Root.SetParent(numbersRoot, false);
        pooledViews.Push(view);
    }

    private void ReleaseAt(int index)
    {
        int lastIndex = activeNumbers.Count - 1;
        if (index < 0 || index > lastIndex)
            return;

        ActiveNumber toRelease = activeNumbers[index];
        ReleaseView(toRelease.View);

        if (index != lastIndex)
            activeNumbers[index] = activeNumbers[lastIndex];

        activeNumbers.RemoveAt(lastIndex);
    }

    private void ReleaseAll()
    {
        for (int i = activeNumbers.Count - 1; i >= 0; i--)
            ReleaseView(activeNumbers[i].View);

        activeNumbers.Clear();
    }

    private void Prewarm(int count)
    {
        int target = Mathf.Max(0, count);

        while (pooledViews.Count < target)
        {
            NumberView view = CreateView();
            if (!view.IsValid)
                break;

            pooledViews.Push(view);
        }
    }

    private NumberView CreateView()
    {
        DamageNumberView viewInstance = Instantiate(numberViewPrefab, numbersRoot);
        if (viewInstance == null)
            return default;

        if (!viewInstance.IsConfigured)
        {
            Debug.LogError("DamageNumberSystem: DamageNumberView prefab has missing references (Root/Text/CanvasGroup).", viewInstance);
            Destroy(viewInstance.gameObject);
            return default;
        }

        if (viewInstance.GetComponent<Canvas>() != null)
            Debug.LogWarning("DamageNumberSystem: DamageNumberView prefab should not have its own Canvas component.", viewInstance);

        viewInstance.Root.gameObject.SetActive(false);

        return new NumberView
        {
            Root = viewInstance.Root,
            Text = viewInstance.ValueText,
            CanvasGroup = viewInstance.CanvasGroup
        };
    }

    private bool EnsureWorldCamera()
    {
        if (cachedWorldCamera != null && cachedWorldCamera.isActiveAndEnabled)
            return true;

        if (worldCamera != null && worldCamera.isActiveAndEnabled)
        {
            cachedWorldCamera = worldCamera;
            loggedMissingWorldCamera = false;
            return true;
        }

        cachedWorldCamera = null;

        if (!loggedMissingWorldCamera)
        {
            loggedMissingWorldCamera = true;
            Debug.LogError("DamageNumberSystem: World Camera is not assigned or disabled.", this);
        }

        return false;
    }

    private bool ValidateSceneWiring()
    {
        bool valid = true;

        if (worldCamera == null)
        {
            Debug.LogError("DamageNumberSystem: World Camera is not assigned.", this);
            valid = false;
        }

        if (numbersRoot == null)
        {
            Debug.LogError("DamageNumberSystem: Numbers Root is not assigned.", this);
            valid = false;
        }

        if (numberViewPrefab == null)
        {
            Debug.LogError("DamageNumberSystem: Number View Prefab is not assigned.", this);
            valid = false;
        }

        if (!valid)
            return false;

        Canvas canvas = numbersRoot.GetComponentInParent<Canvas>();
        if (canvas == null)
        {
            Debug.LogError("DamageNumberSystem: Numbers Root must be under a Canvas.", this);
            return false;
        }

        if (canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            uiProjectionCamera = null;
        else
            uiProjectionCamera = canvas.worldCamera;

        if (canvas.renderMode != RenderMode.ScreenSpaceOverlay && uiProjectionCamera == null)
            Debug.LogWarning("DamageNumberSystem: Canvas is in camera/world mode and Canvas.worldCamera is not assigned.", this);

        if (!numberViewPrefab.IsConfigured)
        {
            Debug.LogError("DamageNumberSystem: Number View Prefab has missing references.", numberViewPrefab);
            return false;
        }

        return true;
    }

    private static float EaseOutQuadratic(float t)
    {
        float clamped = Mathf.Clamp01(t);
        return 1f - (1f - clamped) * (1f - clamped);
    }

    private static float SamplePositiveRange(Vector2 range, float fallback)
    {
        float min = Mathf.Max(0.01f, Mathf.Min(range.x, range.y));
        float max = Mathf.Max(min, Mathf.Max(range.x, range.y));
        float value = Random.Range(min, max);
        return value > 0.001f ? value : Mathf.Max(0.01f, fallback);
    }

    private struct NumberView
    {
        public RectTransform Root;
        public TMP_Text Text;
        public CanvasGroup CanvasGroup;

        public bool IsValid => Root != null && Text != null && CanvasGroup != null;
    }

    private struct ActiveNumber
    {
        public NumberView View;
        public Vector3 StartWorld;
        public Vector2 Drift;
        public float RiseDistance;
        public float StartTime;
        public float Lifetime;
        public float BaseScale;
    }
}
