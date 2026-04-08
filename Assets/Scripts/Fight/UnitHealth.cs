using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

public class UnitHealth : MonoBehaviour
{
    [Header("Health")]
    public int maxHealth = 1;
    private int currentHealth;

    public UnitState CurrentState { get; private set; } = UnitState.Moving;
    public bool IsDead => CurrentState == UnitState.Dead;
    public int CurrentHealth => currentHealth;
    public int MaxHealth => Mathf.Max(1, maxHealth);
    public float NormalizedHealth => Mathf.Clamp01((float)currentHealth / MaxHealth);
    public Collider2D CachedCollider => col;

    public event Action<DamageFeedbackEvent> DamageTaken;
    public event Action<UnitHealth> Died;

    public static event Action<DamageFeedbackEvent> GlobalDamageTaken;
    public static event Action<UnitHealth> GlobalUnitDied;

    private Collider2D col;
    private UnitMovement movement;
    private ManagedUnitDeathLifecycle deathLifecycle;

    public StatusEffectHandler StatusEffectHandler { get; private set; }

    [Header("Death Visuals")]
    public GameObject bloodPoolPrefab;
    public GameObject bloodSplashPrefab;
    [SerializeField] private float despawnToPoolDelay = 2f;

    void Awake()
    {
        col = GetComponent<Collider2D>();
        movement = GetComponent<UnitMovement>();
        StatusEffectHandler = GetComponent<StatusEffectHandler>();

        Animator animator = GetComponent<Animator>();
        SpriteRenderer spriteRenderer = GetComponent<SpriteRenderer>();
        TopDownSorter topDownSorter = GetComponent<TopDownSorter>();
        UnitEffects effects = GetComponent<UnitEffects>();
        EnemyPoolMember enemyPoolMember = GetComponent<EnemyPoolMember>();

        deathLifecycle = new ManagedUnitDeathLifecycle(new ManagedUnitDeathLifecycle.Config
        {
            Owner = this,
            Animator = animator,
            Collider = col,
            SpriteRenderer = spriteRenderer,
            TopDownSorter = topDownSorter,
            Effects = effects,
            ResolveDespawnDelay = GetDespawnDelay,
            ResolveBloodPoolPrefab = () => bloodPoolPrefab,
            ResolveBloodSplashPrefab = () => bloodSplashPrefab,
            TryDespawnToPool = () => enemyPoolMember != null && enemyPoolMember.TryDespawnToPool(),
            DeactivateFallback = () =>
            {
                if (gameObject.activeSelf)
                    gameObject.SetActive(false);
            },
            RandomizeDeathIndex = true,
            DeathIndexVariants = 4
        });
    }

    void OnEnable()
    {
        ResetRuntimeState();
        EnemyRegistry.Register(this);
    }

    void OnDisable()
    {
        EnemyRegistry.Unregister(this);
        UnitHealthLookupCache.Remove(col);
        deathLifecycle.CancelPendingDespawn();
    }

    private void ResetRuntimeState()
    {
        currentHealth = MaxHealth;
        CurrentState = UnitState.Moving;

        if (col != null)
            col.enabled = true;

        deathLifecycle.ResetForSpawn();
    }

    public void TakeDamage(int dmg, DamageType type, Vector2 hitDirection, float knockbackForce)
    {
        if (!CanApplyDamage(dmg))
            return;

        currentHealth -= dmg;

        if (movement != null && knockbackForce > 0.001f)
            movement.ApplyKnockback(hitDirection, knockbackForce);

        RaiseDamageTaken(dmg, type, DamageFeedbackKind.DirectHit);
        TryDie();
    }

    public void TakePureDamage(int dmg, DamageType type = DamageType.Normal, DamageFeedbackKind feedbackKind = DamageFeedbackKind.DotTick)
    {
        if (!CanApplyDamage(dmg))
            return;

        currentHealth -= dmg;
        RaiseDamageTaken(dmg, type, feedbackKind);

        TryDie();
    }

    public void SetState(UnitState newState)
    {
        if (IsDead)
            return;

        CurrentState = newState;
    }

    private bool CanApplyDamage(int damageAmount)
    {
        if (IsDead)
            return false;

        return damageAmount > 0;
    }

    private void TryDie()
    {
        if (IsDead || currentHealth > 0)
            return;

        EnemyRegistry.Unregister(this);
        CurrentState = UnitState.Dead;

        Died?.Invoke(this);
        GlobalUnitDied?.Invoke(this);

        deathLifecycle.HandleDeath();
    }

