public readonly struct TowerUpgradeOptionState
{
    public TowerUpgradeSlot Slot { get; }
    public bool IsAvailable { get; }
    public bool CanAfford { get; }
    public string Label { get; }
    public int Cost { get; }
    public int NextLevel { get; }

    public TowerUpgradeOptionState(
        TowerUpgradeSlot slot,
        bool isAvailable,
        bool canAfford,
        string label,
        int cost,
        int nextLevel)
    {
        Slot = slot;
        IsAvailable = isAvailable;
        CanAfford = canAfford;
        Label = label;
        Cost = cost;
        NextLevel = nextLevel;
    }
}
