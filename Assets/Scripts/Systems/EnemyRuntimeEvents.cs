using System;
using UnityEngine;

public static class EnemyRuntimeEvents
{
    public readonly struct EnemySpawnedEvent
    {
        public UnitHealth Enemy { get; }
        public EnemySpawner.EnemyFamily EnemyFamily { get; }
        public int KillRewardOverride { get; }

        public EnemySpawnedEvent(UnitHealth enemy, EnemySpawner.EnemyFamily enemyFamily, int killRewardOverride)
        {
            Enemy = enemy;
            EnemyFamily = enemyFamily;
            KillRewardOverride = killRewardOverride;
        }
    }

    public static event Action<EnemySpawnedEvent> EnemySpawned;
    public static event Action<UnitHealth> EnemyReachedGoal;

    public static void RaiseEnemySpawned(UnitHealth enemy, EnemySpawner.EnemyFamily enemyFamily, int killRewardOverride)
    {
        if (enemy == null)
            return;

        EnemySpawned?.Invoke(new EnemySpawnedEvent(enemy, enemyFamily, killRewardOverride));
    }

    public static void RaiseEnemyReachedGoal(UnitHealth enemy)
    {
        if (enemy == null)
            return;

        EnemyReachedGoal?.Invoke(enemy);
    }
}
