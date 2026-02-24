# Project Structure (Incremental Migration)

This folder is the new root for production-oriented code organization.

## Current layout
- `Core/` : cross-game foundations and registries.
- `Gameplay/Combat/` : towers, projectiles, damage types.
- `Gameplay/Movement/` : unit movement/pathing/state.
- `Gameplay/Units/` : health, status effects, unit visuals.
- `Gameplay/Visuals/` : camera + visual/sorting helper scripts.
- `Systems/Pooling/` : reusable pooling systems.

## Migration note
This commit is a **folder-only move** (no gameplay logic rewrite), so scene/prefab script references remain valid via Unity meta GUIDs.

## Next planned structure steps
1. Add assembly definitions (`asmdef`) for Core / Gameplay / Systems / UI.
2. Introduce `Services/` for Analytics, Ads, IAP, Save abstractions.
3. Move configuration into `ScriptableObjects/` (towers, enemies, waves).
4. Add `Tools/` editor scripts for validation/build helpers.
