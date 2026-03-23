using System;
using UnityEngine;

[CreateAssetMenu(fileName = "TowerBuildCatalog", menuName = "TWTD/Towers/Build Catalog")]
public sealed class TowerBuildCatalog : ScriptableObject
{
    [SerializeField] private TowerBuildOptionDefinition[] options = Array.Empty<TowerBuildOptionDefinition>();

    public int OptionCount => options != null ? options.Length : 0;

    public bool TryGetOption(string optionId, out TowerBuildOptionDefinition option)
    {
        option = default;
        if (string.IsNullOrWhiteSpace(optionId) || options == null)
            return false;

        for (int i = 0; i < options.Length; i++)
        {
            TowerBuildOptionDefinition candidate = options[i];
            if (!candidate.IsConfigured || !candidate.IsEnabled)
                continue;

            if (!string.Equals(candidate.OptionId, optionId, StringComparison.Ordinal))
                continue;

            option = candidate;
            return true;
        }

        return false;
    }

    public bool TryGetOption(int index, out TowerBuildOptionDefinition option)
    {
        option = default;
        if (options == null || index < 0 || index >= options.Length)
            return false;

        option = options[index];
        return option.IsConfigured && option.IsEnabled;
    }

    public string[] GetConfiguredOptionIds()
    {
        if (options == null || options.Length == 0)
            return Array.Empty<string>();

        int count = 0;
        for (int i = 0; i < options.Length; i++)
        {
            if (options[i].IsConfigured && options[i].IsEnabled)
                count++;
        }

        if (count == 0)
            return Array.Empty<string>();

        string[] ids = new string[count];
        int writeIndex = 0;
        for (int i = 0; i < options.Length; i++)
        {
            TowerBuildOptionDefinition option = options[i];
            if (!option.IsConfigured || !option.IsEnabled)
                continue;

            ids[writeIndex++] = option.OptionId;
        }

        return ids;
    }
}

[Serializable]
public struct TowerBuildOptionDefinition
{
    [Tooltip("Stable option id used in UI and save data. Example: tower_base.")]
    public string OptionId;

    [Tooltip("Player-facing name in Build panel.")]
    public string DisplayName;

    [Min(0)]
    [Tooltip("Gold cost for building this tower.")]
    public int Cost;

    [Tooltip("Tower prefab to spawn at BuildPlace.")]
    public TowerUpgradable TowerPrefab;

    [Tooltip("Toggle option availability without deleting data.")]
    public bool IsEnabled;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(OptionId) &&
        TowerPrefab != null;
}
