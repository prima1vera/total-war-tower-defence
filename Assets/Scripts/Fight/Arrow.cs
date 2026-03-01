using UnityEngine;
using System.Collections.Generic;

public class Arrow : MonoBehaviour
{
    public int damage = 1;
    public DamageType damageType = DamageType.Normal;
    public float knockbackForce = 0.3f;

    public float travelTime = 0.6f;
    public float arcHeight = 2f;

    public int maxPierce = 3;
    public float impactRadius = 1.5f;
    public LayerMask unitLayer;

    [SerializeField] private int maxHitColliders = 32;

    public GameObject dustPrefab;

    private Vector2 startPos;
    private Vector2 targetPos;
    private float timer = 0f;
    private bool hasImpacted = false;

    private int pierceCount = 0;
    private readonly HashSet<UnitHealth> hitUnits = new HashSet<UnitHealth>();
    private ArrowPool ownerPool;
    private Collider2D[] hitBuffer;
    private Transform cachedTransform;

    void Awake()
    {
        cachedTransform = transform;

        int bufferSize = Mathf.Max(8, maxHitColliders);
        hitBuffer = new Collider2D[bufferSize];
    }

    public void Launch(Vector2 target)
    {
        startPos = cachedTransform.position;
        targetPos = target;
        timer = 0f;
        hasImpacted = false;
        pierceCount = 0;
        hitUnits.Clear();
    }

    public void SetPool(ArrowPool pool)
    {
        ownerPool = pool;
    }

    void Update()
    {
        if (hasImpacted) return;

        timer += Time.deltaTime;
        float t = timer / travelTime;

        if (t >= 1f)
        {
            Explode();
            return;
        }

        Vector2 currentPos = Vector2.Lerp(startPos, targetPos, t);

        float height = Mathf.Sin(t * Mathf.PI) * arcHeight;
        currentPos.y += height;

        Vector2 velocity = currentPos - (Vector2)cachedTransform.position;
        if (velocity.sqrMagnitude > 0.001f)
        {
            float angle = Mathf.Atan2(velocity.y, velocity.x) * Mathf.Rad2Deg;
            cachedTransform.rotation = Quaternion.AngleAxis(angle, Vector3.forward);
        }

        cachedTransform.position = currentPos;

        CheckUnits();
    }

    void CheckUnits()
    {
        if (hitBuffer == null || hitBuffer.Length == 0)
            return;

        int hitCount = Physics2D.OverlapCircleNonAlloc(cachedTransform.position, 0.3f, hitBuffer, unitLayer);

        for (int i = 0; i < hitCount; i++)
        {
            Collider2D hit = hitBuffer[i];
            if (hit == null)
                continue;

            UnitHealth health = UnitHealthLookupCache.Resolve(hit);
            if (health == null)
                continue;

            if (!hitUnits.Add(health))
                continue;

            pierceCount++;
            ApplyDamage(health);

            if (pierceCount >= maxPierce)
            {
                Explode();
                return;
            }
        }
    }

    void ApplyDamage(UnitHealth health)
    {
        Vector2 forceDir = (health.transform.position - cachedTransform.position).normalized;

        health.TakeDamage(damage, damageType, forceDir, knockbackForce);

        StatusEffectHandler status = health.StatusEffectHandler;

        if (status != null)
        {
            if (damageType == DamageType.Fire)
                status.ApplyBurn(3f, 1, 0.5f);

            if (damageType == DamageType.Ice)
                status.ApplyFreeze(2f, 0.4f);
        }
    }

    void Explode()
    {
        if (hasImpacted) return;
        hasImpacted = true;

        if (dustPrefab != null)
            VfxPool.Instance.Spawn(dustPrefab, cachedTransform.position, Quaternion.identity);

        ExplodeAreaDamage();

        if (ownerPool != null)
        {
            ownerPool.Despawn(this);
            return;
        }

        Destroy(gameObject);
    }

    private void ExplodeAreaDamage()
    {
        if (hitBuffer == null || hitBuffer.Length == 0)
            return;

        int hitCount = Physics2D.OverlapCircleNonAlloc(cachedTransform.position, impactRadius, hitBuffer, unitLayer);

        for (int i = 0; i < hitCount; i++)
        {
            Collider2D hit = hitBuffer[i];
            if (hit == null)
                continue;

            UnitHealth health = UnitHealthLookupCache.Resolve(hit);
            if (health == null)
                continue;

            ApplyDamage(health);
        }
    }
}
