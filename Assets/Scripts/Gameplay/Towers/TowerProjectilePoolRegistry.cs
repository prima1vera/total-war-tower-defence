using UnityEngine;

public class TowerProjectilePoolRegistry : MonoBehaviour
{
    [Header("Scene Pools")]
    [SerializeField] private ArrowPool basePool;
    [SerializeField] private ArrowPool firePool;
    [SerializeField] private ArrowPool frostPool;
    [SerializeField] private ArrowPool ironPool;

    private static TowerProjectilePoolRegistry instance;
    private static bool fallbackResolved;
    private static ArrowPool fallbackBasePool;
    private static ArrowPool fallbackFirePool;
    private static ArrowPool fallbackFrostPool;
    private static ArrowPool fallbackIronPool;

    private bool autoResolved;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Debug.LogWarning("Duplicate TowerProjectilePoolRegistry detected. Keeping the first instance.", this);
            return;
        }

        instance = this;
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }

    public static bool TryGetPool(TowerProjectilePoolKey key, out ArrowPool pool)
    {
        EnsureInstance();

        if (instance != null)
            return instance.TryGetPoolInternal(key, out pool);

        EnsureFallbackResolved();
        return TryGetFallbackPool(key, out pool);
    }

    private static void EnsureInstance()
    {
        if (instance != null)
            return;

#if UNITY_2022_2_OR_NEWER
        instance = FindFirstObjectByType<TowerProjectilePoolRegistry>();
#else
        instance = FindObjectOfType<TowerProjectilePoolRegistry>();
#endif
    }

    private bool TryGetPoolInternal(TowerProjectilePoolKey key, out ArrowPool pool)
    {
        EnsureAutoResolved();

        switch (key)
        {
            case TowerProjectilePoolKey.Fire:
                pool = firePool != null ? firePool : basePool;
                break;
            case TowerProjectilePoolKey.Frost:
                pool = frostPool != null ? frostPool : basePool;
                break;
            case TowerProjectilePoolKey.Iron:
                pool = ironPool != null ? ironPool : basePool;
                break;
            default:
                pool = basePool;
                break;
        }

        return pool != null;
    }

    private static bool TryGetFallbackPool(TowerProjectilePoolKey key, out ArrowPool pool)
    {
        switch (key)
        {
            case TowerProjectilePoolKey.Fire:
                pool = fallbackFirePool != null ? fallbackFirePool : fallbackBasePool;
                break;
            case TowerProjectilePoolKey.Frost:
                pool = fallbackFrostPool != null ? fallbackFrostPool : fallbackBasePool;
                break;
            case TowerProjectilePoolKey.Iron:
                pool = fallbackIronPool != null ? fallbackIronPool : fallbackBasePool;
                break;
            default:
                pool = fallbackBasePool;
                break;
        }

        return pool != null;
    }

    private void EnsureAutoResolved()
    {
        if (autoResolved)
            return;

        autoResolved = true;

        if (basePool != null && firePool != null && frostPool != null)
            return;

        AutoAssignPools(out basePool, out firePool, out frostPool, out ironPool);
    }

    private static void EnsureFallbackResolved()
    {
        if (fallbackResolved)
            return;

        fallbackResolved = true;
        AutoAssignPools(out fallbackBasePool, out fallbackFirePool, out fallbackFrostPool, out fallbackIronPool);
    }

    private static void AutoAssignPools(
        out ArrowPool basePool,
        out ArrowPool firePool,
        out ArrowPool frostPool,
        out ArrowPool ironPool)
    {
        basePool = null;
        firePool = null;
        frostPool = null;
        ironPool = null;

        ArrowPool[] pools = FindObjectsByType<ArrowPool>(FindObjectsSortMode.None);
        for (int i = 0; i < pools.Length; i++)
        {
            ArrowPool pool = pools[i];
            if (pool == null)
                continue;

            string poolName = pool.name.ToLowerInvariant();
            if (firePool == null && poolName.Contains("fire"))
            {
                firePool = pool;
                continue;
            }

            if (frostPool == null && (poolName.Contains("frost") || poolName.Contains("ice")))
            {
                frostPool = pool;
                continue;
            }

            if (ironPool == null && poolName.Contains("iron"))
            {
                ironPool = pool;
                continue;
            }

            if (basePool == null && (poolName.Contains("normal") || poolName.Contains("base")))
            {
                basePool = pool;
                continue;
            }
        }

        if (basePool == null && pools.Length > 0)
            basePool = pools[0];
    }
}
