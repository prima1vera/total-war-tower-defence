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

    [SerializeField, Tooltip("Optional NE-facing sprite override for directional tower visuals (used when aiming up).")]
    private Sprite towerSpriteNorthEast;

    [SerializeField, Tooltip("Optional animator controller override for evolved tower visuals.")]
    private RuntimeAnimatorController animatorController;
    [SerializeField, Tooltip("Optional NE animator controller override for evolved tower visuals.")]
    private RuntimeAnimatorController animatorControllerNorthEast;
    [SerializeField, Tooltip("Optional NW animator controller override for evolved tower visuals.")]
    private RuntimeAnimatorController animatorControllerNorthWest;
    [SerializeField, Tooltip("Optional SW animator controller override for evolved tower visuals.")]
    private RuntimeAnimatorController animatorControllerSouthWest;

    public string DisplayNameOverride => displayNameOverride;
    public TowerProjectilePoolKey ProjectilePoolKey => projectilePoolKey;
    public GameObject ArrowPrefab => arrowPrefab;
    public Sprite TowerSprite => towerSprite;
    public Sprite TowerSpriteNorthEast => towerSpriteNorthEast;
    public RuntimeAnimatorController AnimatorController => animatorController;
    public RuntimeAnimatorController AnimatorControllerNorthEast => animatorControllerNorthEast;
    public RuntimeAnimatorController AnimatorControllerNorthWest => animatorControllerNorthWest;
    public RuntimeAnimatorController AnimatorControllerSouthWest => animatorControllerSouthWest;
}
