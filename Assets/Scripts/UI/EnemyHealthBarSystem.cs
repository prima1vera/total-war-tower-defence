using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class EnemyHealthBarSystem : MonoBehaviour
{
    private const string RuntimeObjectName = "[EnemyHealthBarSystem]";

    private static EnemyHealthBarSystem instance;

    [SerializeField, Min(1)] private int prewarmCount = 32;
    [SerializeField, Min(0.2f)] private float visibleDuration = 1.15f;
    [SerializeField] private Vector2 barSize = new Vector2(52f, 7f);
    [SerializeField, Min(0f)] private float worldVerticalOffset = 0.35f;
    [SerializeField] private Color backgroundColor = new Color(0f, 0f, 0f, 0.55f);
    [SerializeField] private Color fillColor = new Color(0.22f, 0.9f, 0.34f, 1f);
    [SerializeField] private Color lowHealthColor = new Color(1f, 0.28f, 0.2f, 1f);

    private readonly Stack<HealthBarView> pooledViews = new Stack<HealthBarView>(64);
    private readonly List<TrackedBar> activeBars = new List<TrackedBar>(128);
    private readonly Dictionary<UnitHealth, int> indexByUnit = new Dictionary<UnitHealth, int>(128);

    private Canvas canvas;
    private RectTransform canvasRect;
    private RectTransform barsRoot;
    private Camera cachedCamera;

    public static EnemyHealthBarSystem Instance
    {
        get
        {
            if (instance != null)
                return instance;

            instance = FindObjectOfType<EnemyHealthBarSystem>();
            if (instance != null)
                return instance;

            GameObject runtimeRoot = new GameObject(RuntimeObjectName);
            instance = runtimeRoot.AddComponent<EnemyHealthBarSystem>();
            DontDestroyOnLoad(runtimeRoot);
            return instance;
        }
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);

        EnsureCanvas();
        Prewarm(prewarmCount);
    }

    private void OnEnable()
    {
        UnitHealth.GlobalDamageTaken += HandleGlobalDamageTaken;
        UnitHealth.GlobalUnitDied += HandleGlobalUnitDied;
    }

    private void OnDisable()
    {
        UnitHealth.GlobalDamageTaken -= HandleGlobalDamageTaken;
        UnitHealth.GlobalUnitDied -= HandleGlobalUnitDied;

        ReleaseAll();
    }

    private void LateUpdate()
    {
        if (activeBars.Count == 0)
            return;

        EnsureCamera();
        if (cachedCamera == null)
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
            tracked.NormalizedHealth = normalizedHealth;

            SetFill(tracked.View, normalizedHealth);
            activeBars[existingIndex] = tracked;
            return;
        }

        HealthBarView view = AcquireView();
        SetFill(view, normalizedHealth);

        TrackedBar newTracked = new TrackedBar
        {
            Unit = unit,
            UnitTransform = unit.transform,
            Collider = unit.CachedCollider,
            View = view,
            HideAtTime = Time.time + visibleDuration,
            NormalizedHealth = normalizedHealth
        };

        int index = activeBars.Count;
        activeBars.Add(newTracked);
        indexByUnit[unit] = index;

        EnsureCamera();
        if (cachedCamera != null)
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

        Vector3 screenPos = cachedCamera.WorldToScreenPoint(worldPos);
        if (screenPos.z <= 0f)
        {
            if (tracked.View.Root.gameObject.activeSelf)
                tracked.View.Root.gameObject.SetActive(false);

            return;
        }

        if (!tracked.View.Root.gameObject.activeSelf)
            tracked.View.Root.gameObject.SetActive(true);

        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPos, null, out Vector2 localPos);
        tracked.View.Root.anchoredPosition = localPos;
    }

    private HealthBarView AcquireView()
    {
        EnsureCanvas();

        HealthBarView view = pooledViews.Count > 0 ? pooledViews.Pop() : CreateView();
        view.Root.gameObject.SetActive(true);
        view.Root.SetParent(barsRoot, false);
        return view;
    }

    private void ReleaseView(HealthBarView view)
    {
        if (view.Root == null)
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

    private void EnsureCamera()
    {
        if (cachedCamera != null && cachedCamera.isActiveAndEnabled)
            return;

        cachedCamera = Camera.main;

        if (cachedCamera == null)
            cachedCamera = FindObjectOfType<Camera>();
    }

    private void EnsureCanvas()
    {
        if (canvas != null && barsRoot != null)
            return;

        GameObject canvasObject = new GameObject("EnemyHealthBarCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);

        canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 80;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        GraphicRaycaster raycaster = canvasObject.GetComponent<GraphicRaycaster>();
        raycaster.enabled = false;

        canvasRect = canvas.GetComponent<RectTransform>();

        GameObject barsRootObject = new GameObject("BarsRoot", typeof(RectTransform));
        barsRoot = barsRootObject.GetComponent<RectTransform>();
        barsRoot.SetParent(canvas.transform, false);
        barsRoot.anchorMin = Vector2.zero;
        barsRoot.anchorMax = Vector2.one;
        barsRoot.offsetMin = Vector2.zero;
        barsRoot.offsetMax = Vector2.zero;
    }

    private void Prewarm(int count)
    {
        int target = Mathf.Max(0, count);

        while (pooledViews.Count < target)
            pooledViews.Push(CreateView());
    }

    private HealthBarView CreateView()
    {
        EnsureCanvas();

        GameObject rootObject = new GameObject("EnemyHealthBar", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform root = rootObject.GetComponent<RectTransform>();
        root.SetParent(barsRoot, false);
        root.sizeDelta = barSize;
        root.pivot = new Vector2(0.5f, 0f);

        Image background = rootObject.GetComponent<Image>();
        background.raycastTarget = false;
        background.color = backgroundColor;

        GameObject fillObject = new GameObject("Fill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform fillRect = fillObject.GetComponent<RectTransform>();
        fillRect.SetParent(root, false);
        fillRect.anchorMin = new Vector2(0f, 0f);
        fillRect.anchorMax = new Vector2(0f, 1f);
        fillRect.pivot = new Vector2(0f, 0.5f);
        fillRect.anchoredPosition = Vector2.zero;
        fillRect.sizeDelta = new Vector2(barSize.x, 0f);

        Image fillImage = fillObject.GetComponent<Image>();
        fillImage.raycastTarget = false;
        fillImage.color = fillColor;

        rootObject.SetActive(false);

        return new HealthBarView
        {
            Root = root,
            FillRect = fillRect,
            FillImage = fillImage
        };
    }

    private void SetFill(HealthBarView view, float normalizedHealth)
    {
        float clamped = Mathf.Clamp01(normalizedHealth);
        float width = Mathf.Max(1f, barSize.x * clamped);

        view.FillRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
        view.FillImage.color = Color.Lerp(lowHealthColor, fillColor, clamped);
    }

    private struct HealthBarView
    {
        public RectTransform Root;
        public RectTransform FillRect;
        public Image FillImage;
    }

    private struct TrackedBar
    {
        public UnitHealth Unit;
        public Transform UnitTransform;
        public Collider2D Collider;
        public HealthBarView View;
        public float HideAtTime;
        public float NormalizedHealth;
    }
}

