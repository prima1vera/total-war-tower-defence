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

    public GameObject bloodPoolPrefab;
    public GameObject bloodSplashPrefab;

    void Awake()
    {
        animator = GetComponent<Animator>();
        col = GetComponent<Collider2D>();
        movement = GetComponent<UnitMovement>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        topDownSorter = GetComponent<TopDownSorter>();
    }

    void OnEnable()
    {
        EnemyRegistry.Register(this);
    }

    void OnDisable()
    {
        EnemyRegistry.Unregister(this);
    }

    void Start()
    {
        currentHealth = maxHealth;
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

    void Die()
    {
        EnemyRegistry.Unregister(this);
        CurrentState = UnitState.Dead;

        if (animator != null)
        {
            int randomDeath = Random.Range(0, 5);
            animator.SetInteger("deathIndex", randomDeath);
            animator.SetBool("isDead", true);
        }

        if (bloodSplashPrefab != null)
        {
            Instantiate(bloodSplashPrefab, transform.position, Quaternion.identity);
        }

        if (bloodPoolPrefab != null && col != null)
        {
            Vector3 bloodPos = col.bounds.min;
            GameObject blood = Instantiate(bloodPoolPrefab, bloodPos, Quaternion.identity);

            float scale = Random.Range(0.3f, 1.0f);
            blood.transform.localScale = Vector3.one * scale;
        }

        if (spriteRenderer != null)
        {
            spriteRenderer.sortingLayerName = "Units_Dead";
            spriteRenderer.sortingOrder = 0;
        }

        if (topDownSorter != null)
            topDownSorter.enabled = false;

        if (col != null)
            col.enabled = false;
    }

    public void SetState(UnitState newState)
    {
        if (CurrentState == UnitState.Dead) return;

        CurrentState = newState;
    }
}
