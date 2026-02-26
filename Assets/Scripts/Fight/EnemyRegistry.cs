using System.Collections.Generic;
using UnityEngine;

public static class EnemyRegistry
{
    private static readonly List<UnitHealth> AliveEnemies = new List<UnitHealth>(128);
    private static readonly HashSet<UnitHealth> EnemySet = new HashSet<UnitHealth>();

    public static IReadOnlyList<UnitHealth> Enemies => AliveEnemies;
    public static int Version { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Initialize()
    {
        Clear();
    }

    public static void Register(UnitHealth unit)
    {
        if (unit == null) return;
        if (!EnemySet.Add(unit)) return;

        AliveEnemies.Add(unit);
        Version++;
    }

    public static void Unregister(UnitHealth unit)
    {
        if (unit == null) return;

        if (!EnemySet.Remove(unit))
            return;

        if (AliveEnemies.Remove(unit))
        {
            Version++;
        }
    }

    public static void Clear()
    {
        if (AliveEnemies.Count == 0 && EnemySet.Count == 0)
            return;

        AliveEnemies.Clear();
        EnemySet.Clear();
        Version++;
    }

    private static void RebuildEnemySet()
    {
        EnemySet.Clear();

        for (int i = 0; i < AliveEnemies.Count; i++)
        {
            UnitHealth enemy = AliveEnemies[i];
            if (enemy == null)
                continue;

            EnemySet.Add(enemy);
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
                RebuildEnemySet();
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
