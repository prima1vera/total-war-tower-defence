using TMPro;
using UnityEngine;

public class DamageNumberView : MonoBehaviour
{
    [SerializeField] private RectTransform root;
    [SerializeField] private TMP_Text valueText;
    [SerializeField] private CanvasGroup canvasGroup;

    public RectTransform Root => root;
    public TMP_Text ValueText => valueText;
    public CanvasGroup CanvasGroup => canvasGroup;
    public bool IsConfigured => root != null && valueText != null && canvasGroup != null;

    private void Reset()
    {
        AutoAssignReferences();
    }

    private void OnValidate()
    {
        AutoAssignReferences();
    }

    [ContextMenu("Apply Pixel Text Preset")]
    private void ApplyPixelTextPreset()
    {
        AutoAssignReferences();
        if (valueText == null)
            return;

        valueText.enableAutoSizing = false;
        valueText.fontSize = 24f;
        valueText.alignment = TextAlignmentOptions.Center;
        valueText.enableWordWrapping = false;
        valueText.overflowMode = TextOverflowModes.Overflow;
        valueText.raycastTarget = false;
        valueText.text = "88";
        valueText.color = Color.white;
        valueText.outlineWidth = 0.2f;
        valueText.outlineColor = new Color(0f, 0f, 0f, 1f);

        if (root != null)
        {
            root.anchorMin = new Vector2(0.5f, 0.5f);
            root.anchorMax = new Vector2(0.5f, 0.5f);
            root.pivot = new Vector2(0.5f, 0.5f);
            root.sizeDelta = new Vector2(64f, 28f);
            root.localScale = Vector3.one;
        }

        if (valueText.fontSharedMaterial != null)
        {
            Material source = valueText.fontSharedMaterial;
            Material instanceMaterial = new Material(source)
            {
                name = source.name + " (DamageNumberInstance)"
            };

            if (instanceMaterial.HasProperty(ShaderUtilities.ID_OutlineWidth))
                instanceMaterial.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.22f);

            if (instanceMaterial.HasProperty(ShaderUtilities.ID_OutlineColor))
                instanceMaterial.SetColor(ShaderUtilities.ID_OutlineColor, new Color(0f, 0f, 0f, 1f));

            if (instanceMaterial.HasProperty(ShaderUtilities.ID_UnderlayColor))
                instanceMaterial.SetColor(ShaderUtilities.ID_UnderlayColor, new Color(0f, 0f, 0f, 0.75f));

            if (instanceMaterial.HasProperty(ShaderUtilities.ID_UnderlayOffsetX))
                instanceMaterial.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, 0.35f);

            if (instanceMaterial.HasProperty(ShaderUtilities.ID_UnderlayOffsetY))
                instanceMaterial.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, -0.35f);

            if (instanceMaterial.HasProperty(ShaderUtilities.ID_UnderlaySoftness))
                instanceMaterial.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0f);

            valueText.fontSharedMaterial = instanceMaterial;
            valueText.fontMaterial = instanceMaterial;
        }

        if (valueText.font != null && valueText.font.name.Contains("LiberationSans"))
            Debug.LogWarning("DamageNumberView: Current font is Liberation Sans (smooth). Assign a pixel TMP Font Asset for true pixel look.", this);
    }

    private void AutoAssignReferences()
    {
        if (root == null)
            root = transform as RectTransform;

        if (valueText == null)
            valueText = GetComponentInChildren<TMP_Text>(true);

        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>();
    }
}
