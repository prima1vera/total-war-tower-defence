using UnityEngine;

public class EnemyPoolMember : MonoBehaviour
{
    private EnemyPool ownerPool;

    public void Bind(EnemyPool pool)
    {
        ownerPool = pool;
    }

    public bool TryDespawnToPool()
    {
        if (ownerPool == null)
            return false;

        ownerPool.Despawn(gameObject);
        return true;
    }
}
