namespace SmartHaul;

/// <summary>
/// Mod settings for SmartHaul.
/// </summary>
public sealed class Settings : ModSettings
{
    private static bool _enableForHarvest = true;
    private static bool _enableForMining = true;
    private static bool _enableForDeconstruct = true;
    private static bool _enableForFloorRemoval = true;
    private static bool _enableForCrafting = true;
    private static bool _collectChunks = false;
    private static bool _immediateHaulAfterCrafting = true;
    private static bool _immediateHaulAfterFarming = false;
    private static float _inventoryThreshold = Constants.DEFAULT_INVENTORY_THRESHOLD;

    // Smart Hauling
    private static bool _enableSmartHauling = true;
    private static bool _smartHaulForAutoHaul = true;
    private static float _haulPickupRadius = Constants.DEFAULT_HAUL_PICKUP_RADIUS;

    public static bool EnableForHarvest => _enableForHarvest;
    public static bool EnableForMining => _enableForMining;
    public static bool EnableForDeconstruct => _enableForDeconstruct;
    public static bool EnableForFloorRemoval => _enableForFloorRemoval;
    public static bool EnableForCrafting => _enableForCrafting;
    public static bool CollectChunks => _collectChunks;
    public static bool ImmediateHaulAfterCrafting => _immediateHaulAfterCrafting;
    public static bool ImmediateHaulAfterFarming => _immediateHaulAfterFarming;
    public static float InventoryThreshold => _inventoryThreshold;

    /// <summary>Master switch for smart hauling (auto + manual).</summary>
    public static bool EnableSmartHauling => _enableSmartHauling;

    /// <summary>Replace vanilla auto-haul jobs with SmartHaul_HaulSmart.</summary>
    public static bool SmartHaulForAutoHaul => _smartHaulForAutoHaul;

    /// <summary>Radius in cells to scan for nearby items during smart hauling.</summary>
    public static float HaulPickupRadius => _haulPickupRadius;

    private static Vector2 scrollPosition = Vector2.zero;

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref _enableForHarvest, "enableForHarvest", true);
        Scribe_Values.Look(ref _enableForMining, "enableForMining", true);
        Scribe_Values.Look(ref _enableForDeconstruct, "enableForDeconstruct", true);
        Scribe_Values.Look(ref _enableForFloorRemoval, "enableForFloorRemoval", true);
        Scribe_Values.Look(ref _enableForCrafting, "enableForCrafting", true);
        Scribe_Values.Look(ref _collectChunks, "collectChunks", false);
        Scribe_Values.Look(ref _immediateHaulAfterCrafting, "immediateHaulAfterCrafting", true);
        Scribe_Values.Look(ref _immediateHaulAfterFarming, "immediateHaulAfterFarming", false);
        Scribe_Values.Look(ref _inventoryThreshold, "inventoryThreshold", Constants.DEFAULT_INVENTORY_THRESHOLD);
        Scribe_Values.Look(ref _enableSmartHauling, "enableSmartHauling", true);
        Scribe_Values.Look(ref _smartHaulForAutoHaul, "smartHaulForAutoHaul", true);
        Scribe_Values.Look(ref _haulPickupRadius, "haulPickupRadius", Constants.DEFAULT_HAUL_PICKUP_RADIUS);
    }

    public void DoSettingsWindowContents(Rect inRect)
    {
        Rect viewRect = new(0f, 0f, inRect.width - 16f, 900f);

        Widgets.BeginScrollView(inRect, ref scrollPosition, viewRect);

        var listing = new Listing_Standard();
        listing.Begin(viewRect);

        listing.Label($"SmartHaul v{SmartHaulMod.Version}");
        listing.GapLine();

        // --- Work collection toggles ---
        listing.Label("SmartHaul_EnableHeader".Translate());
        listing.Gap(4f);

        listing.CheckboxLabeled("SmartHaul_EnableHarvest".Translate(), ref _enableForHarvest, "SmartHaul_EnableHarvest_Desc".Translate());
        listing.CheckboxLabeled("SmartHaul_EnableMining".Translate(), ref _enableForMining, "SmartHaul_EnableMining_Desc".Translate());
        listing.CheckboxLabeled("SmartHaul_EnableDeconstruct".Translate(), ref _enableForDeconstruct, "SmartHaul_EnableDeconstruct_Desc".Translate());
        listing.CheckboxLabeled("SmartHaul_EnableFloorRemoval".Translate(), ref _enableForFloorRemoval, "SmartHaul_EnableFloorRemoval_Desc".Translate());
        listing.CheckboxLabeled("SmartHaul_EnableCrafting".Translate(), ref _enableForCrafting, "SmartHaul_EnableCrafting_Desc".Translate());

        listing.Gap();
        listing.CheckboxLabeled("SmartHaul_CollectChunks".Translate(), ref _collectChunks, "SmartHaul_CollectChunks_Desc".Translate());

        listing.GapLine();

        // --- Haul behavior ---
        listing.Label("SmartHaul_BehaviorHeader".Translate());
        listing.Gap(4f);

        listing.CheckboxLabeled("SmartHaul_ImmediateHaulCrafting".Translate(), ref _immediateHaulAfterCrafting, "SmartHaul_ImmediateHaulCrafting_Desc".Translate());
        listing.CheckboxLabeled("SmartHaul_ImmediateHaulFarming".Translate(), ref _immediateHaulAfterFarming, "SmartHaul_ImmediateHaulFarming_Desc".Translate());

        listing.Gap();
        listing.Label("SmartHaul_Threshold".Translate(_inventoryThreshold.ToStringPercent()));
        _inventoryThreshold = listing.Slider(_inventoryThreshold, 0.5f, 1.0f);
        listing.Label("SmartHaul_ThresholdHint".Translate());

        listing.GapLine();

        // --- Smart Hauling ---
        listing.Label("SmartHaul_SmartHaulHeader".Translate());
        listing.Gap(4f);

        listing.CheckboxLabeled("SmartHaul_EnableSmartHauling".Translate(), ref _enableSmartHauling, "SmartHaul_EnableSmartHauling_Desc".Translate());

        if (_enableSmartHauling)
        {
            listing.CheckboxLabeled("SmartHaul_SmartHaulAutoHaul".Translate(), ref _smartHaulForAutoHaul, "SmartHaul_SmartHaulAutoHaul_Desc".Translate());
            listing.Gap(4f);
            listing.Label("SmartHaul_HaulPickupRadius".Translate(_haulPickupRadius.ToString("F1")));
            _haulPickupRadius = listing.Slider(_haulPickupRadius, Constants.MIN_HAUL_PICKUP_RADIUS, Constants.MAX_HAUL_PICKUP_RADIUS);
        }

        listing.GapLine();

        // --- Behavior descriptions ---
        listing.Label("SmartHaul_BehaviorMining".Translate());
        listing.Label("SmartHaul_BehaviorFarming".Translate());
        listing.Label("SmartHaul_BehaviorCrafting".Translate());

        listing.End();
        Widgets.EndScrollView();
    }
}