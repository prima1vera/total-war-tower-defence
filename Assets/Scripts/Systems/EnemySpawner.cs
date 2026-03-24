using UnityEngine;

public class EnemySpawner : MonoBehaviour
{
    public enum EnemyFamily
    {
        Auto = 0,
        Small = 1,
        Ogre = 2
    }

    [SerializeField] private EnemyPool enemyPool;
    [SerializeField] private GameObject enemyPrefab;
    [SerializeField] private Transform[] spawnPoints;
    [SerializeField] private bool autoSpawn = true;
    [SerializeField] private float spawnInterval = 1f;

    [Header("Wave Composition")]
    [Tooltip("Category used by WaveManager composition. Auto infers Ogre by prefab/pool name, otherwise Small.")]
    [SerializeField] private EnemyFamily enemyFamily = EnemyFamily.Auto;
    [Tooltip("Relative spawn weight for this spawner within its family. 2 means this spawner is picked ~2x more often than weight 1.")]
    [SerializeField, Min(0f)] private float waveSpawnWeight = 1f;

    [Header("Economy")]
    [Tooltip("Optional per-spawner gold override. -1 means use global family rewards from EnemyKillRewardService.")]
    [SerializeField, Min(-1)] private int killRewardOverride = -1;

    private float spawnTimer;
    private bool loggedMissingPool;

    public float WaveSpawnWeight => Mathf.Max(0f, waveSpawnWeight);
    public bool IsOgreSpawner => ResolveEnemyFamily() == EnemyFamily.Ogre;

    void Update()
    {
        if (!autoSpawn)
            return;

        spawnTimer -= Time.deltaTime;
        if (spawnTimer > 0f)
            return;

        SpawnEnemy();
        spawnTimer = Mathf.Max(0.05f, spawnInterval);
    }

    public void SetAutoSpawn(bool value)
    {
        autoSpawn = value;
        if (autoSpawn)
            spawnTimer = 0f;
    }

    public bool IsAutoSpawnEnabled => autoSpawn;

    public GameObject SpawnEnemy()
    {
        Transform spawnPoint = ResolveSpawnPoint();
        if (spawnPoint == null)
            return null;

        if (enemyPool == null)
        {
            if (!loggedMissingPool)
            {
                Debug.LogError($"{name}: EnemyPool is not assigned. Scene-wired pool is required (runtime Instantiate fallback removed).", this);
                loggedMissingPool = true;
            }

            return null;
        }

        loggedMissingPool = false;
        GameObject spawnedEnemy = enemyPool.Spawn(spawnPoint.position, spawnPoint.rotation);
        RaiseEnemySpawnedEvent(spawnedEnemy);
        return spawnedEnemy;
    }

    private EnemyFamily ResolveEnemyFamily()
    {
        if (enemyFamily != EnemyFamily.Auto)
            return enemyFamily;

        if (HasOgreHint(enemyPrefab != null ? enemyPrefab.name : null))
            return EnemyFamily.Ogre;

        if (enemyPool != null && HasOgreHint(enemyPool.name))
            return EnemyFamily.Ogre;

        return EnemyFamily.Small;
    }

    private static bool HasOgreHint(string value)
    {
        if (string.IsNullOrEmpty(value))
            return false;

        return value.IndexOf("ogre", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private Transform ResolveSpawnPoint()
    {
        if (spawnPoints == null || spawnPoints.Length == 0)
            return transform;

        int index = Random.Range(0, spawnPoints.Length);
        return spawnPoints[index] != null ? spawnPoints[index] : transform;
    }

    private void RaiseEnemySpawnedEvent(GameObject enemyObject)
    {
        if (enemyObject == null || !enemyObject.TryGetComponent(out UnitHealth unitHealth))
            return;

        EnemyRuntimeEvents.RaiseEnemySpawned(unitHealth, ResolveEnemyFamily(), killRewardOverride);
    }
}
