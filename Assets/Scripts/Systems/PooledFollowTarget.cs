using UnityEngine;

public class PooledFollowTarget : MonoBehaviour
{
    private Transform target;
    private UnitHealth targetHealth;

    private Vector3 localOffset;
    private Vector3 worldOffset;
    private bool followLocalOffset = true;

    private float followDuration;
    private float elapsed;
    private bool releaseWhenTargetLost;

    [SerializeField] private bool releaseWhenUnitDies = true;
    [SerializeField] private bool syncSortingWithTarget = true;
    [SerializeField] private int sortingOrderOffset = 1;

    private bool isFollowing;
    private SpriteRenderer selfSpriteRenderer;
    private SpriteRenderer targetSpriteRenderer;

    private void Awake()
    {
        selfSpriteRenderer = GetComponent<SpriteRenderer>();
    }

    public void Attach(
        Transform followTarget,
        Vector3 offsetInTargetSpace,
        float durationSeconds,
        bool releaseOnTargetLost,
        bool useLocalSpaceOffset = true)
    {
        target = followTarget;
        targetHealth = target != null ? target.GetComponent<UnitHealth>() : null;
        targetSpriteRenderer = target != null ? target.GetComponent<SpriteRenderer>() : null;

        followLocalOffset = useLocalSpaceOffset;
        if (followLocalOffset)
            localOffset = offsetInTargetSpace;
        else
            worldOffset = offsetInTargetSpace;

        followDuration = durationSeconds;
        elapsed = 0f;
        releaseWhenTargetLost = releaseOnTargetLost;
        isFollowing = target != null;

        if (isFollowing)
        {
            transform.position = ResolveTargetPosition();
            SyncSortingFromTarget();
        }
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

        transform.position = ResolveTargetPosition();
        SyncSortingFromTarget();
    }

    private Vector3 ResolveTargetPosition()
    {
        if (target == null)
            return transform.position;

        if (followLocalOffset)
            return target.TransformPoint(localOffset);

        return target.position + worldOffset;
    }

    private void SyncSortingFromTarget()
    {
        if (!syncSortingWithTarget || selfSpriteRenderer == null || targetSpriteRenderer == null)
            return;

        selfSpriteRenderer.sortingLayerID = targetSpriteRenderer.sortingLayerID;
        selfSpriteRenderer.sortingOrder = targetSpriteRenderer.sortingOrder + sortingOrderOffset;
    }

    private void OnDisable()
    {
        isFollowing = false;
        target = null;
        targetHealth = null;
        targetSpriteRenderer = null;
    }
}

