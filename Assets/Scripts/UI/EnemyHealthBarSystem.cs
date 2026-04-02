using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class EnemyHealthBarSystem : MonoBehaviour
{
    private const float PositionRefreshInterval = 1f / 30f;

    private static EnemyHealthBarSystem instance;
    private static bool missingInstanceLogged;

    [Header("Scene Wiring")]
    [SerializeField] private Camera worldCamera;
    [SerializeField] private RectTransform barsRoot;
    [SerializeField] private EnemyHealthBarView barViewPrefab;

    [Header("Behaviour")]
    [SerializeField, Min(1)] private int prewarmCount = 32;
    [SerializeField, Min(0.2f)] private float enemyVisibleDuration = 1.05f;
    [SerializeField, Min(0.2f)] private float defenderVisibleDuration = 1.25f;
    [SerializeField, Min(0f)] private float enemyWorldVerticalOffset = 0.28f;
    [SerializeField, Min(0f)] private float defenderWorldVerticalOffset = 0.18f;
    [SerializeField, Min(0.1f)] private float delayedFillHoldDuration = 0.20f;
    [SerializeField, Min(0.2f)] private float delayedFillCatchupSpeed = 2.2f;
    [SerializeField, Range(0.3f, 1.4f)] private float enemyBarScale = 0.72f;
    [SerializeField, Range(0.3f, 1.4f)] private float defenderBarScale = 0.60f;
    [SerializeField, Min(0.02f)] private float defenderMinWidthScale = 0.65f;

    [Header("Enemy Colors")]
    [SerializeField] private Color fillColor = new Color(0.22f, 0.95f, 0.34f, 1f);
    [SerializeField] private Color lowHealthColor = new Color(0.98f, 0.35f, 0.24f, 1f);

    [Header("Defender Colors")]
    [SerializeField] private Color defenderFillColor = new Color(0.30f, 0.78f, 1f, 1f);
    [SerializeField] private Color defenderLowHealthColor = new Color(0.22f, 0.46f, 1f, 1f);

    [Header("Delayed Damage")]
    [SerializeField] private Color delayedFillColor = new Color(1f, 1f, 1f, 1f);

    private readonly Stack<HealthBarView> pooledViews = new Stack<HealthBarView>(64);
    private readonly List<TrackedBar> activeBars = new List<TrackedBar>(128);
    private readonly Dictionary<int, int> indexByTargetId = new Dictionary<int, int>(128);

    private Camera cachedWorldCamera;
    private Camera uiProjectionCamera;
    private bool isWired;
    private bool loggedMissingWorldCamera;
    private float positionRefreshTimer;

    public static EnemyHealthBarSystem Instance
    {
        get
        {
            if (instance == null && !missingInstanceLogged)
            {
                missingInstanceLogged = true;
                Debug.LogError("EnemyHealthBarSystem instance is missing. Add a scene-wired EnemyHealthBarSystem object to the scene.");
            }

            return instance;
        }
    }

    public static bool TryGetInstance(out EnemyHealthBarSystem system)
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

        positionRefreshTimer = 0f;
        UnitHealth.GlobalDamageTaken += HandleGlobalDamageTaken;
        UnitHealth.GlobalUnitDied += HandleGlobalUnitDied;
        DefenderUnit.GlobalDamageTaken += HandleGlobalDefenderDamageTaken;
        DefenderUnit.GlobalDefenderDied += HandleGlobalDefenderDied;
    }

    private void OnDisable()
    {
        UnitHealth.GlobalDamageTaken -= HandleGlobalDamageTaken;
        UnitHealth.GlobalUnitDied -= HandleGlobalUnitDied;
        DefenderUnit.GlobalDamageTaken -= HandleGlobalDefenderDamageTaken;
        DefenderUnit.GlobalDefenderDied -= HandleGlobalDefenderDied;

        if (isWired)
            ReleaseAll();
    }

    private void LateUpdate()
    {
        if (!isWired || activeBars.Count == 0)
            return;

        bool refreshPositions = false;
        positionRefreshTimer -= Time.deltaTime;
        if (positionRefreshTimer <= 0f)
        {
            positionRefreshTimer = PositionRefreshInterval;
            refreshPositions = EnsureWorldCamera();
        }

        float now = Time.time;
        float deltaTime = Time.deltaTime;

        for (int i = activeBars.Count - 1; i >= 0; i--)
        {
            TrackedBar tracked = activeBars[i];

            if (ShouldRelease(tracked, now))
            {
                ReleaseAt(i);
                continue;
            }

            if (refreshPositions)
                UpdateBarPosition(ref tracked);

            UpdateDelayedFill(ref tracked, now, deltaTime);
            activeBars[i] = tracked;
        }
    }

    private void HandleGlobalDamageTaken(DamageFeedbackEvent damageEvent)
    {
        if (damageEvent.Target == null)
            return;

        ShowOrRefreshEnemy(damageEvent.Target, damageEvent.NormalizedHealth);
    }

    private void HandleGlobalUnitDied(UnitHealth unit)
    {
        ReleaseFor(unit);
    }

    private void HandleGlobalDefenderDamageTaken(DefenderDamageFeedbackEvent damageEvent)
    {
        if (damageEvent.Target == null)
            return;

        ShowOrRefreshDefender(damageEvent.Target, damageEvent.NormalizedHealth);
    }

    private void HandleGlobalDefenderDied(DefenderUnit defender)
    {
        ReleaseFor(defender);
    }

    private void ShowOrRefreshEnemy(UnitHealth unit, float normalizedHealth)
    {
        normalizedHealth = Mathf.Clamp01(normalizedHealth);
        int targetId = unit.GetInstanceID();

        if (indexByTargetId.TryGetValue(targetId, out int existingIndex))
        {
            TrackedBar tracked = activeBars[existingIndex];
            tracked.HideAtTime = Time.time + enemyVisibleDuration;
            SetHealthNormalized(ref tracked, normalizedHealth, fillColor, lowHealthColor, enemyBarScale);
            activeBars[existingIndex] = tracked;
            return;
        }

        HealthBarView view = AcquireView();
        if (!view.IsValid)
            return;

        TrackedBar newTracked = new TrackedBar
        {
            TargetType = HealthBarTargetType.Enemy,
            TargetId = targetId,
            Enemy = unit,
            TargetTransform = unit.transform,
            Collider = unit.CachedCollider,
            View = view,
            HideAtTime = Time.time + enemyVisibleDuration,
            HealthNormalized = normalizedHealth,
            DelayedNormalized = normalizedHealth,
            DelayedHoldUntil = Time.time + delayedFillHoldDuration,
            FullColor = fillColor,
            LowColor = lowHealthColor,
            Scale = enemyBarScale
        };
        ApplyFillVisuals(ref newTracked, true);

        int index = activeBars.Count;
        activeBars.Add(newTracked);
        indexByTargetId[targetId] = index;

        if (EnsureWorldCamera())
        {
            UpdateBarPosition(ref newTracked);
            activeBars[index] = newTracked;
        }
    }

    private void ShowOrRefreshDefender(DefenderUnit defender, float normalizedHealth)
    {
        normalizedHealth = Mathf.Clamp01(normalizedHealth);
        int targetId = defender.GetInstanceID();

        if (indexByTargetId.TryGetValue(targetId, out int existingIndex))
        {
            TrackedBar tracked = activeBars[existingIndex];
            tracked.HideAtTime = Time.time + defenderVisibleDuration;
            SetHealthNormalized(ref tracked, normalizedHealth, defenderFillColor, defenderLowHealthColor, defenderBarScale);
            activeBars[existingIndex] = tracked;
            return;
        }

        HealthBarView view = AcquireView();
        if (!view.IsValid)
            return;

        TrackedBar newTracked = new TrackedBar
        {
            TargetType = HealthBarTargetType.Defender,
            TargetId = targetId,
            Defender = defender,
            TargetTransform = defender.transform,
            Collider = null,
            View = view,
            HideAtTime = Time.time + defenderVisibleDuration,
            HealthNormalized = normalizedHealth,
            DelayedNormalized = normalizedHealth,
            DelayedHoldUntil = Time.time + delayedFillHoldDuration,
            FullColor = defenderFillColor,
            LowColor = defenderLowHealthColor,
            Scale = defenderBarScale
        };
        ApplyFillVisuals(ref newTracked, true);

        int index = activeBars.Count;
        activeBars.Add(newTracked);
        indexByTargetId[targetId] = index;

        if (EnsureWorldCamera())
        {
            UpdateBarPosition(ref newTracked);
            activeBars[index] = newTracked;
        }
    }

    private bool ShouldRelease(TrackedBar tracked, float now)
    {
        if (tracked.TargetType == HealthBarTargetType.Enemy)
        {
            if (tracked.Enemy == null)
                return true;

            if (!tracked.Enemy.gameObject.activeInHierarchy)
                return true;

            if (tracked.Enemy.IsDead)
                return true;

            return now >= tracked.HideAtTime;
        }

        if (tracked.Defender == null)
            return true;

        if (!tracked.Defender.gameObject.activeInHierarchy)
            return true;

        if (!tracked.Defender.IsAlive)
            return true;

        return now >= tracked.HideAtTime;
    }

    private void UpdateBarPosition(ref TrackedBar tracked)
    {
        if (tracked.TargetTransform == null)
            return;

        Vector3 worldPos = tracked.TargetTransform.position;
        float verticalOffset = tracked.TargetType == HealthBarTargetType.Defender ? defenderWorldVerticalOffset : enemyWorldVerticalOffset;

        if (tracked.Collider != null)
            worldPos.y = tracked.Collider.bounds.max.y + verticalOffset;
        else
            worldPos.y += verticalOffset;

        Vector3 screenPos = cachedWorldCamera.WorldToScreenPoint(worldPos);
        if (screenPos.z <= 0f)
        {
            if (tracked.View.Root.gameObject.activeSelf)
                tracked.View.Root.gameObject.SetActive(false);

            return;
        }

        if (!tracked.View.Root.gameObject.activeSelf)
            tracked.View.Root.gameObject.SetActive(true);

        RectTransformUtility.ScreenPointToLocalPointInRectangle(barsRoot, screenPos, uiProjectionCamera, out Vector2 localPos);
        tracked.View.Root.anchoredPosition = localPos;
    }

    private HealthBarView AcquireView()
    {
        while (pooledViews.Count > 0)
        {
            HealthBarView pooled = pooledViews.Pop();
            if (!pooled.IsValid)
                continue;

            pooled.Root.gameObject.SetActive(true);
            pooled.Root.SetParent(barsRoot, false);
            return pooled;
        }

        HealthBarView created = CreateView();
        if (!created.IsValid)
            return default;

        created.Root.gameObject.SetActive(true);
        created.Root.SetParent(barsRoot, false);
        return created;
    }

    private void ReleaseView(HealthBarView view)
    {
        if (!view.IsValid)
            return;

        view.Root.gameObject.SetActive(false);
        view.Root.SetParent(barsRoot, false);
        pooledViews.Push(view);
    }

    private void ReleaseFor(UnitHealth unit)
    {
        if (unit == null)
            return;

        int targetId = unit.GetInstanceID();
        if (!indexByTargetId.TryGetValue(targetId, out int index))
            return;

        ReleaseAt(index);
    }

    private void ReleaseFor(DefenderUnit defender)
    {
        if (defender == null)
            return;

        int targetId = defender.GetInstanceID();
        if (!indexByTargetId.TryGetValue(targetId, out int index))
            return;

        ReleaseAt(index);
    }

    private void ReleaseAt(int index)
    {
        int lastIndex = activeBars.Count - 1;
        if (index < 0 || index > lastIndex)
            return;

        TrackedBar toRelease = activeBars[index];
        if (toRelease.TargetId != 0)
            indexByTargetId.Remove(toRelease.TargetId);

        ReleaseView(toRelease.View);

        if (index != lastIndex)
        {
            TrackedBar moved = activeBars[lastIndex];
            activeBars[index] = moved;

            if (moved.TargetId != 0)
                indexByTargetId[moved.TargetId] = index;
        }

        activeBars.RemoveAt(lastIndex);
    }

    private void ReleaseAll()
    {
        for (int i = activeBars.Count - 1; i >= 0; i--)
            ReleaseView(activeBars[i].View);

        activeBars.Clear();
        indexByTargetId.Clear();
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
            Debug.LogError("EnemyHealthBarSystem: World Camera is not assigned or disabled. Assign Main Camera in inspector.", this);
        }

        return false;
    }

    private bool ValidateSceneWiring()
    {
        bool valid = true;

        if (worldCamera == null)
        {
            Debug.LogError("EnemyHealthBarSystem: World Camera is not assigned.", this);
            valid = false;
        }

        if (barsRoot == null)
        {
            Debug.LogError("EnemyHealthBarSystem: Bars Root is not assigned.", this);
            valid = false;
        }

        if (barViewPrefab == null)
        {
            Debug.LogError("EnemyHealthBarSystem: Bar View Prefab is not assigned.", this);
            valid = false;
        }

        if (!valid)
            return false;

        Canvas canvas = barsRoot.GetComponentInParent<Canvas>();
        if (canvas == null)
        {
            Debug.LogError("EnemyHealthBarSystem: Bars Root must be under a Canvas.", this);
            return false;
        }

        if (canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            uiProjectionCamera = null;
        else
            uiProjectionCamera = canvas.worldCamera;

        if (canvas.renderMode != RenderMode.ScreenSpaceOverlay && uiProjectionCamera == null)
            Debug.LogWarning("EnemyHealthBarSystem: Canvas is camera/world mode but Canvas.worldCamera is not assigned.", this);

        if (!barViewPrefab.IsConfigured)
        {
            Debug.LogError("EnemyHealthBarSystem: Bar View Prefab is missing references. Open prefab and assign Root/FillRect/FillImage.", barViewPrefab);
            return false;
        }

        return true;
    }

    private void Prewarm(int count)
    {
        int target = Mathf.Max(0, count);

        while (pooledViews.Count < target)
        {
            HealthBarView view = CreateView();
            if (!view.IsValid)
                break;

            pooledViews.Push(view);
        }
    }

    private HealthBarView CreateView()
    {
        EnemyHealthBarView instanceView = Instantiate(barViewPrefab, barsRoot);
        if (instanceView == null)
            return default;

        if (!instanceView.IsConfigured)
        {
            Debug.LogError("EnemyHealthBarSystem: Spawned health bar view is not configured.", instanceView);
            Destroy(instanceView.gameObject);
            return default;
        }

        RectTransform root = instanceView.Root;
        RectTransform fillRect = instanceView.FillRect;
        Image fillImage = instanceView.FillImage;
        RectTransform delayedFillRect = instanceView.DelayedFillRect;
        Image delayedFillImage = instanceView.DelayedFillImage;

        EnsureDelayedFillVisual(root, fillRect, ref delayedFillRect, ref delayedFillImage);

        float maxFillWidth = ResolveFillWidth(root, fillRect);

        if (delayedFillImage != null)
            delayedFillImage.color = delayedFillColor;

        root.gameObject.SetActive(false);

        return new HealthBarView
        {
            Root = root,
            FillRect = fillRect,
            FillImage = fillImage,
            DelayedFillRect = delayedFillRect,
            DelayedFillImage = delayedFillImage,
            MaxFillWidth = maxFillWidth
        };
    }

    private static void EnsureDelayedFillVisual(
        RectTransform root,
        RectTransform fillRect,
        ref RectTransform delayedFillRect,
        ref Image delayedFillImage)
    {
        if (root == null || fillRect == null)
            return;

        if (delayedFillRect == null || delayedFillImage == null)
        {
            Transform existing = root.Find("DamageFill");
            if (existing != null)
            {
                delayedFillRect = existing as RectTransform;
                delayedFillImage = existing.GetComponent<Image>();
            }
        }

        if (delayedFillRect == null || delayedFillImage == null)
        {
            GameObject delayedObject = new GameObject("DamageFill", typeof(RectTransform), typeof(Image));
            delayedFillRect = delayedObject.GetComponent<RectTransform>();
            delayedFillImage = delayedObject.GetComponent<Image>();
            delayedFillRect.SetParent(fillRect.parent, false);
        }

        delayedFillRect.anchorMin = fillRect.anchorMin;
        delayedFillRect.anchorMax = fillRect.anchorMax;
        delayedFillRect.pivot = fillRect.pivot;
        delayedFillRect.anchoredPosition = fillRect.anchoredPosition;
        delayedFillRect.sizeDelta = fillRect.sizeDelta;
        delayedFillRect.localScale = fillRect.localScale;
        delayedFillRect.localRotation = fillRect.localRotation;
        delayedFillRect.SetSiblingIndex(fillRect.GetSiblingIndex());
    }

    private static float ResolveFillWidth(RectTransform root, RectTransform fillRect)
    {
        float width = 0f;

        if (fillRect != null)
            width = fillRect.sizeDelta.x;

        if (width <= 0.01f && fillRect != null)
            width = fillRect.rect.width;

        if (width <= 0.01f && root != null)
            width = root.rect.width;

        return Mathf.Max(1f, width);
    }

    private void SetHealthNormalized(ref TrackedBar tracked, float normalizedHealth, Color fullColor, Color lowColor, float scale)
    {
        float clamped = Mathf.Clamp01(normalizedHealth);
        tracked.FullColor = fullColor;
        tracked.LowColor = lowColor;
        tracked.Scale = Mathf.Max(0.3f, scale);

        if (clamped < tracked.HealthNormalized - 0.0001f)
            tracked.DelayedHoldUntil = Time.time + delayedFillHoldDuration;

        tracked.HealthNormalized = clamped;
        ApplyFillVisuals(ref tracked, false);
    }

    private void UpdateDelayedFill(ref TrackedBar tracked, float now, float deltaTime)
    {
        if (!tracked.View.HasDelayed)
            return;

        if (now >= tracked.DelayedHoldUntil && tracked.DelayedNormalized > tracked.HealthNormalized)
        {
            float speed = Mathf.Max(0.2f, delayedFillCatchupSpeed);
            tracked.DelayedNormalized = Mathf.MoveTowards(
                tracked.DelayedNormalized,
                tracked.HealthNormalized,
                speed * deltaTime);
        }

        ApplyDelayedFillVisual(tracked.View, tracked.DelayedNormalized, tracked.TargetType);
    }

    private void ApplyFillVisuals(ref TrackedBar tracked, bool forceDelayedToCurrent)
    {
        HealthBarView view = tracked.View;
        float scale = Mathf.Max(0.3f, tracked.Scale);

        if (view.Root != null)
            view.Root.localScale = new Vector3(scale, scale, 1f);

        ApplyMainFillVisual(view, tracked.HealthNormalized, tracked.FullColor, tracked.LowColor, tracked.TargetType);

        if (forceDelayedToCurrent)
            tracked.DelayedNormalized = tracked.HealthNormalized;

        ApplyDelayedFillVisual(view, tracked.DelayedNormalized, tracked.TargetType);
    }

    private void ApplyMainFillVisual(HealthBarView view, float normalizedHealth, Color fullColor, Color lowColor, HealthBarTargetType targetType)
    {
        float clamped = Mathf.Clamp01(normalizedHealth);
        float width = clamped <= 0.0001f
            ? 0f
            : Mathf.Max(GetMinVisibleWidth(view, targetType), view.MaxFillWidth * clamped);
        view.FillRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
        view.FillImage.color = Color.Lerp(lowColor, fullColor, clamped);
    }

    private void ApplyDelayedFillVisual(HealthBarView view, float normalizedHealth, HealthBarTargetType targetType)
    {
        if (!view.HasDelayed)
            return;

        float clamped = Mathf.Clamp01(normalizedHealth);
        float width = clamped <= 0.0001f
            ? 0f
            : Mathf.Max(GetMinVisibleWidth(view, targetType), view.MaxFillWidth * clamped);
        view.DelayedFillRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
    }

    private float GetMinVisibleWidth(HealthBarView view, HealthBarTargetType targetType)
    {
        float minScale = targetType == HealthBarTargetType.Defender
            ? Mathf.Clamp01(defenderMinWidthScale)
            : 1f;
        return Mathf.Max(0.25f, view.MaxFillWidth * minScale * 0.05f);
    }

    private struct HealthBarView
    {
        public RectTransform Root;
        public RectTransform FillRect;
        public Image FillImage;
        public RectTransform DelayedFillRect;
        public Image DelayedFillImage;
        public float MaxFillWidth;

        public bool IsValid => Root != null && FillRect != null && FillImage != null;
        public bool HasDelayed => DelayedFillRect != null && DelayedFillImage != null;
    }

    private struct TrackedBar
    {
        public HealthBarTargetType TargetType;
        public int TargetId;
        public UnitHealth Enemy;
        public DefenderUnit Defender;
        public Transform TargetTransform;
        public Collider2D Collider;
        public HealthBarView View;
        public float HideAtTime;
        public float HealthNormalized;
        public float DelayedNormalized;
        public float DelayedHoldUntil;
        public Color FullColor;
        public Color LowColor;
        public float Scale;
    }

    private enum HealthBarTargetType
    {
        Enemy = 0,
        Defender = 1
    }
}
