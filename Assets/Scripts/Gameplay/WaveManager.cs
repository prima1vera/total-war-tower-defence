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
        float totalWeight = 0f;
        float totalOgreWeight = 0f;
        float totalSmallWeight = 0f;

        for (int i = 0; i < controlledSpawners.Length; i++)
        {
            EnemySpawner candidate = controlledSpawners[i];
            if (candidate == null)
                continue;

            validCount++;

            float weight = GetEffectiveSpawnWeight(candidate);
            if (weight <= 0f)
                continue;

            totalWeight += weight;
            if (candidate.IsOgreSpawner)
                totalOgreWeight += weight;
            else
                totalSmallWeight += weight;
        }

        if (validCount == 0)
            return null;

        if (useWeightedEnemyComposition && totalWeight > 0f)
        {
            bool hasBothFamilies = totalOgreWeight > 0f && totalSmallWeight > 0f;
            if (hasBothFamilies)
            {
                bool spawnOgre = Random.value < Mathf.Clamp01(ogreSpawnShare);
                float familyWeight = spawnOgre ? totalOgreWeight : totalSmallWeight;

                EnemySpawner familyPick = PickWeightedSpawner(familyWeight, spawnOgreOnly: spawnOgre, filterByFamily: true);
                if (familyPick != null)
                    return familyPick;
            }

            EnemySpawner anyPick = PickWeightedSpawner(totalWeight, spawnOgreOnly: false, filterByFamily: false);
            if (anyPick != null)
                return anyPick;
        }

        return ResolveRoundRobinSpawner(index, validCount);
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

    private EnemySpawner PickWeightedSpawner(float totalWeight, bool spawnOgreOnly, bool filterByFamily)
    {
        if (totalWeight <= 0f)
            return null;

        float pick = Random.value * totalWeight;
        EnemySpawner lastValid = null;

        for (int i = 0; i < controlledSpawners.Length; i++)
        {
            EnemySpawner candidate = controlledSpawners[i];
            if (candidate == null)
                continue;

            if (filterByFamily && candidate.IsOgreSpawner != spawnOgreOnly)
                continue;

            float weight = GetEffectiveSpawnWeight(candidate);
            if (weight <= 0f)
                continue;

            lastValid = candidate;
            pick -= weight;
            if (pick <= 0f)
                return candidate;
        }

        return lastValid;
    }

    private EnemySpawner ResolveRoundRobinSpawner(int index, int validCount)
    {
        if (validCount <= 0)
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
