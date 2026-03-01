using System.Collections;
using UnityEngine;
using Random = UnityEngine.Random;

public class UnitHealth : MonoBehaviour
{
    public int maxHealth = 1;
    private int currentHealth;

    public UnitState CurrentState { get; private set; } = UnitState.Moving;

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
        currentHealth = maxHealth;
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
        if (CurrentState == UnitState.Dead) return;

        currentHealth -= dmg;

        if (movement != null)
        {
            movement.ApplyKnockback(hitDirection, knockbackForce);
        }

        if (currentHealth <= 0)
        {
            Die();
        }
    }

    public void TakePureDamage(int dmg)
    {
        if (CurrentState == UnitState.Dead) return;

        currentHealth -= dmg;

        if (currentHealth <= 0)
            Die();
    }

    public void SetState(UnitState newState)
    {
        if (CurrentState == UnitState.Dead) return;

        CurrentState = newState;
    }
    
    void Die()
    {
        EnemyRegistry.Unregister(this);
        CurrentState = UnitState.Dead;

        if (animator != null)
        {
            int randomDeath = Random.Range(0, 4);
            animator.SetInteger("deathIndex", randomDeath);
            animator.SetBool("isDead", true);
        }

        if (bloodSplashPrefab != null)
        {
            VfxPool.Instance.Spawn(bloodSplashPrefab, transform.position, Quaternion.identity);
        }

        if (topDownSorter != null)
            topDownSorter.enabled = false;

        Vector3 colBounds = (col != null) ? col.bounds.min : transform.position;

        if (col != null)
            col.enabled = false;

        int deadOrder = Mathf.RoundToInt(-colBounds.y * 100f) + Random.Range(-1, 2);

        if (spriteRenderer != null)
        {
            spriteRenderer.sortingLayerName = "Units_Dead";
            spriteRenderer.sortingOrder = deadOrder;
        }

        StartCoroutine(DespawnToPoolAfterDelay(deadOrder, colBounds));

    }

    private IEnumerator DespawnToPoolAfterDelay(int deadOrder,Vector3 colBloodPos)
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
        {
            gameObject.SetActive(false);
        }
    }
}
