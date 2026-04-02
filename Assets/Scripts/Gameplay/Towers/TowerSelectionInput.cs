using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class TowerSelectionInput : MonoBehaviour
{
    private static readonly Collider2D[] OverlapBuffer = new Collider2D[24];

    [SerializeField] private Camera worldCamera;
    [SerializeField] private TowerSelectionService selectionService;
    [SerializeField] private LayerMask towerLayerMask;
    [SerializeField] private bool clearSelectionOnMiss = true;
    [SerializeField] private bool ignorePointerOverUi = true;

    private BarracksController armedRallyBarracks;

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

        if (armedRallyBarracks != null)
        {
            if (!armedRallyBarracks.gameObject.activeInHierarchy)
            {
                CancelBarracksRallyPlacement();
                return;
            }

            if (armedRallyBarracks.TrySetRallyPoint(world))
            {
                CancelBarracksRallyPlacement();
                if (selectionService != null)
                    selectionService.ClearSelection();
            }

            return;
        }

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

    public bool ArmBarracksRallyPlacement(BarracksController barracks)
    {
        if (barracks == null || !barracks.gameObject.activeInHierarchy)
            return false;

        if (armedRallyBarracks != null && armedRallyBarracks != barracks)
            armedRallyBarracks.SetRallyPlacementPreviewActive(false);

        armedRallyBarracks = barracks;
        armedRallyBarracks.SetRallyPlacementPreviewActive(true);
        return true;
    }

    public void CancelBarracksRallyPlacement()
    {
        if (armedRallyBarracks != null)
            armedRallyBarracks.SetRallyPlacementPreviewActive(false);

        armedRallyBarracks = null;
    }

    public bool IsBarracksRallyPlacementArmedFor(BarracksController barracks)
    {
        if (barracks == null)
            return false;

        return armedRallyBarracks == barracks;
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
