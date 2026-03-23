using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class MetaProgressSaveService : MonoBehaviour
{
    [Header("Scene Wiring")]
    [SerializeField, Tooltip("Primary wallet whose balance is persisted.")]
    private PlayerCurrencyWallet currencyWallet;
    [SerializeField, Tooltip("Scene-wired tower identities to persist (no runtime Find).")]
    private TowerPersistentId[] trackedTowers = Array.Empty<TowerPersistentId>();

    [Header("Save Behavior")]
    [SerializeField, Tooltip("Load save data on Start and apply to wallet/towers.")]
    private bool loadOnStart = true;
    [SerializeField, Tooltip("Enable autosave when tracked data changes.")]
    private bool autosaveEnabled = true;
    [SerializeField, Min(0f), Tooltip("Debounce for autosave writes after changes.")]
    private float autosaveDebounceSeconds = 0.25f;
    [SerializeField, Tooltip("Flush save when app is paused/backgrounded.")]
    private bool saveOnApplicationPause = true;
    [SerializeField, Tooltip("Flush save when app is quitting.")]
    private bool saveOnApplicationQuit = true;
    [SerializeField, Tooltip("Save file name in Application.persistentDataPath.")]
    private string saveFileName = "twtd_meta_progress_v1.json";
    [SerializeField, Tooltip("Optional verbose logs for save operations.")]
    private bool verboseLogging;

    private const int SaveFormatVersion = 1;

    private ISaveStore<MetaProgressSaveData> saveStore;
    private readonly Dictionary<string, TowerSaveRecord> loadedTowerMap = new Dictionary<string, TowerSaveRecord>(64);
    private readonly HashSet<string> duplicateIdGuard = new HashSet<string>();

    private bool isDirty;
    private bool isRestoring;
    private bool isSubscribed;
    private float autosaveTimer;

    private void Awake()
    {
        string filePath = BuildSavePath(saveFileName);
        saveStore = new JsonFileSaveStore<MetaProgressSaveData>(filePath);
    }

    private void OnEnable()
    {
        SubscribeRuntimeEvents();
    }

    private void Start()
    {
        if (loadOnStart)
            LoadAndApply();
    }

    private void Update()
    {
        if (!autosaveEnabled || !isDirty)
            return;

        if (autosaveDebounceSeconds <= 0f)
        {
            SaveNow();
            return;
        }

        autosaveTimer -= Time.unscaledDeltaTime;
        if (autosaveTimer <= 0f)
            SaveNow();
    }

    private void OnDisable()
    {
        UnsubscribeRuntimeEvents();
        if (isDirty)
            SaveNow();
    }

    private void OnApplicationPause(bool pauseStatus)
    {
        if (!pauseStatus || !saveOnApplicationPause)
            return;

        SaveNow();
    }

    private void OnApplicationQuit()
    {
        if (!saveOnApplicationQuit)
            return;

        SaveNow();
    }

    [ContextMenu("Save/Save Now")]
    public void SaveNow()
    {
        if (!TryBuildSaveData(out MetaProgressSaveData data))
            return;

        if (!saveStore.Save(data))
            return;

        isDirty = false;
        autosaveTimer = 0f;

        if (verboseLogging)
            Debug.Log($"MetaProgressSaveService: saved to {saveStore.StoragePath}", this);
    }

    [ContextMenu("Save/Load And Apply")]
    public void LoadAndApply()
    {
        if (!saveStore.TryLoad(out MetaProgressSaveData data) || data == null)
        {
            if (verboseLogging)
                Debug.Log("MetaProgressSaveService: no save data found, keeping authoring defaults.", this);

            isDirty = false;
            return;
        }

        ApplySaveData(data);
        isDirty = false;
        autosaveTimer = 0f;

        if (verboseLogging)
            Debug.Log($"MetaProgressSaveService: loaded from {saveStore.StoragePath}", this);
    }

    [ContextMenu("Save/Delete Save File")]
    public void DeleteSaveFile()
    {
        if (!saveStore.Delete())
            return;

        isDirty = false;
        autosaveTimer = 0f;

        if (verboseLogging)
            Debug.Log($"MetaProgressSaveService: deleted save file {saveStore.StoragePath}", this);
    }

    [ContextMenu("Authoring/Collect Tower IDs From Children")]
    private void CollectTowerIdsFromChildren()
    {
        trackedTowers = GetComponentsInChildren<TowerPersistentId>(includeInactive: true);
    }

    private void SubscribeRuntimeEvents()
    {
        if (isSubscribed)
            return;

        if (currencyWallet != null)
            currencyWallet.BalanceChanged += HandleWalletBalanceChanged;

        for (int i = 0; i < trackedTowers.Length; i++)
        {
            TowerPersistentId towerId = trackedTowers[i];
            if (towerId == null || towerId.Tower == null)
                continue;

            towerId.Tower.DataChanged += HandleTowerDataChanged;
            towerId.Tower.Sold += HandleTowerSold;
        }

        isSubscribed = true;
    }

    private void UnsubscribeRuntimeEvents()
    {
        if (!isSubscribed)
            return;

        if (currencyWallet != null)
            currencyWallet.BalanceChanged -= HandleWalletBalanceChanged;

        for (int i = 0; i < trackedTowers.Length; i++)
        {
            TowerPersistentId towerId = trackedTowers[i];
            if (towerId == null || towerId.Tower == null)
                continue;

            towerId.Tower.DataChanged -= HandleTowerDataChanged;
            towerId.Tower.Sold -= HandleTowerSold;
        }

        isSubscribed = false;
    }

    private void HandleWalletBalanceChanged(int _)
    {
        MarkDirty();
    }

    private void HandleTowerDataChanged(TowerUpgradable _)
    {
        MarkDirty();
    }

    private void HandleTowerSold(TowerUpgradable _)
    {
        MarkDirty();
    }

    private void MarkDirty()
    {
        if (isRestoring)
            return;

        isDirty = true;
        if (autosaveEnabled)
            autosaveTimer = autosaveDebounceSeconds;
    }

    private void ApplySaveData(MetaProgressSaveData data)
    {
        if (currencyWallet == null)
        {
            Debug.LogError("MetaProgressSaveService: currencyWallet is not wired.", this);
            return;
        }

        if (data.Version != SaveFormatVersion)
            Debug.LogWarning($"MetaProgressSaveService: save version {data.Version} differs from expected {SaveFormatVersion}. Attempting compatible load.", this);

        isRestoring = true;

        currencyWallet.RestoreBalance(data.CurrencyBalance, notify: true);

        loadedTowerMap.Clear();
        TowerSaveRecord[] saveRecords = data.Towers ?? Array.Empty<TowerSaveRecord>();
        for (int i = 0; i < saveRecords.Length; i++)
        {
            TowerSaveRecord record = saveRecords[i];
            if (string.IsNullOrWhiteSpace(record.TowerId))
                continue;

            if (!loadedTowerMap.ContainsKey(record.TowerId))
                loadedTowerMap.Add(record.TowerId, record);
        }

        duplicateIdGuard.Clear();

        for (int i = 0; i < trackedTowers.Length; i++)
        {
            TowerPersistentId towerId = trackedTowers[i];
            if (towerId == null || towerId.Tower == null)
                continue;

            if (!ValidateTowerId(towerId))
                continue;

            if (!loadedTowerMap.TryGetValue(towerId.PersistentId, out TowerSaveRecord record))
                continue;

            TowerUpgradePersistentState state = new TowerUpgradePersistentState
            {
                LevelIndex = record.LevelIndex,
                IsSold = record.IsSold
            };

            towerId.Tower.RestorePersistentState(state);
        }

        isRestoring = false;
    }

    private bool TryBuildSaveData(out MetaProgressSaveData data)
    {
        if (currencyWallet == null)
        {
            Debug.LogError("MetaProgressSaveService: currencyWallet is not wired.", this);
            data = null;
            return false;
        }

        List<TowerSaveRecord> records = new List<TowerSaveRecord>(trackedTowers.Length);
        duplicateIdGuard.Clear();

        for (int i = 0; i < trackedTowers.Length; i++)
        {
            TowerPersistentId towerId = trackedTowers[i];
            if (towerId == null || towerId.Tower == null)
                continue;

            if (!ValidateTowerId(towerId))
                continue;

            TowerUpgradePersistentState state = towerId.Tower.CapturePersistentState();
            records.Add(new TowerSaveRecord
            {
                TowerId = towerId.PersistentId,
                LevelIndex = state.LevelIndex,
                IsSold = state.IsSold
            });
        }

        data = new MetaProgressSaveData
        {
            Version = SaveFormatVersion,
            CurrencyBalance = currencyWallet.Balance,
            Towers = records.ToArray()
        };

        return true;
    }

    private bool ValidateTowerId(TowerPersistentId towerId)
    {
        if (towerId == null)
            return false;

        if (string.IsNullOrWhiteSpace(towerId.PersistentId))
        {
            Debug.LogError($"MetaProgressSaveService: TowerPersistentId on {towerId.name} is empty.", towerId);
            return false;
        }

        if (!duplicateIdGuard.Add(towerId.PersistentId))
        {
            Debug.LogError($"MetaProgressSaveService: duplicate TowerPersistentId '{towerId.PersistentId}'.", towerId);
            return false;
        }

        return true;
    }

    private static string BuildSavePath(string fileName)
    {
        string safeFileName = string.IsNullOrWhiteSpace(fileName) ? "twtd_meta_progress_v1.json" : fileName.Trim();
        return Path.Combine(Application.persistentDataPath, safeFileName);
    }

    [Serializable]
    private sealed class MetaProgressSaveData
    {
        public int Version = SaveFormatVersion;
        public int CurrencyBalance;
        public TowerSaveRecord[] Towers = Array.Empty<TowerSaveRecord>();
    }

    [Serializable]
    private struct TowerSaveRecord
    {
        public string TowerId;
        public int LevelIndex;
        public bool IsSold;
    }
}

