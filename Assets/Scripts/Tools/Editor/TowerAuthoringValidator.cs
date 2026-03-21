using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class TowerAuthoringValidator
{
    private const string ValidationMenuPath = "TWTD/Validation/Validate Tower Authoring";
    private const string SceneValidationMenuPath = "TWTD/Validation/Validate Open Scene Tower Wiring";
    private const string ShootArrowEventName = "ShootArrow";

    [MenuItem(ValidationMenuPath)]
    private static void ValidateAll()
    {
        int errors = 0;
        int warnings = 0;

        int profileCount = ValidateAllEvolutionProfiles(ref errors, ref warnings);
        int treeCount = ValidateAllUpgradeTrees(ref errors, ref warnings);
        int sceneTowerCount = ValidateOpenSceneTowerWiring(ref errors, ref warnings);

        Debug.Log($"[TowerAuthoringValidator] Validation complete. Scene Towers: {sceneTowerCount}, Trees: {treeCount}, Profiles: {profileCount}, Errors: {errors}, Warnings: {warnings}.");
    }

    [MenuItem(SceneValidationMenuPath)]
    private static void ValidateSceneOnly()
    {
        int errors = 0;
        int warnings = 0;

        int sceneTowerCount = ValidateOpenSceneTowerWiring(ref errors, ref warnings);
        Debug.Log($"[TowerAuthoringValidator] Scene wiring validated. Towers: {sceneTowerCount}, Errors: {errors}, Warnings: {warnings}.");
    }

    [MenuItem("CONTEXT/TowerUpgradeTree/Validate Tree")]
    private static void ValidateSelectedTree(MenuCommand command)
    {
        TowerUpgradeTree tree = command.context as TowerUpgradeTree;
        if (tree == null)
            return;

        int errors = 0;
        int warnings = 0;

        ValidateTree(tree, ref errors, ref warnings);

        Debug.Log($"[TowerAuthoringValidator] Tree '{tree.name}' validated. Errors: {errors}, Warnings: {warnings}.", tree);
    }

    [MenuItem("CONTEXT/TowerEvolutionProfile/Validate Evolution Profile")]
    private static void ValidateSelectedProfile(MenuCommand command)
    {
        TowerEvolutionProfile profile = command.context as TowerEvolutionProfile;
        if (profile == null)
            return;

        int errors = 0;
        int warnings = 0;

        ValidateProfile(profile, ref errors, ref warnings);

        Debug.Log($"[TowerAuthoringValidator] Evolution profile '{profile.name}' validated. Errors: {errors}, Warnings: {warnings}.", profile);
    }

    private static int ValidateAllEvolutionProfiles(ref int errors, ref int warnings)
    {
        string[] guids = AssetDatabase.FindAssets("t:TowerEvolutionProfile");
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            TowerEvolutionProfile profile = AssetDatabase.LoadAssetAtPath<TowerEvolutionProfile>(path);
            if (profile == null)
                continue;

            ValidateProfile(profile, ref errors, ref warnings);
        }

        return guids.Length;
    }

    private static int ValidateAllUpgradeTrees(ref int errors, ref int warnings)
    {
        string[] guids = AssetDatabase.FindAssets("t:TowerUpgradeTree");
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            TowerUpgradeTree tree = AssetDatabase.LoadAssetAtPath<TowerUpgradeTree>(path);
            if (tree == null)
                continue;

            ValidateTree(tree, ref errors, ref warnings);
        }

        return guids.Length;
    }

    private static int ValidateOpenSceneTowerWiring(ref int errors, ref int warnings)
    {
        Tower[] towers = UnityEngine.Object.FindObjectsByType<Tower>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        TowerProjectilePoolRegistry registry = UnityEngine.Object.FindFirstObjectByType<TowerProjectilePoolRegistry>(FindObjectsInactive.Include);
        ValidateProjectilePoolRegistry(registry, ref errors, ref warnings);

        HashSet<TowerProjectilePoolKey> usedPoolKeys = CollectUsedPoolKeys();
        MergeSceneArcherPoolKeys(usedPoolKeys);
        ValidateRegistryCoverage(registry, usedPoolKeys, ref errors);

        for (int i = 0; i < towers.Length; i++)
            ValidateTowerSceneWiring(towers[i], ref errors, ref warnings);

        if (towers.Length == 0)
            LogWarning(null, ref warnings, "No Tower components found in open scenes.");

        return towers.Length;
    }

    private static void ValidateProjectilePoolRegistry(TowerProjectilePoolRegistry registry, ref int errors, ref int warnings)
    {
        if (registry == null)
        {
            LogError(null, ref errors, "TowerProjectilePoolRegistry is missing in open scene.");
            return;
        }

        SerializedObject so = new SerializedObject(registry);

        ArrowPool basePool = GetPoolProperty(so, "basePool");
        ArrowPool firePool = GetPoolProperty(so, "firePool");
        ArrowPool frostPool = GetPoolProperty(so, "frostPool");
        ArrowPool ironPool = GetPoolProperty(so, "ironPool");
        ArrowPool archerPool = GetPoolProperty(so, "archerPool");

        if (basePool == null)
            LogError(registry, ref errors, "TowerProjectilePoolRegistry.basePool is not assigned.");

        ValidatePoolReference(basePool, registry, "basePool", ref errors);
        ValidatePoolReference(firePool, registry, "firePool", ref errors);
        ValidatePoolReference(frostPool, registry, "frostPool", ref errors);
        ValidatePoolReference(ironPool, registry, "ironPool", ref errors);
        ValidatePoolReference(archerPool, registry, "archerPool", ref errors);

        if (firePool == null)
            LogWarning(registry, ref warnings, "firePool is empty, Fire towers will fallback to basePool.");

        if (frostPool == null)
            LogWarning(registry, ref warnings, "frostPool is empty, Frost towers will fallback to basePool.");

        if (ironPool == null)
            LogWarning(registry, ref warnings, "ironPool is empty, Iron towers will fallback to basePool.");

        if (archerPool == null)
            LogWarning(registry, ref warnings, "archerPool is empty, Archer towers will fallback to basePool.");
    }

    private static void ValidateRegistryCoverage(TowerProjectilePoolRegistry registry, HashSet<TowerProjectilePoolKey> usedPoolKeys, ref int errors)
    {
        if (registry == null)
            return;

        SerializedObject so = new SerializedObject(registry);
        ArrowPool basePool = GetPoolProperty(so, "basePool");
        ArrowPool firePool = GetPoolProperty(so, "firePool");
        ArrowPool frostPool = GetPoolProperty(so, "frostPool");
        ArrowPool ironPool = GetPoolProperty(so, "ironPool");
        ArrowPool archerPool = GetPoolProperty(so, "archerPool");

        foreach (TowerProjectilePoolKey key in usedPoolKeys)
        {
            bool hasCoverage = HasEffectivePoolCoverage(key, basePool, firePool, frostPool, ironPool, archerPool);
            if (!hasCoverage)
                LogError(registry, ref errors, $"No effective scene pool coverage for key '{key}'. Assign specific pool or basePool.");
        }
    }

    private static HashSet<TowerProjectilePoolKey> CollectUsedPoolKeys()
    {
        HashSet<TowerProjectilePoolKey> used = new HashSet<TowerProjectilePoolKey> { TowerProjectilePoolKey.Base };

        string[] guids = AssetDatabase.FindAssets("t:TowerUpgradeTree");
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            TowerUpgradeTree tree = AssetDatabase.LoadAssetAtPath<TowerUpgradeTree>(path);
            if (tree == null)
                continue;

            for (int levelIndex = 0; levelIndex < tree.LevelCount; levelIndex++)
            {
                if (!tree.TryGetLevel(levelIndex, out TowerUpgradeLevelDefinition level))
                    continue;

                if (level.EvolutionProfile != null)
                    used.Add(level.EvolutionProfile.ProjectilePoolKey);
            }
        }

        return used;
    }

    private static bool HasEffectivePoolCoverage(
        TowerProjectilePoolKey key,
        ArrowPool basePool,
        ArrowPool firePool,
        ArrowPool frostPool,
        ArrowPool ironPool,
        ArrowPool archerPool)
    {
        switch (key)
        {
            case TowerProjectilePoolKey.Fire:
                return firePool != null || basePool != null;
            case TowerProjectilePoolKey.Frost:
                return frostPool != null || basePool != null;
            case TowerProjectilePoolKey.Iron:
                return ironPool != null || basePool != null;
            case TowerProjectilePoolKey.Archer:
                return archerPool != null || basePool != null;
            default:
                return basePool != null;
        }
    }

    private static void MergeSceneArcherPoolKeys(HashSet<TowerProjectilePoolKey> used)
    {
        ArcherTowerProjectileEmitter[] emitters = UnityEngine.Object.FindObjectsByType<ArcherTowerProjectileEmitter>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < emitters.Length; i++)
        {
            ArcherTowerProjectileEmitter emitter = emitters[i];
            if (emitter == null)
                continue;

            SerializedObject serializedEmitter = new SerializedObject(emitter);
            SerializedProperty keyProperty = serializedEmitter.FindProperty("projectilePoolKey");
            if (keyProperty == null)
                continue;

            int enumValue = keyProperty.enumValueIndex;
            if (enumValue < 0 || enumValue >= Enum.GetValues(typeof(TowerProjectilePoolKey)).Length)
                continue;

            used.Add((TowerProjectilePoolKey)enumValue);
        }
    }

    private static ArrowPool GetPoolProperty(SerializedObject so, string propertyName)
    {
        SerializedProperty property = so.FindProperty(propertyName);
        return property != null ? property.objectReferenceValue as ArrowPool : null;
    }

    private static void ValidatePoolReference(ArrowPool pool, UnityEngine.Object context, string fieldName, ref int errors)
    {
        if (pool == null)
            return;

        if (!pool.gameObject.scene.IsValid())
            LogError(context, ref errors, $"{fieldName} points to prefab asset. Assign scene instance instead.");
    }

    private static void ValidateTowerSceneWiring(Tower tower, ref int errors, ref int warnings)
    {
        if (tower == null)
            return;

        SerializedObject so = new SerializedObject(tower);

        ValidateReferenceProperty(tower, so, "firePoint", "firePoint is not assigned.", ref errors);
        ValidateReferenceProperty(tower, so, "towerSpriteRenderer", "towerSpriteRenderer is not assigned.", ref errors);
        ValidateReferenceProperty(tower, so, "towerGroundRenderer", "towerGroundRenderer is not assigned.", ref errors);

        if (tower.GetComponent<Animator>() == null)
            LogError(tower, ref errors, "Animator component is missing. Shoot animation event cannot run.");

        if (tower.GetComponent<Collider2D>() == null)
            LogError(tower, ref errors, "Collider2D is missing. Tower selection raycast will fail.");

        if (tower.GetComponent<TowerUpgradable>() == null)
            LogWarning(tower, ref warnings, "TowerUpgradable component is missing. Upgrade panel flow may not work.");

        int towerLayer = LayerMask.NameToLayer("Tower");
        if (towerLayer >= 0 && tower.gameObject.layer != towerLayer)
            LogWarning(tower, ref warnings, "Tower is not on 'Tower' layer. Selection input may miss this tower.");
    }

    private static void ValidateReferenceProperty(
        UnityEngine.Object context,
        SerializedObject serializedObject,
        string propertyName,
        string errorMessage,
        ref int errors)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property == null || property.objectReferenceValue == null)
            LogError(context, ref errors, errorMessage);
    }

    private static void ValidateTree(TowerUpgradeTree tree, ref int errors, ref int warnings)
    {
        int levelCount = tree.LevelCount;
        if (levelCount <= 0)
        {
            LogError(tree, ref errors, "Upgrade tree has no levels.");
            return;
        }

        bool[] reachable = BuildReachabilityMap(tree, levelCount, ref errors);

        for (int i = 0; i < levelCount; i++)
        {
            if (!tree.TryGetLevel(i, out TowerUpgradeLevelDefinition level))
            {
                LogError(tree, ref errors, $"Level index {i} is not readable.");
                continue;
            }

            if (level.Level < 1)
                LogWarning(tree, ref warnings, $"Level index {i} has display level < 1.");

            if (level.EvolutionProfile == null && i != tree.StartLevelIndex)
                LogWarning(tree, ref warnings, $"Level index {i} has no evolution profile assigned.");

            ValidateOption(tree, i, levelCount, TowerUpgradeSlot.A, level.UpgradeA, ref errors, ref warnings);
            ValidateOption(tree, i, levelCount, TowerUpgradeSlot.B, level.UpgradeB, ref errors, ref warnings);
            ValidateOption(tree, i, levelCount, TowerUpgradeSlot.C, level.UpgradeC, ref errors, ref warnings);

            if (!reachable[i])
                LogWarning(tree, ref warnings, $"Level index {i} is unreachable from start level index {tree.StartLevelIndex}.");
        }
    }

    private static bool[] BuildReachabilityMap(TowerUpgradeTree tree, int levelCount, ref int errors)
    {
        bool[] reachable = new bool[levelCount];
        Stack<int> pending = new Stack<int>();

        int startIndex = tree.StartLevelIndex;
        if (startIndex < 0 || startIndex >= levelCount)
        {
            LogError(tree, ref errors, $"StartLevelIndex {startIndex} is outside level range 0..{levelCount - 1}.");
            return reachable;
        }

        reachable[startIndex] = true;
        pending.Push(startIndex);

        while (pending.Count > 0)
        {
            int current = pending.Pop();

            if (!tree.TryGetLevel(current, out TowerUpgradeLevelDefinition level))
                continue;

            TryVisitOption(level.UpgradeA, current, levelCount, reachable, pending);
            TryVisitOption(level.UpgradeB, current, levelCount, reachable, pending);
            TryVisitOption(level.UpgradeC, current, levelCount, reachable, pending);
        }

        return reachable;
    }

    private static void TryVisitOption(
        TowerUpgradeOptionDefinition option,
        int fromIndex,
        int levelCount,
        bool[] reachable,
        Stack<int> pending)
    {
        if (!option.IsConfigured)
            return;

        int next = option.NextLevelIndex;
        if (next < 0 || next >= levelCount || next == fromIndex)
            return;

        if (reachable[next])
            return;

        reachable[next] = true;
        pending.Push(next);
    }

    private static void ValidateOption(
        TowerUpgradeTree tree,
        int levelIndex,
        int levelCount,
        TowerUpgradeSlot slot,
        TowerUpgradeOptionDefinition option,
        ref int errors,
        ref int warnings)
    {
        if (!option.IsConfigured)
            return;

        if (option.NextLevelIndex < 0 || option.NextLevelIndex >= levelCount)
        {
            LogError(tree, ref errors, $"Level index {levelIndex} slot {slot}: NextLevelIndex {option.NextLevelIndex} is out of range.");
            return;
        }

        if (option.NextLevelIndex == levelIndex)
            LogError(tree, ref errors, $"Level index {levelIndex} slot {slot}: self-loop detected (NextLevelIndex points to itself).");

        if (string.IsNullOrWhiteSpace(option.Label))
            LogWarning(tree, ref warnings, $"Level index {levelIndex} slot {slot}: label is empty.");
    }

    private static void ValidateProfile(TowerEvolutionProfile profile, ref int errors, ref int warnings)
    {
        if (profile.TowerSprite == null)
            LogWarning(profile, ref warnings, "Tower sprite is not assigned.");

        RuntimeAnimatorController controller = profile.AnimatorController;
        if (controller == null)
        {
            LogWarning(profile, ref warnings, "Animator controller is not assigned. Active controller may keep old visuals.");
            return;
        }

        AnimationClip[] clips = controller.animationClips;
        if (clips == null || clips.Length == 0)
        {
            LogError(profile, ref errors, "Animator controller has no clips.");
            return;
        }

        bool hasFireClip = false;
        for (int i = 0; i < clips.Length; i++)
        {
            AnimationClip clip = clips[i];
            if (clip == null)
                continue;

            if (clip.name.IndexOf("Fire", StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            hasFireClip = true;
            if (!HasAnimationEvent(clip, ShootArrowEventName))
                LogError(profile, ref errors, $"Clip '{clip.name}' has no '{ShootArrowEventName}' animation event.");
        }

        if (!hasFireClip)
            LogError(profile, ref errors, "Animator controller has no clip containing 'Fire' in name.");
    }

    private static bool HasAnimationEvent(AnimationClip clip, string eventName)
    {
        AnimationEvent[] events = clip.events;
        if (events == null || events.Length == 0)
            return false;

        for (int i = 0; i < events.Length; i++)
        {
            if (string.Equals(events[i].functionName, eventName, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static void LogWarning(UnityEngine.Object context, ref int warnings, string message)
    {
        warnings++;
        Debug.LogWarning($"[TowerAuthoringValidator] {message}", context);
    }

    private static void LogError(UnityEngine.Object context, ref int errors, string message)
    {
        errors++;
        Debug.LogError($"[TowerAuthoringValidator] {message}", context);
    }
}
