using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class EnemyKillRewardService : MonoBehaviour
{
    [Header("Scene Wiring")]
    [SerializeField] private PlayerCurrencyWallet currencyWallet;

    [Header("Reward Defaults")]
    [SerializeField, Min(0), Tooltip("Kill reward for enemies resolved as Small family.")]
    private int smallEnemyKillReward = 2;
    [SerializeField, Min(0), Tooltip("Kill reward for enemies resolved as Ogre family.")]
    private int ogreEnemyKillReward = 8;
    [SerializeField, Min(0), Tooltip("Kill reward for enemies resolved as DeathKnight family.")]
    private int deathKnightEnemyKillReward = 12;
    [SerializeField, Min(0), Tooltip("Used only if family cannot be resolved from spawn data.")]
    private int fallbackKillReward = 2;

    private readonly Dictionary<UnitHealth, int> killRewardByEnemy = new Dictionary<UnitHealth, int>(256);

    private void OnEnable()
    {
        EnemyRuntimeEvents.EnemySpawned += HandleEnemySpawned;
        EnemyRuntimeEvents.EnemyReachedGoal += HandleEnemyReachedGoal;
        UnitHealth.GlobalUnitDied += HandleEnemyDied;
    }

    private void OnDisable()
    {
        EnemyRuntimeEvents.EnemySpawned -= HandleEnemySpawned;
        EnemyRuntimeEvents.EnemyReachedGoal -= HandleEnemyReachedGoal;
        UnitHealth.GlobalUnitDied -= HandleEnemyDied;
        killRewardByEnemy.Clear();
    }

    private void HandleEnemySpawned(EnemyRuntimeEvents.EnemySpawnedEvent enemySpawnedEvent)
    {
        if (enemySpawnedEvent.Enemy == null)
            return;

        int reward = ResolveReward(enemySpawnedEvent.EnemyFamily, enemySpawnedEvent.KillRewardOverride);
        killRewardByEnemy[enemySpawnedEvent.Enemy] = reward;
    }

    private void HandleEnemyReachedGoal(UnitHealth enemy)
    {
        if (enemy == null)
            return;

        killRewardByEnemy.Remove(enemy);
    }

    private void HandleEnemyDied(UnitHealth enemy)
    {
        if (enemy == null || currencyWallet == null)
            return;

        int reward = Mathf.Max(0, fallbackKillReward);
        if (killRewardByEnemy.TryGetValue(enemy, out int trackedReward))
            reward = trackedReward;

        killRewardByEnemy.Remove(enemy);

        if (reward > 0)
            currencyWallet.Add(reward);
    }

    private int ResolveReward(EnemySpawner.EnemyFamily enemyFamily, int killRewardOverride)
    {
        if (killRewardOverride >= 0)
            return killRewardOverride;

        return enemyFamily switch
        {
            EnemySpawner.EnemyFamily.Ogre => Mathf.Max(0, ogreEnemyKillReward),
            EnemySpawner.EnemyFamily.DeathKnight => Mathf.Max(0, deathKnightEnemyKillReward),
            EnemySpawner.EnemyFamily.Small => Mathf.Max(0, smallEnemyKillReward),
            _ => Mathf.Max(0, fallbackKillReward)
        };
    }
}
