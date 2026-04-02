using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class TowerSelectionInput : MonoBehaviour
{
    private static readonly Collider2D[] OverlapBuffer = new Collider2D[24];
    private const float MinGroundClickDistanceSqr = 0.0001f;

    [SerializeField] private Camera worldCamera;
    [SerializeField] private TowerSelectionService selectionService;
    [SerializeField] private LayerMask towerLayerMask;
    [SerializeField] private bool clearSelectionOnMiss = true;
    [SerializeField] private bool ignorePointerOverUi = true;
    [SerializeField, Tooltip("When a barracks is selected, click empty ground to move defender rally point.")]
    private bool allowBarracksRallyOnGroundClick = true;

    private void Update()
    {
        if (!TryGetPointerDown(out Vector2 pointerPosition, out int pointerId))
            return;

        if (ignorePointerOverUi && IsPointerOverUi(pointerId))
            return;

        ProcessSelection(pointerPosition);
    }

    private void ProcessSelection(Vector2 pointerPosition)
    {
        if (selectionService == null || worldCamera == null)
            return;

        Vector3 world = worldCamera.ScreenToWorldPoint(new Vector3(pointerPosition.x, pointerPosition.y, Mathf.Abs(worldCamera.transform.position.z)));
        world.z = 0f;

        Collider2D hit = Physics2D.OverlapPoint(world, towerLayerMask);
        if (TryResolveTowerFromCollider(hit, out TowerUpgradable maskedTower))
        {
            selectionService.Select(maskedTower);
            return;
        }

        int overlapCount = Physics2D.OverlapPointNonAlloc(world, OverlapBuffer);
        bool hitBuildPlace = false;
        for (int i = 0; i < overlapCount; i++)
        {
            Collider2D candidate = OverlapBuffer[i];
            OverlapBuffer[i] = null;

            if (!hitBuildPlace && candidate != null && candidate.GetComponentInParent<BuildPlace>() != null)
                hitBuildPlace = true;

            if (!TryResolveTowerFromCollider(candidate, out TowerUpgradable tower))
                continue;

            selectionService.Select(tower);
            return;
        }

        if (!hitBuildPlace && allowBarracksRallyOnGroundClick && TrySetBarracksRally(world))
            return;

        if (clearSelectionOnMiss)
            selectionService.ClearSelection();
    }

    private static bool TryResolveTowerFromCollider(Collider2D collider, out TowerUpgradable tower)
    {
        tower = null;
        if (collider == null)
            return false;

        tower = collider.GetComponentInParent<TowerUpgradable>();
        if (tower == null)
            return false;

        if (!tower.gameObject.activeInHierarchy || tower.IsSold)
            return false;

        return true;
    }

    private bool TrySetBarracksRally(Vector3 worldPoint)
    {
        if (selectionService == null)
            return false;

        TowerUpgradable selected = selectionService.SelectedTower;
        if (selected == null || !selected.gameObject.activeInHierarchy || selected.IsSold)
            return false;

        if (!selected.TryGetComponent(out BarracksController barracks))
            return false;

        Vector2 delta = (Vector2)(worldPoint - selected.transform.position);
        if (delta.sqrMagnitude < MinGroundClickDistanceSqr)
            return false;

        worldPoint.z = selected.transform.position.z;
        return barracks.TrySetRallyPoint(worldPoint);
    }

    private bool IsPointerOverUi(int pointerId)
    {
        if (EventSystem.current == null)
            return false;

        if (pointerId >= 0)
            return EventSystem.current.IsPointerOverGameObject(pointerId);

        return EventSystem.current.IsPointerOverGameObject();
    }

    private bool TryGetPointerDown(out Vector2 pointerPosition, out int pointerId)
    {
#if ENABLE_INPUT_SYSTEM
        if (Touchscreen.current != null)
        {
            var touch = Touchscreen.current.primaryTouch;
            if (touch.press.wasPressedThisFrame)
            {
                pointerPosition = touch.position.ReadValue();
                pointerId = touch.touchId.ReadValue();
                return true;
            }
        }

        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            pointerPosition = Mouse.current.position.ReadValue();
            pointerId = -1;
            return true;
        }
#elif ENABLE_LEGACY_INPUT_MANAGER
        if (Input.touchCount > 0)
        {
            Touch touch = Input.GetTouch(0);
            if (touch.phase == TouchPhase.Began)
            {
                pointerPosition = touch.position;
                pointerId = touch.fingerId;
                return true;
            }
        }

        if (Input.GetMouseButtonDown(0))
        {
            pointerPosition = Input.mousePosition;
            pointerId = -1;
            return true;
        }
#endif

        pointerPosition = default;
        pointerId = -1;
        return false;
    }
}
