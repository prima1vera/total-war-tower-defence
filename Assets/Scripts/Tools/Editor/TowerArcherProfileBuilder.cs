using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class TowerArcherProfileBuilder
{
    private const string MenuBuildAll = "TWTD/Validation/Build Archer Visual Profiles";
    private const string UnitsRoot = "Assets/Sprites/Tower/ArcherTower/3 Units";
    private const int FrameWidth = 48;
    private const int FrameHeight = 48;

    [MenuItem(MenuBuildAll)]
    private static void BuildAllProfiles()
    {
        string[] guids = AssetDatabase.FindAssets("t:TowerArcherVisualProfile");

        if (guids.Length == 0)
        {
            Debug.LogWarning("[TowerArcherProfileBuilder] No TowerArcherVisualProfile assets found.");
            return;
        }

        int success = 0;
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            TowerArcherVisualProfile profile = AssetDatabase.LoadAssetAtPath<TowerArcherVisualProfile>(path);
            if (profile == null)
                continue;

            BuildProfile(profile);
            success++;
        }

        Debug.Log($"[TowerArcherProfileBuilder] Built {success} archer profile(s) from '{UnitsRoot}'.");
    }

    [MenuItem("CONTEXT/TowerArcherVisualProfile/Build From ArcherTower Units Folder")]
    private static void BuildSelectedProfile(MenuCommand command)
    {
        TowerArcherVisualProfile profile = command.context as TowerArcherVisualProfile;
        if (profile == null)
            return;

        BuildProfile(profile);
        Debug.Log($"[TowerArcherProfileBuilder] Profile '{profile.name}' updated.", profile);
    }

    private static void BuildProfile(TowerArcherVisualProfile profile)
    {
        for (int level = 1; level <= 3; level++)
        {
            DirectionalSet down = BuildDirectional(level, "D");
            DirectionalSet side = BuildDirectional(level, "S");
            DirectionalSet up = BuildDirectional(level, "U");

            profile.EditorSetLevelSprites(
                level,
                down.Idle,
                down.PreAttack,
                down.Attack,
                side.Idle,
                side.PreAttack,
                side.Attack,
                up.Idle,
                up.PreAttack,
                up.Attack);
        }

        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();
    }

    private static DirectionalSet BuildDirectional(int level, string directionPrefix)
    {
        string levelPath = $"{UnitsRoot}/{level}";

        DirectionalSet set = new DirectionalSet
        {
            Idle = ImportSliceAndCollectSprites($"{levelPath}/{directionPrefix}_Idle.png"),
            PreAttack = ImportSliceAndCollectSprites($"{levelPath}/{directionPrefix}_Preattack.png"),
            Attack = ImportSliceAndCollectSprites($"{levelPath}/{directionPrefix}_Attack.png")
        };

        return set;
    }

    private static Sprite[] ImportSliceAndCollectSprites(string texturePath)
    {
        if (!File.Exists(texturePath))
        {
            Debug.LogWarning($"[TowerArcherProfileBuilder] Missing texture: {texturePath}");
            return new Sprite[0];
        }

        TextureImporter importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
        if (importer == null)
        {
            Debug.LogError($"[TowerArcherProfileBuilder] TextureImporter not found for: {texturePath}");
            return new Sprite[0];
        }

        Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        if (texture == null)
        {
            Debug.LogError($"[TowerArcherProfileBuilder] Texture not found at: {texturePath}");
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
        if (NeedReplaceSheet(importer.spritesheet, desiredSheet))
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
        Object[] assets = AssetDatabase.LoadAllAssetsAtPath(texturePath);
        List<Sprite> sprites = new List<Sprite>(assets.Length);

        for (int i = 0; i < assets.Length; i++)
        {
            Sprite sprite = assets[i] as Sprite;
            if (sprite != null)
                sprites.Add(sprite);
        }

        sprites.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        return sprites.ToArray();
    }

    private struct DirectionalSet
    {
        public Sprite[] Idle;
        public Sprite[] PreAttack;
        public Sprite[] Attack;
    }
}