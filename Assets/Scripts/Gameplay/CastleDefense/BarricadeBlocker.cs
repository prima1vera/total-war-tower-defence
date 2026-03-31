using System;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class BarricadeBlocker : MonoBehaviour, IEnemyPathBlocker
{
    [Header("Health")]
    [SerializeField, Min(1)] private int maxHealth = 150;

    [Header("Blocking")]
    [SerializeField, Min(0.1f)] private float blockRadius = 0.65f;
    [SerializeField, Tooltip("Used only when two blockers overlap at exactly same path projection.")]
    private int pathPriority = 100;
    [SerializeField] private Collider2D blockingCollider;
    [SerializeField] private bool disableColliderWhenDestroyed = true;

    [Header("Visual")]
    [SerializeField] private GameObject intactVisual;
    [SerializeField] private GameObject destroyedVisual;

    private int currentHealth;
    private bool initialized;
    private bool isDestroyed;

    public event Action<BarricadeBlocker> Destroyed;

    public bool IsBlocking => !isDestroyed && gameObject.activeInHierarchy;
    public Vector2 WorldPosition => blockingCollider != null ? blockingCollider.bounds.center : (Vector2)transform.position;
    public float BlockRadius => ResolveBlockRadius();
    public int PathPriority => pathPriority;
    public int CurrentHealth => currentHealth;
    public int MaxHealth => Mathf.Max(1, maxHealth);

    private void Awake()
    {
        if (blockingCollider == null)
            blockingCollider = GetComponent<Collider2D>();

        InitializeForSpawn();
    }

    private void OnEnable()
    {
        if (!initialized)
            InitializeForSpawn();

        EnemyPathBlockerRegistry.Register(this);
    }

    private void OnDisable()
    {
        EnemyPathBlockerRegistry.Unregister(this);
    }

    [ContextMenu("Reset Barricade State")]
    public void InitializeForSpawn()
    {
        initialized = true;
        isDestroyed = false;
        currentHealth = MaxHealth;

        if (intactVisual != null)
            intactVisual.SetActive(true);

        if (destroyedVisual != null)
            destroyedVisual.SetActive(false);

        if (blockingCollider != null && !blockingCollider.enabled)
            blockingCollider.enabled = true;
    }

    public void ReceiveBlockDamage(int amount, UnitHealth attacker)
    {
        if (!IsBlocking || amount <= 0)
            return;

        currentHealth -= amount;
        if (currentHealth > 0)
            return;

        currentHealth = 0;
        isDestroyed = true;

        if (intactVisual != null)
            intactVisual.SetActive(false);

        if (destroyedVisual != null)
            destroyedVisual.SetActive(true);

        if (disableColliderWhenDestroyed && blockingCollider != null)
            blockingCollider.enabled = false;

        Destroyed?.Invoke(this);
    }

    private float ResolveBlockRadius()
    {
        if (blockingCollider == null)
            return Mathf.Max(0.1f, blockRadius);

        Bounds bounds = blockingCollider.bounds;
        float colliderRadius = Mathf.Max(bounds.extents.x, bounds.extents.y);
        return Mathf.Max(0.1f, Mathf.Max(blockRadius, colliderRadius));
    }
}

