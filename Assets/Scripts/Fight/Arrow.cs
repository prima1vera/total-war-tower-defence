using UnityEngine;
using System.Collections.Generic;

public class Arrow : MonoBehaviour
{
    [Header("Combat")]
    [Tooltip("Direct hit damage per arrow.")]
    public int damage = 1;

    [Tooltip("Normal / Fire / Ice. Controls status effects.")]
    public DamageType damageType = DamageType.Normal;

    [Tooltip("Knockback force applied on hit.")]
    public float knockbackForce = 0.3f;

    [Tooltip("How many different enemies this arrow can pierce before exploding.")]
    public int maxPierce = 3;

    [Tooltip("AoE radius applied on impact (damage + wave visuals scale).")]
    public float impactRadius = 1.5f;

    [Tooltip("Which layers are considered enemies (for OverlapCircle checks).")]
    public LayerMask unitLayer;

    [SerializeField, Tooltip("Internal buffer size for Physics2D overlap queries. Increase only if you have very dense waves.")]
    private int maxHitColliders = 32;

    [Header("Flight (Feel)")]
    [SerializeField, Tooltip("Targets closer than this distance fly almost straight (little to no arc).")]
    private float minStraightDistance = 1.2f;

    [SerializeField, Tooltip("At this distance (and further) arrow reaches maximum arc height and maximum travel time.")]
    private float maxArcDistance = 7f;

    [SerializeField, Tooltip("Maximum arc height for far shots. Increase = more ballistic / cartoony. Decrease = flatter / more realistic.")]
    private float maxArcHeight = 1f;

    [SerializeField, Tooltip("Travel time for very close targets. Lower = snappier close shots.")]
    private float minTravelTime = 0.18f;

    [SerializeField, Tooltip("Travel time for very far targets. Higher = slower, more readable long shots.")]
    private float maxTravelTime = 0.6f;

    [SerializeField, Tooltip("When to start rotating the arrow towards its velocity.\n0 = rotate immediately, 0.1 = start rotating after 10% of flight.\nHelps close shots look less 'jerky'.")]
    private float rotationStartT = 0f;

    [Header("VFX (Impact)")]
    [Tooltip("Small dust / hit spark prefab spawned at impact point (via VfxPool).")]
    public GameObject dustPrefab;

    [Tooltip("Impact wave prefab spawned at impact point (via VfxPool).")]
    public GameObject impactWavePrefab;

    [SerializeField, Tooltip("How long the impact wave animation should play (passed into ImpactWaveVfx.Configure).")]
    private float waveDuration = 0.22f;

    // --- Non-serialized tuning constants (keeps inspector clean) ---
    // Arc grows non-linearly with distance; >1 means arc grows slower at first then ramps up.
    private const float ArcPower = 1.35f;

    // Travel time scaling curve; <1 makes mid distances slightly faster.
    private const float TravelPower = 0.75f;

    // Look-ahead time for stable rotation (avoids jitter).
    private const float LookAhead = 0.015f;

    // Clamp angle for very close shots so arrow doesn't visually "shoot upward".
    private const float MaxCloseAngle = 12f;

    // Hit detection radius around arrow during flight (pierce hits).
    private const float FlightHitRadius = 0.3f;

    // --- Runtime state ---
    private Vector2 startPos;
    private Vector2 targetPos;
    private float timer;
    private bool hasImpacted;

    private int pierceCount;
    private readonly HashSet<UnitHealth> hitUnits = new HashSet<UnitHealth>();

    private ArrowPool ownerPool;
    private Collider2D[] hitBuffer;
    private Transform cachedTransform;

    // Cached per-shot values
    private float cachedDistance;
    private float cachedArcHeight;
    private float cachedTravelTime;

    private void Awake()
    {
        cachedTransform = transform;
        int bufferSize = Mathf.Max(8, maxHitColliders);
        hitBuffer = new Collider2D[bufferSize];
    }

    public void SetPool(ArrowPool pool) => ownerPool = pool;

    public void Launch(Vector2 target)
    {
        startPos = cachedTransform.position;
        targetPos = target;

        timer = 0f;
        hasImpacted = false;
        pierceCount = 0;
        hitUnits.Clear();

        Vector2 dir = (targetPos - startPos);
        if (dir.sqrMagnitude > 0.0001f)
        {
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            cachedTransform.rotation = Quaternion.AngleAxis(angle, Vector3.forward);
        }

        // Precompute distance-based feel (one-time per shot)
        cachedDistance = Vector2.Distance(startPos, targetPos);

        // 0..1 distance ratio (below minStraightDistance => 0, at maxArcDistance => 1)
        float dist01 = Mathf.InverseLerp(minStraightDistance, maxArcDistance, cachedDistance);
        dist01 = Mathf.Clamp01(dist01);

        // Arc amount: grows smoothly with distance
        float arc01 = Mathf.Pow(dist01, ArcPower);
        cachedArcHeight = maxArcHeight * arc01;

        // Travel time: close shots faster, long shots slower
        float time01 = Mathf.Pow(dist01, TravelPower);
        cachedTravelTime = Mathf.Lerp(minTravelTime, maxTravelTime, time01);
        cachedTravelTime = Mathf.Max(0.01f, cachedTravelTime);
    }

