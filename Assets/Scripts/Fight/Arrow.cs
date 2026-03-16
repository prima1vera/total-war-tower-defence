using System.Collections.Generic;
using UnityEngine;

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

    [Tooltip("AoE radius applied on impact.")]
    public float impactRadius = 1.5f;

    [SerializeField, Range(0f, 2f)]
    [Tooltip("How much tower level scale affects AoE radius. 1 = same as arrow scale.")]
    private float impactRadiusScaleWeight = 1f;

    [SerializeField, Range(0f, 2f)]
    [Tooltip("How much tower level scale affects impact VFX/decal size. 1 = same as arrow scale.")]
    private float impactVfxScaleWeight = 1f;

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

    [SerializeField, Range(-180f, 180f)]
    [Tooltip("Sprite forward angle correction in degrees. Use if arrow art is authored with different forward axis.")]
    private float modelForwardAngleOffset = 0f;

    [Header("VFX (Impact)")]
    [Tooltip("Small dust / hit spark prefab spawned at impact point (via VfxPool).")]
    public GameObject dustPrefab;

    [Tooltip("Impact wave prefab spawned at impact point (via VfxPool).")]
    public GameObject impactWavePrefab;

    [Header("VFX (Ground Decal)")]
    public GameObject impactDecalPrefab;

    [Header("Archer Impact (Optional)")]
    [SerializeField] private bool spawnBloodOnDirectHit;
    [SerializeField, Min(0f)] private float directHitBloodFollowDuration = 0.45f;
    [SerializeField] private bool directHitBloodUseLocalSimulation = true;

    [SerializeField] private bool spawnGroundBloodOnDirectHit;
    [SerializeField] private GameObject directHitGroundBloodPrefab;
    [SerializeField, Range(0f, 1f)] private float directHitGroundBloodChance = 0.45f;
    [SerializeField, Min(0f)] private float directHitGroundBloodJitter = 0.08f;
    [SerializeField] private Vector2 directHitGroundBloodScaleRange = new Vector2(0.35f, 0.7f);
    [SerializeField] private Vector2 directHitGroundBloodLifetimeRange = new Vector2(8f, 14f);

    [SerializeField] private Sprite[] directHitGroundBloodVariants;

    [SerializeField] private bool spawnEmbeddedArrowOnDirectHit;
    [SerializeField] private bool spawnEmbeddedArrowOnGroundImpact;
    [SerializeField] private bool embedIntoTargetOnDirectHit = true;
    [SerializeField] private GameObject embeddedArrowPrefab;
    [SerializeField, Min(0.05f)] private float embeddedArrowLifetime = 2.4f;
    [SerializeField] private Vector3 embeddedArrowScale = Vector3.one;
    [SerializeField] private Vector3 embeddedArrowLocalOffset;

    [Header("Authoring")]
    [SerializeField] private bool strictAuthoring = true;

    private const float ArcPower = 1.35f;
    private const float TravelPower = 0.75f;
    private const float LookAhead = 0.015f;
    private const float FlightHitRadius = 0.3f;

    private Vector2 startPos;
    private Vector2 targetPos;
    private float timer;
    private bool hasImpacted;

    private int pierceCount;
    private readonly HashSet<UnitHealth> hitUnits = new HashSet<UnitHealth>();

    private ArrowPool ownerPool;
    private Collider2D[] hitBuffer;
    private Transform cachedTransform;
    private Vector3 cachedBaseScale;

    private float shotScaleMultiplier = 1f;
    private float scaledImpactRadius = 1.5f;

    private float cachedDistance;
    private float cachedArcHeight;
    private float cachedTravelTime;
    private Vector2 lastVelocityDirection = Vector2.right;
    private UnitHealth lastDirectHitTarget;
    private Vector3 lastDirectHitPoint;

    private void Awake()
    {
        cachedTransform = transform;
        cachedBaseScale = cachedTransform.localScale;
        scaledImpactRadius = impactRadius;

        int bufferSize = Mathf.Max(8, maxHitColliders);
        hitBuffer = new Collider2D[bufferSize];
    }

    public void SetPool(ArrowPool pool) => ownerPool = pool;

    public void SetVisualScale(float scaleMultiplier)
    {
        float safeScale = Mathf.Max(0.1f, scaleMultiplier);
        shotScaleMultiplier = safeScale;

        cachedTransform.localScale = cachedBaseScale * safeScale;

        float radiusScale = Mathf.Lerp(1f, safeScale, Mathf.Max(0f, impactRadiusScaleWeight));
        scaledImpactRadius = Mathf.Max(0f, impactRadius) * radiusScale;
    }

    public void Launch(Vector2 target)
    {
        startPos = cachedTransform.position;
        targetPos = target;

        timer = 0f;
        hasImpacted = false;
        pierceCount = 0;
        hitUnits.Clear();
        lastDirectHitTarget = null;
        lastDirectHitPoint = cachedTransform.position;

        Vector2 dir = targetPos - startPos;
        if (dir.sqrMagnitude <= 0.0001f)
            dir = cachedTransform.right;

        lastVelocityDirection = dir.normalized;

        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg + modelForwardAngleOffset;
        cachedTransform.rotation = Quaternion.AngleAxis(angle, Vector3.forward);

        cachedDistance = Vector2.Distance(startPos, targetPos);

        float dist01 = Mathf.InverseLerp(minStraightDistance, maxArcDistance, cachedDistance);
        dist01 = Mathf.Clamp01(dist01);

        float arc01 = Mathf.Pow(dist01, ArcPower);
        cachedArcHeight = maxArcHeight * arc01;

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
            Explode(false);
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

        lastVelocityDirection = dir.normalized;

        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg + modelForwardAngleOffset;
        cachedTransform.rotation = Quaternion.AngleAxis(angle, Vector3.forward);
    }

    private Vector2 EvaluatePosition(float t)
    {
        t = Mathf.Clamp01(t);

        Vector2 pos = Vector2.Lerp(startPos, targetPos, t);

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
            unitLayer);

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
            lastDirectHitTarget = health;
            lastDirectHitPoint = ResolveDirectHitPoint(health);

            if (pierceCount >= maxPierce)
            {
                Explode(true);
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

    private Vector3 ResolveDirectHitPoint(UnitHealth health)
    {
        if (health == null)
            return cachedTransform.position;

        Collider2D targetCollider = health.CachedCollider;
        if (targetCollider == null)
            return health.transform.position;

        Vector2 approach = lastVelocityDirection.sqrMagnitude > 0.0001f ? lastVelocityDirection : Vector2.right;

        Vector2 probe = (Vector2)cachedTransform.position - approach * 0.25f;
        Vector2 closest = targetCollider.ClosestPoint(probe);

        if ((closest - probe).sqrMagnitude <= 0.00001f)
        {
            Vector2 alternateProbe = (Vector2)cachedTransform.position - approach * 0.55f;
            closest = targetCollider.ClosestPoint(alternateProbe);
        }

        closest += approach * 0.02f;
        return new Vector3(closest.x, closest.y, 0f);
    }

    private void Explode(bool reachedPierceTarget)
    {
        if (hasImpacted)
            return;

        hasImpacted = true;

        Vector3 impactPosition = reachedPierceTarget ? lastDirectHitPoint : cachedTransform.position;
        UnitHealth hitTarget = reachedPierceTarget ? lastDirectHitTarget : null;

        Vector3 vfxScale = Vector3.one * GetImpactVfxScaleMultiplier();

        if (VfxPool.TryGetInstance(out VfxPool vfxPool))
        {
            if (dustPrefab != null)
                vfxPool.Spawn(dustPrefab, impactPosition, Quaternion.identity, vfxScale);

            if (impactWavePrefab != null)
                vfxPool.Spawn(impactWavePrefab, impactPosition, Quaternion.identity, vfxScale);

            if (impactDecalPrefab != null)
                vfxPool.Spawn(impactDecalPrefab, impactPosition, Quaternion.identity, vfxScale);
        }

        HandleOptionalDirectHitPresentation(hitTarget, impactPosition);
        HandleOptionalEmbeddedArrow(hitTarget, impactPosition);

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
            scaledImpactRadius,
            hitBuffer,
            unitLayer);

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

    private void HandleOptionalDirectHitPresentation(UnitHealth hitTarget, Vector3 impactPosition)
    {
        if (hitTarget == null)
            return;

        if (!spawnBloodOnDirectHit && !spawnGroundBloodOnDirectHit)
            return;

        if (!VfxPool.TryGetInstance(out VfxPool vfxPool))
            return;

        if (spawnBloodOnDirectHit && hitTarget.bloodSplashPrefab != null)
        {
            Vector3 splashScale = Vector3.one * Random.Range(0.8f, 1.1f);
            GameObject splash = vfxPool.Spawn(hitTarget.bloodSplashPrefab, impactPosition, Quaternion.identity, splashScale);

            if (splash != null)
            {
                if (directHitBloodUseLocalSimulation)
                    SetParticleSimulationSpace(splash, ParticleSystemSimulationSpace.Local);

                AttachInstanceToTarget(splash, hitTarget, impactPosition, directHitBloodFollowDuration);
            }
        }

        if (spawnGroundBloodOnDirectHit && directHitGroundBloodPrefab != null && Random.value <= directHitGroundBloodChance)
        {
            Vector2 randomOffset = Random.insideUnitCircle * Mathf.Max(0f, directHitGroundBloodJitter);
            Vector3 groundPosition = new Vector3(impactPosition.x + randomOffset.x, impactPosition.y + randomOffset.y - 0.03f, 0f);

            float minScale = Mathf.Max(0.05f, Mathf.Min(directHitGroundBloodScaleRange.x, directHitGroundBloodScaleRange.y));
            float maxScale = Mathf.Max(minScale, Mathf.Max(directHitGroundBloodScaleRange.x, directHitGroundBloodScaleRange.y));
            float randomScale = Random.Range(minScale, maxScale);
            Vector3 groundScale = new Vector3(randomScale, randomScale, 1f);

            GameObject groundBlood = vfxPool.Spawn(directHitGroundBloodPrefab, groundPosition, Quaternion.identity, groundScale);
            if (groundBlood != null)
            {
                ApplyGroundBloodVariant(groundBlood);
                ArmGroundBloodLifetime(groundBlood);
            }
        }
    }

    private void HandleOptionalEmbeddedArrow(UnitHealth hitTarget, Vector3 impactPosition)
    {
        if (embeddedArrowPrefab == null)
            return;

        bool spawnOnDirectHit = hitTarget != null && spawnEmbeddedArrowOnDirectHit;
        bool spawnOnGroundImpact = hitTarget == null && spawnEmbeddedArrowOnGroundImpact;
        if (!spawnOnDirectHit && !spawnOnGroundImpact)
            return;

        if (!VfxPool.TryGetInstance(out VfxPool vfxPool))
            return;

        Quaternion rotation = Quaternion.AngleAxis(
            Mathf.Atan2(lastVelocityDirection.y, lastVelocityDirection.x) * Mathf.Rad2Deg + modelForwardAngleOffset,
            Vector3.forward);

        Vector3 spawnPosition = impactPosition + embeddedArrowLocalOffset;
        GameObject embedded = vfxPool.Spawn(embeddedArrowPrefab, spawnPosition, rotation, embeddedArrowScale);
        if (embedded == null)
            return;

        bool attachedToTarget = false;

        if (embedIntoTargetOnDirectHit && hitTarget != null)
        {
            AttachInstanceToTarget(embedded, hitTarget, spawnPosition, 0f);
            attachedToTarget = true;
        }

        PooledTimedAutoReturn timedAutoReturn = embedded.GetComponent<PooledTimedAutoReturn>();
        if (timedAutoReturn != null)
        {
            if (attachedToTarget)
            {
                timedAutoReturn.enabled = false;
            }
            else
            {
                timedAutoReturn.enabled = true;
                timedAutoReturn.Arm(embeddedArrowLifetime);
            }
        }
    }

    private void ApplyGroundBloodVariant(GameObject groundBlood)
    {
        if (groundBlood == null)
            return;

        SpriteRenderer renderer = groundBlood.GetComponent<SpriteRenderer>();
        if (renderer != null && directHitGroundBloodVariants != null && directHitGroundBloodVariants.Length > 0)
        {
            Sprite variant = directHitGroundBloodVariants[Random.Range(0, directHitGroundBloodVariants.Length)];
            if (variant != null)
                renderer.sprite = variant;
        }

        // Blood decals are authored as pre-rotated sprites, so keep world rotation fixed.
        groundBlood.transform.rotation = Quaternion.identity;
    }

    private void ArmGroundBloodLifetime(GameObject groundBlood)
    {
        if (groundBlood == null)
            return;

        float minLifetime = Mathf.Max(0.1f, Mathf.Min(directHitGroundBloodLifetimeRange.x, directHitGroundBloodLifetimeRange.y));
        float maxLifetime = Mathf.Max(minLifetime, Mathf.Max(directHitGroundBloodLifetimeRange.x, directHitGroundBloodLifetimeRange.y));
        float chosenLifetime = Random.Range(minLifetime, maxLifetime);

        PooledTimedAutoReturn timedAutoReturn = groundBlood.GetComponent<PooledTimedAutoReturn>();
        if (timedAutoReturn == null)
        {
            if (strictAuthoring)
                Debug.LogError($"{name}: {groundBlood.name} is missing PooledTimedAutoReturn. Wire it on prefab.", groundBlood);

            return;
        }

        timedAutoReturn.enabled = true;
        timedAutoReturn.Arm(chosenLifetime);
    }

    private void AttachInstanceToTarget(GameObject instance, UnitHealth target, Vector3 worldAnchorPosition, float followDuration)
    {
        if (instance == null || target == null)
            return;

        Transform targetTransform = target.transform;
        Vector3 offsetLocal = targetTransform.InverseTransformPoint(worldAnchorPosition);

        PooledFollowTarget follower = instance.GetComponent<PooledFollowTarget>();
        if (follower == null)
        {
            if (strictAuthoring)
                Debug.LogError($"{name}: {instance.name} is missing PooledFollowTarget. Wire it on prefab.", instance);

            return;
        }

        follower.Attach(targetTransform, offsetLocal, Mathf.Max(0f, followDuration), true);
    }

    private static void SetParticleSimulationSpace(GameObject effectObject, ParticleSystemSimulationSpace simulationSpace)
    {
        if (effectObject == null)
            return;

        ParticleSystem[] particleSystems = effectObject.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < particleSystems.Length; i++)
        {
            ParticleSystem ps = particleSystems[i];
            if (ps == null)
                continue;

            ParticleSystem.MainModule main = ps.main;
            main.simulationSpace = simulationSpace;
        }
    }

    private float GetImpactVfxScaleMultiplier()
    {
        float weight = Mathf.Max(0f, impactVfxScaleWeight);
        return Mathf.Lerp(1f, shotScaleMultiplier, weight);
    }
}

