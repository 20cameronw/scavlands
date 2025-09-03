using UnityEngine;
using UnityEngine.UIElements;

public class DragItemManipulator : PointerManipulator
{
    private readonly ItemViewUITK _view;
    private readonly InventoryGridViewUITK _gridView;

    private bool _active;
    private int _pointerId;

    public DragItemManipulator(ItemViewUITK view, InventoryGridViewUITK gridView)
    {
        _view = view;
        _gridView = gridView;
        activators.Add(new ManipulatorActivationFilter { button = MouseButton.LeftMouse });
    }

    protected override void RegisterCallbacksOnTarget()
    {
        target.RegisterCallback<PointerDownEvent>(OnPointerDown);
        target.RegisterCallback<PointerMoveEvent>(OnPointerMove);
        target.RegisterCallback<PointerUpEvent>(OnPointerUp);
        target.RegisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
    }

    protected override void UnregisterCallbacksFromTarget()
    {
        target.UnregisterCallback<PointerDownEvent>(OnPointerDown);
        target.UnregisterCallback<PointerMoveEvent>(OnPointerMove);
        target.UnregisterCallback<PointerUpEvent>(OnPointerUp);
        target.UnregisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
    }

    private void OnPointerDown(PointerDownEvent evt)
    {
        if (!CanStartManipulation(evt)) return;

        target.BringToFront();
        _active = true;
        _pointerId = evt.pointerId;
        target.CapturePointer(_pointerId);

        _gridView.BeginDrag(_view.Item);

        // Initialize ghost + proxy
        if (_gridView.TryWorldToCell(evt.position, out var cell))
        {
            _gridView.SetGhostPositionAtCell(cell);
            bool valid = _gridView.Grid.CanPlace(_view.Item, cell);
            _gridView.SetGhostValidity(valid);
        }
        _gridView.UpdateDragProxyPosition(evt.position); // center proxy under cursor

        evt.StopImmediatePropagation();
    }

    private void OnPointerMove(PointerMoveEvent evt)
    {
        if (!_active || !target.HasPointerCapture(_pointerId)) return;

        // Move ghost (snap) + proxy (cursor follow)
        if (_gridView.TryWorldToCell(evt.position, out var cell))
        {
            _gridView.SetGhostPositionAtCell(cell);
            bool valid = _gridView.Grid.CanPlace(_view.Item, cell);
            _gridView.SetGhostValidity(valid);
        }
        else
        {
            _gridView.SetGhostValidity(false);
        }

        _gridView.UpdateDragProxyPosition(evt.position); // follow cursor
        evt.StopPropagation();
    }

    private void OnPointerUp(PointerUpEvent evt)
    {
        if (!_active || !target.HasPointerCapture(_pointerId)) return;
        if (!CanStopManipulation(evt)) return;

        target.ReleasePointer(_pointerId);
        _active = false;

        bool placed = false;
        if (_gridView.TryWorldToCell(evt.position, out var cell))
        {
            placed = _gridView.Service.TryPlace(_view.Item, cell);
        }

        _gridView.EndDrag(placed);
        evt.StopPropagation();
    }

    private void OnPointerCaptureOut(PointerCaptureOutEvent evt)
    {
        if (!_active) return;
        _active = false;
        _gridView.EndDrag(false);
    }
}
