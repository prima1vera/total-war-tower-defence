using UnityEngine;
using System;
using System.Collections.Generic;

[DisallowMultipleComponent]
public sealed class UnitShadowRendererSync : MonoBehaviour
{
    [Serializable]
    private struct ShadowSheetBinding
    {
        public Texture2D bodySheet;
        public Texture2D shadowSheet;
    }

    [SerializeField] private SpriteRenderer bodyRenderer;
    [SerializeField] private SpriteRenderer shadowRenderer;
    [SerializeField] private ShadowSheetBinding[] shadowSheetBindings = Array.Empty<ShadowSheetBinding>();
    [SerializeField] private int shadowOrderOffset = -1;
    [SerializeField] private bool syncFlipX = true;
    [SerializeField] private bool syncFlipY;
    [SerializeField] private bool mirrorBodyVisibility = true;
    [SerializeField] private Color shadowTint = new Color(0f, 0f, 0f, 0.34f);
    [SerializeField] private Vector3 shadowLocalOffset = Vector3.zero;

    private readonly Dictionary<Texture2D, Texture2D> shadowSheetByBody = new Dictionary<Texture2D, Texture2D>(16);
    private readonly Dictionary<Sprite, Sprite> generatedShadowsByBodySprite = new Dictionary<Sprite, Sprite>(128);
    private readonly List<Sprite> runtimeShadowSprites = new List<Sprite>(128);
    private Sprite lastBodySprite;

    private void Awake()
    {
        if (bodyRenderer == null)
            bodyRenderer = GetComponent<SpriteRenderer>();

        if (shadowRenderer == null)
        {
            Transform shadowTransform = transform.Find("Shadow");
            if (shadowTransform != null)
                shadowRenderer = shadowTransform.GetComponent<SpriteRenderer>();
        }

        if (shadowRenderer != null)
        {
            shadowRenderer.color = shadowTint;
            shadowRenderer.transform.localPosition = shadowLocalOffset;
        }

        RebuildSheetLookup();
    }

    private void LateUpdate()
    {
        if (bodyRenderer == null || shadowRenderer == null)
            return;

        shadowRenderer.sortingLayerID = bodyRenderer.sortingLayerID;
        shadowRenderer.sortingOrder = bodyRenderer.sortingOrder + shadowOrderOffset;

        if (syncFlipX)
            shadowRenderer.flipX = bodyRenderer.flipX;

        if (syncFlipY)
            shadowRenderer.flipY = bodyRenderer.flipY;

        if (mirrorBodyVisibility)
            shadowRenderer.enabled = bodyRenderer.enabled;

        UpdateShadowSprite();
    }

    private void OnDestroy()
    {
        for (int i = 0; i < runtimeShadowSprites.Count; i++)
        {
            Sprite sprite = runtimeShadowSprites[i];
            if (sprite == null)
                continue;

            if (Application.isPlaying)
                Destroy(sprite);
            else
                DestroyImmediate(sprite);
        }

        runtimeShadowSprites.Clear();
        generatedShadowsByBodySprite.Clear();
        shadowSheetByBody.Clear();
    }

    private void RebuildSheetLookup()
    {
        shadowSheetByBody.Clear();

        if (shadowSheetBindings == null)
            return;

        for (int i = 0; i < shadowSheetBindings.Length; i++)
        {
            ShadowSheetBinding binding = shadowSheetBindings[i];
            if (binding.bodySheet == null || binding.shadowSheet == null)
                continue;

            shadowSheetByBody[binding.bodySheet] = binding.shadowSheet;
        }
    }

    private void UpdateShadowSprite()
    {
        Sprite bodySprite = bodyRenderer.sprite;
        if (ReferenceEquals(bodySprite, lastBodySprite))
            return;

        lastBodySprite = bodySprite;
        if (bodySprite == null)
        {
            shadowRenderer.sprite = null;
            return;
        }

        if (generatedShadowsByBodySprite.TryGetValue(bodySprite, out Sprite cachedShadow))
        {
            shadowRenderer.sprite = cachedShadow;
            return;
        }

        Sprite generatedShadow = CreateShadowSprite(bodySprite);
        generatedShadowsByBodySprite[bodySprite] = generatedShadow;
        shadowRenderer.sprite = generatedShadow;
    }

    private Sprite CreateShadowSprite(Sprite bodySprite)
    {
        if (bodySprite == null || bodySprite.texture == null)
            return null;

        if (!shadowSheetByBody.TryGetValue(bodySprite.texture, out Texture2D shadowSheet) || shadowSheet == null)
            return null;

        Rect rect = bodySprite.rect;
        if (rect.width <= 0f || rect.height <= 0f)
            return null;

        Vector2 normalizedPivot = new Vector2(bodySprite.pivot.x / rect.width, bodySprite.pivot.y / rect.height);
        Sprite runtimeShadow = Sprite.Create(
            shadowSheet,
            rect,
            normalizedPivot,
            bodySprite.pixelsPerUnit,
            0,
            SpriteMeshType.FullRect,
            bodySprite.border,
            false);

        runtimeShadow.name = $"{bodySprite.name}_shadow_runtime";
        runtimeShadowSprites.Add(runtimeShadow);
        return runtimeShadow;
    }

    public void Configure(
        SpriteRenderer body,
        SpriteRenderer shadow,
        Texture2D[] bodySheets,
        Texture2D[] shadowSheets)
    {
        bodyRenderer = body;
        shadowRenderer = shadow;

        int pairCount = Mathf.Min(
            bodySheets != null ? bodySheets.Length : 0,
            shadowSheets != null ? shadowSheets.Length : 0);

        shadowSheetBindings = new ShadowSheetBinding[pairCount];
        for (int i = 0; i < pairCount; i++)
        {
            shadowSheetBindings[i] = new ShadowSheetBinding
            {
                bodySheet = bodySheets[i],
                shadowSheet = shadowSheets[i]
            };
        }

        RebuildSheetLookup();
        lastBodySprite = null;
    }
}
