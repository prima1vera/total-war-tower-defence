using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class DefenderKnightAuthoringBuilder
{
    private const string MenuBuild = "TWTD/Validation/Build Defender Spartan Assets";
    private const string MenuRebuild = "TWTD/Validation/Rebuild Defender Spartan Assets (Force Slice)";

    private const string SpriteRoot = "Assets/Sprites/Defenders/Spartan";
    private const string AnimRoot = "Assets/Animations/Units/DefenderSpartan";

    private const string IdleSheet = SpriteRoot + "/DefenderSpartanSpritesheetIdle.png";
    private const string DieSheet = SpriteRoot + "/DefenderSpartanSpritesheetDie.png";

    private const string WalkDownSheet = SpriteRoot + "/DefenderSpartanSpritesheetWalkDown.png";
    private const string WalkLeftSheet = SpriteRoot + "/DefenderSpartanSpritesheetWalkLeft.png";
    private const string WalkRightSheet = SpriteRoot + "/DefenderSpartanSpritesheetWalkRight.png";
    private const string WalkUpSheet = SpriteRoot + "/DefenderSpartanSpritesheetWalkUp.png";

    private const string AttackDownSheet = SpriteRoot + "/DefenderSpartanSpritesheetAttackDown.png";
    private const string AttackLeftSheet = SpriteRoot + "/DefenderSpartanSpritesheetAttackLeft.png";
    private const string AttackRightSheet = SpriteRoot + "/DefenderSpartanSpritesheetAttackRight.png";
    private const string AttackUpSheet = SpriteRoot + "/DefenderSpartanSpritesheetAttackUp.png";

    private const int IdleFrames = 7;
    private const int DieFrames = 6;
    private const int WalkFrames = 9;
    private const int AttackFrames = 8;

    private const string IdleClipPath = AnimRoot + "/Idle_DefenderSpartan.anim";
    private const string DieClipPath = AnimRoot + "/Die_DefenderSpartan.anim";

    private const string WalkDownClipPath = AnimRoot + "/Walk_Down_DefenderSpartan.anim";
    private const string WalkLeftClipPath = AnimRoot + "/Walk_Left_DefenderSpartan.anim";
    private const string WalkRightClipPath = AnimRoot + "/Walk_Right_DefenderSpartan.anim";
    private const string WalkUpClipPath = AnimRoot + "/Walk_Up_DefenderSpartan.anim";

    private const string AttackDownClipPath = AnimRoot + "/Attack_Down_DefenderSpartan.anim";
    private const string AttackLeftClipPath = AnimRoot + "/Attack_Left_DefenderSpartan.anim";
    private const string AttackRightClipPath = AnimRoot + "/Attack_Right_DefenderSpartan.anim";
    private const string AttackUpClipPath = AnimRoot + "/Attack_Up_DefenderSpartan.anim";

    private const string ControllerPath = AnimRoot + "/DefenderSpartanAnimationController.controller";
    private const string PrefabPath = "Assets/Prefabs/Defenders/DefenderKnight.prefab";

    private const float IdleFps = 8f;
    private const float WalkFps = 12f;
    private const float AttackFps = 12f;
    private const float DieFps = 10f;

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
    // Unity.exe -batchmode -projectPath <path> -executeMethod DefenderKnightAuthoringBuilder.BuildForBatchmode -quit
    public static void BuildForBatchmode()
    {
        BuildAll(false);
    }

    private static void BuildAll(bool forceSlice)
    {
        EnsureDirectory(AnimRoot);

        Sprite[] idleSprites = ImportSliceAndCollectSprites(IdleSheet, IdleFrames, forceSlice);
        Sprite[] dieSprites = ImportSliceAndCollectSprites(DieSheet, DieFrames, forceSlice);

        Sprite[] walkDownSprites = ImportSliceAndCollectSprites(WalkDownSheet, WalkFrames, forceSlice);
        Sprite[] walkLeftSprites = ImportSliceAndCollectSprites(WalkLeftSheet, WalkFrames, forceSlice);
        Sprite[] walkRightSprites = ImportSliceAndCollectSprites(WalkRightSheet, WalkFrames, forceSlice);
        Sprite[] walkUpSprites = ImportSliceAndCollectSprites(WalkUpSheet, WalkFrames, forceSlice);

        Sprite[] attackDownSprites = ImportSliceAndCollectSprites(AttackDownSheet, AttackFrames, forceSlice);
        Sprite[] attackLeftSprites = ImportSliceAndCollectSprites(AttackLeftSheet, AttackFrames, forceSlice);
        Sprite[] attackRightSprites = ImportSliceAndCollectSprites(AttackRightSheet, AttackFrames, forceSlice);
        Sprite[] attackUpSprites = ImportSliceAndCollectSprites(AttackUpSheet, AttackFrames, forceSlice);

        AnimationClip idleClip = CreateOrUpdateClip(IdleClipPath, idleSprites, IdleFps, true);
        AnimationClip dieClip = CreateOrUpdateClip(DieClipPath, dieSprites, DieFps, false);

        AnimationClip walkDownClip = CreateOrUpdateClip(WalkDownClipPath, walkDownSprites, WalkFps, true);
        AnimationClip walkLeftClip = CreateOrUpdateClip(WalkLeftClipPath, walkLeftSprites, WalkFps, true);
        AnimationClip walkRightClip = CreateOrUpdateClip(WalkRightClipPath, walkRightSprites, WalkFps, true);
        AnimationClip walkUpClip = CreateOrUpdateClip(WalkUpClipPath, walkUpSprites, WalkFps, true);

        AnimationClip attackDownClip = CreateOrUpdateClip(AttackDownClipPath, attackDownSprites, AttackFps, false);
        AnimationClip attackLeftClip = CreateOrUpdateClip(AttackLeftClipPath, attackLeftSprites, AttackFps, false);
        AnimationClip attackRightClip = CreateOrUpdateClip(AttackRightClipPath, attackRightSprites, AttackFps, false);
        AnimationClip attackUpClip = CreateOrUpdateClip(AttackUpClipPath, attackUpSprites, AttackFps, false);

        AnimatorController controller = CreateOrReplaceController(
            idleClip,
            dieClip,
            walkDownClip,
            walkLeftClip,
            walkRightClip,
            walkUpClip,
            attackDownClip,
            attackLeftClip,
            attackRightClip,
            attackUpClip);

        PatchDefenderPrefab(controller, idleSprites[0]);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[DefenderKnightAuthoringBuilder] Defender prefab was upgraded to Spartan animations.");
    }

    private static Sprite[] ImportSliceAndCollectSprites(string texturePath, int frameCount, bool forceSlice)
    {
        if (!File.Exists(texturePath))
            throw new InvalidOperationException($"[DefenderKnightAuthoringBuilder] Missing texture: {texturePath}");

        TextureImporter importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
        if (importer == null)
            throw new InvalidOperationException($"[DefenderKnightAuthoringBuilder] TextureImporter missing for: {texturePath}");

        Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        if (texture == null)
            throw new InvalidOperationException($"[DefenderKnightAuthoringBuilder] Texture not loadable at: {texturePath}");

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

        SpriteMetaData[] desiredSheet = BuildStripMeta(texturePath, texture.width, texture.height, frameCount);
        if (forceSlice || NeedReplaceSheet(importer.spritesheet, desiredSheet))
        {
            importer.spritesheet = desiredSheet;
            changed = true;
        }

        if (changed)
            importer.SaveAndReimport();

        Sprite[] sprites = LoadSortedSprites(texturePath);
        if (sprites.Length != frameCount)
            throw new InvalidOperationException(
                $"[DefenderKnightAuthoringBuilder] '{texturePath}' expected {frameCount} frames, got {sprites.Length}.");

        return sprites;
    }

    private static SpriteMetaData[] BuildStripMeta(string texturePath, int width, int height, int frameCount)
    {
        string baseName = Path.GetFileNameWithoutExtension(texturePath);
        SpriteMetaData[] meta = new SpriteMetaData[frameCount];

        for (int i = 0; i < frameCount; i++)
        {
            int xMin = Mathf.RoundToInt((float)(i * width) / frameCount);
            int xMax = Mathf.RoundToInt((float)((i + 1) * width) / frameCount);
            int frameWidth = Mathf.Max(1, xMax - xMin);

            meta[i] = new SpriteMetaData
            {
                name = $"{baseName}_{i:D2}",
                alignment = (int)SpriteAlignment.Center,
                pivot = new Vector2(0.5f, 0.5f),
                rect = new Rect(xMin, 0, frameWidth, height)
            };
        }

        return meta;
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
        UnityEngine.Object[] loaded = AssetDatabase.LoadAllAssetsAtPath(texturePath);
        List<Sprite> sprites = new List<Sprite>(loaded != null ? loaded.Length : 0);
        if (loaded != null)
        {
            for (int i = 0; i < loaded.Length; i++)
            {
                if (loaded[i] is Sprite sprite)
                    sprites.Add(sprite);
            }
        }

        sprites.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        return sprites.ToArray();
    }

    private static AnimationClip CreateOrUpdateClip(string clipPath, Sprite[] sprites, float frameRate, bool loop)
    {
        if (sprites == null || sprites.Length == 0)
            throw new InvalidOperationException($"[DefenderKnightAuthoringBuilder] Empty sprite list for {clipPath}.");

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

        int keyCount = sprites.Length + 1;
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

    private static AnimatorController CreateOrReplaceController(
        AnimationClip idleClip,
        AnimationClip dieClip,
        AnimationClip walkDownClip,
        AnimationClip walkLeftClip,
        AnimationClip walkRightClip,
        AnimationClip walkUpClip,
        AnimationClip attackDownClip,
        AnimationClip attackLeftClip,
        AnimationClip attackRightClip,
        AnimationClip attackUpClip)
    {
        if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(ControllerPath) != null)
            AssetDatabase.DeleteAsset(ControllerPath);

        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        controller.name = "DefenderSpartanAnimationController";

        controller.AddParameter("moveX", AnimatorControllerParameterType.Float);
        controller.AddParameter("moveY", AnimatorControllerParameterType.Float);
        controller.AddParameter("isMoving", AnimatorControllerParameterType.Bool);
        controller.AddParameter("attack", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("isDead", AnimatorControllerParameterType.Bool);

        AnimatorStateMachine sm = controller.layers[0].stateMachine;
        sm.anyStatePosition = new Vector3(390f, -70f, 0f);
        sm.entryPosition = new Vector3(50f, 120f, 0f);

        BlendTree walkBlendTree = new BlendTree
        {
            name = "DefenderSpartanWalkBlendTree",
            blendType = BlendTreeType.SimpleDirectional2D,
            blendParameter = "moveX",
            blendParameterY = "moveY",
            useAutomaticThresholds = false
        };
        AssetDatabase.AddObjectToAsset(walkBlendTree, controller);
        walkBlendTree.AddChild(walkRightClip, new Vector2(1f, 0f));
        walkBlendTree.AddChild(walkLeftClip, new Vector2(-1f, 0f));
        walkBlendTree.AddChild(walkUpClip, new Vector2(0f, 1f));
        walkBlendTree.AddChild(walkDownClip, new Vector2(0f, -1f));

        BlendTree attackBlendTree = new BlendTree
        {
            name = "DefenderSpartanAttackBlendTree",
            blendType = BlendTreeType.SimpleDirectional2D,
            blendParameter = "moveX",
            blendParameterY = "moveY",
            useAutomaticThresholds = false
        };
        AssetDatabase.AddObjectToAsset(attackBlendTree, controller);
        attackBlendTree.AddChild(attackRightClip, new Vector2(1f, 0f));
        attackBlendTree.AddChild(attackLeftClip, new Vector2(-1f, 0f));
        attackBlendTree.AddChild(attackUpClip, new Vector2(0f, 1f));
        attackBlendTree.AddChild(attackDownClip, new Vector2(0f, -1f));

        AnimatorState idleState = sm.AddState("Idle", new Vector3(140f, 60f, 0f));
        idleState.motion = idleClip;
        sm.defaultState = idleState;

        AnimatorState walkState = sm.AddState("Walk", new Vector3(320f, 60f, 0f));
        walkState.motion = walkBlendTree;

        AnimatorState attackState = sm.AddState("Attack", new Vector3(500f, 60f, 0f));
        attackState.motion = attackBlendTree;

        AnimatorState deathState = sm.AddState("Death", new Vector3(320f, 210f, 0f));
        deathState.motion = dieClip;

        AnimatorStateTransition idleToWalk = idleState.AddTransition(walkState);
        idleToWalk.hasExitTime = false;
        idleToWalk.duration = 0.06f;
        idleToWalk.AddCondition(AnimatorConditionMode.If, 0f, "isMoving");

        AnimatorStateTransition walkToIdle = walkState.AddTransition(idleState);
        walkToIdle.hasExitTime = false;
        walkToIdle.duration = 0.06f;
        walkToIdle.AddCondition(AnimatorConditionMode.IfNot, 0f, "isMoving");

        AnimatorStateTransition anyToAttack = sm.AddAnyStateTransition(attackState);
        anyToAttack.hasExitTime = false;
        anyToAttack.duration = 0f;
        anyToAttack.AddCondition(AnimatorConditionMode.IfNot, 0f, "isDead");
        anyToAttack.AddCondition(AnimatorConditionMode.If, 0f, "attack");

        AnimatorStateTransition attackToIdle = attackState.AddTransition(idleState);
        attackToIdle.hasExitTime = true;
        attackToIdle.exitTime = 0.95f;
        attackToIdle.duration = 0.05f;
        attackToIdle.AddCondition(AnimatorConditionMode.IfNot, 0f, "isDead");
        attackToIdle.AddCondition(AnimatorConditionMode.IfNot, 0f, "isMoving");

        AnimatorStateTransition attackToWalk = attackState.AddTransition(walkState);
        attackToWalk.hasExitTime = true;
        attackToWalk.exitTime = 0.95f;
        attackToWalk.duration = 0.05f;
        attackToWalk.AddCondition(AnimatorConditionMode.IfNot, 0f, "isDead");
        attackToWalk.AddCondition(AnimatorConditionMode.If, 0f, "isMoving");

        AnimatorStateTransition anyToDeath = sm.AddAnyStateTransition(deathState);
        anyToDeath.hasExitTime = false;
        anyToDeath.duration = 0f;
        anyToDeath.AddCondition(AnimatorConditionMode.If, 0f, "isDead");

        EditorUtility.SetDirty(controller);
        return controller;
    }

    private static void PatchDefenderPrefab(AnimatorController controller, Sprite idleSprite)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            root.name = "DefenderSpartan";

            SpriteRenderer spriteRenderer = root.GetComponent<SpriteRenderer>();
            if (spriteRenderer != null)
                spriteRenderer.sprite = idleSprite;

            Animator animator = root.GetComponent<Animator>();
            if (animator != null)
                animator.runtimeAnimatorController = controller;

            BoxCollider2D collider = root.GetComponent<BoxCollider2D>();
            DefenderUnit defender = root.GetComponent<DefenderUnit>();
            if (defender != null)
            {
                SerializedObject so = new SerializedObject(defender);
                SerializedProperty blockerCollider = so.FindProperty("blockerCollider");
                SerializedProperty animatorRef = so.FindProperty("animator");

                if (blockerCollider != null && collider != null)
                    blockerCollider.objectReferenceValue = collider;

                if (animatorRef != null && animator != null)
                    animatorRef.objectReferenceValue = animator;

                so.ApplyModifiedPropertiesWithoutUndo();
            }

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
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
