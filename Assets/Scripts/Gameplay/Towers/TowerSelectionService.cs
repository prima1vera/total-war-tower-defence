using System;
using UnityEngine;

public class TowerSelectionService : MonoBehaviour
{
    private TowerUpgradable selectedTower;

    public event Action<TowerUpgradable> SelectionChanged;

    public TowerUpgradable SelectedTower => selectedTower;

    public void Select(TowerUpgradable tower)
    {
        if (tower == selectedTower)
            return;

        if (tower != null && (!tower.gameObject.activeInHierarchy || tower.IsSold))
            tower = null;

        selectedTower = tower;
        SelectionChanged?.Invoke(selectedTower);
    }

    public void ClearSelection()
    {
        Select(null);
    }
}
