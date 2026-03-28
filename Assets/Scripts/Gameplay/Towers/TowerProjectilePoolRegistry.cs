using UnityEngine;

public class TowerProjectilePoolRegistry : MonoBehaviour
{
    [Header("Scene Pools")]
    [SerializeField] private ArrowPool basePool;
    [SerializeField] private ArrowPool firePool;
    [SerializeField] private ArrowPool frostPool;
    [SerializeField] private ArrowPool ironPool;
    [SerializeField] private ArrowPool archerPool;
    [SerializeField] private ArrowPool catapultPool;

    private static TowerProjectilePoolRegistry instance;
    private static bool missingInstanceLogged;
    public static bool HasInstance => instance != null;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Debug.LogWarning("Duplicate TowerProjectilePoolRegistry detected. Keeping the first instance.", this);
            Destroy(gameObject);
            return;
        }

        instance = this;
        missingInstanceLogged = false;

        if (basePool == null)
            Debug.LogWarning("TowerProjectilePoolRegistry: Base pool is not assigned. Upgraded towers may fail to resolve projectile pools.", this);
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }

    public static bool TryGetPool(TowerProjectilePoolKey key, out ArrowPool pool)
    {
        if (instance == null)
        {
            if (!missingInstanceLogged)
            {
                missingInstanceLogged = true;
                Debug.LogError("TowerProjectilePoolRegistry instance is missing. Add and wire it in the scene.");
            }

            pool = null;
            return false;
        }

        return instance.TryGetPoolInternal(key, out pool);
    }

    private bool TryGetPoolInternal(TowerProjectilePoolKey key, out ArrowPool pool)
    {
        switch (key)
        {
            case TowerProjectilePoolKey.Fire:
                pool = firePool;
                break;
            case TowerProjectilePoolKey.Frost:
                pool = frostPool;
                break;
            case TowerProjectilePoolKey.Iron:
                pool = ironPool;
                break;
            case TowerProjectilePoolKey.Archer:
                pool = archerPool;
                break;
            case TowerProjectilePoolKey.Catapult:
                pool = catapultPool;
                break;
            default:
                pool = basePool;
                break;
        }

        return pool != null;
    }
}
