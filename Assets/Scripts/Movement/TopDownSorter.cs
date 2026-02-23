using UnityEngine;

public class TopDownSorter : MonoBehaviour
{
    private SpriteRenderer sr;

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
    }

    void LateUpdate()
    {
        if (GetComponent<UnitHealth>().CurrentState == UnitState.Dead)
            return;

        sr.sortingOrder = Mathf.RoundToInt(-transform.position.y * 100);
    }
}
