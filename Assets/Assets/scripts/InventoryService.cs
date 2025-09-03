// InventoryService.cs
using System;
using UnityEngine;


public class InventoryService
{
    private readonly InventoryGrid _grid;

    public InventoryService(InventoryGrid grid)
    {
        _grid = grid;
    }

    /// <summary>
    /// Attempts to place a not-yet-placed item at a given top-left cell.
    /// </summary>
    public bool TryPlace(ItemInstance item, GridPosition topLeft)
    {
        if (_grid.Contains(item.runtimeId)) return false; // already placed
        if (!_grid.CanPlace(item, topLeft)) return false;

        _grid.PlaceUnsafe(item, topLeft);
        InventoryEvents.OnItemPlaced?.Invoke(item, topLeft);
        return true;
    }

    /// <summary>
    /// Removes an already-placed item from the grid.
    /// </summary>
    public bool TryRemove(ItemInstance item)
    {
        if (!_grid.Contains(item.runtimeId)) return false;

        _grid.RemoveUnsafe(item);
        InventoryEvents.OnItemRemoved?.Invoke(item);
        return true;
    }

    /// <summary>
    /// Moves an already-placed item to a new position (same rotation).
    /// Reverts if the destination is invalid.
    /// </summary>
    public bool TryMove(ItemInstance item, GridPosition newTopLeft)
    {
        if (!_grid.Contains(item.runtimeId)) return false;

        var old = _grid.GetTopLeft(item.runtimeId);
        _grid.RemoveUnsafe(item);

        if (_grid.CanPlace(item, newTopLeft))
        {
            _grid.PlaceUnsafe(item, newTopLeft);
            InventoryEvents.OnItemMoved?.Invoke(item, newTopLeft);
            return true;
        }

        // Revert to original spot
        _grid.PlaceUnsafe(item, old);
        return false;
    }

    /// <summary>
    /// Toggles 90° rotation for an already-placed item.
    /// Tries to keep it in place; falls back to first-fit; reverts if no space.
    /// </summary>
    public bool TryRotate(ItemInstance item)
    {
        if (!_grid.Contains(item.runtimeId)) return false;

        var old = _grid.GetTopLeft(item.runtimeId);

        _grid.RemoveUnsafe(item);
        item.rotated90 = !item.rotated90;

        // Try same top-left after rotation
        if (_grid.CanPlace(item, old))
        {
            _grid.PlaceUnsafe(item, old);
            InventoryEvents.OnItemRotated?.Invoke(item);
            return true;
        }

        // Try anywhere that fits
        if (TryFindFirstFit(item, out var pos))
        {
            _grid.PlaceUnsafe(item, pos);
            InventoryEvents.OnItemRotated?.Invoke(item);
            InventoryEvents.OnItemMoved?.Invoke(item, pos);
            return true;
        }

        // Revert rotation & placement
        item.rotated90 = !item.rotated90;
        _grid.PlaceUnsafe(item, old);
        return false;
    }

    /// <summary>
    /// Finds the first free region that can host the item
    /// with its current rotation state.
    /// </summary>
    public bool TryFindFirstFit(ItemInstance item, out GridPosition pos)
    {
        var (w, h) = item.GetSize();

        for (int y = 0; y <= _grid.rows - h; y++)
        {
            for (int x = 0; x <= _grid.cols - w; x++)
            {
                var p = new GridPosition(x, y);
                if (_grid.CanPlace(item, p))
                {
                    pos = p;
                    return true;
                }
            }
        }

        pos = default;
        return false;
    }

    /// <summary>
    /// Convenience: find a fit trying both orientations; restores original rotation if both fail.
    /// </summary>
    public bool TryFindFitEitherRotation(ItemInstance item, out GridPosition pos, out bool rotatedApplied)
    {
        rotatedApplied = false;

        if (TryFindFirstFit(item, out pos))
            return true;

        // Try toggled rotation
        item.rotated90 = !item.rotated90;
        if (TryFindFirstFit(item, out pos))
        {
            rotatedApplied = true;
            return true;
        }

        // Restore rotation
        item.rotated90 = !item.rotated90;
        pos = default;
        return false;
    }

    /// <summary>
    /// Optional helper: auto-place a new item anywhere (tries both rotations).
    /// </summary>
    public bool TryAutoPlace(ItemInstance item)
    {
        if (_grid.Contains(item.runtimeId)) return false;

        if (TryFindFitEitherRotation(item, out var pos, out _))
            return TryPlace(item, pos);

        return false;
    }
}
