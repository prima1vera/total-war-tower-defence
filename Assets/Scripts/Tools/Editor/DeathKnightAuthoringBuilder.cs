using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class DeathKnightAuthoringBuilder
{
    private const string MenuBuild = "TWTD/Validation/Build Death Knight Assets";
    private const string MenuRebuild = "TWTD/Validation/Rebuild Death Knight Assets (Force)";

    private const string SpriteRoot = "Assets/Sprites/DK";
    private const string AnimRoot = "Assets/Animations/Units/DeathKnight";

    private const string RunDownSheet = SpriteRoot + "/dk_walk_down.png";
    private const string RunLeftSheet = SpriteRoot + "/dk_walk_left.png";
    private const string RunRightSheet = SpriteRoot + "/dk_walk_right.png";
    private const string RunUpSheet = SpriteRoot + "/dk_walk_up.png";
    private const string AttackDownSheet = SpriteRoot + "/dk_attack_1_down.png";
    private const string AttackRightSheet = SpriteRoot + "/dk_attack_1_right.png";
    private const string AttackUpSheet = SpriteRoot + "/dk_attack_1_up.png";
    private const string Die1Sheet = SpriteRoot + "/dk_dead_1.png";
    private const string Die2Sheet = SpriteRoot + "/dk_dead_2.png";
    private const string Die3Sheet = SpriteRoot + "/dk_dead_3.png";
    private const string Die4Sheet = SpriteRoot + "/dk_dead_4.png";

    private const string RunDownClipPath = AnimRoot + "/Run_Down_DeathKnight.anim";
    private const string RunLeftClipPath = AnimRoot + "/Run_Left_DeathKnight.anim";
    private const string RunRightClipPath = AnimRoot + "/Run_Right_DeathKnight.anim";
    private const string RunUpClipPath = AnimRoot + "/Run_Up_DeathKnight.anim";
    private const string AttackDownClipPath = AnimRoot + "/Attack_Down_DeathKnight.anim";
    private const string AttackRightClipPath = AnimRoot + "/Attack_Right_DeathKnight.anim";
    private const string AttackUpClipPath = AnimRoot + "/Attack_Up_DeathKnight.anim";
    private const string Die1ClipPath = AnimRoot + "/Die_DeathKnight_1.anim";
    private const string Die2ClipPath = AnimRoot + "/Die_DeathKnight_2.anim";
    private const string Die3ClipPath = AnimRoot + "/Die_DeathKnight_3.anim";
    private const string Die4ClipPath = AnimRoot + "/Die_DeathKnight_4.anim";
    private const string ControllerPath = AnimRoot + "/DeathKnightAnimationController.controller";

    private const string UnitPrefabPath = "Assets/Prefabs/Enemies/Unit.prefab";
    private const string DeathKnightPrefabPath = "Assets/Prefabs/Enemies/DeathKnight.prefab";

    private const string UnitPoolPrefabPath = "Assets/Prefabs/RuntimePool/EnemyPool/EnemyPoolUnit.prefab";
    private const string DeathKnightPoolPrefabPath = "Assets/Prefabs/RuntimePool/EnemyPool/EnemyPoolDeathKnight.prefab";

    private const string UnitSpawnerPrefabPath = "Assets/Prefabs/Enemies/Spawners/EnemySpawnerUnit.prefab";
    private const string DeathKnightSpawnerPrefabPath = "Assets/Prefabs/Enemies/Spawners/EnemySpawnerDeathKnight.prefab";

    private const int FrameWidth = 96;
    private const int FrameHeight = 96;
    private const float RunFps = 12f;
    private const float AttackFps = 10f;
    private const float DeathFps = 12f;

    [MenuItem(MenuBuild)]
    private static void BuildMenu()
    {
        BuildAll(false);
    }

    [MenuItem(MenuRebuild)]
    private static void RebuildMenu()
    {
        BuildAll(true);
    }

    // Batchmode entrypoint: Unity.exe -batchmode -projectPath <path> -executeMethod DeathKnightAuthoringBuilder.BuildForBatchmode -quit
    public static void BuildForBatchmode()
    {
        BuildAll(false);
    }

    private static void BuildAll(bool forceRebuild)
    {
        EnsureDirectory(AnimRoot);

        Sprite[] runDownSprites = ImportSliceAndCollectSprites(RunDownSheet, forceRebuild);
        Sprite[] runLeftSprites = ImportSliceAndCollectSprites(RunLeftSheet, forceRebuild);
        Sprite[] runRightSprites = ImportSliceAndCollectSprites(RunRightSheet, forceRebuild);
        Sprite[] runUpSprites = ImportSliceAndCollectSprites(RunUpSheet, forceRebuild);
        Sprite[] attackDownSprites = ImportSliceAndCollectSprites(AttackDownSheet, forceRebuild);
        Sprite[] attackRightSprites = ImportSliceAndCollectSprites(AttackRightSheet, forceRebuild);
        Sprite[] attackUpSprites = ImportSliceAndCollectSprites(AttackUpSheet, forceRebuild);
        Sprite[] die1Sprites = ImportSliceAndCollectSprites(Die1Sheet, forceRebuild);
        Sprite[] die2Sprites = ImportSliceAndCollectSprites(Die2Sheet, forceRebuild);
        Sprite[] die3Sprites = ImportSliceAndCollectSprites(Die3Sheet, forceRebuild);
        Sprite[] die4Sprites = ImportSliceAndCollectSprites(Die4Sheet, forceRebuild);

        ValidateSpriteSet(runDownSprites, RunDownSheet);
        ValidateSpriteSet(runLeftSprites, RunLeftSheet);
        ValidateSpriteSet(runRightSprites, RunRightSheet);
        ValidateSpriteSet(runUpSprites, RunUpSheet);
        ValidateSpriteSet(attackDownSprites, AttackDownSheet);
        ValidateSpriteSet(attackRightSprites, AttackRightSheet);
        ValidateSpriteSet(attackUpSprites, AttackUpSheet);
        ValidateSpriteSet(die1Sprites, Die1Sheet);
        ValidateSpriteSet(die2Sprites, Die2Sheet);
        ValidateSpriteSet(die3Sprites, Die3Sheet);
        ValidateSpriteSet(die4Sprites, Die4Sheet);

        AnimationClip runDownClip = CreateOrUpdateClip(RunDownClipPath, runDownSprites, RunFps, true);
        AnimationClip runLeftClip = CreateOrUpdateClip(RunLeftClipPath, runLeftSprites, RunFps, true);
        AnimationClip runRightClip = CreateOrUpdateClip(RunRightClipPath, runRightSprites, RunFps, true);
        AnimationClip runUpClip = CreateOrUpdateClip(RunUpClipPath, runUpSprites, RunFps, true);
        AnimationClip attackDownClip = CreateOrUpdateClip(AttackDownClipPath, attackDownSprites, AttackFps, true);
        AnimationClip attackRightClip = CreateOrUpdateClip(AttackRightClipPath, attackRightSprites, AttackFps, true);
        AnimationClip attackUpClip = CreateOrUpdateClip(AttackUpClipPath, attackUpSprites, AttackFps, true);
        AnimationClip die1Clip = CreateOrUpdateClip(Die1ClipPath, die1Sprites, DeathFps, false);
        AnimationClip die2Clip = CreateOrUpdateClip(Die2ClipPath, die2Sprites, DeathFps, false);
        AnimationClip die3Clip = CreateOrUpdateClip(Die3ClipPath, die3Sprites, DeathFps, false);
        AnimationClip die4Clip = CreateOrUpdateClip(Die4ClipPath, die4Sprites, DeathFps, false);

        AnimatorController controller = CreateController(
            runDownClip,
            runLeftClip,
            runRightClip,
            runUpClip,
            attackDownClip,
            attackRightClip,
            attackUpClip,
            die1Clip,
            die2Clip,
            die3Clip,
            die4Clip);

        GameObject deathKnightPrefab = CreateOrUpdateDeathKnightPrefab(controller, runDownSprites);
        EnemyPool deathKnightPool = CreateOrUpdateDeathKnightPool(deathKnightPrefab);
        CreateOrUpdateDeathKnightSpawner(deathKnightPrefab, deathKnightPool);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("[DeathKnightAuthoringBuilder] Death Knight assets are ready: prefab, pool, spawner, clips and animator controller.");
    }

    private static Sprite[] ImportSliceAndCollectSprites(string texturePath, bool forceRebuild)
    {
        if (!File.Exists(texturePath))
        {
            Debug.LogError($"[DeathKnightAuthoringBuilder] Missing texture: {texturePath}");
            return new Sprite[0];
        }

        TextureImporter importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
        if (importer == null)
        {
            Debug.LogError($"[DeathKnightAuthoringBuilder] TextureImporter missing for: {texturePath}");
            return new Sprite[0];
        }

        Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        if (texture == null)
        {
            Debug.LogError($"[DeathKnightAuthoringBuilder] Texture not loadable at: {texturePath}");
            return new Sprite[0];
        }

        int columns = Mathf.Max(1, texture.width / FrameWidth);
        int rows = Mathf.Max(1, texture.height / FrameHeight);

        bool changed = false;

        if (importer.textureType != TextureImporterType.Sprite)
        {
            importer.textureType = TextureImporterType.Sprite;
            changed = true;
        }

        if (importer.spriteImportMode != SpriteImportMode.Multiple)
        {
            importer.spriteImportMode = SpriteImportMode.Multiple;
            changed = true;
        }

        if (Mathf.Abs(importer.spritePixelsPerUnit - 100f) > 0.01f)
        {
            importer.spritePixelsPerUnit = 100f;
            changed = true;
        }

        if (importer.filterMode != FilterMode.Point)
        {
            importer.filterMode = FilterMode.Point;
            changed = true;
        }

        if (importer.textureCompression != TextureImporterCompression.Uncompressed)
        {
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            changed = true;
        }

        SpriteMetaData[] desiredSheet = BuildSheetMeta(texturePath, columns, rows);
        if (forceRebuild || NeedReplaceSheet(importer.spritesheet, desiredSheet))
        {
            importer.spritesheet = desiredSheet;
            changed = true;
        }

        if (changed)
            importer.SaveAndReimport();

        return LoadSortedSprites(texturePath);
    }

    private static SpriteMetaData[] BuildSheetMeta(string texturePath, int columns, int rows)
    {
        List<SpriteMetaData> meta = new List<SpriteMetaData>(columns * rows);
        string baseName = Path.GetFileNameWithoutExtension(texturePath);
        int frameIndex = 0;

        for (int row = rows - 1; row >= 0; row--)
        {
            for (int col = 0; col < columns; col++)
            {
                SpriteMetaData frame = new SpriteMetaData
                {
                    name = $"{baseName}_{frameIndex:D2}",
                    alignment = (int)SpriteAlignment.Center,
                    pivot = new Vector2(0.5f, 0.5f),
                    rect = new Rect(col * FrameWidth, row * FrameHeight, FrameWidth, FrameHeight)
                };

                meta.Add(frame);
                frameIndex++;
            }
        }

        return meta.ToArray();
    }

    private static bool NeedReplaceSheet(SpriteMetaData[] current, SpriteMetaData[] desired)
    {
        if (current == null || current.Length != desired.Length)
            return true;

        for (int i = 0; i < desired.Length; i++)
        {
            if (current[i].name != desired[i].name)
                return true;

            if (current[i].rect != desired[i].rect)
                return true;
        }

        return false;
    }

    private static void ValidateSpriteSet(Sprite[] sprites, string sourcePath)
    {
        if (sprites == null || sprites.Length == 0)
            throw new System.InvalidOperationException($"[DeathKnightAuthoringBuilder] No sliced sprites found for '{sourcePath}'.");
    }

    private static Sprite[] LoadSortedSprites(string texturePath)
    {
        Object[] loaded = AssetDatabase.LoadAllAssetsAtPath(texturePath);
        if (loaded == null || loaded.Length == 0)
            return new Sprite[0];

        List<Sprite> sprites = new List<Sprite>(loaded.Length);
        for (int i = 0; i < loaded.Length; i++)
        {
            if (loaded[i] is Sprite sprite)
                sprites.Add(sprite);
        }

        sprites.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        return sprites.ToArray();
    }

    private static AnimationClip CreateOrUpdateClip(string clipPath, Sprite[] sprites, float frameRate, bool loop)
    {
        AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
        if (clip == null)
        {
            clip = new AnimationClip { name = Path.GetFileNameWithoutExtension(clipPath) };
            AssetDatabase.CreateAsset(clip, clipPath);
        }

        ApplySpritesToClip(clip, sprites, frameRate, loop);
        EditorUtility.SetDirty(clip);
        return clip;
    }

    private static void ApplySpritesToClip(AnimationClip clip, Sprite[] sprites, float frameRate, bool loop)
    {
        float fps = Mathf.Max(1f, frameRate);
        float frameTime = 1f / fps;

        EditorCurveBinding binding = new EditorCurveBinding
        {
            path = string.Empty,
            type = typeof(SpriteRenderer),
            propertyName = "m_Sprite"
        };

        int keyCount = loop ? sprites.Length + 1 : sprites.Length + 1;
        ObjectReferenceKeyframe[] keys = new ObjectReferenceKeyframe[keyCount];

        for (int i = 0; i < sprites.Length; i++)
        {
            keys[i] = new ObjectReferenceKeyframe
            {
                time = i * frameTime,
                value = sprites[i]
            };
        }

        keys[keyCount - 1] = new ObjectReferenceKeyframe
        {
            time = sprites.Length * frameTime,
            value = loop ? sprites[0] : sprites[sprites.Length - 1]
        };

        clip.frameRate = fps;
        AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);
        SetLoopTime(clip, loop);
    }

    private static void SetLoopTime(AnimationClip clip, bool enabled)
    {
        SerializedObject serializedClip = new SerializedObject(clip);
        SerializedProperty settings = serializedClip.FindProperty("m_AnimationClipSettings");
        if (settings != null)
        {
            SerializedProperty loopTime = settings.FindPropertyRelative("m_LoopTime");
            if (loopTime != null)
                loopTime.boolValue = enabled;
        }

        serializedClip.ApplyModifiedPropertiesWithoutUndo();
    }

    private static AnimatorController CreateController(
        AnimationClip runDownClip,
        AnimationClip runLeftClip,
        AnimationClip runRightClip,
        AnimationClip runUpClip,
        AnimationClip attackDownClip,
        AnimationClip attackRightClip,
        AnimationClip attackUpClip,
        AnimationClip die1Clip,
        AnimationClip die2Clip,
        AnimationClip die3Clip,
        AnimationClip die4Clip)
    {
        if (AssetDatabase.LoadAssetAtPath<Object>(ControllerPath) != null)
            AssetDatabase.DeleteAsset(ControllerPath);

        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        controller.name = "DeathKnightAnimationController";

        controller.AddParameter("moveX", AnimatorControllerParameterType.Float);
        controller.AddParameter("moveY", AnimatorControllerParameterType.Float);
        controller.AddParameter("isMoving", AnimatorControllerParameterType.Bool);
        controller.AddParameter("attack", AnimatorControllerParameterType.Bool);
        controller.AddParameter("isDead", AnimatorControllerParameterType.Bool);
        controller.AddParameter("deathIndex", AnimatorControllerParameterType.Int);

        AnimatorStateMachine sm = controller.layers[0].stateMachine;
        sm.anyStatePosition = new Vector3(340f, -60f, 0f);
        sm.entryPosition = new Vector3(50f, 120f, 0f);
        sm.exitPosition = new Vector3(800f, 120f, 0f);

        BlendTree runBlendTree = new BlendTree
        {
            name = "DeathKnightRunBlendTree",
            blendType = BlendTreeType.SimpleDirectional2D,
            blendParameter = "moveX",
            blendParameterY = "moveY",
            useAutomaticThresholds = false
        };

        AssetDatabase.AddObjectToAsset(runBlendTree, controller);
        runBlendTree.AddChild(runRightClip, new Vector2(1f, 0f));
        runBlendTree.AddChild(runLeftClip, new Vector2(-1f, 0f));
        runBlendTree.AddChild(runUpClip, new Vector2(0f, 1f));
        runBlendTree.AddChild(runDownClip, new Vector2(0f, -1f));

        BlendTree attackBlendTree = new BlendTree
        {
            name = "DeathKnightAttackBlendTree",
            blendType = BlendTreeType.SimpleDirectional2D,
            blendParameter = "moveX",
            blendParameterY = "moveY",
            useAutomaticThresholds = false
        };

        AssetDatabase.AddObjectToAsset(attackBlendTree, controller);
        attackBlendTree.AddChild(attackRightClip, new Vector2(1f, 0f));
        attackBlendTree.AddChild(attackRightClip, new Vector2(-1f, 0f));
        attackBlendTree.AddChild(attackUpClip, new Vector2(0f, 1f));
        attackBlendTree.AddChild(attackDownClip, new Vector2(0f, -1f));

        AnimatorState runState = sm.AddState("Run", new Vector3(220f, 60f, 0f));
        runState.motion = runBlendTree;

        AnimatorState attackState = sm.AddState("Attack", new Vector3(220f, 165f, 0f));
        attackState.motion = attackBlendTree;
        sm.defaultState = runState;

        AnimatorStateTransition runToAttack = runState.AddTransition(attackState);
        runToAttack.hasExitTime = false;
        runToAttack.hasFixedDuration = true;
        runToAttack.duration = 0.05f;
        runToAttack.AddCondition(AnimatorConditionMode.If, 0f, "attack");

        AnimatorStateTransition attackToRun = attackState.AddTransition(runState);
        attackToRun.hasExitTime = false;
        attackToRun.hasFixedDuration = true;
        attackToRun.duration = 0.05f;
        attackToRun.AddCondition(AnimatorConditionMode.IfNot, 0f, "attack");

        AnimatorState dieState1 = sm.AddState("Die_1", new Vector3(430f, 0f, 0f));
        AnimatorState dieState2 = sm.AddState("Die_2", new Vector3(430f, 70f, 0f));
        AnimatorState dieState3 = sm.AddState("Die_3", new Vector3(430f, 140f, 0f));
        AnimatorState dieState4 = sm.AddState("Die_4", new Vector3(430f, 210f, 0f));

        dieState1.motion = die1Clip;
        dieState2.motion = die2Clip;
        dieState3.motion = die3Clip;
        dieState4.motion = die4Clip;

        AddDeathAnyStateTransition(sm, dieState1, 0);
        AddDeathAnyStateTransition(sm, dieState2, 1);
        AddDeathAnyStateTransition(sm, dieState3, 2);
        AddDeathAnyStateTransition(sm, dieState4, 3);

        EditorUtility.SetDirty(controller);
        return controller;
    }

    private static void AddDeathAnyStateTransition(AnimatorStateMachine sm, AnimatorState targetState, int deathIndex)
    {
        AnimatorStateTransition transition = sm.AddAnyStateTransition(targetState);
        transition.hasExitTime = false;
        transition.hasFixedDuration = true;
        transition.duration = 0f;
        transition.exitTime = 0f;
        transition.interruptionSource = TransitionInterruptionSource.None;
        transition.orderedInterruption = true;
        transition.canTransitionToSelf = false;
        transition.AddCondition(AnimatorConditionMode.If, 0f, "isDead");
        transition.AddCondition(AnimatorConditionMode.Equals, deathIndex, "deathIndex");
    }

    private static GameObject CreateOrUpdateDeathKnightPrefab(AnimatorController controller, Sprite[] runDownSprites)
    {
        GameObject unitPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(UnitPrefabPath);
        if (unitPrefab == null)
            throw new System.InvalidOperationException($"[DeathKnightAuthoringBuilder] Missing source prefab: {UnitPrefabPath}");

        GameObject root = PrefabUtility.LoadPrefabContents(UnitPrefabPath);
        try
        {
            root.name = "DeathKnight";

            SpriteRenderer spriteRenderer = root.GetComponent<SpriteRenderer>();
            if (spriteRenderer != null && runDownSprites.Length > 0)
                spriteRenderer.sprite = runDownSprites[0];

            Animator animator = root.GetComponent<Animator>();
            if (animator != null)
                animator.runtimeAnimatorController = controller;

            UnitHealth health = root.GetComponent<UnitHealth>();
            if (health != null)
                health.maxHealth = 180;

            UnitMovement movement = root.GetComponent<UnitMovement>();
            if (movement != null)
                movement.speed = 0.68f;

            GameObject prefabAsset = PrefabUtility.SaveAsPrefabAsset(root, DeathKnightPrefabPath);
            if (prefabAsset == null)
                throw new System.InvalidOperationException("[DeathKnightAuthoringBuilder] Failed to save DeathKnight prefab.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        return AssetDatabase.LoadAssetAtPath<GameObject>(DeathKnightPrefabPath);
    }

    private static EnemyPool CreateOrUpdateDeathKnightPool(GameObject deathKnightPrefab)
    {
        if (deathKnightPrefab == null)
            throw new System.InvalidOperationException("[DeathKnightAuthoringBuilder] DeathKnight prefab is null; cannot build pool.");

        GameObject root = PrefabUtility.LoadPrefabContents(UnitPoolPrefabPath);
        try
        {
            root.name = "EnemyPoolDeathKnight";
            EnemyPool pool = root.GetComponent<EnemyPool>();
            if (pool == null)
                throw new System.InvalidOperationException("[DeathKnightAuthoringBuilder] EnemyPool component missing on source pool prefab.");

            SerializedObject so = new SerializedObject(pool);
            so.FindProperty("enemyPrefab").objectReferenceValue = deathKnightPrefab;
            so.FindProperty("prewarmCount").intValue = 10;
            so.FindProperty("maxPoolSize").intValue = 96;
            so.ApplyModifiedPropertiesWithoutUndo();

            GameObject poolAsset = PrefabUtility.SaveAsPrefabAsset(root, DeathKnightPoolPrefabPath);
            if (poolAsset == null)
                throw new System.InvalidOperationException("[DeathKnightAuthoringBuilder] Failed to save EnemyPoolDeathKnight prefab.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        return AssetDatabase.LoadAssetAtPath<EnemyPool>(DeathKnightPoolPrefabPath);
    }

    private static void CreateOrUpdateDeathKnightSpawner(GameObject deathKnightPrefab, EnemyPool deathKnightPool)
    {
        if (deathKnightPrefab == null)
            throw new System.InvalidOperationException("[DeathKnightAuthoringBuilder] DeathKnight prefab is null; cannot build spawner.");

        GameObject root = PrefabUtility.LoadPrefabContents(UnitSpawnerPrefabPath);
        try
        {
            root.name = "EnemySpawnerDeathKnight";

            EnemySpawner spawner = root.GetComponent<EnemySpawner>();
            if (spawner == null)
                throw new System.InvalidOperationException("[DeathKnightAuthoringBuilder] EnemySpawner component missing on source spawner prefab.");

            SerializedObject so = new SerializedObject(spawner);
            so.FindProperty("enemyPool").objectReferenceValue = deathKnightPool;
            so.FindProperty("enemyPrefab").objectReferenceValue = deathKnightPrefab;
            so.FindProperty("enemyFamily").enumValueIndex = (int)EnemySpawner.EnemyFamily.DeathKnight;
            so.FindProperty("spawnInterval").floatValue = 1.1f;
            so.FindProperty("waveSpawnWeight").floatValue = 0.45f;
            so.FindProperty("killRewardOverride").intValue = -1;
            so.ApplyModifiedPropertiesWithoutUndo();

            if (PrefabUtility.SaveAsPrefabAsset(root, DeathKnightSpawnerPrefabPath) == null)
                throw new System.InvalidOperationException("[DeathKnightAuthoringBuilder] Failed to save EnemySpawnerDeathKnight prefab.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void EnsureDirectory(string directoryPath)
    {
        if (AssetDatabase.IsValidFolder(directoryPath))
            return;

        string[] parts = directoryPath.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = $"{current}/{parts[i]}";
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);

            current = next;
        }
    }
}