    private void RaiseDamageTaken(int amount, DamageType type, DamageFeedbackKind feedbackKind)
    {
        int clampedHealth = Mathf.Max(0, currentHealth);

        DamageFeedbackEvent damageEvent = new DamageFeedbackEvent(
            this,
            amount,
            clampedHealth,
            MaxHealth,
            type,
            feedbackKind,
            clampedHealth <= 0
        );

        DamageTaken?.Invoke(damageEvent);
        GlobalDamageTaken?.Invoke(damageEvent);
    }

    internal float GetDespawnDelay() => Mathf.Max(0f, despawnToPoolDelay);
}

internal sealed class ManagedUnitDeathLifecycle
{
    private static readonly int IsDeadHash = Animator.StringToHash("isDead");
    private static readonly int DeathIndexHash = Animator.StringToHash("deathIndex");

    internal sealed class Config
    {
        public MonoBehaviour Owner;
        public Animator Animator;
        public Collider2D Collider;
        public SpriteRenderer SpriteRenderer;
        public TopDownSorter TopDownSorter;
        public UnitEffects Effects;
        public Func<float> ResolveDespawnDelay;
        public Func<GameObject> ResolveBloodPoolPrefab;
        public Func<GameObject> ResolveBloodSplashPrefab;
        public Func<bool> TryDespawnToPool;
        public Action DeactivateFallback;
        public Action NotifyLifecycleFinished;
        public bool RandomizeDeathIndex;
        public int DeathIndexVariants;
    }

    private readonly Config config;
    private readonly bool hasIsDeadBoolParameter;
    private readonly bool hasDeathIndexIntParameter;
    private string defaultSortingLayerName;
    private int defaultSortingOrder;
    private Coroutine despawnRoutine;

    public ManagedUnitDeathLifecycle(Config config)
    {
        this.config = config;
        hasIsDeadBoolParameter = HasAnimatorParameter(config.Animator, IsDeadHash, AnimatorControllerParameterType.Bool);
        hasDeathIndexIntParameter = HasAnimatorParameter(config.Animator, DeathIndexHash, AnimatorControllerParameterType.Int);

        if (config.SpriteRenderer != null)
        {
            defaultSortingLayerName = config.SpriteRenderer.sortingLayerName;
            defaultSortingOrder = config.SpriteRenderer.sortingOrder;
        }
    }

    public void ResetForSpawn()
    {
        CancelPendingDespawn();

        if (config.TopDownSorter != null)
            config.TopDownSorter.enabled = true;

        if (config.SpriteRenderer != null)
        {
            config.SpriteRenderer.sortingLayerName = defaultSortingLayerName;
            config.SpriteRenderer.sortingOrder = defaultSortingOrder;
        }

        if (config.Animator != null && hasIsDeadBoolParameter)
            config.Animator.SetBool(IsDeadHash, false);
    }

    public void HandleDeath()
    {
        if (config.Animator != null)
        {
            if (config.RandomizeDeathIndex && hasDeathIndexIntParameter)
            {
                int variants = Mathf.Max(1, config.DeathIndexVariants);
                int randomDeath = Random.Range(0, variants);
                config.Animator.SetInteger(DeathIndexHash, randomDeath);
            }

            if (hasIsDeadBoolParameter)
                config.Animator.SetBool(IsDeadHash, true);
        }

        GameObject bloodSplashPrefab = config.ResolveBloodSplashPrefab != null ? config.ResolveBloodSplashPrefab() : null;
        if (bloodSplashPrefab != null && VfxPool.TryGetInstance(out VfxPool vfxPool) && config.Owner != null)
            vfxPool.Spawn(bloodSplashPrefab, config.Owner.transform.position, Quaternion.identity);

        if (config.TopDownSorter != null)
            config.TopDownSorter.enabled = false;

        Vector3 ownerPosition = config.Owner != null ? config.Owner.transform.position : Vector3.zero;
        Vector3 bloodPosition = config.Collider != null ? config.Collider.bounds.min : ownerPosition;
        bloodPosition.z = 0f;

        if (config.Collider != null)
            config.Collider.enabled = false;

        int deadOrder = Mathf.RoundToInt(-bloodPosition.y * 100f) + Random.Range(-1, 2);

        CancelPendingDespawn();
        if (config.Owner != null && config.Owner.gameObject.activeInHierarchy)
            despawnRoutine = config.Owner.StartCoroutine(DespawnToPoolAfterDelay(deadOrder, bloodPosition));
    }