public interface ISaveStore<TData>
{
    string StoragePath { get; }
    bool TryLoad(out TData data);
    bool Save(TData data);
    bool Delete();
}

public sealed class JsonFileSaveStore<TData> : ISaveStore<TData> where TData : class
{
    public string StoragePath { get; }

    public JsonFileSaveStore(string storagePath)
    {
        StoragePath = storagePath;
    }

    public bool TryLoad(out TData data)
    {
        data = null;

        try
        {
            if (!File.Exists(StoragePath))
                return false;

            string json = File.ReadAllText(StoragePath);
            if (string.IsNullOrWhiteSpace(json))
                return false;

            data = JsonUtility.FromJson<TData>(json);
            return data != null;
        }
        catch (Exception exception)
        {
            Debug.LogError($"JsonFileSaveStore: failed to load save file '{StoragePath}'. {exception}");
            return false;
        }
    }

    public bool Save(TData data)
    {
        if (data == null)
            return false;

        try
        {
            string directory = Path.GetDirectoryName(StoragePath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            string json = JsonUtility.ToJson(data, prettyPrint: false);
            File.WriteAllText(StoragePath, json);
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogError($"JsonFileSaveStore: failed to save '{StoragePath}'. {exception}");
            return false;
        }
    }

    public bool Delete()
    {
        try
        {
            if (!File.Exists(StoragePath))
                return true;

            File.Delete(StoragePath);
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogError($"JsonFileSaveStore: failed to delete '{StoragePath}'. {exception}");
            return false;
        }
    }
}
