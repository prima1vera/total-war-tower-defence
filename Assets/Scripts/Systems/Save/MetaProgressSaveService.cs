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
    [SerializeField, Tooltip("Build service used to restore towers on build slots.")]
    private TowerBuildService towerBuildService;
    [SerializeField, Tooltip("Build slots to persist (occupied/empty + built tower state).")]
    private BuildPlace[] trackedBuildPlaces = Array.Empty<BuildPlace>();

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
    private readonly Dictionary<string, BuildPlaceSaveRecord> loadedBuildPlaceMap = new Dictionary<string, BuildPlaceSaveRecord>(64);
    private readonly Dictionary<string, BuildPlace> buildPlaceById = new Dictionary<string, BuildPlace>(64);
    private readonly HashSet<string> duplicateIdGuard = new HashSet<string>();
    private readonly HashSet<string> duplicateBuildPlaceIdGuard = new HashSet<string>();

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

    [ContextMenu("Authoring/Collect Build Places From Children")]
    private void CollectBuildPlacesFromChildren()
    {
        trackedBuildPlaces = GetComponentsInChildren<BuildPlace>(includeInactive: true);
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

        for (int i = 0; i < trackedBuildPlaces.Length; i++)
        {
            BuildPlace place = trackedBuildPlaces[i];
            if (place == null)
                continue;

            place.StateChanged += HandleBuildPlaceStateChanged;
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

        for (int i = 0; i < trackedBuildPlaces.Length; i++)
        {
            BuildPlace place = trackedBuildPlaces[i];
            if (place == null)
                continue;

            place.StateChanged -= HandleBuildPlaceStateChanged;
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

    private void HandleBuildPlaceStateChanged(BuildPlace _)
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
        try
        {
            currencyWallet.RestoreBalance(data.CurrencyBalance, notify: true);
            RestoreTrackedTowers(data);
            RestoreBuildPlaces(data);
        }
        finally
        {
            isRestoring = false;
        }
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
            Towers = records.ToArray(),
            BuildPlaces = CaptureBuildPlaceRecords()
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

    private void RestoreTrackedTowers(MetaProgressSaveData data)
    {
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
    }

    private BuildPlaceSaveRecord[] CaptureBuildPlaceRecords()
    {
        if (trackedBuildPlaces == null || trackedBuildPlaces.Length == 0)
            return Array.Empty<BuildPlaceSaveRecord>();

        duplicateBuildPlaceIdGuard.Clear();
        var records = new List<BuildPlaceSaveRecord>(trackedBuildPlaces.Length);

        for (int i = 0; i < trackedBuildPlaces.Length; i++)
        {
            BuildPlace place = trackedBuildPlaces[i];
            if (!ValidateBuildPlace(place))
                continue;

            BuildPlaceSaveRecord record = new BuildPlaceSaveRecord
            {
                PlaceId = place.PlaceId,
                HasTower = place.IsOccupied,
                OptionId = string.Empty,
                LevelIndex = -1,
                IsSold = false
            };

            if (place.IsOccupied && place.OccupiedTower != null && !string.IsNullOrWhiteSpace(place.OccupiedOptionId))
            {
                TowerUpgradePersistentState towerState = place.OccupiedTower.CapturePersistentState();
                record.HasTower = true;
                record.OptionId = place.OccupiedOptionId;
                record.LevelIndex = towerState.LevelIndex;
                record.IsSold = towerState.IsSold;
            }

            records.Add(record);
        }

        return records.ToArray();
    }

    private void RestoreBuildPlaces(MetaProgressSaveData data)
    {
        loadedBuildPlaceMap.Clear();
        buildPlaceById.Clear();
        duplicateBuildPlaceIdGuard.Clear();

        BuildPlaceSaveRecord[] savedRecords = data.BuildPlaces ?? Array.Empty<BuildPlaceSaveRecord>();
        for (int i = 0; i < savedRecords.Length; i++)
        {
            BuildPlaceSaveRecord record = savedRecords[i];
            if (string.IsNullOrWhiteSpace(record.PlaceId))
                continue;

            if (!loadedBuildPlaceMap.ContainsKey(record.PlaceId))
                loadedBuildPlaceMap.Add(record.PlaceId, record);
        }

        for (int i = 0; i < trackedBuildPlaces.Length; i++)
        {
            BuildPlace place = trackedBuildPlaces[i];
            if (!ValidateBuildPlace(place))
                continue;

            buildPlaceById[place.PlaceId] = place;
            if (place.IsOccupied)
                place.ClearOccupiedTower(destroyTowerObject: true);
        }

        if (towerBuildService == null && loadedBuildPlaceMap.Count > 0)
        {
            Debug.LogError("MetaProgressSaveService: towerBuildService is not wired, cannot restore build places.", this);
            return;
        }

        foreach (var pair in loadedBuildPlaceMap)
        {
            BuildPlaceSaveRecord record = pair.Value;
            if (!record.HasTower)
                continue;

            if (!buildPlaceById.TryGetValue(record.PlaceId, out BuildPlace place))
                continue;

            if (string.IsNullOrWhiteSpace(record.OptionId))
                continue;

            TowerUpgradePersistentState state = new TowerUpgradePersistentState
            {
                LevelIndex = record.LevelIndex,
                IsSold = record.IsSold
            };

            bool restored = towerBuildService.TryRestorePlaceState(place, record.OptionId, state);
            if (!restored)
                Debug.LogWarning($"MetaProgressSaveService: failed to restore build place '{record.PlaceId}' with option '{record.OptionId}'.", this);
        }
    }

    private bool ValidateBuildPlace(BuildPlace place)
    {
        if (place == null)
            return false;

        if (!place.HasValidId())
        {
            Debug.LogError($"MetaProgressSaveService: BuildPlace on {place.name} has empty PlaceId.", place);
            return false;
        }

        if (!duplicateBuildPlaceIdGuard.Add(place.PlaceId))
        {
            Debug.LogError($"MetaProgressSaveService: duplicate BuildPlace id '{place.PlaceId}'.", place);
            return false;
        }

        return true;
    }

    [Serializable]
    private sealed class MetaProgressSaveData
    {
        public int Version = SaveFormatVersion;
        public int CurrencyBalance;
        public TowerSaveRecord[] Towers = Array.Empty<TowerSaveRecord>();
        public BuildPlaceSaveRecord[] BuildPlaces = Array.Empty<BuildPlaceSaveRecord>();
    }

    [Serializable]
    private struct TowerSaveRecord
    {
        public string TowerId;
        public int LevelIndex;
        public bool IsSold;
    }

    [Serializable]
    private struct BuildPlaceSaveRecord
    {
        public string PlaceId;
        public bool HasTower;
        public string OptionId;
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
