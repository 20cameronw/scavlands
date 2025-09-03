// Assets/Scripts/Inventory/Model/GridPosition.cs
using System;

[Serializable]
public struct GridPosition
{
    public int x;
    public int y;

    public GridPosition(int x, int y)
    {
        this.x = x;
        this.y = y;
    }

    public override string ToString() => $"({x},{y})";
}
