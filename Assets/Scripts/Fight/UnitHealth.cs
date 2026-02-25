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

    public StatusEffectHandler StatusEffectHandler { get; private set; }

    public GameObject bloodPoolPrefab;
    public GameObject bloodSplashPrefab;

    void Awake()
    {
        animator = GetComponent<Animator>();
        col = GetComponent<Collider2D>();
        movement = GetComponent<UnitMovement>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        topDownSorter = GetComponent<TopDownSorter>();
        StatusEffectHandler = GetComponent<StatusEffectHandler>();
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
            Vector3 bloodPos = col != null ? col.bounds.min : transform.position;
            bloodPos.z = 0f;

            GameObject blood = Instantiate(bloodPoolPrefab, bloodPos, Quaternion.identity);

            var bloodSR = blood.GetComponent<SpriteRenderer>();
            if (bloodSR != null)
            {
                bloodSR.sortingLayerName = "Units_Dead"; 
                bloodSR.sortingOrder = -1;           
            }

            float targetScale = UnityEngine.Random.Range(0.35f, 1.05f);

            StartCoroutine(AnimateBloodPoolAAA(blood.transform, bloodSR, targetScale));
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


    IEnumerator AnimateBloodPoolAAA(Transform blood, SpriteRenderer sr, float targetScale)
    {
        float startUniform = UnityEngine.Random.Range(0.05f, 0.15f);

        Vector3 startScale = new Vector3(startUniform, startUniform, 1f);
        Vector3 endScale = new Vector3(targetScale, targetScale, 1f);

        float duration = Mathf.Lerp(0.25f, 0.95f, Mathf.InverseLerp(0.35f, 1.05f, targetScale));
        duration *= UnityEngine.Random.Range(1.9f, 3.25f);

        float overshoot = UnityEngine.Random.Range(1.05f, 1.1f);
        Vector3 overScale = endScale * overshoot;

        float startAlpha = 0f;
        float endAlpha = UnityEngine.Random.Range(0.8f, 1f);

        if (sr != null)
        {
            var c = sr.color;
            c.a = startAlpha;
            sr.color = c;
        }

        blood.localScale = startScale;

        float t = 0f;

        float growPhase = duration * 0.75f;
        while (t < growPhase)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / growPhase);

            // smoothstep
            k = k * k * (3f - 2f * k);

            blood.localScale = Vector3.Lerp(startScale, overScale, k);

            if (sr != null)
            {
                var c = sr.color;
                c.a = Mathf.Lerp(startAlpha, endAlpha, k);
                sr.color = c;
            }

            yield return null;
        }

        float settleT = 0f;
        float settlePhase = duration * 0.25f;
        while (settleT < settlePhase)
        {
            settleT += Time.deltaTime;
            float k = Mathf.Clamp01(settleT / settlePhase);

            // ÷óòü áîëåå ìÿãêî
            k = Mathf.SmoothStep(0f, 1f, k);

            blood.localScale = Vector3.Lerp(overScale, endScale, k);
            yield return null;
        }

        blood.localScale = endScale;
        if (sr != null)
        {
            var c = sr.color;
            c.r *= 0.9f; c.g *= 0.85f; c.b *= 0.85f;
            sr.color = c;
        }
    }
}
