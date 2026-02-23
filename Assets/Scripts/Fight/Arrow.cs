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

    public GameObject dustPrefab;

    private Vector2 startPos;
    private Vector2 targetPos;
    private float timer = 0f;
    private bool hasImpacted = false;

    private int pierceCount = 0;
    private List<UnitHealth> hitUnits = new List<UnitHealth>();
    private ArrowPool ownerPool;

    public void Launch(Vector2 target)
    {
        startPos = transform.position;
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

        Vector2 velocity = currentPos - (Vector2)transform.position;
        if (velocity.sqrMagnitude > 0.001f)
        {
            float angle = Mathf.Atan2(velocity.y, velocity.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.AngleAxis(angle, Vector3.forward);
        }

        transform.position = currentPos;

        CheckUnits();
    }

    void CheckUnits()
    {
        Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, 0.3f, unitLayer);

        foreach (Collider2D hit in hits)
        {
            UnitHealth health = hit.GetComponent<UnitHealth>();
            if (health == null) continue;

            if (hitUnits.Contains(health)) continue;

            hitUnits.Add(health);
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
        Vector2 forceDir = (health.transform.position - transform.position).normalized;

        health.TakeDamage(damage, damageType, forceDir, knockbackForce);

        StatusEffectHandler status = health.GetComponent<StatusEffectHandler>();

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
            Instantiate(dustPrefab, transform.position, Quaternion.identity);

        Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, impactRadius, unitLayer);

        foreach (Collider2D hit in hits)
        {
            UnitHealth health = hit.GetComponent<UnitHealth>();
            if (health == null) continue;

            ApplyDamage(health);
        }

        if (ownerPool != null)
        {
            ownerPool.Despawn(this);
            return;
        }

        Destroy(gameObject);
    }
}