    public void CancelPendingDespawn()
    {
        if (despawnRoutine == null || config.Owner == null)
            return;

        config.Owner.StopCoroutine(despawnRoutine);
        despawnRoutine = null;
    }

    private IEnumerator DespawnToPoolAfterDelay(int deadOrder, Vector3 bloodPosition)
    {
        float delay = config.ResolveDespawnDelay != null ? Mathf.Max(0f, config.ResolveDespawnDelay()) : 0f;
        if (delay > 0f)
            yield return WaitForSecondsCache.Get(delay);

        if (config.Owner == null)
        {
            despawnRoutine = null;
            yield break;
        }

        ManagedDeathVisuals.TrySpawn(
            config.Owner.transform,
            config.SpriteRenderer,
            config.Effects,
            config.ResolveBloodPoolPrefab != null ? config.ResolveBloodPoolPrefab() : null,
            bloodPosition,
            deadOrder
        );

        despawnRoutine = null;

        if (config.TryDespawnToPool != null && config.TryDespawnToPool())
        {
            config.NotifyLifecycleFinished?.Invoke();
            yield break;
        }

        config.DeactivateFallback?.Invoke();
        config.NotifyLifecycleFinished?.Invoke();
    }

    private static bool HasAnimatorParameter(Animator animator, int hash, AnimatorControllerParameterType type)
    {
        if (animator == null)
            return false;

        AnimatorControllerParameter[] parameters = animator.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            AnimatorControllerParameter parameter = parameters[i];
            if (parameter.nameHash == hash && parameter.type == type)
                return true;
        }

        return false;
    }

    private static class WaitForSecondsCache
    {
        private static readonly Dictionary<float, WaitForSeconds> Cache = new Dictionary<float, WaitForSeconds>(8);

        public static WaitForSeconds Get(float seconds)
        {
            float roundedSeconds = Mathf.Round(seconds * 100f) * 0.01f;
            if (roundedSeconds <= 0f)
                roundedSeconds = 0.01f;

            if (Cache.TryGetValue(roundedSeconds, out WaitForSeconds cached))
                return cached;

            WaitForSeconds created = new WaitForSeconds(roundedSeconds);
            Cache[roundedSeconds] = created;
            return created;
        }
    }
}

internal static class ManagedDeathVisuals
{
    public static bool TrySpawn(
        Transform ownerTransform,
        SpriteRenderer corpseRenderer,
        UnitEffects effects,
        GameObject bloodPoolPrefab,
        Vector3 bloodPosition,
        int corpseSortingOrder)
    {
        if (ownerTransform == null)
            return false;

        if (!EnemyDeathVisualManager.TryGetInstance(out EnemyDeathVisualManager deathVisualManager))
            return false;

        Sprite corpseSprite = corpseRenderer != null ? corpseRenderer.sprite : null;
        if (corpseSprite == null && bloodPoolPrefab == null)
            return false;

        Color corpseTint = effects != null ? effects.GetCorpseTint() : Color.white;

        deathVisualManager.SpawnDeathVisuals(
            corpseSprite,
            corpseRenderer != null && corpseRenderer.flipX,
            corpseTint,
            ownerTransform.position,
            ownerTransform.localScale,
            corpseSortingOrder,
            bloodPoolPrefab,
            bloodPosition
        );

        return true;
    }
}

public enum DamageFeedbackKind
{
    DirectHit,
    DotTick,
    BurnTick
}

public readonly struct DamageFeedbackEvent
{
    public UnitHealth Target { get; }
    public int Amount { get; }
    public int CurrentHealth { get; }
    public int MaxHealth { get; }
    public DamageType DamageType { get; }
    public DamageFeedbackKind FeedbackKind { get; }
    public bool IsFatal { get; }
    public float NormalizedHealth => MaxHealth > 0 ? Mathf.Clamp01((float)CurrentHealth / MaxHealth) : 0f;

    public DamageFeedbackEvent(
        UnitHealth target,
        int amount,
        int currentHealth,
        int maxHealth,
        DamageType damageType,
        DamageFeedbackKind feedbackKind,
        bool isFatal)
    {
        Target = target;
        Amount = Mathf.Max(0, amount);
        CurrentHealth = Mathf.Max(0, currentHealth);
        MaxHealth = Mathf.Max(1, maxHealth);
        DamageType = damageType;
        FeedbackKind = feedbackKind;
        IsFatal = isFatal;
    }
}
