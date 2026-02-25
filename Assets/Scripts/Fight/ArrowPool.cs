using System.Collections.Generic;
using UnityEngine;

public class ArrowPool : MonoBehaviour
{
    [SerializeField] private Arrow arrowPrefab;
    [SerializeField] private int prewarmCount = 16;

    private readonly Queue<Arrow> pooledArrows = new Queue<Arrow>(32);

    void Awake()
    {
        Prewarm();
    }

    public Arrow Spawn(Vector3 position, Quaternion rotation)
    {
        Arrow arrow = pooledArrows.Count > 0 ? pooledArrows.Dequeue() : CreateArrow();

        Transform arrowTransform = arrow.transform;
        arrowTransform.SetPositionAndRotation(position, rotation);
        arrow.gameObject.SetActive(true);
        arrow.SetPool(this);

        return arrow;
    }

    public void Despawn(Arrow arrow)
    {
        if (arrow == null)
            return;

        arrow.gameObject.SetActive(false);
        pooledArrows.Enqueue(arrow);
    }

    private void Prewarm()
    {
        if (arrowPrefab == null)
            return;

        int count = Mathf.Max(0, prewarmCount);

        for (int i = 0; i < count; i++)
        {
            Arrow arrow = CreateArrow();
            arrow.gameObject.SetActive(false);
            pooledArrows.Enqueue(arrow);
        }
    }

    private Arrow CreateArrow()
    {
        Arrow arrow = Instantiate(arrowPrefab, transform);
        arrow.SetPool(this);

        return arrow;
    }
}