global using System;
global using System.Collections.Generic;
global using UnityEngine;
global using Verse;
global using Verse.AI;
global using RimWorld;

namespace SmartHaul;

/// <summary>
/// Mod entry point.
/// </summary>
public sealed class SmartHaulMod : Mod
{
    public const string Version = "1.3.0";

    public static Settings Settings { get; private set; } = null!;

    public SmartHaulMod(ModContentPack content) : base(content)
    {
        Settings = GetSettings<Settings>();
    }

    public override void DoSettingsWindowContents(Rect inRect) =>
        Settings.DoSettingsWindowContents(inRect);

    public override string SettingsCategory() =>
        "SmartHaul";
}