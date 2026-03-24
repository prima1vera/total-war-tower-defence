using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

#pragma warning disable 0649
[DisallowMultipleComponent]
public sealed class BuildPlacePanelPresenter : MonoBehaviour
{
    [Serializable]
    private struct BuildButtonBinding
    {
        public string OptionId;
        public Button Button;
        public TMP_Text LabelText;
    }

    [Header("Dependencies")]
    [SerializeField] private TowerBuildService buildService;
    [SerializeField] private PlayerCurrencyWallet currencyWallet;

    [Header("Panel")]
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private string defaultTitle = "Build";
    [SerializeField] private BuildButtonBinding[] buildButtons = Array.Empty<BuildButtonBinding>();

    [Header("World Anchor")]
    [SerializeField] private bool followSelectedWorldTarget = true;
    [SerializeField] private Camera worldCamera;
    [SerializeField] private Canvas uiCanvas;
    [SerializeField] private RectTransform panelRectTransform;
    [SerializeField, Tooltip("Optional inner content root to move as popup. Use this when panelRoot is full-screen/stretch container.")]
    private RectTransform floatingContentRoot;
    [SerializeField] private Vector3 worldOffset = new Vector3(0f, 0.9f, 0f);
    [SerializeField] private bool clampToCanvas = true;
    [SerializeField] private Vector2 canvasClampPadding = new Vector2(24f, 24f);

    [Header("Labels")]
    [SerializeField] private string currencySuffix = "g";
    [SerializeField] private string unavailableLabel = "N/A";

    private CanvasGroup panelCanvasGroup;
    private bool useCanvasGroupVisibility;
    private bool panelVisible;
    private Transform currentWorldTarget;

    private void Awake()
    {
        InitializeAnchoringReferences();
        InitializePanelVisibilityMode();
        WireButtons();
        SetPanelVisible(false);
    }

    private void OnEnable()
    {
        if (buildService != null)
            buildService.SelectedBuildPlaceChanged += HandleSelectedPlaceChanged;

        if (currencyWallet != null)
            currencyWallet.BalanceChanged += HandleBalanceChanged;

        RefreshPanel();
    }

    private void OnDisable()
    {
        if (buildService != null)
            buildService.SelectedBuildPlaceChanged -= HandleSelectedPlaceChanged;

        if (currencyWallet != null)
            currencyWallet.BalanceChanged -= HandleBalanceChanged;
    }

