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

    public GameObject impactWavePrefab;
    [SerializeField] private float waveDuration = 0.22f;

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

    [Header("Flight feel")]
    [SerializeField] private float minStraightDistance = 1.2f;   // ближе этого — почти прямой
    [SerializeField] private float maxArcDistance = 7f;          // дальше этого — полный arcHeight
    [SerializeField] private float arcPower = 1.35f;             // кривая роста дуги (1 = линейно)

    [SerializeField] private float minTravelTime = 0.18f;        // ближний выстрел
    [SerializeField] private float maxTravelTime = 0.6f;         // дальний выстрел (твой текущий)
    [SerializeField] private float travelPower = 0.75f;          // кривая скорости (меньше = быстрее на ближних)

    [SerializeField] private float lookAhead = 0.015f;           // сек вперёд для стабильного угла
    [SerializeField] private float maxCloseAngle = 12f;          // чтобы на ближних не "задирать" стрелу

    private float cachedArc;
    private float cachedTravelTime;
    private float cachedDistance;

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

        cachedDistance = Vector2.Distance(startPos, targetPos);

        // 0..1 по дистанции (с учётом зоны "прямого выстрела")
        float dist01 = Mathf.InverseLerp(minStraightDistance, maxArcDistance, cachedDistance);
        dist01 = Mathf.Clamp01(dist01);

        // дуга: растёт плавно
        float arc01 = Mathf.Pow(dist01, arcPower);
        cachedArc = arcHeight * arc01;

        // время полёта: ближе быстрее
        float time01 = Mathf.Pow(dist01, travelPower);
        cachedTravelTime = Mathf.Lerp(minTravelTime, maxTravelTime, time01);

        // важно: используем cachedTravelTime вместо travelTime
    }

    public void SetPool(ArrowPool pool)
    {
        ownerPool = pool;
    }

    void Update()
    {
        if (hasImpacted) return;

        timer += Time.deltaTime;
        float t = timer / Mathf.Max(0.0001f, cachedTravelTime);

        if (t >= 1f)
        {
            Explode();
            return;
        }

        // позиция сейчас
        Vector2 currentPos = EvaluatePosition(t);

        // позиция чуть вперёд для вычисления угла (убирает дёргания)
        float t2 = Mathf.Min(1f, (timer + lookAhead) / Mathf.Max(0.0001f, cachedTravelTime));
        Vector2 nextPos = EvaluatePosition(t2);

        Vector2 dir = nextPos - currentPos;
        if (dir.sqrMagnitude > 0.00001f)
        {
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

            // на ближних дистанциях держим угол почти горизонтальным
            if (cachedDistance <= minStraightDistance)
                angle = Mathf.Clamp(angle, -maxCloseAngle, maxCloseAngle);

            cachedTransform.rotation = Quaternion.AngleAxis(angle, Vector3.forward);
        }

        cachedTransform.position = currentPos;

        CheckUnits();
    }

    private Vector2 EvaluatePosition(float t)
    {
        t = Mathf.Clamp01(t);

        // базовая линия
        Vector2 pos = Vector2.Lerp(startPos, targetPos, t);

        // дуга (0 на ближних, cachedArc на дальних)
        if (cachedArc > 0.0001f)
        {
            float height = Mathf.Sin(t * Mathf.PI) * cachedArc;
            pos.y += height;
        }

        return pos;
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

        if (impactWavePrefab != null)
        {
            GameObject wave = VfxPool.Instance.Spawn(impactWavePrefab, cachedTransform.position, Quaternion.identity);
            var waveFx = wave.GetComponent<ImpactWaveVfx>();
            if (waveFx != null)
                waveFx.Configure(impactRadius, waveDuration);
        }

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
