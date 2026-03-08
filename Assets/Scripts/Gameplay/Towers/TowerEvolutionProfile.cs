using UnityEngine;

[CreateAssetMenu(fileName = "TowerEvolutionProfile", menuName = "TWTD/Towers/Evolution Profile")]
public sealed class TowerEvolutionProfile : ScriptableObject
{
    [SerializeField, Tooltip("Optional name shown in upgrade panel after this evolution is applied.")]
    private string displayNameOverride;

    [SerializeField, Tooltip("Which scene projectile pool family should this tower use after evolution.")]
    private TowerProjectilePoolKey projectilePoolKey = TowerProjectilePoolKey.Base;

    [SerializeField, Tooltip("Projectile prefab fallback when pool is missing.")]
    private GameObject arrowPrefab;

    [SerializeField, Tooltip("Optional sprite override for evolved tower visuals.")]
    private Sprite towerSprite;

    [SerializeField, Tooltip("Optional animator controller override for evolved tower visuals.")]
    private RuntimeAnimatorController animatorController;

    public string DisplayNameOverride => displayNameOverride;
    public TowerProjectilePoolKey ProjectilePoolKey => projectilePoolKey;
    public GameObject ArrowPrefab => arrowPrefab;
    public Sprite TowerSprite => towerSprite;
    public RuntimeAnimatorController AnimatorController => animatorController;
}
