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
