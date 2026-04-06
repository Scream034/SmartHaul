using System.Diagnostics;
using System.Linq;

namespace SmartHaul;

/// <summary>
/// Centralized debug logging with configurable levels.
/// All methods are no-ops in Release builds via [Conditional("DEBUG")].
/// Levels: 0=Off, 1=Error, 2=Warn, 3=Info, 4=Verbose, 5=Trace
/// </summary>
public static class Log
{
    public static int TraceLevel { get; set; }

    [Conditional("DEBUG")]
    public static void Error(string msg) { if (TraceLevel >= 1) Verse.Log.Error($"[SmartHaul] {msg}"); }

    [Conditional("DEBUG")]
    public static void Warning(string msg) { if (TraceLevel >= 2) Verse.Log.Warning($"[SmartHaul] {msg}"); }

    [Conditional("DEBUG")]
    public static void Info(string msg) { if (TraceLevel >= 3) Verse.Log.Message($"[SmartHaul] {msg}"); }

    [Conditional("DEBUG")]
    public static void Verbose(string msg) { if (TraceLevel >= 4) Verse.Log.Message($"[SmartHaul:V] {msg}"); }

    [Conditional("DEBUG")]
    public static void Trace(string msg) { if (TraceLevel >= 5) Verse.Log.Message($"[SmartHaul:T] {msg}"); }

    public static string ThingInfo(Thing? t) =>
        t == null ? "null" : $"{t.LabelShort}({t.thingIDNumber})";

    public static string PawnInfo(Pawn? p) =>
        p == null ? "null" : $"{p.LabelShort}@{p.Position}";

    public static string RouteInfo(List<Thing>? route) =>
        route == null || route.Count == 0
            ? "empty"
            : string.Join(" -> ", route.Select((t, i) => $"[{i}]{t.LabelShort}"));
}