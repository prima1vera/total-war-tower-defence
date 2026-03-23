using System;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(TowerUpgradable))]
public sealed class TowerPersistentId : MonoBehaviour
{
    [SerializeField, Tooltip("Stable unique ID used to bind this tower to saved progress. Must be unique in scene.")]
    private string persistentId;

    [SerializeField, Tooltip("TowerUpgradable on this same tower root.")]
    private TowerUpgradable tower;

    public string PersistentId => persistentId;
    public TowerUpgradable Tower => tower;
    public bool IsConfigured => tower != null && !string.IsNullOrWhiteSpace(persistentId);

    private void Reset()
    {
        EnsureTowerReference();
        EnsurePersistentId();
    }

    private void OnValidate()
    {
        EnsureTowerReference();
    }

    [ContextMenu("Generate New Persistent Id")]
    private void GenerateNewPersistentId()
    {
        persistentId = Guid.NewGuid().ToString("N");
    }

    private void EnsureTowerReference()
    {
        if (tower == null)
            tower = GetComponent<TowerUpgradable>();
    }

    private void EnsurePersistentId()
    {
        if (!string.IsNullOrWhiteSpace(persistentId))
            return;

        persistentId = Guid.NewGuid().ToString("N");
    }
}
