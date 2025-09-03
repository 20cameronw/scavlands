using System;
using System.Collections.Generic;
using UnityEngine;


[Serializable]
public class InventoryGrid
{
    public readonly int cols;
    public readonly int rows;

    // cellOwner[y, x] holds the runtimeId of the occupying item, or Guid.Empty if free.
    private Guid[,] cellOwner;

    // Track placed items and their top-left positions.
    private readonly Dictionary<Guid, (ItemInstance item, GridPosition topLeft)> items = new();

    public InventoryGrid(int cols, int rows)
    {
        this.cols = cols;
        this.rows = rows;
        cellOwner = new Guid[rows, cols]; // default: Guid.Empty
    }

    public bool IsInside(int x, int y) => x >= 0 && y >= 0 && x < cols && y < rows;

    public bool IsCellFree(int x, int y) => IsInside(x, y) && cellOwner[y, x] == Guid.Empty;

    public bool TryGetItemAtCell(int x, int y, out ItemInstance item)
    {
        item = null;
        if (!IsInside(x, y)) return false;
        var id = cellOwner[y, x];
        if (id == Guid.Empty) return false;
        item = items[id].item;
        return true;
    }

    public bool Contains(Guid runtimeId) => items.ContainsKey(runtimeId);

    public GridPosition GetTopLeft(Guid id) => items[id].topLeft;

    public IEnumerable<(ItemInstance item, GridPosition pos)> GetAllItems()
    {
        foreach (var kv in items) yield return kv.Value;
    }

    // ---- Core placement API used by a service/controller ----

    internal bool CanPlace(ItemInstance item, GridPosition topLeft)
    {
        var (w, h) = item.GetSize();

        // Bounds
        if (!IsInside(topLeft.x, topLeft.y)) return false;
        if (!IsInside(topLeft.x + w - 1, topLeft.y + h - 1)) return false;

        // Collision
        for (int dy = 0; dy < h; dy++)
            for (int dx = 0; dx < w; dx++)
                if (!IsCellFree(topLeft.x + dx, topLeft.y + dy))
                    return false;

        return true;
    }

    internal void PlaceUnsafe(ItemInstance item, GridPosition topLeft)
    {
        var (w, h) = item.GetSize();
        items[item.runtimeId] = (item, topLeft);

        for (int dy = 0; dy < h; dy++)
            for (int dx = 0; dx < w; dx++)
                cellOwner[topLeft.y + dy, topLeft.x + dx] = item.runtimeId;
    }

    internal void RemoveUnsafe(ItemInstance item)
    {
        if (!items.TryGetValue(item.runtimeId, out var data)) return;

        var (w, h) = item.GetSize();
        for (int dy = 0; dy < h; dy++)
            for (int dx = 0; dx < w; dx++)
                cellOwner[data.topLeft.y + dy, data.topLeft.x + dx] = Guid.Empty;

        items.Remove(item.runtimeId);
    }

    internal void MoveUnsafe(ItemInstance item, GridPosition newTopLeft)
    {
        RemoveUnsafe(item);
        PlaceUnsafe(item, newTopLeft);
    }

    public void Clear()
    {
        cellOwner = new Guid[rows, cols];
        items.Clear();
    }
}
