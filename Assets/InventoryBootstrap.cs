using UnityEngine;

public class InventoryBootstrap : MonoBehaviour
{
    public InventoryGridViewUITK gridView;
    public ItemDefinition[] defs;

    void Start()
    {
        if (gridView == null)
        {
            Debug.LogError("Bootstrap: gridView is not assigned.");
            return;
        }

        Debug.Log($"Bootstrap: grid {gridView.Grid.cols}x{gridView.Grid.rows}, defs: {(defs?.Length ?? 0)}");

        if (defs == null || defs.Length == 0)
        {
            Debug.LogWarning("Bootstrap: no ItemDefinition assets assigned.");
            return;
        }

        foreach (var d in defs)
        {
            if (d == null)
            {
                Debug.LogWarning("Bootstrap: encountered a null ItemDefinition.");
                continue;
            }

            var item = new ItemInstance(d);
            bool ok = gridView.TryAddNewItem(item);
            Debug.Log($"Bootstrap: add '{d.displayName}' ({d.width}x{d.height}) → {ok}");
        }
    }
}
