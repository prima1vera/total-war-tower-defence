using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class TowerAuthoringValidator
{
    private const string ValidationMenuPath = "TWTD/Validation/Validate Tower Authoring";
    private const string ShootArrowEventName = "ShootArrow";

    [MenuItem(ValidationMenuPath)]
    private static void ValidateAll()
    {
        int errors = 0;
        int warnings = 0;

        int profileCount = ValidateAllEvolutionProfiles(ref errors, ref warnings);
        int treeCount = ValidateAllUpgradeTrees(ref errors, ref warnings);

        Debug.Log($"[TowerAuthoringValidator] Validation complete. Trees: {treeCount}, Profiles: {profileCount}, Errors: {errors}, Warnings: {warnings}.");
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
        if (profile.ArrowPrefab == null)
            LogWarning(profile, ref warnings, "Arrow prefab is not assigned (fallback instantiate will fail if pool is missing).");

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
