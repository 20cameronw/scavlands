using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public class InventoryGridViewUITK : MonoBehaviour
{
    [Header("UI Toolkit")]
    [SerializeField] private UIDocument uiDocument;
    [SerializeField] private string gridElementName = "InventoryGrid";

    [Header("Grid Config")]
    public int cols = 10;
    public int rows = 12;
    public float cellWidth = 64f;
    public float cellHeight = 64f;
    public float spacing = 2f;

    [Header("Visuals")]
    public bool drawCells = true;

    // Model + Service
    private InventoryGrid _grid;
    private InventoryService _service;

    // UI
    private VisualElement _gridRoot;                 // grid surface
    private VisualElement _ghost;                    // snap preview
    private VisualElement _dragProxy;                // follows cursor while dragging (scaled)
    private readonly Dictionary<Guid, ItemViewUITK> _views = new();

    // Drag state
    internal ItemInstance CurrentDraggingItem { get; private set; }
    internal GridPosition? FallbackReturnPos { get; private set; }
    private ItemViewUITK _draggingView;
    private bool _suppressRemoveForDrag = false;

    private const float DRAG_SCALE = 0.88f;          // <— smaller while dragging

    void Awake()
    {
        if (uiDocument == null) uiDocument = GetComponent<UIDocument>();
        if (uiDocument == null) { Debug.LogError("InventoryGridViewUITK: UIDocument missing."); enabled = false; return; }

        var root = uiDocument.rootVisualElement;
        _gridRoot = root.Q<VisualElement>(gridElementName);
        if (_gridRoot == null)
        {
            Debug.LogError($"InventoryGridViewUITK: Could not find '{gridElementName}' in UIDocument.");
            enabled = false; return;
        }

        // Container must be pickable & host absolute children
        _gridRoot.focusable = true;
        _gridRoot.pickingMode = PickingMode.Position;
        _gridRoot.style.position = Position.Relative;

        // Model + service
        if (cols < 1 || rows < 1) { cols = 10; rows = 12; }
        _grid = new InventoryGrid(cols, rows);
        _service = new InventoryService(_grid);

        // Ghost (snap preview) — lives in grid
        _ghost = new VisualElement();
        _ghost.AddToClassList("ghost");
        _ghost.style.display = DisplayStyle.None;
        _ghost.style.position = Position.Absolute;
        _ghost.pickingMode = PickingMode.Ignore;
        _gridRoot.Add(_ghost);

        // Drag proxy — lives at panel root so it’s never clipped and always under the cursor
        _dragProxy = new VisualElement();
        _dragProxy.AddToClassList("drag-proxy");
        _dragProxy.style.display = DisplayStyle.None;
        _dragProxy.style.position = Position.Absolute;
        _dragProxy.pickingMode = PickingMode.Ignore;
        uiDocument.rootVisualElement.Add(_dragProxy);   // IMPORTANT: add to panel root

        BuildGridSurface();

        // Domain → UI bindings
        InventoryEvents.OnItemPlaced  += HandlePlaced;
        InventoryEvents.OnItemRemoved += HandleRemoved;
        InventoryEvents.OnItemMoved   += HandleMoved;
        InventoryEvents.OnItemRotated += HandleRotated;

        // Rotate on R while dragging (also resizes proxy)
        _gridRoot.RegisterCallback<KeyDownEvent>(OnKeyDown);
    }

    private void OnDestroy()
    {
        InventoryEvents.OnItemPlaced  -= HandlePlaced;
        InventoryEvents.OnItemRemoved -= HandleRemoved;
        InventoryEvents.OnItemMoved   -= HandleMoved;
        InventoryEvents.OnItemRotated -= HandleRotated;

        if (_gridRoot != null) _gridRoot.UnregisterCallback<KeyDownEvent>(OnKeyDown);
    }

    // --- Public API ---
    public InventoryService Service => _service;
    public InventoryGrid Grid => _grid;
    public VisualElement GridRoot => _gridRoot;

    public bool TryAddNewItem(ItemInstance item)
    {
        if (_service.TryFindFirstFit(item, out var pos))
            return _service.TryPlace(item, pos);
        return false;
    }

    // --- Drag lifecycle (called by manipulator) ---
    internal void BeginDrag(ItemInstance item)
    {
        CurrentDraggingItem = item;

        // Bring real view to front + mark as dragging; hide it (proxy will represent it)
        if (_views.TryGetValue(item.runtimeId, out var viewForDrag))
        {
            _draggingView = viewForDrag;
            viewForDrag.Root.BringToFront();
            viewForDrag.Root.AddToClassList("dragging");
            viewForDrag.Root.style.opacity = 0f;    // hide real item, keep capture
        }
        else
        {
            _draggingView = null;
        }

        // Remember origin before freeing occupancy
        if (_grid.Contains(item.runtimeId))
            FallbackReturnPos = _grid.GetTopLeft(item.runtimeId);
        else
            FallbackReturnPos = null;

        // Free the model, but keep the view alive
        _suppressRemoveForDrag = true;
        _service.TryRemove(item);

        // Show ghost + scaled proxy
        _gridRoot.Focus();
        _ghost.style.display = DisplayStyle.Flex;
        SetGhostSize(item);
        UpdateDragProxyVisual(item);      // sets size + icon at scaled size
        _dragProxy.style.display = DisplayStyle.Flex;
        _dragProxy.BringToFront();
    }

    internal void EndDrag(bool placedSomewhere)
    {
        _ghost.style.display = DisplayStyle.None;
        _ghost.RemoveFromClassList("ghost-valid");
        _ghost.RemoveFromClassList("ghost-invalid");

        // Hide proxy
        _dragProxy.style.display = DisplayStyle.None;
        _dragProxy.style.backgroundImage = new StyleBackground();

        // Stop suppressing removals
        _suppressRemoveForDrag = false;

        // If drop failed, restore
        if (!placedSomewhere && CurrentDraggingItem != null)
        {
            if (FallbackReturnPos.HasValue && _service.TryPlace(CurrentDraggingItem, FallbackReturnPos.Value))
            { /* restored */ }
            else if (_service.TryFindFirstFit(CurrentDraggingItem, out var pos))
            { _service.TryPlace(CurrentDraggingItem, pos); }
        }

        // Unhide the real view
        if (_draggingView != null)
        {
            _draggingView.Root.style.opacity = 1f;
            _draggingView.Root.RemoveFromClassList("dragging");
            _draggingView = null;
        }

        CurrentDraggingItem = null;
        FallbackReturnPos = null;
    }

    // Called every PointerMove while dragging
    internal void UpdateDragProxyPosition(Vector2 worldPos)
    {
        if (_dragProxy == null || _dragProxy.resolvedStyle.width <= 0f) return;

        // position proxy centered under cursor, at panel root space
        var panel = uiDocument.rootVisualElement;
        Vector2 local = panel.WorldToLocal(worldPos);

        float halfW = _dragProxy.resolvedStyle.width  * 0.5f;
        float halfH = _dragProxy.resolvedStyle.height * 0.5f;

        _dragProxy.style.left = local.x - halfW;
        _dragProxy.style.top  = local.y - halfH;
        _dragProxy.BringToFront();
    }

    // Resize/re-skin proxy from current item size/rotation
    private void UpdateDragProxyVisual(ItemInstance item)
    {
        var (w, h) = item.GetSize();
        float width  = (w * cellWidth  + (w - 1) * spacing) * DRAG_SCALE;
        float height = (h * cellHeight + (h - 1) * spacing) * DRAG_SCALE;

        _dragProxy.style.width  = width;
        _dragProxy.style.height = height;

        if (item.def != null && item.def.icon != null)
        {
            _dragProxy.style.backgroundImage = new StyleBackground(item.def.icon.texture);
        }
        else
        {
            _dragProxy.style.backgroundImage = new StyleBackground();
            _dragProxy.style.backgroundColor = new Color(0, 0, 0, 0.25f);
            _dragProxy.style.borderTopWidth = _dragProxy.style.borderLeftWidth =
                _dragProxy.style.borderRightWidth = _dragProxy.style.borderBottomWidth = 1;
            var c = new Color(0, 0, 0, 0.6f);
            _dragProxy.style.borderTopColor = _dragProxy.style.borderLeftColor =
                _dragProxy.style.borderRightColor = _dragProxy.style.borderBottomColor = c;
        }
    }

    // --- Build grid + cells ---
    private void BuildGridSurface()
    {
        float totalW = cols * cellWidth + (cols - 1) * spacing;
        float totalH = rows * cellHeight + (rows - 1) * spacing;

        _gridRoot.style.width = totalW;
        _gridRoot.style.height = totalH;

        if (!drawCells) return;

        // remove old cells only (keep items/proxy/ghost)
        for (int i = _gridRoot.childCount - 1; i >= 0; i--)
        {
            var child = _gridRoot[i];
            if (child.ClassListContains("cell"))
                child.RemoveFromHierarchy();
        }

        for (int y = 0; y < rows; y++)
        for (int x = 0; x < cols; x++)
        {
            var cell = new VisualElement();
            cell.AddToClassList("cell");
            cell.pickingMode = PickingMode.Ignore;
            cell.style.position = Position.Absolute;
            cell.style.left = x * (cellWidth + spacing);
            cell.style.top  = y * (cellHeight + spacing);
            cell.style.width  = cellWidth;
            cell.style.height = cellHeight;
            _gridRoot.Add(cell);
        }

        _ghost.BringToFront();
    }

    // --- Model → UI handlers ---
    private void HandlePlaced(ItemInstance item, GridPosition pos)
    {
        if (_views.TryGetValue(item.runtimeId, out var existing))
        {
            PositionItemView(existing, pos, item.GetSize());
            existing.RefreshGraphic(item);
            existing.Root.BringToFront();
            return;
        }

        var view = new ItemViewUITK(item, this);
        _views[item.runtimeId] = view;

        PositionItemView(view, pos, item.GetSize());
        _gridRoot.Add(view.Root);
        view.Root.BringToFront();
    }

    private void HandleRemoved(ItemInstance item)
    {
        if (_suppressRemoveForDrag && CurrentDraggingItem != null &&
            item.runtimeId == CurrentDraggingItem.runtimeId)
        {
            // keep the view alive during drag
            return;
        }

        if (_views.TryGetValue(item.runtimeId, out var view))
        {
            view.Root.RemoveFromHierarchy();
            _views.Remove(item.runtimeId);
        }
    }

    private void HandleMoved(ItemInstance item, GridPosition pos)
    {
        if (_views.TryGetValue(item.runtimeId, out var view))
        {
            PositionItemView(view, pos, item.GetSize());
            view.Root.BringToFront();
        }
    }

    private void HandleRotated(ItemInstance item)
    {
        if (_views.TryGetValue(item.runtimeId, out var view))
        {
            var pos = _grid.GetTopLeft(item.runtimeId);
            PositionItemView(view, pos, item.GetSize());
            view.RefreshGraphic(item);
            view.Root.BringToFront();
        }

        // keep proxy size in sync while dragging
        if (CurrentDraggingItem != null && item.runtimeId == CurrentDraggingItem.runtimeId)
        {
            UpdateDragProxyVisual(item);
        }
    }

    private void PositionItemView(ItemViewUITK view, GridPosition pos, (int w, int h) size)
    {
        var (w, h) = size;
        float left = pos.x * (cellWidth + spacing);
        float top  = pos.y * (cellHeight + spacing);
        float width  = w * cellWidth + (w - 1) * spacing;
        float height = h * cellHeight + (h - 1) * spacing;

        view.Root.style.position = Position.Absolute;
        view.Root.style.left = left;
        view.Root.style.top = top;
        view.Root.style.width = width;
        view.Root.style.height = height;
    }

    // --- Helpers for manipulator ---
    internal bool TryWorldToCell(Vector2 worldPos, out GridPosition cell)
    {
        var local = _gridRoot.WorldToLocal(worldPos);
        return TryLocalToCell(local, out cell);
    }

    internal bool TryLocalToCell(Vector2 local, out GridPosition cell)
    {
        cell = default;

        if (local.x < 0 || local.y < 0) return false;
        float stepX = cellWidth + spacing;
        float stepY = cellHeight + spacing;

        int x = Mathf.FloorToInt(local.x / stepX);
        int y = Mathf.FloorToInt(local.y / stepY);

        if (x < 0 || y < 0 || x >= cols || y >= rows) return false;

        cell = new GridPosition(x, y);
        return true;
    }

    internal void SetGhostSize(ItemInstance item)
    {
        var (w, h) = item.GetSize();
        float width  = w * cellWidth + (w - 1) * spacing;
        float height = h * cellHeight + (h - 1) * spacing;

        _ghost.style.width = width;
        _ghost.style.height = height;
    }

    internal void SetGhostPositionAtCell(GridPosition cell)
    {
        float left = cell.x * (cellWidth + spacing);
        float top  = cell.y * (cellHeight + spacing);

        _ghost.style.left = left;
        _ghost.style.top  = top;
    }

    internal void SetGhostValidity(bool valid)
    {
        _ghost.RemoveFromClassList("ghost-valid");
        _ghost.RemoveFromClassList("ghost-invalid");
        _ghost.AddToClassList(valid ? "ghost-valid" : "ghost-invalid");
        _ghost.BringToFront();
    }

    private void OnKeyDown(KeyDownEvent evt)
    {
        if (CurrentDraggingItem == null) return;

        if (evt.keyCode == KeyCode.R)
        {
            CurrentDraggingItem.rotated90 = !CurrentDraggingItem.rotated90;
            SetGhostSize(CurrentDraggingItem);
            UpdateDragProxyVisual(CurrentDraggingItem);   // keep proxy in sync
            evt.StopImmediatePropagation();
        }
    }
}
