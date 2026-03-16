using System;
using System.Collections.Generic;
using UnityEngine;

public static class BloodDecalSpriteSheetCache
{
    private readonly struct CacheKey : IEquatable<CacheKey>
    {
        private readonly int textureId;
        private readonly int columns;
        private readonly int rows;
        private readonly int pixelsPerUnit100;

        public CacheKey(Texture2D texture, int columns, int rows, float pixelsPerUnit)
        {
            textureId = texture != null ? texture.GetInstanceID() : 0;
            this.columns = Mathf.Max(1, columns);
            this.rows = Mathf.Max(1, rows);
            pixelsPerUnit100 = Mathf.RoundToInt(Mathf.Max(1f, pixelsPerUnit) * 100f);
        }

        public bool Equals(CacheKey other)
        {
            return textureId == other.textureId
                   && columns == other.columns
                   && rows == other.rows
                   && pixelsPerUnit100 == other.pixelsPerUnit100;
        }

        public override bool Equals(object obj)
        {
            return obj is CacheKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = textureId;
                hash = (hash * 397) ^ columns;
                hash = (hash * 397) ^ rows;
                hash = (hash * 397) ^ pixelsPerUnit100;
                return hash;
            }
        }
    }

    private static readonly Dictionary<CacheKey, Sprite[]> Cache = new Dictionary<CacheKey, Sprite[]>(4);

    public static bool TryGetRandomSprite(Texture2D spriteSheet, int columns, int rows, float pixelsPerUnit, out Sprite sprite)
    {
        sprite = null;

        if (spriteSheet == null)
            return false;

        int safeColumns = Mathf.Max(1, columns);
        int safeRows = Mathf.Max(1, rows);
        float safePPU = Mathf.Max(1f, pixelsPerUnit);

        CacheKey key = new CacheKey(spriteSheet, safeColumns, safeRows, safePPU);
        if (!Cache.TryGetValue(key, out Sprite[] variants) || variants == null || variants.Length == 0)
        {
            variants = BuildVariants(spriteSheet, safeColumns, safeRows, safePPU);
            Cache[key] = variants;
        }

        if (variants == null || variants.Length == 0)
            return false;

        sprite = variants[UnityEngine.Random.Range(0, variants.Length)];
        return sprite != null;
    }

    private static Sprite[] BuildVariants(Texture2D spriteSheet, int columns, int rows, float pixelsPerUnit)
    {
        int cellWidth = spriteSheet.width / columns;
        int cellHeight = spriteSheet.height / rows;

        if (cellWidth <= 0 || cellHeight <= 0)
            return Array.Empty<Sprite>();

        List<Sprite> sprites = new List<Sprite>(columns * rows);

        for (int row = rows - 1; row >= 0; row--)
        {
            for (int col = 0; col < columns; col++)
            {
                Rect rect = new Rect(col * cellWidth, row * cellHeight, cellWidth, cellHeight);
                Sprite sprite = Sprite.Create(spriteSheet, rect, new Vector2(0.5f, 0.5f), pixelsPerUnit, 0, SpriteMeshType.FullRect);
                if (sprite == null)
                    continue;

                sprite.name = $"{spriteSheet.name}_r{rows - row}_c{col + 1}";
                sprites.Add(sprite);
            }
        }

        return sprites.ToArray();
    }
}
