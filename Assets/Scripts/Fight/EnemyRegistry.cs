using System.Collections.Generic;
using UnityEngine;

public static class EnemyRegistry
{
    private static readonly List<UnitHealth> AliveEnemies = new List<UnitHealth>(128);

    public static IReadOnlyList<UnitHealth> Enemies => AliveEnemies;

    public static void Register(UnitHealth unit)
    {
        if (unit == null) return;
        if (AliveEnemies.Contains(unit)) return;

        AliveEnemies.Add(unit);
    }

    public static void Unregister(UnitHealth unit)
    {
        if (unit == null) return;

        AliveEnemies.Remove(unit);
    }
}
