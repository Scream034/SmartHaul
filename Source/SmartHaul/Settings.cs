namespace SmartHaul;

/// <summary>
/// Mod settings with localization support.
/// All UI strings use Translate() for multi-language support.
/// </summary>
public class Settings : ModSettings
{
    #region Fields

    private static bool _allowCorpses;
    private static bool _allowAnimals = true;
    private static bool _allowMechanoids = true;
    private static float _maxCapacity = 0.8f;

    private static bool _showHaulOverlay = true;
    private static bool _showPawnName = true;
    private static int _pawnNameMaxChars = 12;
    private static bool _overlayAllFactions = true;

    private static float _maxNeighborDistance = 30f;
    private static int _maxCandidates = 24;
    private static int _rotUrgentHours = 12;
    private static float _wornThreshold = 0.3f;

    private static bool _notifyOtherPawns = true;

    private static bool _haulAfterDeconstruct = true;
    private static DeconstructHaulMode _deconstructMode = DeconstructHaulMode.All;
    private static bool _deconstructCheckPriority;

    private static bool _haulAfterButcher = true;
    private static ButcherHaulMode _butcherMode = ButcherHaulMode.All;
    private static bool _butcherCheckPriority;
    private static float _recipeUnloadThreshold = 0.5f;

    private static bool _haulAfterMining = true;
    private static bool _miningCollectChunks;
    private static bool _miningCheckPriority;

    private static bool _haulAfterHarvest = true;
    private static bool _harvestCheckPriority;

    private static bool _dropNearStorage = true;

    private static bool _smartCleanup = true;
    private static bool _dropWhenHaulingDisabled;

    private static int _debugTraceLevel;
    private static Vector2 _scrollPos;

    #endregion

    #region Enums

    /// <summary>What to collect after deconstruction.</summary>
    public enum DeconstructHaulMode { All, NoChunks, Valuable }

    /// <summary>What to collect after butchering.</summary>
    public enum ButcherHaulMode { All, PerishableOnly, MeatOnly }

    #endregion

    #region Public Accessors

    /// <summary>Whether to allow corpses in inventory.</summary>
    public static bool AllowCorpses => _allowCorpses;

    /// <summary>Whether pack animals can use inventory for hauling.</summary>
    public static bool AllowAnimals => _allowAnimals;

    /// <summary>Whether mechanoids can use inventory for hauling.</summary>
    public static bool AllowMechanoids => _allowMechanoids;

    /// <summary>Max gear ratio before falling back to vanilla hauling.</summary>
    public static float MaximumOccupiedCapacityToConsiderHauling => _maxCapacity;

    /// <summary>Whether to show haul queue overlay on items.</summary>
    public static bool ShowHaulOverlay => _showHaulOverlay;

    /// <summary>Whether to show hauler name under items.</summary>
    public static bool ShowPawnName => _showPawnName;

    /// <summary>Max characters to show for pawn name.</summary>
    public static int PawnNameMaxChars => _pawnNameMaxChars;

    /// <summary>Whether to show overlay for non-player pawns.</summary>
    public static bool OverlayAllFactions => _overlayAllFactions;

    /// <summary>Max distance to search for nearby items.</summary>
    public static float MaxNeighborDistance => _maxNeighborDistance;

    /// <summary>Max items to collect per trip.</summary>
    public static int MaxCandidates => _maxCandidates;

    /// <summary>Ticks until rot is considered urgent.</summary>
    public static int RotUrgentTicks => _rotUrgentHours * 2500;

    /// <summary>HP ratio below which item is considered worn.</summary>
    public static float WornThreshold => _wornThreshold;

    /// <summary>Whether to notify nearby pawns when inventory is full.</summary>
    public static bool NotifyOtherPawns => _notifyOtherPawns;

    /// <summary>Whether to auto-haul after deconstruction.</summary>
    public static bool HaulAfterDeconstruct => _haulAfterDeconstruct;

    /// <summary>What to collect after deconstruction.</summary>
    public static DeconstructHaulMode DeconstructMode => _deconstructMode;

    /// <summary>Whether to check work priority for deconstruction.</summary>
    public static bool DeconstructCheckPriority => _deconstructCheckPriority;

    /// <summary>Whether to auto-haul after butchering.</summary>
    public static bool HaulAfterButcher => _haulAfterButcher;

    /// <summary>What to collect after butchering.</summary>
    public static ButcherHaulMode ButcherMode => _butcherMode;

