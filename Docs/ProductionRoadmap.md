# Tower Defense Mobile Production Roadmap (30–45 Days)

## Current codebase snapshot (Phase 1 analysis)

### Gameplay systems and responsibilities
- `UnitMovement` handles path selection, movement along waypoints, knockback and local separation logic. It currently destroys the unit at path end. 
- `Waypoints` builds static path arrays from scene hierarchy.
- `UnitHealth` controls HP, death flow, death visuals, collider/sorting state, and enemy registration lifecycle.
- `StatusEffectHandler` applies burn/freeze effects and manipulates movement speed + effect visuals.
- `Tower` handles retarget cadence, target validation, fire animation trigger, and projectile spawn.
- `EnemyRegistry` stores alive enemies and exposes nearest-target query + versioning for cache invalidation.
- `Arrow` handles projectile arc movement, hit detection, pierce, AoE damage, status application.
- `ArrowPool` prewarms/spawns/returns projectile instances.

### Key architecture risks
1. No domain-level separation between runtime systems (combat, progression, economy, monetization, analytics). 
2. Scene-coupled static path setup (`Waypoints.AllPaths`) is fragile when multiple scenes/load flows are added.
3. Gameplay and presentation are mixed in `UnitHealth` (health + blood VFX + sorting + animation).
4. No save/progression abstraction yet (risk for live tuning and data migrations).
5. No service boundary for ads/IAP/analytics; direct SDK calls later would tightly couple gameplay and monetization.

### Main performance risks to address in sequence
1. Frequent physics allocation patterns in combat/hit checks.
2. Runtime `Instantiate`/`Destroy` still used for VFX and possibly enemies (pooling coverage incomplete).
3. High-frequency Animator parameter writes and per-unit update loops need profiling budget and throttling strategy.
4. Mobile memory pressure risk from sprite/material duplication and unbounded pooled object growth.


## Target production-oriented structure (Unity)

- `Assets/Scripts/Core`
  - App bootstrapping, scene composition root, shared event bus, game loop state.
- `Assets/Scripts/Gameplay`
  - Pure gameplay domain: towers, enemies, projectiles, waves, combat rules.
- `Assets/Scripts/Systems`
  - Cross-cutting runtime systems: save/progression, economy, upgrade pipeline, level runtime orchestration.
- `Assets/Scripts/Services`
  - External adapters: ads, IAP, analytics, remote config, crash reporting.
- `Assets/Scripts/UI`
  - Presentation only: HUD/menu/view models; no combat decisions.
- `Assets/Scripts/ScriptableObjects`
  - Data definitions: tower/enemy/wave configs, economy tables, live-tuning presets.
- `Assets/Scripts/Tools`
  - Editor validation tools, build scripts, profiling helpers.

## Immediate architecture actions (Phase 1)

1. Keep gameplay stable while introducing low-risk, high-impact optimizations.
2. Move hot-path lookups to cached/registry-driven access patterns.
3. Gate new subsystems behind interfaces (`IAdsService`, `IIapService`, `IAnalyticsService`, `ISaveService`).
4. Add simple scene wiring checklists for every system addition to avoid prefab regressions.

## 30–45 day delivery plan

### Week 1 — Foundation and architecture hardening
- Freeze folder conventions and assembly boundaries.
- Introduce `Services` interfaces (`IAnalyticsService`, `IAdsService`, `IIapService`, `ISaveService`).
- Add bootstrap/composition root for scene startup.
- Add profiler baseline (Android device + Editor deep profile samples).

### Week 2 — Data-driven gameplay and progression scaffold
- Move tower/enemy/wave parameters into ScriptableObjects.
- Add persistent player profile (currency, unlocked levels, upgrades).
- Build minimal upgrade economy loop and validation events.

### Week 3 — Meta flow + UI shell
- Main menu, level select, gameplay HUD, pause/defeat/victory flow.
- Safe area handling and resolution policy for mobile UI.
- Add first-time user flow and settings (sound/vibration toggles).

### Week 4 — Monetization + analytics
- Rewarded ad placements (continue/reward currency) and interstitial pacing rules.
- Optional IAP (`remove ads`, starter pack).
- Analytics event map: funnel, economy sinks/sources, ad impressions/rewards, retention markers.

### Week 5 — Content, balancing, and optimization
- Expand to 5–10 levels with difficulty ramp.
- CPU/GC optimization pass (pooling coverage, update cadence, physics query budgets).
- Memory optimization pass (atlases, sprite compression, pooled caps).
- QA bug backlog burn-down.

### Week 6 (buffer for 45-day target) — Release readiness
- Android release pipeline (keystore, versioning, CI build script, internal testing track).
- Crash logging + remote config hooks.
- iOS compatibility review checklist and deferred pipeline prep.

## Immediate execution order (next technical slices)
1. Combat GC reduction (projectile hit queries + component caching). ✅
2. Extract enemy spawn/despawn lifecycle into a pooled enemy spawner.
3. Introduce ScriptableObject configs for Tower and Enemy stats.
4. Add `GameSession` service for run state, currency, and result events.


## Execution log

- Iteration 1: Added `UnitHealthLookupCache` and switched projectile hit resolution to cached collider->`UnitHealth` lookup to reduce repeated `GetComponent` calls in combat hot paths.
