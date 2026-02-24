using System.Collections.Generic;
using UnityEngine;

public static class EnemyRegistry
{
    private static readonly List<UnitHealth> AliveEnemies = new List<UnitHealth>(128);

    public static IReadOnlyList<UnitHealth> Enemies => AliveEnemies;
    public static int Version { get; private set; }

    public static void Register(UnitHealth unit)
    {
        if (unit == null) return;
        if (AliveEnemies.Contains(unit)) return;

        AliveEnemies.Add(unit);
        Version++;
    }

    public static void Unregister(UnitHealth unit)
    {
        if (unit == null) return;

        if (AliveEnemies.Remove(unit))
        {
            Version++;
        }
    }

    public static bool TryGetNearestEnemy(Vector3 origin, float range, out Transform nearest)
    {
        float rangeSqr = range * range;
        float nearestDistSqr = float.MaxValue;

        nearest = null;

        for (int i = AliveEnemies.Count - 1; i >= 0; i--)
        {
            UnitHealth enemy = AliveEnemies[i];

            if (enemy == null)
            {
                AliveEnemies.RemoveAt(i);
                Version++;
                continue;
            }

            if (enemy.CurrentState == UnitState.Dead)
                continue;

            Vector3 delta = enemy.transform.position - origin;
            float distSqr = delta.sqrMagnitude;

            if (distSqr > rangeSqr || distSqr >= nearestDistSqr)
                continue;

            nearestDistSqr = distSqr;
            nearest = enemy.transform;
        }

        return nearest != null;
    }
}