    /// <summary>Whether to check work priority for butchering.</summary>
    public static bool ButcherCheckPriority => _butcherCheckPriority;

    /// <summary>Inventory fill fraction at which pawn unloads during recipe work (butcher etc).</summary>
    public static float RecipeUnloadThreshold => _recipeUnloadThreshold;

    /// <summary>Whether to auto-haul after mining.</summary>
    public static bool HaulAfterMining => _haulAfterMining;

    /// <summary>Whether to collect stone chunks when mining.</summary>
    public static bool MiningCollectChunks => _miningCollectChunks;

    /// <summary>Whether to check work priority for mining.</summary>
    public static bool MiningCheckPriority => _miningCheckPriority;

    /// <summary>Whether to auto-haul after harvesting.</summary>
    public static bool HaulAfterHarvest => _haulAfterHarvest;

    /// <summary>Whether to check work priority for harvesting.</summary>
    public static bool HarvestCheckPriority => _harvestCheckPriority;

    /// <summary>
    /// When storage is full, drop items near the storage zone instead of at pawn's feet.
    /// </summary>
    public static bool DropNearStorage => _dropNearStorage;

    /// <summary>Whether to drop items when hauling priority is lower than current work.</summary>
    public static bool SmartCleanup => _smartCleanup;

    /// <summary>Whether to drop items when hauling is disabled entirely.</summary>
    public static bool DropWhenHaulingDisabled => _dropWhenHaulingDisabled;

    /// <summary>
    /// Checks if a race is allowed to use smart hauling.
    /// </summary>
    /// <param name="props">Race properties to check.</param>
    /// <returns>True if race can use smart hauling.</returns>
    public static bool IsAllowedRace(RaceProperties props) =>
        props.Humanlike
        || (AllowAnimals && props.Animal)
        || (AllowMechanoids && props.IsMechanoid);

    #endregion

    #region UI

