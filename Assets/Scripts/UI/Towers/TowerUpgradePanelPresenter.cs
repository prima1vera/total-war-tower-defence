using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class TowerUpgradePanelPresenter : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private TowerSelectionService selectionService;
    [SerializeField] private PlayerCurrencyWallet currencyWallet;

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
    [SerializeField] private Button sellButton;
    [SerializeField] private TMP_Text sellButtonText;

    [Header("Optional")]
    [SerializeField] private TMP_Text goldText;
    [SerializeField] private string unavailableUpgradeLabel = "MAX";

    private TowerUpgradable selectedTower;

    private void Awake()
    {
        if (upgradeButtonA != null)
            upgradeButtonA.onClick.AddListener(HandleUpgradeA);

        if (upgradeButtonB != null)
            upgradeButtonB.onClick.AddListener(HandleUpgradeB);

        if (sellButton != null)
            sellButton.onClick.AddListener(HandleSell);

        if (panelRoot != null)
            panelRoot.SetActive(false);
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

    private void HandleSelectionChanged(TowerUpgradable tower)
    {
        if (tower != null && (!tower.gameObject.activeInHierarchy || tower.IsSold))
            tower = null;

        if (selectedTower == tower)
        {
            RefreshPanel();
            return;
        }

        DetachTowerEvents();
        selectedTower = tower;
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
            goldText.text = $"Gold: {balance}";

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

        selectedTower.TrySell(currencyWallet);
    }

    private void RefreshPanel()
    {
        bool hasSelection = selectedTower != null && selectedTower.gameObject.activeInHierarchy && !selectedTower.IsSold;

        if (panelRoot != null)
            panelRoot.SetActive(hasSelection);

        if (!hasSelection)
            return;

        if (selectedTower.TryGetCurrentLevel(out TowerUpgradeLevelDefinition level))
        {
            if (towerNameText != null)
                towerNameText.text = selectedTower.TowerDisplayName;

            if (levelText != null)
                levelText.text = $"Level {level.Level}";

            if (damageText != null)
                damageText.text = $"Damage: {level.Stats.Damage}";

            if (rangeText != null)
                rangeText.text = $"Range: {level.Stats.Range:0.0}";

            if (fireRateText != null)
                fireRateText.text = $"Fire Rate: {level.Stats.FireRate:0.00}";

            if (sellButtonText != null)
                sellButtonText.text = $"Sell (+{level.SellValue})";
        }

        RefreshUpgradeButtons();
    }

    private void RefreshUpgradeButtons()
    {
        if (selectedTower == null)
        {
            ApplyButtonState(upgradeButtonA, upgradeButtonAText, false, unavailableUpgradeLabel);
            ApplyButtonState(upgradeButtonB, upgradeButtonBText, false, unavailableUpgradeLabel);

            if (sellButton != null)
                sellButton.interactable = false;

            return;
        }

        TowerUpgradeOptionState optionA = selectedTower.GetUpgradeOptionState(TowerUpgradeSlot.A, currencyWallet);
        TowerUpgradeOptionState optionB = selectedTower.GetUpgradeOptionState(TowerUpgradeSlot.B, currencyWallet);

        string labelA = BuildUpgradeLabel(optionA);
        string labelB = BuildUpgradeLabel(optionB);

        ApplyButtonState(upgradeButtonA, upgradeButtonAText, optionA.IsAvailable && optionA.CanAfford, labelA);
        ApplyButtonState(upgradeButtonB, upgradeButtonBText, optionB.IsAvailable && optionB.CanAfford, labelB);

        if (sellButton != null)
            sellButton.interactable = true;
    }

    private string BuildUpgradeLabel(TowerUpgradeOptionState option)
    {
        if (!option.IsAvailable)
            return unavailableUpgradeLabel;

        string label = string.IsNullOrWhiteSpace(option.Label)
            ? $"Upgrade {option.Slot}"
            : option.Label;

        return $"{label} ({option.Cost})";
    }

    private static void ApplyButtonState(Button button, TMP_Text buttonText, bool interactable, string label)
    {
        if (button != null)
            button.interactable = interactable;

        if (buttonText != null)
            buttonText.text = label;
    }
}
