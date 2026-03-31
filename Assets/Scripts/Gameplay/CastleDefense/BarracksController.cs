using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class BarracksController : MonoBehaviour
{
    [Header("Defender Prefab")]
    [SerializeField] private DefenderUnit defenderPrefab;
    [SerializeField] private Transform defendersRoot;

    [Header("Slots")]
    [SerializeField, Tooltip("Spawn point per defender slot. If empty, barracks transform is used.")]
    private Transform[] spawnPoints;
    [SerializeField, Tooltip("Guard points near choke where each defender should hold position.")]
    private Transform[] defensePoints;

    [Header("Respawn")]
    [SerializeField, Min(0.1f)] private float respawnCooldown = 4f;
    [SerializeField] private bool spawnOnEnable = true;
    [SerializeField, Min(1), Tooltip("How many defender slots are active simultaneously.")]
    private int defendersPerBarracks = 2;
    [SerializeField, Tooltip("If enabled, barracks uses upgrade level as squad size (clamped by Max Defenders From Upgrade).")]
    private bool useUpgradeLevelAsSquadSize;
    [SerializeField, Min(1)] private int maxDefendersFromUpgrade = 4;

    private readonly Queue<DefenderUnit> pooledDefenders = new Queue<DefenderUnit>(8);
    private readonly List<Coroutine> respawnCoroutines = new List<Coroutine>(8);
    private readonly List<DefenderUnit> activeDefenders = new List<DefenderUnit>(8);
    private bool isAuthoringValid;
    private int runtimeSquadSize;

    private void Awake()
    {
        isAuthoringValid = ValidateAuthoring();
        runtimeSquadSize = Mathf.Max(1, defendersPerBarracks);
        if (!isAuthoringValid)
            enabled = false;
    }

    private void OnEnable()
    {
        if (!isAuthoringValid)
            return;

        EnsureRuntimeBuffers();
        if (spawnOnEnable)
            SpawnAllMissingDefenders();
    }

    private void OnDisable()
    {
        StopAllRespawns();
        ReturnAllActiveDefendersToPool();
    }

    public void SpawnAllMissingDefenders()
    {
        if (!isAuthoringValid)
            return;

        EnsureRuntimeBuffers();
        int activeSlotCount = ResolveActiveSlotCount();
        for (int i = 0; i < defensePoints.Length; i++)
        {
            if (i >= activeSlotCount)
            {
                ClearSlotAndCancelRespawn(i);
                continue;
            }

            if (activeDefenders[i] != null && activeDefenders[i].IsAlive)
                continue;

            SpawnDefenderForSlot(i);
        }
    }

    public void ApplyUpgradeLevel(int level)
    {
        if (useUpgradeLevelAsSquadSize)
            runtimeSquadSize = Mathf.Clamp(level, 1, Mathf.Max(1, maxDefendersFromUpgrade));
        else
            runtimeSquadSize = Mathf.Max(1, defendersPerBarracks);

        if (isActiveAndEnabled)
            SpawnAllMissingDefenders();
    }

    private bool ValidateAuthoring()
    {
        if (defenderPrefab == null)
        {
            Debug.LogError($"{name}: BarracksController requires defenderPrefab.", this);
            return false;
        }

        if (defensePoints == null || defensePoints.Length == 0)
        {
            Debug.LogError($"{name}: BarracksController requires at least one defense point.", this);
            return false;
        }

        for (int i = 0; i < defensePoints.Length; i++)
        {
            if (defensePoints[i] != null)
                continue;

            Debug.LogError($"{name}: defensePoints[{i}] is null.", this);
            return false;
        }

        return true;
    }

    private void EnsureRuntimeBuffers()
    {
        while (activeDefenders.Count < defensePoints.Length)
            activeDefenders.Add(null);

        while (respawnCoroutines.Count < defensePoints.Length)
            respawnCoroutines.Add(null);
    }

    private int ResolveActiveSlotCount()
    {
        int maxByPoints = defensePoints != null ? defensePoints.Length : 0;
        if (maxByPoints <= 0)
            return 0;

        int target = Mathf.Max(1, runtimeSquadSize);
        return Mathf.Clamp(target, 0, maxByPoints);
    }

    private void SpawnDefenderForSlot(int slotIndex)
    {
        DefenderUnit defender = GetOrCreateDefender();
        if (defender == null)
            return;

        Transform spawn = ResolveSpawnPoint(slotIndex);
        if (spawn != null)
            defender.transform.position = spawn.position;
        else
            defender.transform.position = transform.position;

        defender.transform.rotation = Quaternion.identity;
        defender.Died -= HandleDefenderDied;
        defender.Died += HandleDefenderDied;
        defender.ActivateAt(defensePoints[slotIndex]);

        activeDefenders[slotIndex] = defender;
    }

    private DefenderUnit GetOrCreateDefender()
    {
        while (pooledDefenders.Count > 0)
        {
            DefenderUnit pooled = pooledDefenders.Dequeue();
            if (pooled == null)
                continue;

            return pooled;
        }

        Transform parent = defendersRoot != null ? defendersRoot : null;
        return Instantiate(defenderPrefab, transform.position, Quaternion.identity, parent);
    }

    private Transform ResolveSpawnPoint(int slotIndex)
    {
        if (spawnPoints == null || spawnPoints.Length == 0)
            return null;

        int clamped = Mathf.Clamp(slotIndex, 0, spawnPoints.Length - 1);
        Transform spawn = spawnPoints[clamped];
        return spawn != null ? spawn : null;
    }

    private void HandleDefenderDied(DefenderUnit defender)
    {
        int slotIndex = FindSlotOfDefender(defender);
        if (slotIndex < 0)
        {
            if (defender != null)
                ReturnDefenderToPool(defender);
            return;
        }

        activeDefenders[slotIndex] = null;
        ReturnDefenderToPool(defender);
        ScheduleRespawn(slotIndex);
    }

    private int FindSlotOfDefender(DefenderUnit defender)
    {
        if (defender == null)
            return -1;

        for (int i = 0; i < activeDefenders.Count; i++)
        {
            if (activeDefenders[i] == defender)
                return i;
        }

        return -1;
    }

    private void ScheduleRespawn(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= respawnCoroutines.Count)
            return;

        if (slotIndex >= ResolveActiveSlotCount())
            return;

        if (respawnCoroutines[slotIndex] != null)
            StopCoroutine(respawnCoroutines[slotIndex]);

        respawnCoroutines[slotIndex] = StartCoroutine(RespawnAfterCooldown(slotIndex));
    }

    private IEnumerator RespawnAfterCooldown(int slotIndex)
    {
        float cooldown = Mathf.Max(0.1f, respawnCooldown);
        yield return new WaitForSeconds(cooldown);

        respawnCoroutines[slotIndex] = null;
        if (!isActiveAndEnabled || !isAuthoringValid)
            yield break;

        SpawnDefenderForSlot(slotIndex);
    }

    private void StopAllRespawns()
    {
        for (int i = 0; i < respawnCoroutines.Count; i++)
        {
            if (respawnCoroutines[i] == null)
                continue;

            StopCoroutine(respawnCoroutines[i]);
            respawnCoroutines[i] = null;
        }
    }

    private void ReturnAllActiveDefendersToPool()
    {
        for (int i = 0; i < activeDefenders.Count; i++)
        {
            DefenderUnit defender = activeDefenders[i];
            activeDefenders[i] = null;
            if (defender == null)
                continue;

            ReturnDefenderToPool(defender);
        }
    }

    private void ReturnDefenderToPool(DefenderUnit defender)
    {
        if (defender == null)
            return;

        defender.Died -= HandleDefenderDied;
        if (defender.gameObject.activeSelf)
            defender.gameObject.SetActive(false);

        pooledDefenders.Enqueue(defender);
    }

    private void ClearSlotAndCancelRespawn(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= activeDefenders.Count)
            return;

        if (slotIndex < respawnCoroutines.Count && respawnCoroutines[slotIndex] != null)
        {
            StopCoroutine(respawnCoroutines[slotIndex]);
            respawnCoroutines[slotIndex] = null;
        }

        DefenderUnit defender = activeDefenders[slotIndex];
        activeDefenders[slotIndex] = null;
        if (defender != null)
            ReturnDefenderToPool(defender);
    }
}
