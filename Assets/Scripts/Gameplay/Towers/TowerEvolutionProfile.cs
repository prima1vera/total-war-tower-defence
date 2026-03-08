using UnityEngine;

[CreateAssetMenu(fileName = "TowerEvolutionProfile", menuName = "TWTD/Towers/Evolution Profile")]
public sealed class TowerEvolutionProfile : ScriptableObject
{
    [SerializeField, Tooltip("Optional name shown in upgrade panel after this evolution is applied.")]
    private string displayNameOverride;

    [SerializeField, Tooltip("Projectile prefab fallback used by Tower when no pool is assigned.")]
    private GameObject arrowPrefab;

    [SerializeField, Tooltip("Projectile pool used by evolved tower.")]
    private ArrowPool arrowPool;

    [SerializeField, Tooltip("Optional sprite override for evolved tower visuals.")]
    private Sprite towerSprite;

    [SerializeField, Tooltip("Optional animator controller override for evolved tower visuals.")]
    private RuntimeAnimatorController animatorController;

    public string DisplayNameOverride => displayNameOverride;
    public GameObject ArrowPrefab => arrowPrefab;
    public ArrowPool ArrowPool => arrowPool;
    public Sprite TowerSprite => towerSprite;
    public RuntimeAnimatorController AnimatorController => animatorController;
}
