using System;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class BuildPlace : MonoBehaviour
{
    [SerializeField, Tooltip("Stable unique build slot id used in save data.")]
    private string placeId;

    [Header("Optional Visual")]
    [SerializeField, Tooltip("Optional marker SpriteRenderer for empty build slot.")]
    private SpriteRenderer placeMarkerRenderer;
    [SerializeField, Tooltip("If true, marker renderer is hidden while slot is occupied.")]
    private bool hideMarkerWhenOccupied = true;

    private TowerUpgradable occupiedTower;
    private string occupiedOptionId;

    public event Action<BuildPlace> StateChanged;

    public string PlaceId => placeId;
    public bool IsOccupied => occupiedTower != null && occupiedTower.gameObject.activeInHierarchy && !occupiedTower.IsSold;
    public TowerUpgradable OccupiedTower => occupiedTower;
    public string OccupiedOptionId => occupiedOptionId;

    private void Reset()
    {
        EnsurePlaceId();
        RefreshMarkerVisibility();
    }

    private void OnValidate()
    {
        EnsurePlaceId();
        RefreshMarkerVisibility();
    }

    public bool HasValidId()
    {
        return !string.IsNullOrWhiteSpace(placeId);
    }

    [ContextMenu("Generate New Place Id")]
    private void GenerateNewPlaceId()
    {
        placeId = Guid.NewGuid().ToString("N");
    }

    public void SetOccupiedTower(TowerUpgradable tower, string optionId)
    {
        DetachTowerEvents();

        occupiedTower = tower;
        occupiedOptionId = optionId;

        AttachTowerEvents();
        RefreshMarkerVisibility();
        StateChanged?.Invoke(this);
    }

    public void ClearOccupiedTower(bool destroyTowerObject)
    {
        TowerUpgradable towerToClear = occupiedTower;
        DetachTowerEvents();

        occupiedTower = null;
        occupiedOptionId = string.Empty;

        if (destroyTowerObject && towerToClear != null)
            Destroy(towerToClear.gameObject);

        RefreshMarkerVisibility();
        StateChanged?.Invoke(this);
    }

    private void AttachTowerEvents()
    {
        if (occupiedTower == null)
            return;

        occupiedTower.DataChanged += HandleOccupiedTowerDataChanged;
        occupiedTower.Sold += HandleOccupiedTowerSold;
    }

    private void DetachTowerEvents()
    {
        if (occupiedTower == null)
            return;

        occupiedTower.DataChanged -= HandleOccupiedTowerDataChanged;
        occupiedTower.Sold -= HandleOccupiedTowerSold;
    }

    private void HandleOccupiedTowerDataChanged(TowerUpgradable _)
    {
        StateChanged?.Invoke(this);
    }

    private void HandleOccupiedTowerSold(TowerUpgradable soldTower)
    {
        if (soldTower != occupiedTower)
            return;

        ClearOccupiedTower(destroyTowerObject: true);
    }

    private void RefreshMarkerVisibility()
    {
        if (placeMarkerRenderer == null)
            return;

        bool shouldShow = !(hideMarkerWhenOccupied && IsOccupied);
        if (placeMarkerRenderer.enabled != shouldShow)
            placeMarkerRenderer.enabled = shouldShow;
    }

    private void EnsurePlaceId()
    {
        if (!string.IsNullOrWhiteSpace(placeId))
            return;

        placeId = Guid.NewGuid().ToString("N");
    }
}
