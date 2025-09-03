using System;

[Serializable]
public class ItemInstance
{
    public Guid runtimeId;          // unique per instance
    public ItemDefinition def;      // reference to the SO
    public int currentStack = 1;
    public bool rotated90;          // true = swap width/height
    public float durability = 1f;   // 0..1 (optional)

    public ItemInstance(ItemDefinition def)
    {
        this.runtimeId = Guid.NewGuid();
        this.def = def;
        this.currentStack = 1;
        this.rotated90 = false;
    }

    public (int w, int h) GetSize()
    {
        return rotated90 ? (def.height, def.width) : (def.width, def.height);
    }
}
