namespace SmartHaul;

/// <summary>
/// Job definitions for SmartHaul.
/// </summary>
[DefOf]
public static class SmartHaulJobDefOf
{
    public static JobDef HaulToInventory = null!;
    public static JobDef UnloadYourHauledInventory = null!;

    static SmartHaulJobDefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(SmartHaulJobDefOf));
}