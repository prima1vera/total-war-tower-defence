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
    [Tooltip("Button text shown in the panel, for example: Fire Tower.")]
    public string Label;

    [Min(0)]
    [Tooltip("How much gold this upgrade costs.")]
    public int Cost;

    [Tooltip("Target level index in the Levels array (0-based). Set -1 when this upgrade is unavailable.")]
    public int NextLevelIndex;

    public bool IsConfigured => NextLevelIndex >= 0;
}

[Serializable]
public struct TowerUpgradeLevelDefinition
{
    [Min(1)]
    [Tooltip("Displayed tower level number in UI. This is not the array index.")]
    public int Level;

    public TowerCombatStats Stats;

    [Min(0)]
    [Tooltip("Gold returned on sell from this level.")]
    public int SellValue;

    [Tooltip("Optional name shown in UI when this level is active.")]
    public string DisplayNameOverride;

    [Tooltip("Optional evolution profile: projectile source + visual overrides.")]
    public TowerEvolutionProfile EvolutionProfile;

    public TowerUpgradeOptionDefinition UpgradeA;
    public TowerUpgradeOptionDefinition UpgradeB;

    public TowerUpgradeOptionDefinition GetOption(TowerUpgradeSlot slot)
    {
        return slot == TowerUpgradeSlot.A ? UpgradeA : UpgradeB;
    }
}
