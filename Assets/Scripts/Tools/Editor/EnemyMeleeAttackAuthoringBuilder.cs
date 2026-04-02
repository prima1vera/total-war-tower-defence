using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class EnemyMeleeAttackAuthoringBuilder
{
    private const string MenuBuild = "TWTD/Validation/Build Enemy Melee Attack Assets";
    private const string MenuRebuild = "TWTD/Validation/Rebuild Enemy Melee Attack Assets (Force Slice)";

    private const int FrameWidth = 96;
    private const int FrameHeight = 96;
    private const float AttackFps = 11f;

    private const string OgreSpriteRoot = "Assets/Sprites/Ogre";
    private const string OgreAnimRoot = "Assets/Animations/Units/Ogre";
    private const string OgreAttackDownSheet = OgreSpriteRoot + "/ogre_attack_1_down.png";
    private const string OgreAttackRightSheet = OgreSpriteRoot + "/ogre_attack_1_right.png";
    private const string OgreAttackUpSheet = OgreSpriteRoot + "/ogre_attack_1_up.png";
    private const string OgreAttackDownClipPath = OgreAnimRoot + "/Attack_Down_Ogre.anim";
    private const string OgreAttackRightClipPath = OgreAnimRoot + "/Attack_Right_Ogre.anim";
    private const string OgreAttackUpClipPath = OgreAnimRoot + "/Attack_Up_Ogre.anim";
    private const string OgreRunDownClipPath = OgreAnimRoot + "/Run_Down_Ogre.anim";
    private const string OgreRunLeftClipPath = OgreAnimRoot + "/Run_Left_Ogre.anim";
    private const string OgreRunRightClipPath = OgreAnimRoot + "/Run_Right_Ogre.anim";
    private const string OgreRunUpClipPath = OgreAnimRoot + "/Run_Up_Ogre.anim";
    private const string OgreControllerPath = OgreAnimRoot + "/OgreAnimationController.controller";
    private const string OgrePrefabPath = "Assets/Prefabs/Enemies/Ogre.prefab";

    private const string UnitSpriteRoot = "Assets/Sprites/Unit";
    private const string UnitAnimRoot = "Assets/Animations/Units/Unit";
    private const string UnitAttackDownSheet = UnitSpriteRoot + "/unit_attack_1_down.png";
    private const string UnitAttackRightSheet = UnitSpriteRoot + "/unit_attack_1_right.png";
    private const string UnitAttackUpSheet = UnitSpriteRoot + "/unit_attack_1_up.png";
    private const string UnitAttackDownClipPath = UnitAnimRoot + "/Attack_Down_Unit.anim";
    private const string UnitAttackRightClipPath = UnitAnimRoot + "/Attack_Right_Unit.anim";
    private const string UnitAttackUpClipPath = UnitAnimRoot + "/Attack_Up_Unit.anim";
    private const string UnitRunDownClipPath = UnitAnimRoot + "/Run_Down.anim";
    private const string UnitRunLeftClipPath = UnitAnimRoot + "/Run_Left.anim";
    private const string UnitRunRightClipPath = UnitAnimRoot + "/Run_Right.anim";
    private const string UnitRunUpClipPath = UnitAnimRoot + "/Run_Up.anim";
    private const string UnitControllerPath = UnitAnimRoot + "/UnitAnimationController.controller";
    private const string UnitPrefabPath = "Assets/Prefabs/Enemies/Unit.prefab";

    private static readonly string[] OgreDeathClipPaths =
    {
        OgreAnimRoot + "/Die_Ogre_1.anim",
        OgreAnimRoot + "/Die_Ogre_2.anim",
        OgreAnimRoot + "/Die_Ogre_3.anim",
        OgreAnimRoot + "/Die_Ogre_4.anim"
    };

    private static readonly string[] UnitDeathClipPaths =
    {
        UnitAnimRoot + "/Die_Unit_1.anim",
        UnitAnimRoot + "/Die_Unit_2.anim",
        UnitAnimRoot + "/Die_Unit_3.anim",
        UnitAnimRoot + "/Die_Unit_4.anim",
        UnitAnimRoot + "/Die_Unit_5.anim"
    };

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

    // Batchmode entrypoint:
    // Unity.exe -batchmode -projectPath <path> -executeMethod EnemyMeleeAttackAuthoringBuilder.BuildForBatchmode -quit
    public static void BuildForBatchmode()
    {
        BuildAll(false);
    }

    private static void BuildAll(bool forceSlice)
    {
        EnsureDirectory(OgreAnimRoot);
        EnsureDirectory(UnitAnimRoot);

        BuildOgre(forceSlice);
        BuildUnit(forceSlice);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[EnemyMeleeAttackAuthoringBuilder] Ogre/Unit attack animation assets are ready.");
    }

    private static void BuildOgre(bool forceSlice)
    {
        Sprite[] attackDownSprites = ImportSliceAndCollectSprites(OgreAttackDownSheet, forceSlice);
        Sprite[] attackRightSprites = ImportSliceAndCollectSprites(OgreAttackRightSheet, forceSlice);
        Sprite[] attackUpSprites = ImportSliceAndCollectSprites(OgreAttackUpSheet, forceSlice);

        ValidateSpriteSet(attackDownSprites, OgreAttackDownSheet);
        ValidateSpriteSet(attackRightSprites, OgreAttackRightSheet);
        ValidateSpriteSet(attackUpSprites, OgreAttackUpSheet);

        AnimationClip runDownClip = RequireClip(OgreRunDownClipPath);
        AnimationClip runLeftClip = RequireClip(OgreRunLeftClipPath);
        AnimationClip runRightClip = RequireClip(OgreRunRightClipPath);
        AnimationClip runUpClip = RequireClip(OgreRunUpClipPath);
        AnimationClip[] deathClips = RequireClips(OgreDeathClipPaths);

        AnimationClip attackDownClip = CreateOrUpdateClip(OgreAttackDownClipPath, attackDownSprites, AttackFps, true);
        AnimationClip attackRightClip = CreateOrUpdateClip(OgreAttackRightClipPath, attackRightSprites, AttackFps, true);
        AnimationClip attackUpClip = CreateOrUpdateClip(OgreAttackUpClipPath, attackUpSprites, AttackFps, true);

        AnimatorController controller = CreateOrReplaceController(
            OgreControllerPath,
            "OgreAnimationController",
            runDownClip,
            runLeftClip,
            runRightClip,
            runUpClip,
            attackDownClip,
            attackRightClip,
            attackUpClip,
            deathClips);

        PatchEnemyPrefabAnimator(OgrePrefabPath, controller);
    }

    private static void BuildUnit(bool forceSlice)
    {
        Sprite[] attackDownSprites = ImportSliceAndCollectSprites(UnitAttackDownSheet, forceSlice);
        Sprite[] attackRightSprites = ImportSliceAndCollectSprites(UnitAttackRightSheet, forceSlice);
        Sprite[] attackUpSprites = ImportSliceAndCollectSprites(UnitAttackUpSheet, forceSlice);

        ValidateSpriteSet(attackDownSprites, UnitAttackDownSheet);
        ValidateSpriteSet(attackRightSprites, UnitAttackRightSheet);
        ValidateSpriteSet(attackUpSprites, UnitAttackUpSheet);

        AnimationClip runDownClip = RequireClip(UnitRunDownClipPath);
        AnimationClip runLeftClip = RequireClip(UnitRunLeftClipPath);
        AnimationClip runRightClip = RequireClip(UnitRunRightClipPath);
        AnimationClip runUpClip = RequireClip(UnitRunUpClipPath);
        AnimationClip[] deathClips = RequireClips(UnitDeathClipPaths);

        AnimationClip attackDownClip = CreateOrUpdateClip(UnitAttackDownClipPath, attackDownSprites, AttackFps, true);
        AnimationClip attackRightClip = CreateOrUpdateClip(UnitAttackRightClipPath, attackRightSprites, AttackFps, true);
        AnimationClip attackUpClip = CreateOrUpdateClip(UnitAttackUpClipPath, attackUpSprites, AttackFps, true);

        AnimatorController controller = CreateOrReplaceController(
            UnitControllerPath,
            "UnitAnimationController",
            runDownClip,
            runLeftClip,
            runRightClip,
            runUpClip,
            attackDownClip,
            attackRightClip,
            attackUpClip,
            deathClips);

        PatchEnemyPrefabAnimator(UnitPrefabPath, controller);
    }

    private static AnimatorController CreateOrReplaceController(
        string controllerPath,
        string controllerName,
        AnimationClip runDownClip,
        AnimationClip runLeftClip,
        AnimationClip runRightClip,
        AnimationClip runUpClip,
        AnimationClip attackDownClip,
        AnimationClip attackRightClip,
        AnimationClip attackUpClip,
        AnimationClip[] deathClips)
    {
        if (AssetDatabase.LoadAssetAtPath<Object>(controllerPath) != null)
            AssetDatabase.DeleteAsset(controllerPath);

        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
        controller.name = controllerName;

        controller.AddParameter("moveX", AnimatorControllerParameterType.Float);
        controller.AddParameter("moveY", AnimatorControllerParameterType.Float);
        controller.AddParameter("isMoving", AnimatorControllerParameterType.Bool);
        controller.AddParameter("attack", AnimatorControllerParameterType.Bool);
        controller.AddParameter("isDead", AnimatorControllerParameterType.Bool);
        controller.AddParameter("deathIndex", AnimatorControllerParameterType.Int);

        AnimatorStateMachine sm = controller.layers[0].stateMachine;
        sm.anyStatePosition = new Vector3(460f, -120f, 0f);
        sm.entryPosition = new Vector3(40f, 120f, 0f);

        BlendTree runBlendTree = new BlendTree
        {
            name = controllerName + "_RunBlendTree",
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
            name = controllerName + "_AttackBlendTree",
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

        AnimatorState runState = sm.AddState("Run", new Vector3(220f, 70f, 0f));
        runState.motion = runBlendTree;
        sm.defaultState = runState;

        AnimatorState attackState = sm.AddState("Attack", new Vector3(220f, 180f, 0f));
        attackState.motion = attackBlendTree;

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

        for (int i = 0; i < deathClips.Length; i++)
        {
            AnimationClip deathClip = deathClips[i];
            if (deathClip == null)
                continue;

            AnimatorState deathState = sm.AddState($"Die_{i + 1}", new Vector3(500f, i * 80f, 0f));
            deathState.motion = deathClip;
            deathState.speed = 1f;

            AnimatorStateTransition anyToDeath = sm.AddAnyStateTransition(deathState);
            anyToDeath.hasExitTime = false;
            anyToDeath.hasFixedDuration = true;
            anyToDeath.duration = 0f;
            anyToDeath.canTransitionToSelf = false;
            anyToDeath.AddCondition(AnimatorConditionMode.If, 0f, "isDead");
            anyToDeath.AddCondition(AnimatorConditionMode.Equals, i, "deathIndex");
        }

        EditorUtility.SetDirty(controller);
        return controller;
    }

    private static void PatchEnemyPrefabAnimator(string prefabPath, AnimatorController controller)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            Animator animator = root.GetComponent<Animator>();
            if (animator != null)
                animator.runtimeAnimatorController = controller;

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static AnimationClip[] RequireClips(string[] clipPaths)
    {
        AnimationClip[] clips = new AnimationClip[clipPaths.Length];
        for (int i = 0; i < clipPaths.Length; i++)
            clips[i] = RequireClip(clipPaths[i]);
        return clips;
    }

    private static AnimationClip RequireClip(string clipPath)
    {
        AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
        if (clip == null)
            throw new System.InvalidOperationException($"[EnemyMeleeAttackAuthoringBuilder] Required clip missing: {clipPath}");
        return clip;
    }

    private static Sprite[] ImportSliceAndCollectSprites(string texturePath, bool forceSlice)
    {
        if (!File.Exists(texturePath))
        {
            Debug.LogError($"[EnemyMeleeAttackAuthoringBuilder] Missing texture: {texturePath}");
            return new Sprite[0];
        }

        TextureImporter importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
        if (importer == null)
        {
            Debug.LogError($"[EnemyMeleeAttackAuthoringBuilder] TextureImporter missing for: {texturePath}");
            return new Sprite[0];
        }

        Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        if (texture == null)
        {
            Debug.LogError($"[EnemyMeleeAttackAuthoringBuilder] Texture not loadable at: {texturePath}");
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
        if (forceSlice || NeedReplaceSheet(importer.spritesheet, desiredSheet))
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

    private static void ValidateSpriteSet(Sprite[] sprites, string sourcePath)
    {
        if (sprites == null || sprites.Length == 0)
            throw new System.InvalidOperationException($"[EnemyMeleeAttackAuthoringBuilder] No sliced sprites found for '{sourcePath}'.");
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

    private static void EnsureDirectory(string assetPath)
    {
        string[] parts = assetPath.Split('/');
        if (parts.Length < 2)
            return;

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
