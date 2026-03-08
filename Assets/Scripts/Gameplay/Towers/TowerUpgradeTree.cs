using System;
using UnityEngine;

[CreateAssetMenu(fileName = "TowerUpgradeTree", menuName = "TWTD/Towers/Upgrade Tree")]
public sealed class TowerUpgradeTree : ScriptableObject
{
    [SerializeField, Tooltip("Display name shown in the upgrade panel.")]
    private string towerDisplayName = "Tower";

    [SerializeField, Tooltip("Start level index in the Levels array (0-based). Usually 0.")]
    private int startLevelIndex;

    [SerializeField, Tooltip("Upgrade levels array. Upgrade links use these 0-based indices.")]
    private TowerUpgradeLevelDefinition[] levels = Array.Empty<TowerUpgradeLevelDefinition>();

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

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (levels == null)
            levels = Array.Empty<TowerUpgradeLevelDefinition>();

        int maxIndex = levels.Length - 1;
        if (startLevelIndex < 0)
            startLevelIndex = 0;

        if (maxIndex < 0)
            return;

        if (startLevelIndex > maxIndex)
            startLevelIndex = maxIndex;

        for (int i = 0; i < levels.Length; i++)
        {
            TowerUpgradeLevelDefinition level = levels[i];
            level.Stats = TowerCombatStats.Clamp(level.Stats);
            level.SellValue = Mathf.Max(0, level.SellValue);
            level.UpgradeA = NormalizeOption(level.UpgradeA, maxIndex);
            level.UpgradeB = NormalizeOption(level.UpgradeB, maxIndex);
            levels[i] = level;
        }
    }

    private static TowerUpgradeOptionDefinition NormalizeOption(TowerUpgradeOptionDefinition option, int maxIndex)
    {
        option.Cost = Mathf.Max(0, option.Cost);

        if (option.NextLevelIndex < 0)
            return option;

        option.NextLevelIndex = Mathf.Clamp(option.NextLevelIndex, 0, maxIndex);
        return option;
    }
#endif
}
