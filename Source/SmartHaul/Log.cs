using System.Diagnostics;

namespace SmartHaul;

/// <summary>
/// Debug logging - only in DEBUG builds.
/// </summary>
public static class Log
{
    [Conditional("DEBUG")]
    public static void Info(string msg) => Verse.Log.Message($"[SmartHaul] {msg}");

    [Conditional("DEBUG")]
    public static void Warning(string msg) => Verse.Log.Warning($"[SmartHaul] {msg}");

    [Conditional("DEBUG")]
    public static void Error(string msg) => Verse.Log.Error($"[SmartHaul] {msg}");
}