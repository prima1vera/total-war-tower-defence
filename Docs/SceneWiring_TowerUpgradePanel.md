# Scene Wiring Checklist - Tower Selection + Upgrade Panel

## Required scene objects
- `TowerSelectionService` (component: `TowerSelectionService`)
- `TowerSelectionInput` (component: `TowerSelectionInput`)
- `PlayerCurrencyWallet` (component: `PlayerCurrencyWallet`)
- `TowerUpgradePanelPresenter` (component on `TowerUpgradePanel` UI object)

## Required tower components
- Every selectable tower prefab/object must have:
  - `Tower`
  - `Collider2D`
  - `TowerUpgradable`

## Required data assets
- Create one `TowerUpgradeTree` asset per tower archetype.
- Assign corresponding `TowerUpgradeTree` to each `TowerUpgradable`.

## Inspector links
- `TowerSelectionInput`
  - `World Camera` -> gameplay camera
  - `Selection Service` -> `TowerSelectionService`
  - `Tower Layer Mask` -> layer containing tower colliders
- `TowerUpgradePanelPresenter`
  - `Selection Service` -> `TowerSelectionService`
  - `Currency Wallet` -> `PlayerCurrencyWallet`
  - all TMP labels/buttons/panel root references
- `TowerUpgradable`
  - `Tower` reference (same object)
  - `Upgrade Tree` asset

## Validation checks
- Tapping a tower selects it and opens panel.
- Upgrade A/B changes tower stats and updates panel immediately.
- Sell hides tower and closes panel.
- No runtime-created manager/UI objects and no `FindObjectOfType` usage.
