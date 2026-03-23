using System;
using System.Collections;
using UnityEngine;
using Random = UnityEngine.Random;

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

    [Header("Wave Composition")]
    [Tooltip("If enabled, WaveManager chooses spawners by configured enemy family and weights. If disabled, uses simple round-robin.")]
    [SerializeField] private bool useWeightedEnemyComposition = true;
    [Tooltip("Target share of ogre spawns in a wave (0 = no ogres, 1 = only ogres), if both families exist.")]
    [SerializeField, Range(0f, 1f)] private float ogreSpawnShare = 0.22f;
    [Tooltip("Global multiplier for all Ogre-family spawner weights. Lower this to reduce ogres.")]
    [SerializeField, Min(0f)] private float ogreWeightMultiplier = 0.45f;
    [Tooltip("Global multiplier for all Small-family spawner weights. Raise this to get more small enemies.")]
    [SerializeField, Min(0f)] private float smallWeightMultiplier = 1f;

    [SerializeField] private WaveDefinition[] waves =
    {
        new WaveDefinition { enemyCount = 10, spawnInterval = 0.7f, startDelay = 1f },
        new WaveDefinition { enemyCount = 40, spawnInterval = 0.6f, startDelay = 1.5f },
        new WaveDefinition { enemyCount = 60, spawnInterval = 0.5f, startDelay = 2f }
    };

    public event Action<int, int> WaveChanged;
    public event Action AllWavesCompleted;

    public int CurrentWaveIndex { get; private set; } = -1;
    public int TotalWaves => waves != null ? waves.Length : 0;
    public bool IsCompleted { get; private set; }

    private Coroutine waveRoutine;
    private EnemySpawner[] validSpawners = Array.Empty<EnemySpawner>();
    private EnemySpawner[] weightedAllSpawners = Array.Empty<EnemySpawner>();
    private float[] weightedAllCumulative = Array.Empty<float>();
    private EnemySpawner[] weightedOgreSpawners = Array.Empty<EnemySpawner>();
    private float[] weightedOgreCumulative = Array.Empty<float>();
    private EnemySpawner[] weightedSmallSpawners = Array.Empty<EnemySpawner>();
    private float[] weightedSmallCumulative = Array.Empty<float>();

    private int validSpawnerCount;
    private int weightedAllCount;
    private int weightedOgreCount;
    private int weightedSmallCount;

    private float totalWeightAll;
    private float totalWeightOgre;
    private float totalWeightSmall;

    private void Start()
    {
        if (controlledSpawners == null || controlledSpawners.Length == 0 || waves == null || waves.Length == 0)
            return;

        for (int i = 0; i < controlledSpawners.Length; i++)
        {
            EnemySpawner spawner = controlledSpawners[i];
            if (spawner != null)
                spawner.SetAutoSpawn(false);
        }

        RebuildSpawnerCache();
        waveRoutine = StartCoroutine(RunWaves());
    }

    private IEnumerator RunWaves()
    {
        for (int waveIndex = 0; waveIndex < waves.Length; waveIndex++)
        {
            RebuildSpawnerCache();

            CurrentWaveIndex = waveIndex;
            WaveChanged?.Invoke(CurrentWaveIndex + 1, waves.Length);

            WaveDefinition wave = waves[waveIndex];
            WaitForSeconds startDelayWait = wave.startDelay > 0f ? new WaitForSeconds(wave.startDelay) : null;
            if (wave.startDelay > 0f)
                yield return startDelayWait;

            int spawnCount = Mathf.Max(1, wave.enemyCount);
            float spawnInterval = Mathf.Max(0.05f, wave.spawnInterval);
            WaitForSeconds spawnIntervalWait = new WaitForSeconds(spawnInterval);

            for (int i = 0; i < spawnCount; i++)
            {
                EnemySpawner spawner = ResolveSpawnerForIndex(i);
                if (spawner != null)
                    spawner.SpawnEnemy();

                if (i < spawnCount - 1)
                    yield return spawnIntervalWait;
            }

            while (EnemyRegistry.Enemies.Count > 0)
                yield return null;
        }

        IsCompleted = true;
        AllWavesCompleted?.Invoke();
    }

    private EnemySpawner ResolveSpawnerForIndex(int index)
    {
        if (validSpawnerCount == 0)
            return null;
        if (useWeightedEnemyComposition && totalWeightAll > 0f)
        {
            bool hasBothFamilies = totalWeightOgre > 0f && totalWeightSmall > 0f;
            if (hasBothFamilies)
            {
                bool spawnOgre = Random.value < Mathf.Clamp01(ogreSpawnShare);
                EnemySpawner familyPick = spawnOgre
                    ? PickWeightedSpawner(weightedOgreSpawners, weightedOgreCumulative, weightedOgreCount, totalWeightOgre)
                    : PickWeightedSpawner(weightedSmallSpawners, weightedSmallCumulative, weightedSmallCount, totalWeightSmall);
                if (familyPick != null)
                    return familyPick;
            }

            EnemySpawner anyPick = PickWeightedSpawner(weightedAllSpawners, weightedAllCumulative, weightedAllCount, totalWeightAll);
            if (anyPick != null)
                return anyPick;
        }

        return ResolveRoundRobinSpawner(index);
    }

    private float GetEffectiveSpawnWeight(EnemySpawner spawner)
    {
        if (spawner == null)
            return 0f;

        float familyMultiplier = spawner.IsOgreSpawner
            ? Mathf.Max(0f, ogreWeightMultiplier)
            : Mathf.Max(0f, smallWeightMultiplier);

        return spawner.WaveSpawnWeight * familyMultiplier;
    }

    private EnemySpawner PickWeightedSpawner(EnemySpawner[] spawners, float[] cumulativeWeights, int count, float totalWeight)
    {
        if (count <= 0 || totalWeight <= 0f)
            return null;

        float pick = Random.value * totalWeight;
        for (int i = 0; i < count; i++)
        {
            if (pick <= cumulativeWeights[i])
                return spawners[i];
        }

        return spawners[count - 1];
    }

    private EnemySpawner ResolveRoundRobinSpawner(int index)
    {
        if (validSpawnerCount <= 0)
            return null;

        int target = index % validSpawnerCount;
        return validSpawners[target];
    }

    private void RebuildSpawnerCache()
    {
        validSpawnerCount = 0;
        weightedAllCount = 0;
        weightedOgreCount = 0;
        weightedSmallCount = 0;
        totalWeightAll = 0f;
        totalWeightOgre = 0f;
        totalWeightSmall = 0f;

        if (controlledSpawners == null || controlledSpawners.Length == 0)
            return;

        EnsureCacheCapacity(controlledSpawners.Length);

        for (int i = 0; i < controlledSpawners.Length; i++)
        {
            EnemySpawner candidate = controlledSpawners[i];
            if (candidate == null)
                continue;

            validSpawners[validSpawnerCount++] = candidate;

            float weight = GetEffectiveSpawnWeight(candidate);
            if (weight <= 0f)
                continue;

            totalWeightAll += weight;
            weightedAllSpawners[weightedAllCount] = candidate;
            weightedAllCumulative[weightedAllCount] = totalWeightAll;
            weightedAllCount++;

            if (candidate.IsOgreSpawner)
            {
                totalWeightOgre += weight;
                weightedOgreSpawners[weightedOgreCount] = candidate;
                weightedOgreCumulative[weightedOgreCount] = totalWeightOgre;
                weightedOgreCount++;
            }
            else
            {
                totalWeightSmall += weight;
                weightedSmallSpawners[weightedSmallCount] = candidate;
                weightedSmallCumulative[weightedSmallCount] = totalWeightSmall;
                weightedSmallCount++;
            }
        }
    }

    private void EnsureCacheCapacity(int capacity)
    {
        if (validSpawners.Length >= capacity)
            return;

        validSpawners = new EnemySpawner[capacity];
        weightedAllSpawners = new EnemySpawner[capacity];
        weightedAllCumulative = new float[capacity];
        weightedOgreSpawners = new EnemySpawner[capacity];
        weightedOgreCumulative = new float[capacity];
        weightedSmallSpawners = new EnemySpawner[capacity];
        weightedSmallCumulative = new float[capacity];
    }

    private void OnDisable()
    {
        if (waveRoutine != null)
        {
            StopCoroutine(waveRoutine);
            waveRoutine = null;
        }
    }

    public void ApplySpawnCompositionTuning(bool weightedComposition, float ogreShareValue, float ogreWeightValue, float smallWeightValue)
    {
        useWeightedEnemyComposition = weightedComposition;
        ogreSpawnShare = Mathf.Clamp01(ogreShareValue);
        ogreWeightMultiplier = Mathf.Max(0f, ogreWeightValue);
        smallWeightMultiplier = Mathf.Max(0f, smallWeightValue);
        RebuildSpawnerCache();
    }
}
