using System.Collections.Generic;
using UnityEngine;

public class EnemyPool : MonoBehaviour
{
    [SerializeField] private GameObject enemyPrefab;
    [SerializeField] private int prewarmCount = 16;
    [SerializeField] private int maxPoolSize = 128;

    private readonly Queue<GameObject> pooledEnemies = new Queue<GameObject>(32);
    private readonly HashSet<GameObject> activeEnemies = new HashSet<GameObject>();
    private int createdCount;

    void Awake()
    {
        Prewarm();
    }

    public GameObject Spawn(Vector3 position, Quaternion rotation)
    {
        GameObject enemy = pooledEnemies.Count > 0 ? pooledEnemies.Dequeue() : CreateEnemy();
        if (enemy == null)
            return null;

        Transform enemyTransform = enemy.transform;
        enemyTransform.SetPositionAndRotation(position, rotation);

        activeEnemies.Add(enemy);
        enemy.SetActive(true);
        return enemy;
    }

    public void Despawn(GameObject enemy)
    {
        if (enemy == null)
            return;

        if (!activeEnemies.Remove(enemy))
            return;

        enemy.SetActive(false);
        pooledEnemies.Enqueue(enemy);
    }

    private void Prewarm()
    {
        int count = Mathf.Max(0, prewarmCount);

        for (int i = 0; i < count; i++)
        {
            GameObject enemy = CreateEnemy();
            if (enemy == null)
                break;

            enemy.SetActive(false);
            pooledEnemies.Enqueue(enemy);
        }
    }

    private GameObject CreateEnemy()
    {
        if (enemyPrefab == null)
            return null;

        int clampedMax = Mathf.Max(1, maxPoolSize);
        if (createdCount >= clampedMax)
            return null;

        GameObject enemy = Instantiate(enemyPrefab, transform);
        createdCount++;

        EnemyPoolMember poolMember = enemy.GetComponent<EnemyPoolMember>();
        if (poolMember != null)
            poolMember.Bind(this);

        return enemy;
    }
}