    private void Update()
    {
        if (hasImpacted)
            return;

        timer += Time.deltaTime;
        float t = timer / cachedTravelTime;

        if (t >= 1f)
        {
            Explode();
            return;
        }

        Vector2 currentPos = EvaluatePosition(t);
        cachedTransform.position = currentPos;

        UpdateRotation(t, currentPos);

        CheckUnits();
    }

    private void UpdateRotation(float t, Vector2 currentPos)
    {
        if (t < rotationStartT)
            return;

        float t2 = Mathf.Min(1f, (timer + LookAhead) / cachedTravelTime);
        Vector2 nextPos = EvaluatePosition(t2);

        Vector2 dir = nextPos - currentPos;
        if (dir.sqrMagnitude <= 0.00001f)
            return;

        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

        // On very close shots keep angle mostly horizontal
        if (cachedDistance <= minStraightDistance)
            angle = ClampCloseShotAngle(angle, MaxCloseAngle);

        cachedTransform.rotation = Quaternion.AngleAxis(angle, Vector3.forward);
    }

    private Vector2 EvaluatePosition(float t)
    {
        t = Mathf.Clamp01(t);

        // Base line (straight)
        Vector2 pos = Vector2.Lerp(startPos, targetPos, t);

        // Arc (0 if close, >0 if farther)
        if (cachedArcHeight > 0.0001f)
        {
            float height = Mathf.Sin(t * Mathf.PI) * cachedArcHeight;
            pos.y += height;
        }

        return pos;
    }

    private void CheckUnits()
    {
        if (hitBuffer == null || hitBuffer.Length == 0)
            return;

        int hitCount = Physics2D.OverlapCircleNonAlloc(
            cachedTransform.position,
            FlightHitRadius,
            hitBuffer,
            unitLayer
        );

        for (int i = 0; i < hitCount; i++)
        {
            Collider2D hit = hitBuffer[i];
            if (hit == null)
                continue;

            UnitHealth health = UnitHealthLookupCache.Resolve(hit);
            if (health == null)
                continue;

            // Prevent double hit on same target for this arrow
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

    private void ApplyDamage(UnitHealth health)
    {
        Vector2 forceDir = (health.transform.position - cachedTransform.position).normalized;
        health.TakeDamage(damage, damageType, forceDir, knockbackForce);

        StatusEffectHandler status = health.StatusEffectHandler;
        if (status == null)
            return;

        if (damageType == DamageType.Fire)
            status.ApplyBurn(3f, 1, 0.5f);

        if (damageType == DamageType.Ice)
            status.ApplyFreeze(2f, 0.4f);
    }

    private static float ClampCloseShotAngle(float angleDeg, float maxCloseAngleDeg)
    {
        // Normalize to [-180..180]
        angleDeg = Mathf.DeltaAngle(0f, angleDeg);

        // If pointing mostly right (between -90..+90) clamp around 0
        if (Mathf.Abs(angleDeg) <= 90f)
            return Mathf.Clamp(angleDeg, -maxCloseAngleDeg, maxCloseAngleDeg);

        // Otherwise pointing mostly left, clamp around 180/-180
        // Convert to a "delta from 180", clamp that delta, then convert back.
        float deltaFromLeft = Mathf.DeltaAngle(180f, angleDeg); // [-180..180] difference from 180
        deltaFromLeft = Mathf.Clamp(deltaFromLeft, -maxCloseAngleDeg, maxCloseAngleDeg);
        return 180f + deltaFromLeft;
    }

    private void Explode()
    {
        if (hasImpacted)
            return;

        hasImpacted = true;

        if (dustPrefab != null)
            VfxPool.Instance.Spawn(dustPrefab, cachedTransform.position, Quaternion.identity);

        if (impactWavePrefab != null)
        {
            GameObject wave = VfxPool.Instance.Spawn(impactWavePrefab, cachedTransform.position, Quaternion.identity);
            ImpactWaveVfx waveFx = wave != null ? wave.GetComponent<ImpactWaveVfx>() : null;
            if (waveFx != null)
                waveFx.Configure(impactRadius, waveDuration, 0.6f);
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

        int hitCount = Physics2D.OverlapCircleNonAlloc(
            cachedTransform.position,
            impactRadius,
            hitBuffer,
            unitLayer
        );

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