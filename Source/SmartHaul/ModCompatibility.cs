using System.Linq;

namespace SmartHaul;

/// <summary>
/// Lazy-initialized mod compatibility flags.
/// </summary>
public static class ModCompatibility
{
    private static bool? _combatExtended;
    private static bool? _allowTool;
    private static bool? _extendedStorage;

    public static bool CombatExtendedIsActive =>
        _combatExtended ??= HasMod("Combat Extended");

    public static bool AllowToolIsActive =>
        _allowTool ??= HasMod("Allow Tool");

    public static bool ExtendedStorageIsActive =>
        _extendedStorage ??= HasMod("ExtendedStorageFluffyHarmonised")
                          || HasMod("Extended Storage")
                          || HasMod("Core SK");

    private static bool HasMod(string name) =>
        ModsConfig.ActiveModsInLoadOrder.Any(m => m.Name == name);
}