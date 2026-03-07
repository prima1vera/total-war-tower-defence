using System;
using UnityEngine;

public enum TowerUpgradeSlot
{
    A,
    B
}

[Serializable]
public struct TowerCombatStats
{
    [Min(1)] public int Damage;
    [Min(0.1f)] public float Range;
    [Min(0.05f)] public float FireRate;

    public static TowerCombatStats Clamp(TowerCombatStats stats)
    {
        stats.Damage = Mathf.Max(1, stats.Damage);
        stats.Range = Mathf.Max(0.1f, stats.Range);
        stats.FireRate = Mathf.Max(0.05f, stats.FireRate);
        return stats;
    }
}

[Serializable]
public struct TowerUpgradeOptionDefinition
{
    public string Label;
    [Min(0)] public int Cost;
    [Tooltip("Set -1 when this upgrade path is unavailable.")]
    public int NextLevelIndex;

    public bool IsConfigured => NextLevelIndex >= 0;
}

[Serializable]
public struct TowerUpgradeLevelDefinition
{
    [Min(1)] public int Level;
    public TowerCombatStats Stats;
    [Min(0)] public int SellValue;
    public TowerUpgradeOptionDefinition UpgradeA;
    public TowerUpgradeOptionDefinition UpgradeB;

    public TowerUpgradeOptionDefinition GetOption(TowerUpgradeSlot slot)
    {
        return slot == TowerUpgradeSlot.A ? UpgradeA : UpgradeB;
    }
}

[CreateAssetMenu(fileName = "TowerUpgradeTree", menuName = "TWTD/Towers/Upgrade Tree")]
public sealed class TowerUpgradeTree : ScriptableObject
{
    [SerializeField] private string towerDisplayName = "Tower";
    [SerializeField] private int startLevelIndex;
    [SerializeField] private TowerUpgradeLevelDefinition[] levels = Array.Empty<TowerUpgradeLevelDefinition>();

    public string TowerDisplayName => string.IsNullOrWhiteSpace(towerDisplayName) ? name : towerDisplayName;
    public int StartLevelIndex => Mathf.Clamp(startLevelIndex, 0, Mathf.Max(0, LevelCount - 1));
    public int LevelCount => levels != null ? levels.Length : 0;

    public bool TryGetLevel(int index, out TowerUpgradeLevelDefinition level)
    {
        if (levels == null || index < 0 || index >= levels.Length)
        {
            level = default;
            return false;
        }

        level = levels[index];
        level.Stats = TowerCombatStats.Clamp(level.Stats);
        return true;
    }
}
