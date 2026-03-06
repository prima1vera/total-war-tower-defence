# Scene Wiring Checklist - Damage Feedback + Enemy Health Bars

## Scope
- Event-driven damage feedback (`UnitHealth` -> visual subscribers).
- Enemy hit flash + burn tick flash in `UnitEffects`.
- Runtime pooled world-to-screen health bars via `EnemyHealthBarSystem`.

## Required wiring
- Enemy prefab must include:
  - `UnitHealth`
  - `StatusEffectHandler`
  - `UnitEffects`
- Combat damage must still flow through:
  - `Tower` -> `ShootArrow` animation event -> `ArrowPool` -> `Arrow`
- Death chain must stay intact:
  - `UnitHealth` -> `EnemyDeathVisualManager` -> pooled despawn (`EnemyPoolMember`) fallback deactivate

## Runtime bootstrap behavior
- `EnemyHealthBarBootstrap` auto-creates `EnemyHealthBarSystem` only in `FirstScene`.
- No manual scene object wiring is required for health bar system.

## Scene prerequisites
- Active gameplay camera should be tagged `MainCamera`.
- If no tagged camera exists, health bars fallback to first active camera found at runtime.

## Performance notes
- No per-frame `Find*` calls in combat hot path.
- Health bars are pooled and reused (no instantiate/destroy per hit).
- Damage events are struct-based to avoid per-hit GC allocations.

## Rollback-safe defaults
- Disable health bars: remove/disable `EnemyHealthBarBootstrap`.
- Disable hit flash logic: reduce flash strengths/durations in `UnitEffects` to `0`.
- Core combat and death flow remain operational without these presentation systems.
