using System;
using System.Collections;
using UnityEngine;

public class WaveManager : MonoBehaviour
{
    [Serializable]
    public struct WaveDefinition
    {
        [Min(1)] public int enemyCount;
        [Min(0.05f)] public float spawnInterval;
        [Min(0f)] public float startDelay;
    }

    [SerializeField] private EnemySpawner[] controlledSpawners;
    [SerializeField] private WaveDefinition[] waves =
    {
        new WaveDefinition { enemyCount = 10, spawnInterval = 0.7f, startDelay = 1f },
        new WaveDefinition { enemyCount = 40, spawnInterval = 0.6f, startDelay = 1.5f },
        new WaveDefinition { enemyCount = 160, spawnInterval = 0.5f, startDelay = 2f }
    };

    public event Action<int, int> WaveChanged;
    public event Action AllWavesCompleted;

    public int CurrentWaveIndex { get; private set; } = -1;
    public int TotalWaves => waves != null ? waves.Length : 0;
    public bool IsCompleted { get; private set; }

    private Coroutine waveRoutine;

    private void Start()
    {
        if (controlledSpawners == null || controlledSpawners.Length == 0)
            controlledSpawners = FindObjectsOfType<EnemySpawner>();

        if (controlledSpawners == null || controlledSpawners.Length == 0 || waves == null || waves.Length == 0)
            return;

        for (int i = 0; i < controlledSpawners.Length; i++)
        {
            EnemySpawner spawner = controlledSpawners[i];
            if (spawner != null)
                spawner.SetAutoSpawn(false);
        }

        waveRoutine = StartCoroutine(RunWaves());
    }

    private IEnumerator RunWaves()
    {
        for (int waveIndex = 0; waveIndex < waves.Length; waveIndex++)
        {
            CurrentWaveIndex = waveIndex;
            WaveChanged?.Invoke(CurrentWaveIndex + 1, waves.Length);

            WaveDefinition wave = waves[waveIndex];
            if (wave.startDelay > 0f)
                yield return new WaitForSeconds(wave.startDelay);

            int spawnCount = Mathf.Max(1, wave.enemyCount);
            float spawnInterval = Mathf.Max(0.05f, wave.spawnInterval);

            for (int i = 0; i < spawnCount; i++)
            {
                EnemySpawner spawner = ResolveSpawnerForIndex(i);
                if (spawner != null)
                    spawner.SpawnEnemy();

                if (i < spawnCount - 1)
                    yield return new WaitForSeconds(spawnInterval);
            }

            while (EnemyRegistry.Enemies.Count > 0)
                yield return null;
        }

        IsCompleted = true;
        AllWavesCompleted?.Invoke();
    }

    private EnemySpawner ResolveSpawnerForIndex(int index)
    {
        if (controlledSpawners == null || controlledSpawners.Length == 0)
            return null;

        int validCount = 0;
        for (int i = 0; i < controlledSpawners.Length; i++)
        {
            if (controlledSpawners[i] != null)
                validCount++;
        }

        if (validCount == 0)
            return null;

        int target = index % validCount;
        int current = 0;

        for (int i = 0; i < controlledSpawners.Length; i++)
        {
            EnemySpawner candidate = controlledSpawners[i];
            if (candidate == null)
                continue;

            if (current == target)
                return candidate;

            current++;
        }

        return null;
    }

    private void OnDisable()
    {
        if (waveRoutine != null)
        {
            StopCoroutine(waveRoutine);
            waveRoutine = null;
        }
    }
}
