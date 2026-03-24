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
    [SerializeField, Min(0.2f)] private float visibleDuration = 1.15f;
    [SerializeField, Min(0f)] private float worldVerticalOffset = 0.35f;
    [SerializeField] private Color fillColor = new Color(0.22f, 0.9f, 0.34f, 1f);
    [SerializeField] private Color lowHealthColor = new Color(1f, 0.28f, 0.2f, 1f);

    private readonly Stack<HealthBarView> pooledViews = new Stack<HealthBarView>(64);
    private readonly List<TrackedBar> activeBars = new List<TrackedBar>(128);
    private readonly Dictionary<UnitHealth, int> indexByUnit = new Dictionary<UnitHealth, int>(128);

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
    }

    private void OnDisable()
    {
        UnitHealth.GlobalDamageTaken -= HandleGlobalDamageTaken;
        UnitHealth.GlobalUnitDied -= HandleGlobalUnitDied;

        if (isWired)
            ReleaseAll();
    }

    private void LateUpdate()
    {
        if (!isWired || activeBars.Count == 0)
            return;

        positionRefreshTimer -= Time.deltaTime;
        if (positionRefreshTimer > 0f)
            return;

        positionRefreshTimer = PositionRefreshInterval;

        if (!EnsureWorldCamera())
            return;

        float now = Time.time;

        for (int i = activeBars.Count - 1; i >= 0; i--)
        {
            TrackedBar tracked = activeBars[i];

            if (ShouldRelease(tracked, now))
            {
                ReleaseAt(i);
                continue;
            }

            UpdateBarPosition(ref tracked);
            activeBars[i] = tracked;
        }
    }

    private void HandleGlobalDamageTaken(DamageFeedbackEvent damageEvent)
    {
        if (damageEvent.Target == null)
            return;

        ShowOrRefresh(damageEvent.Target, damageEvent.NormalizedHealth);
    }

    private void HandleGlobalUnitDied(UnitHealth unit)
    {
        ReleaseFor(unit);
    }

    private void ShowOrRefresh(UnitHealth unit, float normalizedHealth)
    {
        normalizedHealth = Mathf.Clamp01(normalizedHealth);

        if (indexByUnit.TryGetValue(unit, out int existingIndex))
        {
            TrackedBar tracked = activeBars[existingIndex];
            tracked.HideAtTime = Time.time + visibleDuration;

            SetFill(tracked.View, normalizedHealth);
            activeBars[existingIndex] = tracked;
            return;
        }

        HealthBarView view = AcquireView();
        if (!view.IsValid)
            return;

        SetFill(view, normalizedHealth);

        TrackedBar newTracked = new TrackedBar
        {
            Unit = unit,
            UnitTransform = unit.transform,
            Collider = unit.CachedCollider,
            View = view,
            HideAtTime = Time.time + visibleDuration
        };

        int index = activeBars.Count;
        activeBars.Add(newTracked);
        indexByUnit[unit] = index;

        if (EnsureWorldCamera())
        {
            UpdateBarPosition(ref newTracked);
            activeBars[index] = newTracked;
        }
    }

    private bool ShouldRelease(TrackedBar tracked, float now)
    {
        if (tracked.Unit == null)
            return true;

        if (!tracked.Unit.gameObject.activeInHierarchy)
            return true;

        if (tracked.Unit.IsDead)
            return true;

        return now >= tracked.HideAtTime;
    }

    private void UpdateBarPosition(ref TrackedBar tracked)
    {
        if (tracked.UnitTransform == null)
            return;

        Vector3 worldPos = tracked.UnitTransform.position;

        if (tracked.Collider != null)
            worldPos.y = tracked.Collider.bounds.max.y + worldVerticalOffset;
        else
            worldPos.y += worldVerticalOffset;

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

        if (!indexByUnit.TryGetValue(unit, out int index))
            return;

        ReleaseAt(index);
    }

    private void ReleaseAt(int index)
    {
        int lastIndex = activeBars.Count - 1;
        if (index < 0 || index > lastIndex)
            return;

        TrackedBar toRelease = activeBars[index];
        if (toRelease.Unit != null)
            indexByUnit.Remove(toRelease.Unit);

        ReleaseView(toRelease.View);

        if (index != lastIndex)
        {
            TrackedBar moved = activeBars[lastIndex];
            activeBars[index] = moved;

            if (moved.Unit != null)
                indexByUnit[moved.Unit] = index;
        }

        activeBars.RemoveAt(lastIndex);
    }

    private void ReleaseAll()
    {
        for (int i = activeBars.Count - 1; i >= 0; i--)
            ReleaseView(activeBars[i].View);

        activeBars.Clear();
        indexByUnit.Clear();
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

        float maxFillWidth = ResolveFillWidth(root, fillRect);

        root.gameObject.SetActive(false);

        return new HealthBarView
        {
            Root = root,
            FillRect = fillRect,
            FillImage = fillImage,
            MaxFillWidth = maxFillWidth
        };
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

    private void SetFill(HealthBarView view, float normalizedHealth)
    {
        float clamped = Mathf.Clamp01(normalizedHealth);
        float width = Mathf.Max(1f, view.MaxFillWidth * clamped);

        view.FillRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
        view.FillImage.color = Color.Lerp(lowHealthColor, fillColor, clamped);
    }

    private struct HealthBarView
    {
        public RectTransform Root;
        public RectTransform FillRect;
        public Image FillImage;
        public float MaxFillWidth;

        public bool IsValid => Root != null && FillRect != null && FillImage != null;
    }

    private struct TrackedBar
    {
        public UnitHealth Unit;
        public Transform UnitTransform;
        public Collider2D Collider;
        public HealthBarView View;
        public float HideAtTime;
    }
}
