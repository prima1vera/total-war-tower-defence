using UnityEngine;

public class PooledTimedAutoReturn : MonoBehaviour
{
    [SerializeField, Min(0.05f)] private float lifetime = 2f;

    private float timer;
    private bool armed;

    private void OnEnable()
    {
        timer = 0f;
        armed = lifetime > 0.01f;
    }

    public void Arm(float duration)
    {
        lifetime = Mathf.Max(0.05f, duration);
        timer = 0f;
        armed = true;
    }

    private void Update()
    {
        if (!armed)
            return;

        timer += Time.deltaTime;
        if (timer < lifetime)
            return;

        armed = false;

        if (VfxPool.TryGetInstance(out VfxPool vfxPool))
            vfxPool.Release(gameObject);
        else
            Destroy(gameObject);
    }

    private void OnDisable()
    {
        armed = false;
    }
}
