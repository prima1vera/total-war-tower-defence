using System.Collections.Generic;
using UnityEngine;

public static class UnitHealthLookupCache
{
    private static readonly Dictionary<int, UnitHealth> Cache = new Dictionary<int, UnitHealth>(256);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        Cache.Clear();
    }

    public static UnitHealth Resolve(Collider2D collider)
    {
        if (collider == null)
            return null;

        int id = collider.GetInstanceID();

        if (Cache.TryGetValue(id, out UnitHealth cached))
        {
            if (cached != null)
                return cached;

            Cache.Remove(id);
        }

        UnitHealth health = collider.GetComponent<UnitHealth>();
        if (health != null)
            Cache[id] = health;

        return health;
    }

    public static void Remove(Collider2D collider)
    {
        if (collider == null)
            return;

        Cache.Remove(collider.GetInstanceID());
    }
}
