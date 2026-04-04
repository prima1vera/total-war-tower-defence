using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class DefenderKnightAuthoringBuilder
{
    private const string MenuBuild = "TWTD/Validation/Build Defender Knight Assets";
    private const string MenuRebuild = "TWTD/Validation/Rebuild Defender Knight Assets (Force Slice)";

    private const string SpriteRoot = "Assets/Sprites/DefenderKnight";
    private const string AnimRoot = "Assets/Animations/Units/DefenderKnight";

    private const string IdleSheet = SpriteRoot + "/KnightIdle.png";
    private const string RunSheet = SpriteRoot + "/KnightRun.png";
    private const string AttackSheet = SpriteRoot + "/KnightAttack.png";
    private const string DeathSheet = SpriteRoot + "/KnightDeath.png";

    private const string IdleClipPath = AnimRoot + "/DefenderKnight_Idle.anim";
    private const string RunClipPath = AnimRoot + "/DefenderKnight_Run.anim";
    private const string AttackClipPath = AnimRoot + "/DefenderKnight_Attack.anim";
    private const string DeathClipPath = AnimRoot + "/DefenderKnight_Death.anim";
    private const string ControllerPath = AnimRoot + "/DefenderKnightAnimationController.controller";

    private const string PrefabPath = "Assets/Prefabs/Defenders/DefenderKnight.prefab";

    private const int FrameWidth = 64;
    private const int FrameHeight = 64;

    private const float IdleFps = 8f;
    private const float RunFps = 12f;
    private const float AttackFps = 14f;
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

    // Batchmode entrypoint:
    // Unity.exe -batchmode -projectPath <path> -executeMethod DefenderKnightAuthoringBuilder.BuildForBatchmode -quit
    public static void BuildForBatchmode()
    {
        BuildAll(false);
    }

    private static void BuildAll(bool forceSlice)
    {
        EnsureDirectory(AnimRoot);

        Sprite[] idleSprites = ImportAndLoadSprites(IdleSheet, forceSlice);
        Sprite[] runSprites = ImportAndLoadSprites(RunSheet, forceSlice);
        Sprite[] attackSprites = ImportAndLoadSprites(AttackSheet, forceSlice);
        Sprite[] deathSprites = ImportAndLoadSprites(DeathSheet, forceSlice);

        ValidateSpriteSet(idleSprites, IdleSheet);
        ValidateSpriteSet(runSprites, RunSheet);
        ValidateSpriteSet(attackSprites, AttackSheet);
        ValidateSpriteSet(deathSprites, DeathSheet);

        AnimationClip idleClip = CreateOrUpdateClip(IdleClipPath, idleSprites, IdleFps, true);
        AnimationClip runClip = CreateOrUpdateClip(RunClipPath, runSprites, RunFps, true);
        AnimationClip attackClip = CreateOrUpdateClip(AttackClipPath, attackSprites, AttackFps, false);
        AnimationClip deathClip = CreateOrUpdateClip(DeathClipPath, deathSprites, DeathFps, false);

        AnimatorController controller = CreateOrReplaceController(idleClip, runClip, attackClip, deathClip);
        PatchDefenderPrefab(controller, idleSprites[0]);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[DefenderKnightAuthoringBuilder] DefenderKnight prefab + clips + controller are ready.");
    }

    private static Sprite[] ImportAndLoadSprites(string texturePath, bool forceSlice)
    {
        if (!File.Exists(texturePath))
        {
            Debug.LogError($"[DefenderKnightAuthoringBuilder] Missing texture: {texturePath}");
            return new Sprite[0];
        }

        TextureImporter importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
        if (importer == null)
        {
            Debug.LogError($"[DefenderKnightAuthoringBuilder] TextureImporter missing for: {texturePath}");
            return new Sprite[0];
        }

        Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        if (texture == null)
        {
            Debug.LogError($"[DefenderKnightAuthoringBuilder] Texture not loadable: {texturePath}");
            return new Sprite[0];
        }

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

        if (forceSlice)
        {
            int columns = Mathf.Max(1, texture.width / FrameWidth);
            int rows = Mathf.Max(1, texture.height / FrameHeight);
            importer.spritesheet = BuildSheetMeta(texturePath, columns, rows);
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
                SpriteMetaData data = new SpriteMetaData
                {
                    name = $"{baseName}_{frameIndex:D2}",
                    alignment = (int)SpriteAlignment.Center,
                    pivot = new Vector2(0.5f, 0.5f),
                    rect = new Rect(col * FrameWidth, row * FrameHeight, FrameWidth, FrameHeight)
                };

                meta.Add(data);
                frameIndex++;
            }
        }

        return meta.ToArray();
    }

    private static Sprite[] LoadSortedSprites(string texturePath)
    {
        Object[] loaded = AssetDatabase.LoadAllAssetsAtPath(texturePath);
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

    private static void ValidateSpriteSet(Sprite[] sprites, string sourcePath)
    {
        if (sprites == null || sprites.Length == 0)
            throw new System.InvalidOperationException($"[DefenderKnightAuthoringBuilder] No sprites found for '{sourcePath}'.");
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

        int keyCount = loop ? sprites.Length + 1 : sprites.Length;
        ObjectReferenceKeyframe[] keys = new ObjectReferenceKeyframe[keyCount];

        for (int i = 0; i < sprites.Length; i++)
        {
            keys[i] = new ObjectReferenceKeyframe
            {
                time = i * frameTime,
                value = sprites[i]
            };
        }

        if (loop)
        {
            keys[keyCount - 1] = new ObjectReferenceKeyframe
            {
                time = sprites.Length * frameTime,
                value = sprites[0]
            };
        }

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
        AnimationClip runClip,
        AnimationClip attackClip,
        AnimationClip deathClip)
    {
        if (AssetDatabase.LoadAssetAtPath<Object>(ControllerPath) != null)
            AssetDatabase.DeleteAsset(ControllerPath);

        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        controller.name = "DefenderKnightAnimationController";

        controller.AddParameter("isMoving", AnimatorControllerParameterType.Bool);
        controller.AddParameter("attack", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("isDead", AnimatorControllerParameterType.Bool);

        AnimatorStateMachine sm = controller.layers[0].stateMachine;
        sm.anyStatePosition = new Vector3(340f, -20f, 0f);
        sm.entryPosition = new Vector3(40f, 90f, 0f);

        AnimatorState idle = sm.AddState("Idle", new Vector3(180f, 80f, 0f));
        AnimatorState run = sm.AddState("Run", new Vector3(360f, 80f, 0f));
        AnimatorState attack = sm.AddState("Attack", new Vector3(540f, 80f, 0f));
        AnimatorState death = sm.AddState("Death", new Vector3(360f, 230f, 0f));

        idle.motion = idleClip;
        run.motion = runClip;
        attack.motion = attackClip;
        death.motion = deathClip;

        sm.defaultState = idle;

        AnimatorStateTransition idleToRun = idle.AddTransition(run);
        idleToRun.hasExitTime = false;
        idleToRun.duration = 0.06f;
        idleToRun.AddCondition(AnimatorConditionMode.If, 0f, "isMoving");

        AnimatorStateTransition runToIdle = run.AddTransition(idle);
        runToIdle.hasExitTime = false;
        runToIdle.duration = 0.06f;
        runToIdle.AddCondition(AnimatorConditionMode.IfNot, 0f, "isMoving");

        AnimatorStateTransition anyToAttack = sm.AddAnyStateTransition(attack);
        anyToAttack.hasExitTime = false;
        anyToAttack.duration = 0f;
        anyToAttack.AddCondition(AnimatorConditionMode.IfNot, 0f, "isDead");
        anyToAttack.AddCondition(AnimatorConditionMode.If, 0f, "attack");

        AnimatorStateTransition attackToIdle = attack.AddTransition(idle);
        attackToIdle.hasExitTime = true;
        attackToIdle.exitTime = 0.95f;
        attackToIdle.duration = 0.04f;
        attackToIdle.AddCondition(AnimatorConditionMode.IfNot, 0f, "isDead");
        attackToIdle.AddCondition(AnimatorConditionMode.IfNot, 0f, "isMoving");

        AnimatorStateTransition attackToRun = attack.AddTransition(run);
        attackToRun.hasExitTime = true;
        attackToRun.exitTime = 0.95f;
        attackToRun.duration = 0.04f;
        attackToRun.AddCondition(AnimatorConditionMode.IfNot, 0f, "isDead");
        attackToRun.AddCondition(AnimatorConditionMode.If, 0f, "isMoving");

        AnimatorStateTransition anyToDeath = sm.AddAnyStateTransition(death);
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
            SpriteRenderer sr = root.GetComponent<SpriteRenderer>();
            if (sr != null)
                sr.sprite = idleSprite;

            Animator animator = root.GetComponent<Animator>();
            if (animator != null)
                animator.runtimeAnimatorController = controller;

            BoxCollider2D collider = root.GetComponent<BoxCollider2D>();
            if (collider != null && (collider.size.x < 0.1f || collider.size.y < 0.1f))
            {
                collider.size = new Vector2(0.42f, 0.56f);
                collider.offset = new Vector2(0f, -0.03f);
            }

            DefenderUnit defender = root.GetComponent<DefenderUnit>();
            if (defender != null)
            {
                SerializedObject serializedDefender = new SerializedObject(defender);
                SerializedProperty blockerCollider = serializedDefender.FindProperty("blockerCollider");
                SerializedProperty animatorRef = serializedDefender.FindProperty("animator");

                if (blockerCollider != null && blockerCollider.objectReferenceValue == null && collider != null)
                    blockerCollider.objectReferenceValue = collider;

                if (animatorRef != null && animatorRef.objectReferenceValue == null && animator != null)
                    animatorRef.objectReferenceValue = animator;

                serializedDefender.ApplyModifiedPropertiesWithoutUndo();
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
