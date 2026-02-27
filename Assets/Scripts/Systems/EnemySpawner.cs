using UnityEngine;

public class EnemySpawner : MonoBehaviour
{
    [SerializeField] private EnemyPool enemyPool;
    [SerializeField] private GameObject enemyPrefab;
    [SerializeField] private Transform[] spawnPoints;
    [SerializeField] private bool autoSpawn = true;
    [SerializeField] private float spawnInterval = 1f;

    private float spawnTimer;

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

    public GameObject SpawnEnemy()
    {
        Transform spawnPoint = ResolveSpawnPoint();
        if (spawnPoint == null)
            return null;

        if (enemyPool != null)
            return enemyPool.Spawn(spawnPoint.position, spawnPoint.rotation);

        if (enemyPrefab == null)
            return null;

        return Instantiate(enemyPrefab, spawnPoint.position, spawnPoint.rotation);
    }

    private Transform ResolveSpawnPoint()
    {
        if (spawnPoints == null || spawnPoints.Length == 0)
            return transform;

        int index = Random.Range(0, spawnPoints.Length);
        return spawnPoints[index] != null ? spawnPoints[index] : transform;
    }
}