    /// <summary>
    /// Draws the settings window content with localized strings.
    /// </summary>
    /// <param name="inRect">Available rect for drawing.</param>
    public static void DoSettingsWindowContents(Rect inRect)
    {
        const float lineHeight = 28f;
        var contentHeight = lineHeight * 65 + 12f * 12;

        var viewRect = new Rect(0, 0, inRect.width - 20f, contentHeight);
        Widgets.BeginScrollView(inRect, ref _scrollPos, viewRect);

        var ls = new Listing_Standard();
        ls.Begin(viewRect);

        // === General ===
        ls.Label($"--- {"SH.SettingsGeneral".Translate()} ---");
        ls.CheckboxLabeled("SH.AllowCorpses".Translate(), ref _allowCorpses,
            "SH.AllowCorpsesTooltip".Translate());
        ls.CheckboxLabeled("SH.AllowAnimals".Translate(), ref _allowAnimals,
            "SH.AllowAnimalsTooltip".Translate());
        ls.CheckboxLabeled("SH.AllowMechanoids".Translate(), ref _allowMechanoids,
            "SH.AllowMechanoidsTooltip".Translate());
        ls.Label("SH.MinFreeInventory".Translate() + $": {(1f - _maxCapacity) * 100f:F0}%",
            tooltip: "SH.MinFreeInventoryTooltip".Translate());
        _maxCapacity = 1f - ls.Slider(1f - _maxCapacity, 0f, 1f);

        ls.GapLine();

        // === Overlay ===
        ls.Label($"--- {"SH.SettingsOverlay".Translate()} ---");
        ls.CheckboxLabeled("SH.ShowHaulOverlay".Translate(), ref _showHaulOverlay,
            "SH.ShowHaulOverlayTooltip".Translate());
        if (_showHaulOverlay)
        {
            ls.CheckboxLabeled("  " + "SH.ShowPawnName".Translate(), ref _showPawnName,
                "SH.ShowPawnNameTooltip".Translate());
            if (_showPawnName)
            {
                ls.Label("  " + "SH.PawnNameMaxChars".Translate(_pawnNameMaxChars));
                _pawnNameMaxChars = (int)ls.Slider(_pawnNameMaxChars, 3, 24);
            }
            ls.CheckboxLabeled("  " + "SH.OverlayAllFactions".Translate(), ref _overlayAllFactions,
                "SH.OverlayAllFactionsTooltip".Translate());
        }

        ls.GapLine();

        // === Route ===
        ls.Label($"--- {"SH.SettingsRoute".Translate()} ---");
        ls.Label("SH.MaxNeighborDistance".Translate($"{_maxNeighborDistance:F0}"));
        _maxNeighborDistance = ls.Slider(_maxNeighborDistance, 10f, 60f);
        ls.Label("SH.MaxCandidates".Translate(_maxCandidates));
        _maxCandidates = (int)ls.Slider(_maxCandidates, 5, 50);
        ls.Label("SH.RotUrgentHours".Translate(_rotUrgentHours));
        _rotUrgentHours = (int)ls.Slider(_rotUrgentHours, 1, 48);
        ls.Label("SH.WornThreshold".Translate($"{_wornThreshold * 100f:F0}"));
        _wornThreshold = ls.Slider(_wornThreshold, 0.1f, 0.9f);

        ls.GapLine();

        // === Cooperation ===
        ls.Label($"--- {"SH.SettingsCooperation".Translate()} ---");
        ls.CheckboxLabeled("SH.NotifyOtherPawns".Translate(), ref _notifyOtherPawns,
            "SH.NotifyOtherPawnsTooltip".Translate());

        ls.GapLine();

        // === Smart Cleanup ===
        ls.Label($"--- {"SH.SettingsSmartCleanup".Translate()} ---");
        ls.CheckboxLabeled("SH.SmartCleanup".Translate(), ref _smartCleanup,
            "SH.SmartCleanupTooltip".Translate());
        ls.CheckboxLabeled("SH.DropWhenHaulingDisabled".Translate(), ref _dropWhenHaulingDisabled,
            "SH.DropWhenHaulingDisabledTooltip".Translate());

        ls.GapLine();

        // === Fallback Behavior ===
        ls.Label($"--- {"SH.SettingsFallback".Translate()} ---");
        ls.CheckboxLabeled("SH.DropNearStorage".Translate(), ref _dropNearStorage,
            "SH.DropNearStorageTooltip".Translate());

        // === Auto-Haul Triggers ===
        ls.Label($"--- {"SH.SettingsAutoHaul".Translate()} ---");

        // Deconstruct
        ls.CheckboxLabeled("SH.HaulAfterDeconstruct".Translate(), ref _haulAfterDeconstruct);
        if (_haulAfterDeconstruct)
        {
            if (ls.RadioButton("  " + "SH.DeconstructModeAll".Translate(),
                _deconstructMode == DeconstructHaulMode.All))
                _deconstructMode = DeconstructHaulMode.All;
            if (ls.RadioButton("  " + "SH.DeconstructModeNoChunks".Translate(),
                _deconstructMode == DeconstructHaulMode.NoChunks))
                _deconstructMode = DeconstructHaulMode.NoChunks;
            if (ls.RadioButton("  " + "SH.DeconstructModeValuable".Translate(),
                _deconstructMode == DeconstructHaulMode.Valuable))
                _deconstructMode = DeconstructHaulMode.Valuable;
            ls.CheckboxLabeled("  " + "SH.RespectWorkPriority".Translate(), ref _deconstructCheckPriority,
                "SH.RespectWorkPriorityTooltip".Translate());
        }

        ls.Gap();

        // Butcher
        ls.CheckboxLabeled("SH.HaulAfterButcher".Translate(), ref _haulAfterButcher);
        if (_haulAfterButcher)
        {
            if (ls.RadioButton("  " + "SH.ButcherModeAll".Translate(),
                _butcherMode == ButcherHaulMode.All))
                _butcherMode = ButcherHaulMode.All;
            if (ls.RadioButton("  " + "SH.ButcherModePerishable".Translate(),
                _butcherMode == ButcherHaulMode.PerishableOnly))
                _butcherMode = ButcherHaulMode.PerishableOnly;
            if (ls.RadioButton("  " + "SH.ButcherModeMeat".Translate(),
                _butcherMode == ButcherHaulMode.MeatOnly))
                _butcherMode = ButcherHaulMode.MeatOnly;
            ls.Label("SH.RecipeUnloadThreshold".Translate($"{_recipeUnloadThreshold * 100f:F0}"));
            _recipeUnloadThreshold = ls.Slider(_recipeUnloadThreshold, 0.3f, 0.9f);
            ls.CheckboxLabeled("  " + "SH.RespectWorkPriority".Translate(), ref _butcherCheckPriority,
                "SH.RespectWorkPriorityTooltip".Translate());
        }

        ls.Gap();

        // Mining
        ls.CheckboxLabeled("SH.HaulAfterMining".Translate(), ref _haulAfterMining);
        if (_haulAfterMining)
        {
            ls.CheckboxLabeled("  " + "SH.MiningCollectChunks".Translate(), ref _miningCollectChunks);
            ls.CheckboxLabeled("  " + "SH.RespectWorkPriority".Translate(), ref _miningCheckPriority,
                "SH.RespectWorkPriorityTooltip".Translate());
        }

        ls.Gap();

        // Harvest
        ls.CheckboxLabeled("SH.HaulAfterHarvest".Translate(), ref _haulAfterHarvest);
        if (_haulAfterHarvest)
        {
            ls.CheckboxLabeled("  " + "SH.RespectWorkPriority".Translate(), ref _harvestCheckPriority,
                "SH.RespectWorkPriorityTooltip".Translate());
        }

#if DEBUG
        ls.GapLine();
        ls.Label($"--- {"SH.SettingsDebug".Translate()} ---");
        ls.Label("SH.TraceLevel".Translate(_debugTraceLevel));
        _debugTraceLevel = (int)ls.Slider(_debugTraceLevel, 0, 5);
        Log.TraceLevel = _debugTraceLevel;
#endif

        ls.End();
        Widgets.EndScrollView();
    }

