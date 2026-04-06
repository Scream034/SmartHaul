using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace SmartHaul;

/// <summary>
/// Minimal IL helper for Harmony transpilers.
/// Only contains methods actually used in HarmonyPatches.
/// </summary>
public static class FishTranspiler
{
    /// <summary>Loads 'this' (ldarg.0).</summary>
    public static CodeInstruction This => new(OpCodes.Ldarg_0);

    /// <summary>Creates a call to a delegate method.</summary>
    public static CodeInstruction Call<T>(T method) where T : Delegate
    {
        var mi = method.Method;
        return new CodeInstruction(mi.IsStatic ? OpCodes.Call : OpCodes.Callvirt, mi);
    }

    /// <summary>Creates a call to a property getter.</summary>
    public static CodeInstruction CallPropertyGetter(Type type, string name)
    {
        var method = AccessTools.PropertyGetter(type, name)
            ?? throw new ArgumentException($"Property getter not found: {type.FullName}.{name}");
        return new CodeInstruction(method.IsStatic ? OpCodes.Call : OpCodes.Callvirt, method);
    }

    /// <summary>Loads a named method argument.</summary>
    public static CodeInstruction Argument(MethodBase method, string name)
    {
        var parameters = method.GetParameters();
        var offset = method.IsStatic ? 0 : 1;

        for (var i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].Name == name)
            {
                var index = i + offset;
                return index switch
                {
                    0 => new CodeInstruction(OpCodes.Ldarg_0),
                    1 => new CodeInstruction(OpCodes.Ldarg_1),
                    2 => new CodeInstruction(OpCodes.Ldarg_2),
                    3 => new CodeInstruction(OpCodes.Ldarg_3),
                    < 256 => new CodeInstruction(OpCodes.Ldarg_S, (byte)index),
                    _ => new CodeInstruction(OpCodes.Ldarg, index)
                };
            }
        }

        throw new ArgumentException($"Parameter '{name}' not found in {method.Name}");
    }
}