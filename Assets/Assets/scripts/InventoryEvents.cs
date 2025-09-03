using UnityEngine;
using System;


public static class InventoryEvents
{
    public static Action<ItemInstance, GridPosition> OnItemPlaced;
    public static Action<ItemInstance> OnItemRemoved;
    public static Action<ItemInstance, GridPosition> OnItemMoved;
    public static Action<ItemInstance> OnItemRotated;
}