    #endregion

    #region Persistence

    /// <summary>
    /// Saves and loads settings from XML.
    /// </summary>
    public override void ExposeData()
    {
        base.ExposeData();

        Scribe_Values.Look(ref _allowCorpses, "allowCorpses");
        Scribe_Values.Look(ref _allowAnimals, "allowAnimals", true);
        Scribe_Values.Look(ref _allowMechanoids, "allowMechanoids", true);
        Scribe_Values.Look(ref _maxCapacity, "maxCapacity", 0.8f);

        Scribe_Values.Look(ref _showHaulOverlay, "showHaulOverlay", true);
        Scribe_Values.Look(ref _showPawnName, "showPawnName", true);
        Scribe_Values.Look(ref _pawnNameMaxChars, "pawnNameMaxChars", 12);
        Scribe_Values.Look(ref _overlayAllFactions, "overlayAllFactions", true);

        Scribe_Values.Look(ref _maxNeighborDistance, "maxNeighborDistance", 30f);
        Scribe_Values.Look(ref _maxCandidates, "maxCandidates", 24);
        Scribe_Values.Look(ref _rotUrgentHours, "rotUrgentHours", 12);
        Scribe_Values.Look(ref _wornThreshold, "wornThreshold", 0.3f);

        Scribe_Values.Look(ref _notifyOtherPawns, "notifyOtherPawns", true);

        Scribe_Values.Look(ref _haulAfterDeconstruct, "haulAfterDeconstruct", true);
        Scribe_Values.Look(ref _deconstructMode, "deconstructMode", DeconstructHaulMode.All);
        Scribe_Values.Look(ref _deconstructCheckPriority, "deconstructCheckPriority");

        Scribe_Values.Look(ref _haulAfterButcher, "haulAfterButcher", true);
        Scribe_Values.Look(ref _butcherMode, "butcherMode", ButcherHaulMode.All);
        Scribe_Values.Look(ref _butcherCheckPriority, "butcherCheckPriority");

        Scribe_Values.Look(ref _haulAfterMining, "haulAfterMining", true);
        Scribe_Values.Look(ref _miningCollectChunks, "miningCollectChunks");
        Scribe_Values.Look(ref _miningCheckPriority, "miningCheckPriority");

        Scribe_Values.Look(ref _haulAfterHarvest, "haulAfterHarvest", true);
        Scribe_Values.Look(ref _harvestCheckPriority, "harvestCheckPriority");

        Scribe_Values.Look(ref _dropNearStorage, "dropNearStorage", true);

        Scribe_Values.Look(ref _smartCleanup, "smartCleanup", true);
        Scribe_Values.Look(ref _dropWhenHaulingDisabled, "dropWhenHaulingDisabled");

#if DEBUG
        // WHY: In DEBUG, default to verbose logging (4), not saved value
        Scribe_Values.Look(ref _debugTraceLevel, "debugTraceLevel", 4);
#else
        Scribe_Values.Look(ref _debugTraceLevel, "debugTraceLevel");
#endif
        Log.TraceLevel = _debugTraceLevel;
    }

    #endregion
}