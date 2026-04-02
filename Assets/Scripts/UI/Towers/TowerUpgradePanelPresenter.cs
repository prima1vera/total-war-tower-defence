using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class TowerUpgradePanelPresenter : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private TowerSelectionService selectionService;
    [SerializeField] private PlayerCurrencyWallet currencyWallet;
    [SerializeField] private TowerSelectionInput selectionInput;

    [Header("Panel")]
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private TMP_Text towerNameText;
    [SerializeField] private TMP_Text levelText;

    [Header("Stats")]
    [SerializeField] private TMP_Text damageText;
    [SerializeField] private TMP_Text rangeText;
    [SerializeField] private TMP_Text fireRateText;

    [Header("Actions")]
    [SerializeField] private Button upgradeButtonA;
    [SerializeField] private TMP_Text upgradeButtonAText;
    [SerializeField] private Button upgradeButtonB;
    [SerializeField] private TMP_Text upgradeButtonBText;
    [SerializeField] private Button upgradeButtonC;
    [SerializeField] private TMP_Text upgradeButtonCText;
    [SerializeField] private Button sellButton;
    [SerializeField] private TMP_Text sellButtonText;
    [SerializeField] private Button rallyPointButton;
    [SerializeField] private TMP_Text rallyPointButtonText;

    [Header("Optional")]
    [SerializeField] private TMP_Text goldText;

    [Header("World Anchor")]
    [SerializeField] private bool followSelectedTowerWorldTarget = true;
    [SerializeField] private Camera worldCamera;
    [SerializeField] private Canvas uiCanvas;
    [SerializeField] private RectTransform panelRectTransform;
    [SerializeField, Tooltip("Optional inner content root to move as popup. Use when panelRoot is stretch/fullscreen container.")]
    private RectTransform floatingContentRoot;
    [SerializeField] private Vector3 worldOffset = new Vector3(0f, 0.95f, 0f);
    [SerializeField] private bool clampToCanvas = true;
    [SerializeField] private Vector2 canvasClampPadding = new Vector2(24f, 24f);

    [Header("Labels")]
    [SerializeField] private string unavailableUpgradeLabel = "MAX";
    [SerializeField] private string levelPrefix = "Lv";
    [SerializeField] private string goldPrefix = "Gold";
    [SerializeField] private string sellLabel = "Sell";
    [SerializeField] private string rallyLabel = "Rally";
    [SerializeField] private string rallyArmedLabel = "Tap Ground";
    [SerializeField] private string currencySuffix = "g";
    [SerializeField] private string damageShortLabel = "DMG";
    [SerializeField] private string rangeShortLabel = "RNG";
    [SerializeField] private string fireRateShortLabel = "SPD";

    private TowerUpgradable selectedTower;
    private CanvasGroup panelCanvasGroup;
    private bool useCanvasGroupVisibility;
    private bool loggedMissingUpgradeCButton;
    private bool panelVisible;
    private Transform currentWorldTarget;

    private void Awake()
    {
        InitializeAnchoringReferences();

        if (upgradeButtonA != null)
            upgradeButtonA.onClick.AddListener(HandleUpgradeA);

        if (upgradeButtonB != null)
            upgradeButtonB.onClick.AddListener(HandleUpgradeB);

        if (upgradeButtonC != null)
            upgradeButtonC.onClick.AddListener(HandleUpgradeC);

        if (sellButton != null)
            sellButton.onClick.AddListener(HandleSell);

        if (rallyPointButton != null)
            rallyPointButton.onClick.AddListener(HandleRallyPoint);

        InitializePanelVisibilityMode();
        SetPanelVisible(false);
    }

    private void OnEnable()
    {
        if (selectionService != null)
            selectionService.SelectionChanged += HandleSelectionChanged;

        if (currencyWallet != null)
            currencyWallet.BalanceChanged += HandleBalanceChanged;

        HandleBalanceChanged(currencyWallet != null ? currencyWallet.Balance : 0);

        if (selectionService != null)
            HandleSelectionChanged(selectionService.SelectedTower);
        else
            RefreshPanel();
    }

    private void OnDisable()
    {
        if (selectionService != null)
            selectionService.SelectionChanged -= HandleSelectionChanged;

        if (currencyWallet != null)
            currencyWallet.BalanceChanged -= HandleBalanceChanged;

        DetachTowerEvents();
    }

    private void LateUpdate()
    {
        if (!followSelectedTowerWorldTarget || !panelVisible || currentWorldTarget == null)
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

    private void HandleSelectionChanged(TowerUpgradable tower)
    {
        if (tower != null && (!tower.gameObject.activeInHierarchy || tower.IsSold))
            tower = null;

        if (selectionInput != null && selectedTower != tower)
            selectionInput.CancelBarracksRallyPlacement();

        if (selectedTower == tower)
        {
            RefreshPanel();
            return;
        }

        DetachTowerEvents();
        selectedTower = tower;
        currentWorldTarget = selectedTower != null ? selectedTower.transform : null;
        AttachTowerEvents();
        RefreshPanel();
    }

    private void AttachTowerEvents()
    {
        if (selectedTower == null)
            return;

        selectedTower.DataChanged += HandleTowerDataChanged;
        selectedTower.Sold += HandleTowerSold;
    }

    private void DetachTowerEvents()
    {
        if (selectedTower == null)
            return;

        selectedTower.DataChanged -= HandleTowerDataChanged;
        selectedTower.Sold -= HandleTowerSold;
    }

    private void HandleTowerDataChanged(TowerUpgradable _)
    {
        RefreshPanel();
    }

    private void HandleTowerSold(TowerUpgradable tower)
    {
        if (selectionService != null && selectionService.SelectedTower == tower)
            selectionService.ClearSelection();

        RefreshPanel();
    }

    private void HandleBalanceChanged(int balance)
    {
        if (goldText != null)
            goldText.text = $"{goldPrefix}: {balance}";

        RefreshUpgradeButtons();
    }

    private void HandleUpgradeA()
    {
        TryUpgrade(TowerUpgradeSlot.A);
    }

    private void HandleUpgradeB()
    {
        TryUpgrade(TowerUpgradeSlot.B);
    }

    private void HandleUpgradeC()
    {
        TryUpgrade(TowerUpgradeSlot.C);
    }

    private void TryUpgrade(TowerUpgradeSlot slot)
    {
        if (selectedTower == null)
            return;

        selectedTower.TryUpgrade(slot, currencyWallet);
    }

    private void HandleSell()
    {
        if (selectedTower == null)
            return;

        if (selectionInput != null)
            selectionInput.CancelBarracksRallyPlacement();

        selectedTower.TrySell(currencyWallet);
    }

    private void HandleRallyPoint()
    {
        if (selectedTower == null || selectionInput == null)
            return;

        if (!selectedTower.TryGetComponent(out BarracksController barracks))
            return;

        if (selectionInput.IsBarracksRallyPlacementArmedFor(barracks))
            selectionInput.CancelBarracksRallyPlacement();
        else
            selectionInput.ArmBarracksRallyPlacement(barracks);

        RefreshBarracksActions();
    }

    private void RefreshPanel()
    {
        bool hasSelection = selectedTower != null && selectedTower.gameObject.activeInHierarchy && !selectedTower.IsSold;
        currentWorldTarget = hasSelection ? selectedTower.transform : null;
        SetPanelVisible(hasSelection);

        if (!hasSelection)
        {
            RefreshUpgradeButtons();
            RefreshBarracksActions();
            return;
        }

        if (selectedTower.TryGetCurrentLevel(out TowerUpgradeLevelDefinition level))
        {
            if (towerNameText != null)
                towerNameText.text = selectedTower.TowerDisplayName;

            if (levelText != null)
                levelText.text = $"{levelPrefix} {level.Level}";

            if (damageText != null)
                damageText.text = $"{damageShortLabel} {level.Stats.Damage}";

            if (rangeText != null)
                rangeText.text = $"{rangeShortLabel} {level.Stats.Range:0.0}";

            if (fireRateText != null)
                fireRateText.text = $"{fireRateShortLabel} {level.Stats.FireRate:0.00}";

            if (sellButtonText != null)
                sellButtonText.text = $"{sellLabel} (+{level.SellValue}{currencySuffix})";
        }

        RefreshUpgradeButtons();
        RefreshBarracksActions();
    }

    private void RefreshUpgradeButtons()
    {
        if (selectedTower == null)
        {
            ApplyButtonState(upgradeButtonA, upgradeButtonAText, false, unavailableUpgradeLabel);
            ApplyButtonState(upgradeButtonB, upgradeButtonBText, false, unavailableUpgradeLabel);
            ApplyButtonState(upgradeButtonC, upgradeButtonCText, false, unavailableUpgradeLabel);

            if (sellButton != null)
                sellButton.interactable = false;

            return;
        }

        TowerUpgradeOptionState optionA = selectedTower.GetUpgradeOptionState(TowerUpgradeSlot.A, currencyWallet);
        TowerUpgradeOptionState optionB = selectedTower.GetUpgradeOptionState(TowerUpgradeSlot.B, currencyWallet);
        TowerUpgradeOptionState optionC = selectedTower.GetUpgradeOptionState(TowerUpgradeSlot.C, currencyWallet);

        if (upgradeButtonC == null && optionC.IsAvailable && !loggedMissingUpgradeCButton)
        {
            Debug.LogWarning("TowerUpgradePanelPresenter: Upgrade C is configured but Upgrade Button C is not assigned in Inspector.", this);
            loggedMissingUpgradeCButton = true;
        }

        string labelA = BuildUpgradeLabel(optionA, "A");
        string labelB = BuildUpgradeLabel(optionB, "B");
        string labelC = BuildUpgradeLabel(optionC, "C");

        ApplyButtonState(upgradeButtonA, upgradeButtonAText, optionA.IsAvailable && optionA.CanAfford, labelA);
        ApplyButtonState(upgradeButtonB, upgradeButtonBText, optionB.IsAvailable && optionB.CanAfford, labelB);
        ApplyButtonState(upgradeButtonC, upgradeButtonCText, optionC.IsAvailable && optionC.CanAfford, labelC);

        if (sellButton != null)
            sellButton.interactable = true;
    }

    private void RefreshBarracksActions()
    {
        if (rallyPointButton == null && rallyPointButtonText == null)
            return;

        BarracksController barracks = null;
        bool hasBarracks = selectedTower != null && selectedTower.TryGetComponent(out barracks);
        bool armed = hasBarracks && selectionInput != null && selectionInput.IsBarracksRallyPlacementArmedFor(barracks);

        if (rallyPointButton != null)
        {
            rallyPointButton.gameObject.SetActive(hasBarracks);
            rallyPointButton.interactable = hasBarracks && selectionInput != null;
        }

        if (rallyPointButtonText != null)
        {
            rallyPointButtonText.text = armed ? rallyArmedLabel : rallyLabel;
            rallyPointButtonText.alpha = hasBarracks ? 1f : 0.6f;
        }
    }

    private string BuildUpgradeLabel(TowerUpgradeOptionState option, string slotLabel)
    {
        if (!option.IsAvailable)
            return unavailableUpgradeLabel;

        string label = string.IsNullOrWhiteSpace(option.Label)
            ? $"Upgrade {slotLabel}"
            : option.Label;

        return $"{label} ({option.Cost}{currencySuffix})";
    }

    private static void ApplyButtonState(Button button, TMP_Text buttonText, bool interactable, string label)
    {
        if (button != null)
            button.interactable = interactable;

        if (buttonText != null)
        {
            buttonText.text = label;
            buttonText.alpha = interactable ? 1f : 0.6f;
        }
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
