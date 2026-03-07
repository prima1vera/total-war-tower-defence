using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class TowerSelectionInput : MonoBehaviour
{
    [SerializeField] private Camera worldCamera;
    [SerializeField] private TowerSelectionService selectionService;
    [SerializeField] private LayerMask towerLayerMask;
    [SerializeField] private bool clearSelectionOnMiss = true;
    [SerializeField] private bool ignorePointerOverUi = true;

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
        if (hit != null)
        {
            TowerUpgradable tower = hit.GetComponentInParent<TowerUpgradable>();
            if (tower != null && tower.gameObject.activeInHierarchy && !tower.IsSold)
            {
                selectionService.Select(tower);
                return;
            }
        }

        if (clearSelectionOnMiss)
            selectionService.ClearSelection();
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
