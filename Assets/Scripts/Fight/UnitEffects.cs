using UnityEngine;

public class UnitEffects : MonoBehaviour
{
    private SpriteRenderer sr;

    public GameObject fireEffect; // сюда префаб огня

    public GameObject frostEffectPrefab;


    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();

        if (fireEffect != null)
            fireEffect.SetActive(false);

        if (frostEffectPrefab != null)
            frostEffectPrefab.SetActive(false);
    }


    public void SetFireVisual(bool state)
    {
        if (fireEffect != null)
            fireEffect.SetActive(state);

        if (sr != null)
            sr.color = state ? new Color(1f, 0.5f, 0.2f) : Color.white;
    }

    public void SetFreezeVisual(bool state)
    {
        if (frostEffectPrefab != null)
            frostEffectPrefab.SetActive(state);

        if (sr != null)
            sr.color = state ? Color.cyan : Color.white;
    }

}
