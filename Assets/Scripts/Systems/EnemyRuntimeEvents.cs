using System;
using UnityEngine;

public static class EnemyRuntimeEvents
{
    public static event Action<UnitHealth> EnemyReachedGoal;

    public static void RaiseEnemyReachedGoal(UnitHealth enemy)
    {
        if (enemy == null)
            return;

        EnemyReachedGoal?.Invoke(enemy);
    }
}
