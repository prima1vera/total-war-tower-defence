using System;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class TowerBuildService : MonoBehaviour
{
    [Header("Scene Wiring")]
    [SerializeField] private PlayerCurrencyWallet currencyWallet;
    [SerializeField] private TowerBuildCatalog buildCatalog;
    [SerializeField] private Transform spawnedTowerRoot;
    [SerializeField] private TowerSelectionService towerSelectionService;

    private BuildPlace selectedPlace;
    private bool suppressCrossSelection;

    public event Action<BuildPlace> SelectedBuildPlaceChanged;
    public BuildPlace SelectedBuildPlace => selectedPlace;

    private void OnEnable()
    {
        if (towerSelectionService != null)
            towerSelectionService.SelectionChanged += HandleTowerSelectionChanged;
    }

    private void OnDisable()
    {
        if (towerSelectionService != null)
            towerSelectionService.SelectionChanged -= HandleTowerSelectionChanged;
    }

    public void SelectBuildPlace(BuildPlace buildPlace)
    {
        if (buildPlace == selectedPlace)
            return;

        selectedPlace = buildPlace;
        SelectedBuildPlaceChanged?.Invoke(selectedPlace);

        if (selectedPlace != null && towerSelectionService != null && !suppressCrossSelection)
        {
            suppressCrossSelection = true;
            towerSelectionService.ClearSelection();
            suppressCrossSelection = false;
        }
    }

    public void ClearBuildPlaceSelection()
    {
        SelectBuildPlace(null);
    }

    public bool TryGetBuildOption(string optionId, out TowerBuildOptionDefinition option)
    {
        option = default;
        return buildCatalog != null && buildCatalog.TryGetOption(optionId, out option);
    }

    public bool TryBuildOnSelectedPlace(string optionId)
    {
        if (selectedPlace == null || selectedPlace.IsOccupied)
            return false;

        return TryBuildAtPlace(selectedPlace, optionId, spendCurrency: true);
    }

    public bool TryBuildAtPlace(BuildPlace place, string optionId, bool spendCurrency)
    {
        if (place == null || !place.HasValidId() || place.IsOccupied)
            return false;

        if (!place.AllowsOption(optionId))
            return false;

        if (!TryGetBuildOption(optionId, out TowerBuildOptionDefinition option))
            return false;

        int cost = Mathf.Max(0, option.Cost);
        if (spendCurrency && currencyWallet != null && cost > 0 && !currencyWallet.TrySpend(cost))
            return false;

        if (!TrySpawnTower(option, place.transform.position, out TowerUpgradable spawnedTower))
            return false;

        place.SetOccupiedTower(spawnedTower, option.OptionId);
        return true;
    }

    public bool TryRestorePlaceState(BuildPlace place, string optionId, TowerUpgradePersistentState towerState)
    {
        if (!TryBuildAtPlace(place, optionId, spendCurrency: false))
            return false;

        if (place.OccupiedTower == null)
            return false;

        place.OccupiedTower.RestorePersistentState(towerState);
        if (towerState.IsSold)
            place.ClearOccupiedTower(destroyTowerObject: true);

        return true;
    }

    private bool TrySpawnTower(TowerBuildOptionDefinition option, Vector3 position, out TowerUpgradable spawnedTower)
    {
        spawnedTower = null;
        if (!option.IsConfigured)
            return false;

        Transform parent = spawnedTowerRoot != null ? spawnedTowerRoot : null;
        TowerUpgradable created = Instantiate(option.TowerPrefab, position, Quaternion.identity, parent);
        if (created == null)
            return false;

        created.gameObject.SetActive(true);
        spawnedTower = created;
        return true;
    }

    private void HandleTowerSelectionChanged(TowerUpgradable selectedTower)
    {
        if (suppressCrossSelection || selectedTower == null)
            return;

        suppressCrossSelection = true;
        ClearBuildPlaceSelection();
        suppressCrossSelection = false;
    }
}
