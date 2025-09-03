using UnityEngine;
using UnityEngine.UIElements;

public class ItemViewUITK
{
    public VisualElement Root { get; private set; }
    public ItemInstance Item { get; private set; }
    private InventoryGridViewUITK _gridView;

    public ItemViewUITK(ItemInstance item, InventoryGridViewUITK gridView)
    {
        Item = item;
        _gridView = gridView;

        Root = new VisualElement();
        Root.AddToClassList("item");
        Root.pickingMode = PickingMode.Position; // receive pointer input
        Root.style.position = Position.Absolute; // required for left/top sizing

        RefreshGraphic(item);

        // Make it draggable
        Root.AddManipulator(new DragItemManipulator(this, gridView));
    }

    public void RefreshGraphic(ItemInstance item)
    {
        if (item.def != null && item.def.icon != null)
        {
            Root.style.backgroundImage = new StyleBackground(item.def.icon.texture);
        }
        else
        {
            Root.style.backgroundImage = new StyleBackground();
            // visible fallback
            Root.style.backgroundColor = new Color(0, 0, 0, 0.18f);
            Root.style.borderLeftWidth = Root.style.borderTopWidth =
                Root.style.borderRightWidth = Root.style.borderBottomWidth = 1;
            var c = new Color(0, 0, 0, 0.6f);
            Root.style.borderLeftColor = Root.style.borderTopColor =
                Root.style.borderRightColor = Root.style.borderBottomColor = c;
        }
    }
}
