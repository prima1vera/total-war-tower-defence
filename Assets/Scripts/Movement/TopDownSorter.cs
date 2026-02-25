using UnityEngine;

public class TopDownSorter : MonoBehaviour
{
    private Collider2D col;
    private SpriteRenderer sr;
    private UnitHealth unitHealth;

    void Awake()
    {
        col = GetComponent<Collider2D>();
        sr = GetComponent<SpriteRenderer>();
        unitHealth = GetComponent<UnitHealth>();
    }

    void LateUpdate()
    {
        if (sr == null || unitHealth == null)
            return;

        if (unitHealth.CurrentState == UnitState.Dead)
            return;

        float y = (col != null) ? col.bounds.min.y : transform.position.y;
        sr.sortingOrder = Mathf.RoundToInt(-y * 100f);
    }
}
