using System;
using System.Collections;
using UnityEngine;
using Random = UnityEngine.Random;

public class UnitHealth : MonoBehaviour
{
    public int maxHealth = 1;
    private int currentHealth;

    public UnitState CurrentState { get; private set; } = UnitState.Moving;
    public int CurrentHealth => currentHealth;
    public int MaxHealth => Mathf.Max(1, maxHealth);
    public float NormalizedHealth => Mathf.Clamp01((float)currentHealth / MaxHealth);
    public Collider2D CachedCollider => col;

    public event Action<DamageFeedbackEvent> DamageTaken;
    public event Action<UnitHealth> Died;

    public static event Action<DamageFeedbackEvent> GlobalDamageTaken;
    public static event Action<UnitHealth> GlobalUnitDied;

    private Animator animator;
    private Collider2D col;
    private UnitMovement movement;
    private SpriteRenderer spriteRenderer;
    private TopDownSorter topDownSorter;
    private EnemyPoolMember enemyPoolMember;
    private string defaultSortingLayerName;
    private int defaultSortingOrder;

    public StatusEffectHandler StatusEffectHandler { get; private set; }

    public GameObject bloodPoolPrefab;
    public GameObject bloodSplashPrefab;
    [SerializeField] private float despawnToPoolDelay = 2f;

    void Awake()
    {
        animator = GetComponent<Animator>();
        col = GetComponent<Collider2D>();
        movement = GetComponent<UnitMovement>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        topDownSorter = GetComponent<TopDownSorter>();
        enemyPoolMember = GetComponent<EnemyPoolMember>();
        StatusEffectHandler = GetComponent<StatusEffectHandler>();

        if (spriteRenderer != null)
        {
            defaultSortingLayerName = spriteRenderer.sortingLayerName;
            defaultSortingOrder = spriteRenderer.sortingOrder;
        }
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
    }

    private void ResetRuntimeState()
    {
        currentHealth = MaxHealth;
        CurrentState = UnitState.Moving;

        if (col != null)
            col.enabled = true;

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

    public void TakeDamage(int dmg, DamageType type, Vector2 hitDirection, float knockbackForce)
    {
        if (CurrentState == UnitState.Dead)
            return;

        if (dmg <= 0)
            return;

        currentHealth -= dmg;

        if (movement != null)
            movement.ApplyKnockback(hitDirection, knockbackForce);

        RaiseDamageTaken(dmg, type, DamageFeedbackKind.DirectHit);

        if (currentHealth <= 0)
            Die();
    }

    public void TakePureDamage(int dmg, DamageType type = DamageType.Normal, DamageFeedbackKind feedbackKind = DamageFeedbackKind.DotTick)
    {
        if (CurrentState == UnitState.Dead)
            return;

        if (dmg <= 0)
            return;

        currentHealth -= dmg;
        RaiseDamageTaken(dmg, type, feedbackKind);

        if (currentHealth <= 0)
            Die();
    }

    public void SetState(UnitState newState)
    {
        if (CurrentState == UnitState.Dead)
            return;

        CurrentState = newState;
    }

    void Die()
    {
        EnemyRegistry.Unregister(this);
        CurrentState = UnitState.Dead;

        Died?.Invoke(this);
        GlobalUnitDied?.Invoke(this);

        if (animator != null)
        {
            int randomDeath = Random.Range(0, 4);
            animator.SetInteger("deathIndex", randomDeath);
            animator.SetBool("isDead", true);
        }

        if (bloodSplashPrefab != null)
            VfxPool.Instance.Spawn(bloodSplashPrefab, transform.position, Quaternion.identity);

        if (topDownSorter != null)
            topDownSorter.enabled = false;

        Vector3 colBounds = col != null ? col.bounds.min : transform.position;

        if (col != null)
            col.enabled = false;

        int deadOrder = Mathf.RoundToInt(-colBounds.y * 100f) + Random.Range(-1, 2);

        StartCoroutine(DespawnToPoolAfterDelay(deadOrder, colBounds));
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

    private IEnumerator DespawnToPoolAfterDelay(int deadOrder, Vector3 colBloodPos)
    {
        yield return new WaitForSeconds(Mathf.Max(0f, despawnToPoolDelay));

        colBloodPos.z = 0f;

        EnemyDeathVisualManager.Instance.SpawnDeathVisuals(
            spriteRenderer != null ? spriteRenderer.sprite : null,
            spriteRenderer != null && spriteRenderer.flipX,
            transform.position,
            transform.localScale,
            deadOrder,
            bloodPoolPrefab,
            colBloodPos
        );

        if (enemyPoolMember != null && enemyPoolMember.TryDespawnToPool())
            yield break;

        if (gameObject.activeSelf)
            gameObject.SetActive(false);
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
