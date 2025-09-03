using UnityEngine;

[CreateAssetMenu(menuName = "Inventory/Item Definition")]
public class ItemDefinition : ScriptableObject
{
    [Header("Identity")]
    public string itemId;               // unique key (e.g., "medkit_a")
    public string displayName;

    [Header("Sizing (cells)")]
    [Min(1)] public int width = 1;
    [Min(1)] public int height = 1;

    [Header("Visuals")]
    public Sprite icon;

    [Header("Stacking & Rules")]
    public bool stackable;
    [Min(1)] public int maxStack = 1;

    [Tooltip("Optional tags (Weapon, Ammo, Med, Quest, etc.)")]
    public string[] tags;
}