    private void LateUpdate()
    {
        if (!followSelectedWorldTarget || !panelVisible || currentWorldTarget == null)
            return;

        if (!TryResolveAnchorContext(out RectTransform canvasRect, out Camera sceneCamera, out Camera eventCamera))
            return;

        Vector3 worldPosition = currentWorldTarget.position + worldOffset;
        Vector3 screenPoint = sceneCamera.WorldToScreenPoint(worldPosition);
        if (screenPoint.z <= 0f)
            return;

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPoint, eventCamera, out Vector2 localPoint))
            return;

        if (clampToCanvas)
            localPoint = ClampToCanvas(localPoint, canvasRect);

        RectTransform targetRect = GetFloatingTargetRect();
        if (targetRect != null)
            targetRect.anchoredPosition = localPoint;
    }

    private void InitializePanelVisibilityMode()
    {
        if (panelRoot == null)
            return;

        useCanvasGroupVisibility = panelRoot == gameObject;
        if (!useCanvasGroupVisibility)
            return;

        panelCanvasGroup = panelRoot.GetComponent<CanvasGroup>();
        if (panelCanvasGroup == null)
            panelCanvasGroup = panelRoot.AddComponent<CanvasGroup>();
    }

    private void InitializeAnchoringReferences()
    {
        if (panelRectTransform == null && panelRoot != null)
            panelRectTransform = panelRoot.GetComponent<RectTransform>();

        if (panelRectTransform == null)
            panelRectTransform = GetComponent<RectTransform>();

        if (uiCanvas == null && panelRectTransform != null)
            uiCanvas = panelRectTransform.GetComponentInParent<Canvas>();

        if (floatingContentRoot == null && panelRectTransform != null && panelRectTransform.childCount > 0)
        {
            RectTransform firstChild = panelRectTransform.GetChild(0) as RectTransform;
            if (firstChild != null)
                floatingContentRoot = firstChild;
        }
    }

    private void WireButtons()
    {
        for (int i = 0; i < buildButtons.Length; i++)
        {
            BuildButtonBinding binding = buildButtons[i];
            if (binding.Button == null)
                continue;

            string optionId = binding.OptionId;
            binding.Button.onClick.AddListener(() => HandleBuildClicked(optionId));
        }
    }

    private void HandleSelectedPlaceChanged(BuildPlace _)
    {
        currentWorldTarget = buildService != null && buildService.SelectedBuildPlace != null
            ? buildService.SelectedBuildPlace.transform
            : null;

        RefreshPanel();
    }

    private void HandleBalanceChanged(int _)
    {
        RefreshButtons();
    }

    private void HandleBuildClicked(string optionId)
    {
        if (buildService == null || string.IsNullOrWhiteSpace(optionId))
            return;

        if (buildService.TryBuildOnSelectedPlace(optionId))
            buildService.ClearBuildPlaceSelection();
    }

    private void RefreshPanel()
    {
        BuildPlace selectedPlace = buildService != null ? buildService.SelectedBuildPlace : null;
        bool hasSelection = selectedPlace != null && !selectedPlace.IsOccupied;
        currentWorldTarget = hasSelection ? selectedPlace.transform : null;

        SetPanelVisible(hasSelection);

        if (titleText != null)
            titleText.text = hasSelection ? $"{defaultTitle} #{selectedPlace.PlaceId}" : defaultTitle;

        RefreshButtons();
    }

    private void RefreshButtons()
    {
        BuildPlace selectedPlace = buildService != null ? buildService.SelectedBuildPlace : null;
        bool canBuild = selectedPlace != null && !selectedPlace.IsOccupied;

        for (int i = 0; i < buildButtons.Length; i++)
        {
            BuildButtonBinding binding = buildButtons[i];
            if (binding.Button == null)
                continue;

            TowerBuildOptionDefinition option = default;
            bool isAvailable = canBuild && buildService != null && buildService.TryGetBuildOption(binding.OptionId, out option);
            bool canAfford = isAvailable && (currencyWallet == null || currencyWallet.CanAfford(option.Cost));

            binding.Button.interactable = isAvailable && canAfford;

            if (binding.LabelText == null)
                continue;

            if (!isAvailable)
            {
                binding.LabelText.text = unavailableLabel;
                binding.LabelText.alpha = 0.6f;
                continue;
            }

            binding.LabelText.text = $"{option.DisplayName} ({option.Cost}{currencySuffix})";
            binding.LabelText.alpha = canAfford ? 1f : 0.6f;
        }
    }

    private void SetPanelVisible(bool isVisible)
    {
        panelVisible = isVisible;

        if (panelRoot == null)
            return;

        if (useCanvasGroupVisibility && panelCanvasGroup != null)
        {
            panelCanvasGroup.alpha = isVisible ? 1f : 0f;
            panelCanvasGroup.blocksRaycasts = isVisible;
            panelCanvasGroup.interactable = isVisible;
            return;
        }

        if (panelRoot.activeSelf != isVisible)
            panelRoot.SetActive(isVisible);
    }

    private bool TryResolveAnchorContext(out RectTransform canvasRect, out Camera sceneCamera, out Camera eventCamera)
    {
        canvasRect = null;
        sceneCamera = null;
        eventCamera = null;

        InitializeAnchoringReferences();
        if (panelRectTransform == null || uiCanvas == null)
            return false;

        canvasRect = uiCanvas.transform as RectTransform;
        if (canvasRect == null)
            return false;

        sceneCamera = worldCamera != null ? worldCamera : Camera.main;
        if (sceneCamera == null)
            return false;

        eventCamera = uiCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : uiCanvas.worldCamera;
        if (uiCanvas.renderMode != RenderMode.ScreenSpaceOverlay && eventCamera == null)
            eventCamera = sceneCamera;

        return true;
    }

    private Vector2 ClampToCanvas(Vector2 localPoint, RectTransform canvasRect)
    {
        RectTransform targetRect = GetFloatingTargetRect();
        if (targetRect == null || canvasRect == null)
            return localPoint;

        Rect canvas = canvasRect.rect;
        Rect panel = targetRect.rect;
        Vector2 pivot = targetRect.pivot;

        float halfWidthLeft = panel.width * pivot.x;
        float halfWidthRight = panel.width * (1f - pivot.x);
        float halfHeightBottom = panel.height * pivot.y;
        float halfHeightTop = panel.height * (1f - pivot.y);

        float minX = canvas.xMin + halfWidthLeft + canvasClampPadding.x;
        float maxX = canvas.xMax - halfWidthRight - canvasClampPadding.x;
        float minY = canvas.yMin + halfHeightBottom + canvasClampPadding.y;
        float maxY = canvas.yMax - halfHeightTop - canvasClampPadding.y;

        localPoint.x = Mathf.Clamp(localPoint.x, minX, maxX);
        localPoint.y = Mathf.Clamp(localPoint.y, minY, maxY);
        return localPoint;
    }

    private RectTransform GetFloatingTargetRect()
    {
        if (floatingContentRoot != null)
            return floatingContentRoot;

        return panelRectTransform;
    }
}
#pragma warning restore 0649
