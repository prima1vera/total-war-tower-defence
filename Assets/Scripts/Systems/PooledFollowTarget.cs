using UnityEngine;

public class PooledFollowTarget : MonoBehaviour
{
    private Transform target;
    private UnitHealth targetHealth;
    private Vector3 localOffset;
    private float followDuration;
    private float elapsed;
    private bool releaseWhenTargetLost;

    [SerializeField] private bool releaseWhenUnitDies = true;

    private bool isFollowing;

    public void Attach(Transform followTarget, Vector3 offsetInTargetLocalSpace, float durationSeconds, bool releaseOnTargetLost)
    {
        target = followTarget;
        targetHealth = target != null ? target.GetComponent<UnitHealth>() : null;
        localOffset = offsetInTargetLocalSpace;
        followDuration = durationSeconds;
        elapsed = 0f;
        releaseWhenTargetLost = releaseOnTargetLost;
        isFollowing = target != null;

        if (isFollowing)
            transform.position = target.TransformPoint(localOffset);
    }

    private void Update()
    {
        if (!isFollowing)
            return;

        bool lostTarget = target == null || !target.gameObject.activeInHierarchy;
        bool deadTarget = releaseWhenUnitDies && targetHealth != null && targetHealth.IsDead;

        if (lostTarget || deadTarget)
        {
            isFollowing = false;

            if (releaseWhenTargetLost)
            {
                if (VfxPool.TryGetInstance(out VfxPool vfxPool))
                    vfxPool.Release(gameObject);
                else
                    Destroy(gameObject);
            }

            return;
        }

        if (followDuration > 0f)
        {
            elapsed += Time.deltaTime;
            if (elapsed >= followDuration)
            {
                isFollowing = false;
                return;
            }
        }

        transform.position = target.TransformPoint(localOffset);
    }

    private void OnDisable()
    {
        isFollowing = false;
        target = null;
        targetHealth = null;
    }
}
