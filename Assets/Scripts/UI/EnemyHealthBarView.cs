using UnityEngine;
using UnityEngine.UI;

public class EnemyHealthBarView : MonoBehaviour
{
    [SerializeField] private RectTransform root;
    [SerializeField] private RectTransform fillRect;
    [SerializeField] private Image fillImage;
    [SerializeField] private RectTransform delayedFillRect;
    [SerializeField] private Image delayedFillImage;

    public RectTransform Root => root;
    public RectTransform FillRect => fillRect;
    public Image FillImage => fillImage;
    public RectTransform DelayedFillRect => delayedFillRect;
    public Image DelayedFillImage => delayedFillImage;
    public bool IsConfigured => root != null && fillRect != null && fillImage != null;

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

        if (fillRect == null)
        {
            Transform fill = transform.Find("Fill");
            if (fill != null)
                fillRect = fill as RectTransform;
        }

        if (fillImage == null && fillRect != null)
            fillImage = fillRect.GetComponent<Image>();

        if (delayedFillRect == null)
        {
            Transform delayed = transform.Find("DamageFill");
            if (delayed != null)
                delayedFillRect = delayed as RectTransform;
        }

        if (delayedFillImage == null && delayedFillRect != null)
            delayedFillImage = delayedFillRect.GetComponent<Image>();
    }
}
