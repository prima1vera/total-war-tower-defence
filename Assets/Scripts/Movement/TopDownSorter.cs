using UnityEngine;

public class TopDownSorter : MonoBehaviour
{
    private SpriteRenderer sr;
    private UnitHealth unitHealth;

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        unitHealth = GetComponent<UnitHealth>();
    }

    void LateUpdate()
    {
        if (sr == null || unitHealth == null)
            return;

        if (unitHealth.CurrentState == UnitState.Dead)
            return;

        sr.sortingOrder = Mathf.RoundToInt(-transform.position.y * 100);
    }
}
