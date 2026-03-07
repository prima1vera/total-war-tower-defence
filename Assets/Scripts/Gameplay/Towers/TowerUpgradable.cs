using System;
using UnityEngine;

[RequireComponent(typeof(Tower))]
public class TowerUpgradable : MonoBehaviour
{
    [SerializeField] private Tower tower;
    [SerializeField] private TowerUpgradeTree upgradeTree;
    [SerializeField] private int startLevelOverride = -1;
    [SerializeField] private bool initializeOnAwake = true;

    private int currentLevelIndex = -1;
    private bool initialized;
    private bool sold;

    public event Action<TowerUpgradable> DataChanged;
    public event Action<TowerUpgradable> Sold;

    public TowerUpgradeTree UpgradeTree => upgradeTree;
    public bool IsSold => sold;
    public int CurrentLevelIndex => currentLevelIndex;
    public string TowerDisplayName => upgradeTree != null ? upgradeTree.TowerDisplayName : gameObject.name;

    private void Awake()
    {
        if (tower == null)
            tower = GetComponent<Tower>();

        if (initializeOnAwake)
            Initialize();
    }

    private void OnEnable()
    {
        if (initializeOnAwake && (!initialized || sold))
            Initialize();
    }

    public void Initialize()
    {
        sold = false;

        if (tower == null)
            tower = GetComponent<Tower>();

        if (upgradeTree == null)
        {
            currentLevelIndex = -1;
            initialized = true;
            DataChanged?.Invoke(this);
            return;
        }

        int desiredStart = startLevelOverride >= 0 ? startLevelOverride : upgradeTree.StartLevelIndex;

        if (!upgradeTree.TryGetLevel(desiredStart, out TowerUpgradeLevelDefinition _))
            desiredStart = upgradeTree.StartLevelIndex;

        currentLevelIndex = desiredStart;
        initialized = true;

        ApplyCurrentStatsToTower();
        DataChanged?.Invoke(this);
    }

    public bool TryGetCurrentLevel(out TowerUpgradeLevelDefinition level)
    {
        if (!initialized)
            Initialize();

        if (upgradeTree == null)
        {
            level = default;
            return false;
        }

        return upgradeTree.TryGetLevel(currentLevelIndex, out level);
    }

    public TowerUpgradeOptionState GetUpgradeOptionState(TowerUpgradeSlot slot, ICurrencyWallet wallet)
    {
        if (!TryGetCurrentLevel(out TowerUpgradeLevelDefinition level))
            return new TowerUpgradeOptionState(slot, false, false, string.Empty, 0, -1);

        TowerUpgradeOptionDefinition option = level.GetOption(slot);
        if (!option.IsConfigured)
            return new TowerUpgradeOptionState(slot, false, false, option.Label, option.Cost, option.NextLevelIndex);

        if (upgradeTree == null || !upgradeTree.TryGetLevel(option.NextLevelIndex, out TowerUpgradeLevelDefinition _))
            return new TowerUpgradeOptionState(slot, false, false, option.Label, option.Cost, option.NextLevelIndex);

        bool canAfford = wallet == null || wallet.CanAfford(option.Cost);
        return new TowerUpgradeOptionState(slot, true, canAfford, option.Label, option.Cost, option.NextLevelIndex);
    }

    public bool TryUpgrade(TowerUpgradeSlot slot, ICurrencyWallet wallet)
    {
        if (sold)
            return false;

        if (!TryGetCurrentLevel(out TowerUpgradeLevelDefinition level))
            return false;

        TowerUpgradeOptionDefinition option = level.GetOption(slot);
        if (!option.IsConfigured)
            return false;

        if (upgradeTree == null || !upgradeTree.TryGetLevel(option.NextLevelIndex, out TowerUpgradeLevelDefinition _))
            return false;

        int cost = Mathf.Max(0, option.Cost);

        if (wallet != null && cost > 0 && !wallet.TrySpend(cost))
            return false;

        currentLevelIndex = option.NextLevelIndex;
        ApplyCurrentStatsToTower();
        DataChanged?.Invoke(this);
        return true;
    }

    public bool TrySell(ICurrencyWallet wallet)
    {
        if (sold)
            return false;

        if (TryGetCurrentLevel(out TowerUpgradeLevelDefinition level) && wallet != null)
            wallet.Add(level.SellValue);

        sold = true;
        Sold?.Invoke(this);

        if (gameObject.activeSelf)
            gameObject.SetActive(false);

        return true;
    }

    private void ApplyCurrentStatsToTower()
    {
        if (tower == null || upgradeTree == null)
            return;

        if (!upgradeTree.TryGetLevel(currentLevelIndex, out TowerUpgradeLevelDefinition level))
            return;

        TowerCombatStats stats = TowerCombatStats.Clamp(level.Stats);
        tower.SetCombatStats(stats.Damage, stats.Range, stats.FireRate);
    }
}
