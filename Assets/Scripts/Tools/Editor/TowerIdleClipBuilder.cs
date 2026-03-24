using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class TowerIdleClipBuilder
{
    private const string MenuBuild = "TWTD/Validation/Build Tower Idle Clips (Fire/Frost L1-L2)";
    private const float DefaultFrameRate = 8f;

    private static readonly BuildEntry[] Entries =
    {
        new BuildEntry(
            "Assets/Sprites/Tower/fire/lvl1/Fire_lvl1_128_SE_Idle.png",
            "Assets/Animations/Towers/FireTowerIdle.anim"),
        new BuildEntry(
            "Assets/Sprites/Tower/fire/lvl2/Fire_lvl2_128_SE_Idle.png",
            "Assets/Animations/Towers/FireTowerIdle_Lv2.anim"),
        new BuildEntry(
            "Assets/Sprites/Tower/frost/lvl1/Frost_lvl1_128_SE_Idle.png",
            "Assets/Animations/Towers/FrostTowerIdle.anim"),
        new BuildEntry(
            "Assets/Sprites/Tower/frost/lvl2/Frost_lvl2_128_SE_Idle.png",
            "Assets/Animations/Towers/FrostTowerIdle_Lv2.anim"),
    };

    [MenuItem(MenuBuild)]
    private static void Build()
    {
        int updated = 0;
        int skipped = 0;

        for (int i = 0; i < Entries.Length; i++)
        {
            BuildEntry entry = Entries[i];

            Sprite[] sprites = LoadSortedSprites(entry.SpriteSheetPath);
            if (sprites.Length == 0)
            {
                Debug.LogWarning($"[TowerIdleClipBuilder] No sprites found in '{entry.SpriteSheetPath}'. Skipping.");
                skipped++;
                continue;
            }

            AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(entry.ClipPath);
            if (clip == null)
            {
                clip = new AnimationClip();
                AssetDatabase.CreateAsset(clip, entry.ClipPath);
            }

            ApplySpritesToClip(clip, sprites, DefaultFrameRate);
            EditorUtility.SetDirty(clip);
            updated++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[TowerIdleClipBuilder] Done. Updated: {updated}, Skipped: {skipped}.");
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

    private static void ApplySpritesToClip(AnimationClip clip, Sprite[] sprites, float frameRate)
    {
        float fps = Mathf.Max(1f, frameRate);
        float frameTime = 1f / fps;

        EditorCurveBinding binding = new EditorCurveBinding
        {
            path = string.Empty,
            type = typeof(SpriteRenderer),
            propertyName = "m_Sprite"
        };

        int keyCount = sprites.Length == 1 ? 1 : sprites.Length + 1;
        ObjectReferenceKeyframe[] keys = new ObjectReferenceKeyframe[keyCount];

        for (int i = 0; i < sprites.Length; i++)
        {
            keys[i] = new ObjectReferenceKeyframe
            {
                time = i * frameTime,
                value = sprites[i]
            };
        }

        if (sprites.Length > 1)
        {
            keys[keyCount - 1] = new ObjectReferenceKeyframe
            {
                time = sprites.Length * frameTime,
                value = sprites[0]
            };
        }

        clip.frameRate = fps;
        AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);
        SetLoopTime(clip, true);
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

    private readonly struct BuildEntry
    {
        public readonly string SpriteSheetPath;
        public readonly string ClipPath;

        public BuildEntry(string spriteSheetPath, string clipPath)
        {
            SpriteSheetPath = spriteSheetPath;
            ClipPath = clipPath;
        }
    }
}
