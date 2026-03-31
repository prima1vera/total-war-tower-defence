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

    [SerializeField, Min(0.01f)]
    [Tooltip("Radius used for direct-hit probing during flight. Smaller = less early hit feeling.")]
    private float directHitProbeRadius = 0.11f;

    [SerializeField]
    [Tooltip("Forward probe offset from arrow center toward tip. Positive values make contact happen closer to arrow tip.")]
    private float directHitProbeForwardOffset = 0.08f;

    [SerializeField, Range(-180f, 180f)]
    [Tooltip("Sprite forward angle correction in degrees. Use if arrow art is authored with different forward axis.")]
    private float modelForwardAngleOffset = 0f;

    [Header("Rendering")]
    [Tooltip("If enabled, arrow can switch to Units_Alive near low arc phase. Disable for strict Projectiles-through-flight behavior.")]
    [SerializeField] private bool useAdaptiveFlightSorting = false;
    [Tooltip("Normalized arc height threshold to move arrow to Projectiles layer while it is high in the air.")]
    [SerializeField, Range(0f, 1f)] private float highArcProjectilesThreshold = 0.42f;
    [Tooltip("Sorting order offset used while arrow is in flight.")]
    [SerializeField] private int flightSortingOrderOffset = 2;
    [Tooltip("Sorting order offset for embedded arrows attached to a living target.")]
    [SerializeField] private int embeddedOnTargetSortingOrderOffset = 1;
    [Tooltip("Sorting order offset for embedded arrows stuck on ground.")]
    [SerializeField] private int embeddedGroundSortingOrderOffset = 0;

    [Header("VFX (Impact)")]
    [Tooltip("Small dust / hit spark prefab spawned at impact point (via VfxPool).")]
    public GameObject dustPrefab;

    [Tooltip("Impact wave prefab spawned at impact point (via VfxPool).")]
    public GameObject impactWavePrefab;

    [Header("VFX (Ground Decal)")]
    public GameObject impactDecalPrefab;

    [Header("Ground Fire (Optional)")]
    [Tooltip("Spawn persistent burning ground zone after impact.")]
    [SerializeField] private bool spawnGroundFireZoneOnImpact;
    [Tooltip("Ground fire zone prefab (looped fire + burn ticks).")]
    [SerializeField] private GameObject groundFireZonePrefab;
    [Tooltip("Extra scale multiplier applied to spawned ground fire zone.")]
    [SerializeField, Min(0.1f)] private float groundFireZoneScaleMultiplier = 1f;

    [Header("Archer Impact (Optional)")]
    [Tooltip("Spawn blood splash particles attached to hit target on direct hit.")]
    [SerializeField] private bool spawnBloodOnDirectHit;
    [Tooltip("How long direct-hit blood particles stay attached to moving target.")]
    [SerializeField, Min(0f)] private float directHitBloodFollowDuration = 0.45f;
    [Tooltip("Switch particle systems to Local simulation while attached to target.")]
    [SerializeField] private bool directHitBloodUseLocalSimulation = true;

    [Tooltip("Spawn ground blood decal on direct hit (managed by EnemyDeathVisualManager cap).")]
    [SerializeField] private bool spawnGroundBloodOnDirectHit;
    [Tooltip("Ground blood decal prefab used for direct-hit blood marks.")]
    [SerializeField] private GameObject directHitGroundBloodPrefab;
    [Tooltip("Chance to spawn direct-hit ground blood when cadence condition passes.")]
    [SerializeField, Range(0f, 1f)] private float directHitGroundBloodChance = 0.45f;
    [Tooltip("Random position jitter for direct-hit ground blood spawn.")]
    [SerializeField, Min(0f)] private float directHitGroundBloodJitter = 0.08f;
    [Tooltip("Scale range for spawned direct-hit ground blood decals.")]
    [SerializeField] private Vector2 directHitGroundBloodScaleRange = new Vector2(0.35f, 0.7f);
    [Tooltip("Cadence limiter: ground blood appears every N direct hits.")]
    [SerializeField, Min(1)] private int directHitGroundBloodEveryMinHits = 5;
    [Tooltip("Cadence limiter upper bound: random N between Min/Max each cycle.")]
    [SerializeField, Min(1)] private int directHitGroundBloodEveryMaxHits = 10;

    [Tooltip("Allow embedded arrow spawn when direct hit happens.")]
    [SerializeField] private bool spawnEmbeddedArrowOnDirectHit;
    [Tooltip("Allow embedded arrow spawn when projectile impacts ground (miss / no direct hit).")]
    [SerializeField] private bool spawnEmbeddedArrowOnGroundImpact;
    [Tooltip("Attach embedded arrows to hit target transform (instead of leaving on ground).")]
    [SerializeField] private bool embedIntoTargetOnDirectHit = true;
    [Tooltip("Embedded arrow prefab used for stuck-arrow visuals.")]
    [SerializeField] private GameObject embeddedArrowPrefab;
    [Tooltip("Local offset applied at embedded arrow spawn point.")]
    [SerializeField] private Vector3 embeddedArrowLocalOffset;
    [Tooltip("Maximum embedded arrows kept on one target at the same time.")]
    [SerializeField, Min(1)] private int maxEmbeddedArrowsPerTarget = 7;
    [Tooltip("Maximum embedded arrows kept on whole battlefield. Oldest are recycled first.")]
    [SerializeField, Min(1)] private int maxEmbeddedArrowsOnScene = 120;
    [Tooltip("When max is reached, oldest embedded arrows are released so new arrows can stick.")]
    [SerializeField] private bool recycleOldestEmbeddedArrow = true;
    [Tooltip("Random spread radius applied to embedded arrow placement around hit point.")]
    [SerializeField, Min(0f)] private float embeddedArrowSpreadRadius = 0.12f;
    [Tooltip("Minimum spacing between embedded arrows on same target.")]
    [SerializeField, Min(0f)] private float embeddedArrowMinSpacing = 0.07f;
    [Tooltip("Placement attempts before fallback to unclamped base hit offset.")]
    [SerializeField, Min(1)] private int embeddedArrowPlacementAttempts = 7;
    [Tooltip("Max offset distance from target center for embedded arrow attachment.")]
    [SerializeField, Min(0.05f)] private float embeddedArrowMaxTargetOffset = 0.65f;

    [Header("Authoring")]
    [SerializeField] private bool strictAuthoring = true;

    private const float ArcPower = 1.35f;
    private const float TravelPower = 0.75f;
    private const float LookAhead = 0.015f;
    private const float SortingOrderYMultiplier = 100f;

    private const string SortingLayerUnitsAlive = "Units_Alive";
    private const string SortingLayerProjectiles = "Projectiles";
    private const string SortingLayerUnitsDead = "Units_Dead";


    private Vector2 startPos;
    private Vector2 targetPos;
    private float timer;
    private bool hasImpacted;

    private int pierceCount;
    private readonly HashSet<UnitHealth> hitUnits = new HashSet<UnitHealth>();

    private ArrowPool ownerPool;
    private Collider2D[] hitBuffer;
    private Transform cachedTransform;
    private SpriteRenderer cachedSpriteRenderer;
    private TrailRenderer cachedTrailRenderer;
    private Vector3 cachedBaseScale;

    private float shotScaleMultiplier = 1f;
    private float scaledImpactRadius = 1.5f;

    private float cachedDistance;
    private float cachedArcHeight;
    private float cachedTravelTime;
    private Vector2 lastVelocityDirection = Vector2.right;
    private UnitHealth lastDirectHitTarget;
    private Vector3 lastDirectHitPoint;
    private bool hasAppliedFlightSorting;
    private int lastFlightSortingLayerId;
    private int lastFlightSortingOrder;
    private bool pendingTrailEmission;

    private static int directHitGroundBloodCounter;
    private static int nextDirectHitGroundBloodThreshold = -1;
    private static readonly Dictionary<int, EmbeddedTargetState> EmbeddedByTarget = new Dictionary<int, EmbeddedTargetState>(64);
    private static readonly Queue<GameObject> EmbeddedSceneQueue = new Queue<GameObject>(160);

    private void Awake()
    {
        cachedTransform = transform;
        cachedSpriteRenderer = GetComponent<SpriteRenderer>();
        cachedTrailRenderer = GetComponent<TrailRenderer>();
        cachedBaseScale = cachedTransform.localScale;
        scaledImpactRadius = impactRadius;

        int bufferSize = Mathf.Max(8, maxHitColliders);
        hitBuffer = new Collider2D[bufferSize];
    }

    private void OnEnable()
    {
        ResetTrailForSpawn();
    }

    private void OnDisable()
    {
        ResetTrailForRelease();
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
        hasAppliedFlightSorting = false;

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

        ResetTrailForSpawn();
        ApplyFlightSorting(0f);
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
        EnableTrailAfterFirstStep();
        ApplyFlightSorting(t);

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

        Vector2 probeDirection = lastVelocityDirection.sqrMagnitude > 0.0001f
            ? lastVelocityDirection
            : (Vector2)cachedTransform.right;

        Vector2 probeCenter = (Vector2)cachedTransform.position + probeDirection * Mathf.Max(0f, directHitProbeForwardOffset);
        float probeRadius = Mathf.Max(0.01f, directHitProbeRadius);

        int hitCount = Physics2D.OverlapCircleNonAlloc(
            probeCenter,
            probeRadius,
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

        closest += approach * 0.01f;
        Vector3 targetCenter = health.transform.position;
        Vector2 delta = (Vector2)closest - (Vector2)targetCenter;
        float maxDistance = Mathf.Max(0.05f, embeddedArrowMaxTargetOffset);
        if (delta.sqrMagnitude > maxDistance * maxDistance)
            closest = (Vector2)targetCenter + delta.normalized * maxDistance;

        return new Vector3(closest.x, closest.y, 0f);
    }

    private void Explode(bool reachedPierceTarget)
    {
        if (hasImpacted)
            return;

        hasImpacted = true;
        ResetTrailForRelease();

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

            if (spawnGroundFireZoneOnImpact && groundFireZonePrefab != null)
            {
                Vector3 fireScale = vfxScale * Mathf.Max(0.1f, groundFireZoneScaleMultiplier);
                vfxPool.Spawn(groundFireZonePrefab, impactPosition, Quaternion.identity, fireScale);
            }
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

        if (spawnBloodOnDirectHit && hitTarget.bloodSplashPrefab != null && VfxPool.TryGetInstance(out VfxPool vfxPool))
        {
            Vector3 splashScale = Vector3.one * Random.Range(0.8f, 1.1f);
            GameObject splash = vfxPool.Spawn(hitTarget.bloodSplashPrefab, impactPosition, Quaternion.identity, splashScale);

            if (splash != null)
            {
                if (directHitBloodUseLocalSimulation)
                    SetParticleSimulationSpace(splash, ParticleSystemSimulationSpace.Local);

                Vector3 splashOffset = impactPosition - hitTarget.transform.position;
                AttachInstanceToTarget(splash, hitTarget, splashOffset, directHitBloodFollowDuration);
            }
        }

        bool canSpawnGroundBlood = spawnGroundBloodOnDirectHit
            && directHitGroundBloodPrefab != null
            && Random.value <= directHitGroundBloodChance
            && ShouldSpawnGroundBloodByCadence();

        if (!canSpawnGroundBlood)
            return;

        if (!EnemyDeathVisualManager.TryGetInstance(out EnemyDeathVisualManager deathVisualManager))
        {
            if (strictAuthoring)
                Debug.LogError($"{name}: EnemyDeathVisualManager is missing. Ground blood requires scene-wired EnemyDeathManager.", this);

            return;
        }

        Vector2 randomOffset = Random.insideUnitCircle * Mathf.Max(0f, directHitGroundBloodJitter);
        Vector3 groundPosition = new Vector3(impactPosition.x + randomOffset.x, impactPosition.y + randomOffset.y - 0.03f, 0f);

        float minScale = Mathf.Max(0.05f, Mathf.Min(directHitGroundBloodScaleRange.x, directHitGroundBloodScaleRange.y));
        float maxScale = Mathf.Max(minScale, Mathf.Max(directHitGroundBloodScaleRange.x, directHitGroundBloodScaleRange.y));
        float randomScale = Random.Range(minScale, maxScale);

        bool spawned = deathVisualManager.TrySpawnManagedGroundBlood(directHitGroundBloodPrefab, groundPosition, randomScale);
        if (!spawned && strictAuthoring)
            Debug.LogError($"{name}: Failed to spawn managed direct-hit blood decal.", this);
    }

    private bool ShouldSpawnGroundBloodByCadence()
    {
        int minHits = Mathf.Max(1, Mathf.Min(directHitGroundBloodEveryMinHits, directHitGroundBloodEveryMaxHits));
        int maxHits = Mathf.Max(minHits, Mathf.Max(directHitGroundBloodEveryMinHits, directHitGroundBloodEveryMaxHits));

        if (nextDirectHitGroundBloodThreshold <= 0)
            nextDirectHitGroundBloodThreshold = Random.Range(minHits, maxHits + 1);

        directHitGroundBloodCounter++;
        if (directHitGroundBloodCounter < nextDirectHitGroundBloodThreshold)
            return false;

        directHitGroundBloodCounter = 0;
        nextDirectHitGroundBloodThreshold = Random.Range(minHits, maxHits + 1);
        return true;
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
        Vector3 prefabScale = embeddedArrowPrefab.transform.localScale;
        GameObject embedded = vfxPool.Spawn(embeddedArrowPrefab, spawnPosition, rotation, prefabScale);
        if (embedded == null)
            return;

        DisableTimedAutoReturn(embedded);
        EnableFollowTarget(embedded);

        UnitHealth embedTarget = hitTarget;

        bool attachedToTarget = false;
        if (embedIntoTargetOnDirectHit && embedTarget != null)
        {
            attachedToTarget = TryAttachEmbeddedArrowToTarget(embedded, embedTarget, spawnPosition);
            if (!attachedToTarget)
            {
                ReleaseEmbeddedArrowInstance(embedded);
                return;
            }
        }

        ConfigureEmbeddedArrowRendering(embedded, embedTarget, spawnPosition, attachedToTarget);
        RegisterEmbeddedArrowOnScene(embedded, Mathf.Max(1, maxEmbeddedArrowsOnScene));
    }

    private bool TryAttachEmbeddedArrowToTarget(GameObject embedded, UnitHealth target, Vector3 impactPosition)
    {
        if (embedded == null || target == null)
            return false;

        if (!TryReserveEmbeddedOffset(target, impactPosition, out Vector3 worldOffset))
            return false;

        if (!AttachInstanceToTarget(
                embedded,
                target,
                worldOffset,
                0f,
                releaseOnTargetLost: true,
                useFollowerAuthoringDefaults: true))
            return false;

        int targetId = target.GetInstanceID();
        EmbeddedTargetState state = GetOrCreateEmbeddedState(targetId);
        CleanupEmbeddedState(state);
        state.Records.Add(new EmbeddedArrowRecord(embedded, worldOffset));

        return true;
    }

    private bool TryReserveEmbeddedOffset(UnitHealth target, Vector3 impactPosition, out Vector3 reservedWorldOffset)
    {
        reservedWorldOffset = Vector3.zero;
        if (target == null)
            return false;

        int targetId = target.GetInstanceID();
        EmbeddedTargetState state = GetOrCreateEmbeddedState(targetId);
        CleanupEmbeddedState(state);

        int maxPerTarget = Mathf.Max(1, maxEmbeddedArrowsPerTarget);
        if (state.Records.Count >= maxPerTarget)
        {
            if (!recycleOldestEmbeddedArrow)
                return false;

            while (state.Records.Count >= maxPerTarget)
            {
                if (!TryReleaseOldestEmbeddedArrow(state))
                    break;
            }

            if (state.Records.Count >= maxPerTarget)
                return false;
        }

        Vector3 targetPosition = target.transform.position;
        float maxAttachDistance = ResolveTargetAttachMaxDistance(target);
        Vector3 baseOffset = impactPosition - targetPosition;
        baseOffset.z = 0f;
        baseOffset = ClampEmbedOffset(baseOffset, maxAttachDistance);

        reservedWorldOffset = FindAvailableEmbeddedOffset(state, baseOffset, maxAttachDistance);
        return true;
    }

    private Vector3 FindAvailableEmbeddedOffset(EmbeddedTargetState state, Vector3 baseOffset, float maxAttachDistance)
    {
        float spread = Mathf.Max(0f, embeddedArrowSpreadRadius);
        int attempts = Mathf.Max(1, embeddedArrowPlacementAttempts);

        for (int i = 0; i < attempts; i++)
        {
            Vector3 candidate = baseOffset;
            if (spread > 0.0001f)
            {
                Vector2 jitter = Random.insideUnitCircle * spread;
                candidate.x += jitter.x;
                candidate.y += jitter.y;
            }

            candidate = ClampEmbedOffset(candidate, maxAttachDistance);
            if (IsOffsetAvailable(state, candidate))
                return candidate;
        }

        for (int i = 0; i < attempts; i++)
        {
            float angle = (state.Records.Count * 53f + i * 37f) * Mathf.Deg2Rad;
            float radius = spread * Mathf.Lerp(0.35f, 1f, (i + 1f) / attempts);
            Vector3 candidate = baseOffset + new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f);
            candidate = ClampEmbedOffset(candidate, maxAttachDistance);

            if (IsOffsetAvailable(state, candidate))
                return candidate;
        }

        return baseOffset;
    }

    private bool IsOffsetAvailable(EmbeddedTargetState state, Vector3 candidate)
    {
        float minSpacing = Mathf.Max(0f, embeddedArrowMinSpacing);
        if (minSpacing <= 0.0001f)
            return true;

        float minSpacingSq = minSpacing * minSpacing;
        for (int i = 0; i < state.Records.Count; i++)
        {
            Vector3 existing = state.Records[i].WorldOffset;
            existing.z = 0f;
            Vector3 probe = candidate;
            probe.z = 0f;

            if ((existing - probe).sqrMagnitude < minSpacingSq)
                return false;
        }

        return true;
    }

    private Vector3 ClampEmbedOffset(Vector3 offset, float maxDistance)
    {
        maxDistance = Mathf.Max(0.05f, maxDistance);
        Vector2 offset2D = new Vector2(offset.x, offset.y);
        float distanceSq = offset2D.sqrMagnitude;
        if (distanceSq <= maxDistance * maxDistance)
            return new Vector3(offset2D.x, offset2D.y, 0f);

        Vector2 clamped = offset2D.normalized * maxDistance;
        return new Vector3(clamped.x, clamped.y, 0f);
    }

    private float ResolveTargetAttachMaxDistance(UnitHealth target)
    {
        float authoringMax = Mathf.Max(0.05f, embeddedArrowMaxTargetOffset);
        if (target == null)
            return authoringMax;

        Collider2D targetCollider = target.CachedCollider;
        if (targetCollider == null)
            return authoringMax;

        Bounds bounds = targetCollider.bounds;
        float colliderRadius = Mathf.Max(0.05f, Mathf.Min(bounds.extents.x, bounds.extents.y) * 0.95f);
        return Mathf.Min(authoringMax, colliderRadius);
    }

    private static EmbeddedTargetState GetOrCreateEmbeddedState(int targetId)
    {
        if (EmbeddedByTarget.TryGetValue(targetId, out EmbeddedTargetState existing))
            return existing;

        EmbeddedTargetState created = new EmbeddedTargetState();
        EmbeddedByTarget[targetId] = created;
        return created;
    }

    private static void CleanupEmbeddedState(EmbeddedTargetState state)
    {
        if (state == null)
            return;

        for (int i = state.Records.Count - 1; i >= 0; i--)
        {
            GameObject instance = state.Records[i].Instance;
            if (instance != null && instance.activeInHierarchy)
                continue;

            state.Records.RemoveAt(i);
        }
    }
    private static bool TryReleaseOldestEmbeddedArrow(EmbeddedTargetState state)
    {
        if (state == null || state.Records.Count == 0)
            return false;

        EmbeddedArrowRecord oldest = state.Records[0];
        state.Records.RemoveAt(0);

        ReleaseEmbeddedArrowInstance(oldest.Instance);
        return true;
    }

    private static void RegisterEmbeddedArrowOnScene(GameObject instance, int cap)
    {
        if (instance == null)
            return;

        CompactEmbeddedSceneQueue();
        EmbeddedSceneQueue.Enqueue(instance);

        int safeCap = Mathf.Max(1, cap);
        while (EmbeddedSceneQueue.Count > safeCap)
        {
            GameObject oldest = EmbeddedSceneQueue.Dequeue();
            ReleaseEmbeddedArrowInstance(oldest);
            CompactEmbeddedSceneQueue();
        }
    }

    private static void CompactEmbeddedSceneQueue()
    {
        int count = EmbeddedSceneQueue.Count;
        if (count == 0)
            return;

        for (int i = 0; i < count; i++)
        {
            GameObject candidate = EmbeddedSceneQueue.Dequeue();
            if (candidate == null || !candidate.activeInHierarchy)
                continue;

            EmbeddedSceneQueue.Enqueue(candidate);
        }
    }

    private static void ReleaseEmbeddedArrowInstance(GameObject instance)
    {
        if (instance == null || !instance.activeInHierarchy)
            return;

        if (VfxPool.TryGetInstance(out VfxPool vfxPool))
            vfxPool.Release(instance);
        else
            Object.Destroy(instance);
    }

    private static void DisableTimedAutoReturn(GameObject instance)
    {
        if (instance == null)
            return;

        PooledTimedAutoReturn timedAutoReturn = instance.GetComponent<PooledTimedAutoReturn>();
        if (timedAutoReturn != null)
            timedAutoReturn.enabled = false;
    }

    private static void EnableFollowTarget(GameObject instance)
    {
        if (instance == null)
            return;

        PooledFollowTarget follower = instance.GetComponent<PooledFollowTarget>();
        if (follower != null && !follower.enabled)
            follower.enabled = true;
    }

    private bool AttachInstanceToTarget(
        GameObject instance,
        UnitHealth target,
        Vector3 worldOffset,
        float followDuration,
        bool releaseOnTargetLost = true,
        bool releaseOnUnitDeath = true,
        bool syncSortingWithTarget = false,
        int sortOrderOffset = 0,
        bool useFollowerAuthoringDefaults = false)
    {
        if (instance == null || target == null)
            return false;

        Transform targetTransform = target.transform;

        PooledFollowTarget follower = instance.GetComponent<PooledFollowTarget>();
        if (follower == null)
        {
            if (strictAuthoring)
                Debug.LogError($"{name}: {instance.name} is missing PooledFollowTarget. Wire it on prefab.", instance);

            return false;
        }

        if (useFollowerAuthoringDefaults)
        {
            follower.Attach(
                targetTransform,
                worldOffset,
                Mathf.Max(0f, followDuration),
                releaseOnTargetLost: releaseOnTargetLost,
                useLocalSpaceOffset: false);
        }
        else
        {
            follower.AttachWithOptions(
                targetTransform,
                worldOffset,
                Mathf.Max(0f, followDuration),
                releaseOnTargetLost: releaseOnTargetLost,
                useLocalSpaceOffset: false,
                releaseOnUnitDeath: releaseOnUnitDeath,
                syncSorting: syncSortingWithTarget,
                sortOffset: sortOrderOffset);
        }

        return true;
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

    private void ApplyFlightSorting(float t)
    {
        string sortingLayer = ResolveFlightSortingLayerName(t);
        int sortingOrder = ResolveSortingOrder(cachedTransform.position.y, flightSortingOrderOffset);

        int sortingLayerId = SortingLayer.NameToID(sortingLayer);
        if (hasAppliedFlightSorting
            && lastFlightSortingLayerId == sortingLayerId
            && lastFlightSortingOrder == sortingOrder)
            return;

        hasAppliedFlightSorting = true;
        lastFlightSortingLayerId = sortingLayerId;
        lastFlightSortingOrder = sortingOrder;

        ApplyRendererSorting(cachedSpriteRenderer, sortingLayer, sortingOrder);
        ApplyRendererSorting(cachedTrailRenderer, sortingLayer, sortingOrder);
    }

    private string ResolveFlightSortingLayerName(float t)
    {
        if (!useAdaptiveFlightSorting)
            return SortingLayerProjectiles;

        if (cachedArcHeight <= 0.0001f)
            return SortingLayerProjectiles;

        float normalizedHeight = Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI);
        return normalizedHeight >= highArcProjectilesThreshold
            ? SortingLayerProjectiles
            : SortingLayerUnitsAlive;
    }

    private void ConfigureEmbeddedArrowRendering(GameObject embedded, UnitHealth hitTarget, Vector3 impactPosition, bool attachedToTarget)
    {
        if (embedded == null)
            return;

        SpriteRenderer embeddedRenderer = embedded.GetComponent<SpriteRenderer>();
        if (embeddedRenderer == null)
            return;

        if (attachedToTarget && hitTarget != null && !hitTarget.IsDead)
        {
            SpriteRenderer targetRenderer = hitTarget.GetComponent<SpriteRenderer>();
            if (targetRenderer != null)
            {
                embeddedRenderer.sortingLayerID = targetRenderer.sortingLayerID;
                embeddedRenderer.sortingOrder = targetRenderer.sortingOrder + embeddedOnTargetSortingOrderOffset;
            }
            else
            {
                embeddedRenderer.sortingLayerName = SortingLayerUnitsAlive;
                embeddedRenderer.sortingOrder = ResolveSortingOrder(hitTarget.transform.position.y, embeddedOnTargetSortingOrderOffset);
            }

            return;
        }

        embeddedRenderer.sortingLayerName = SortingLayerUnitsDead;
        embeddedRenderer.sortingOrder = ResolveSortingOrder(impactPosition.y, embeddedGroundSortingOrderOffset);
    }

    private static void ApplyRendererSorting(Renderer renderer, string sortingLayerName, int sortingOrder)
    {
        if (renderer == null)
            return;

        renderer.sortingLayerName = sortingLayerName;
        renderer.sortingOrder = sortingOrder;
    }

    private static int ResolveSortingOrder(float worldY, int offset)
    {
        return Mathf.RoundToInt(-worldY * SortingOrderYMultiplier) + offset;
    }

    private void ResetTrailForSpawn()
    {
        if (cachedTrailRenderer == null)
            return;

        cachedTrailRenderer.Clear();
        cachedTrailRenderer.emitting = false;
        pendingTrailEmission = true;
    }

    private void ResetTrailForRelease()
    {
        if (cachedTrailRenderer == null)
            return;

        cachedTrailRenderer.emitting = false;
        cachedTrailRenderer.Clear();
        pendingTrailEmission = false;
    }

    private void EnableTrailAfterFirstStep()
    {
        if (!pendingTrailEmission || cachedTrailRenderer == null)
            return;

        cachedTrailRenderer.Clear();
        cachedTrailRenderer.emitting = true;
        pendingTrailEmission = false;
    }

    private float GetImpactVfxScaleMultiplier()
    {
        float weight = Mathf.Max(0f, impactVfxScaleWeight);
        return Mathf.Lerp(1f, shotScaleMultiplier, weight);
    }

    private sealed class EmbeddedTargetState
    {
        public readonly List<EmbeddedArrowRecord> Records = new List<EmbeddedArrowRecord>(8);
    }

    private readonly struct EmbeddedArrowRecord
    {
        public readonly GameObject Instance;
        public readonly Vector3 WorldOffset;

        public EmbeddedArrowRecord(GameObject instance, Vector3 worldOffset)
        {
            Instance = instance;
            WorldOffset = worldOffset;
        }
    }
}

















