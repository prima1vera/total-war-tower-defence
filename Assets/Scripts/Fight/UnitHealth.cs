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
    private UnitDeathLifecycle deathLifecycle;

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

        deathLifecycle = new UnitDeathLifecycle(this);
        deathLifecycle.Initialize();
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

internal sealed class UnitDeathLifecycle
{
    private readonly UnitHealth owner;

    private Animator animator;
    private Collider2D collider;
    private SpriteRenderer spriteRenderer;
    private TopDownSorter topDownSorter;
    private EnemyPoolMember enemyPoolMember;

    private string defaultSortingLayerName;
    private int defaultSortingOrder;

    private Coroutine despawnRoutine;

    public UnitDeathLifecycle(UnitHealth owner)
    {
        this.owner = owner;
    }

    public void Initialize()
    {
        animator = owner.GetComponent<Animator>();
        collider = owner.GetComponent<Collider2D>();
        spriteRenderer = owner.GetComponent<SpriteRenderer>();
        topDownSorter = owner.GetComponent<TopDownSorter>();
        enemyPoolMember = owner.GetComponent<EnemyPoolMember>();

        if (spriteRenderer != null)
        {
            defaultSortingLayerName = spriteRenderer.sortingLayerName;
            defaultSortingOrder = spriteRenderer.sortingOrder;
        }
    }

    public void ResetForSpawn()
    {
        CancelPendingDespawn();

        if (topDownSorter != null)
            topDownSorter.enabled = true;

        if (spriteRenderer != null)
        {
            spriteRenderer.sortingLayerName = defaultSortingLayerName;
            spriteRenderer.sortingOrder = defaultSortingOrder;
        }

        if (animator != null)
            animator.SetBool("isDead", false);
    }

    public void HandleDeath()
    {
        if (animator != null)
        {
            int randomDeath = Random.Range(0, 4);
            animator.SetInteger("deathIndex", randomDeath);
            animator.SetBool("isDead", true);
        }

        if (owner.bloodSplashPrefab != null && VfxPool.TryGetInstance(out VfxPool vfxPool))
            vfxPool.Spawn(owner.bloodSplashPrefab, owner.transform.position, Quaternion.identity);

        if (topDownSorter != null)
            topDownSorter.enabled = false;

        Vector3 bloodPosition = collider != null ? collider.bounds.min : owner.transform.position;
        bloodPosition.z = 0f;

        if (collider != null)
            collider.enabled = false;

        int deadOrder = Mathf.RoundToInt(-bloodPosition.y * 100f) + Random.Range(-1, 2);

        CancelPendingDespawn();
        despawnRoutine = owner.StartCoroutine(DespawnToPoolAfterDelay(deadOrder, bloodPosition));
    }

    public void CancelPendingDespawn()
    {
        if (despawnRoutine == null)
            return;

        owner.StopCoroutine(despawnRoutine);
        despawnRoutine = null;
    }

    private IEnumerator DespawnToPoolAfterDelay(int deadOrder, Vector3 bloodPosition)
    {
        float delay = owner.GetDespawnDelay();
        if (delay > 0f)
            yield return WaitForSecondsCache.Get(delay);

        if (EnemyDeathVisualManager.TryGetInstance(out EnemyDeathVisualManager deathVisualManager))
        {
            deathVisualManager.SpawnDeathVisuals(
                spriteRenderer != null ? spriteRenderer.sprite : null,
                spriteRenderer != null && spriteRenderer.flipX,
                owner.transform.position,
                owner.transform.localScale,
                deadOrder,
                owner.bloodPoolPrefab,
                bloodPosition
            );
        }

        if (enemyPoolMember != null && enemyPoolMember.TryDespawnToPool())
        {
            despawnRoutine = null;
            yield break;
        }

        if (owner.gameObject.activeSelf)
            owner.gameObject.SetActive(false);

        despawnRoutine = null;
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
