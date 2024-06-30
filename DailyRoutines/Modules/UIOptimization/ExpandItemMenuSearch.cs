using System.Collections.Generic;
using System.Linq;
using DailyRoutines.Helpers;
using DailyRoutines.Infos;
using DailyRoutines.Managers;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ImGuiNET;
using Lumina.Excel.GeneratedSheets;

namespace DailyRoutines.Modules;

[ModuleDescription("ExpandItemMenuSearchTitle", "ExpandItemMenuSearchDescription", ModuleCategories.界面优化)]
public unsafe class ExpandItemMenuSearch : DailyModuleBase
{
    private const int ChatLogContextItemId = 0x948;

    private const string CollectorUrl = "https://www.ffxivsc.cn/#/search?text={0}&type=armor";
    private const string WikiUrl = "https://ff14.huijiwiki.com/index.php?search={0}&profile=default&fulltext=1";

    private static readonly MenuItem CollectorItem = new()
    {
        IsEnabled = true,
        IsReturn = false,
        UseDefaultPrefix = true,
        Name = new SeStringBuilder().Append(DRPrefix).Append(Service.Lang.GetText("ExpandItemMenuSearch-CollectorSearch"))
                                    .Build(),
        OnClicked = OnCollector,
        IsSubmenu = false,
        PrefixColor = 34,
    };

    private static readonly MenuItem WikiItem = new()
    {
        IsEnabled = true,
        IsReturn = false,
        UseDefaultPrefix = true,
        Name = new SeStringBuilder().Append(DRPrefix).Append(Service.Lang.GetText("ExpandItemMenuSearch-WikiSearch"))
                                    .Build(),
        OnClicked = OnWiki,
        IsSubmenu = false,
        PrefixColor = 34,
    };

    private static Item? _LastItem;
    private static Item? _LastPrismBoxItem;
    private static Item? _LastGlamourItem;
    private static ulong _LastHoveredItemID;
    private static ulong _LastDetailItemID;

    private static bool _IsOnItemHover;
    private static bool _IsOnItemDetail;
    private static readonly HashSet<InventoryItem> _CharacterInspectItems = [];

    private static bool SearchCollector;
    private static bool SearchCollectorByGlamour;
    private static bool SearchWiki;
    private static bool SearchWikiByGlamour;
    public override string? Author { get; set; } = "HSS";

    public override void Init()
    {
        AddConfig("SearchCollector", true);
        SearchCollector = GetConfig<bool>("SearchCollector");

        AddConfig("SearchCollectorByGlamour", true);
        SearchCollectorByGlamour = GetConfig<bool>("SearchCollectorByGlamour");

        AddConfig("SearchWiki", true);
        SearchWiki = GetConfig<bool>("SearchWiki");

        AddConfig("SearchWikiByGlamour", true);
        SearchWikiByGlamour = GetConfig<bool>("SearchWikiByGlamour");

        TaskHelper ??= new TaskHelper { AbortOnTimeout = true, TimeLimitMS = 5000, ShowDebug = false };

        Service.ContextMenu.OnMenuOpened += OnMenuOpened;
        Service.Gui.HoveredItemChanged += OnHoveredItemChanged;
        Service.FrameworkManager.Register(OnUpdate);

        Service.AddonLifecycle.RegisterListener(AddonEvent.PostRefresh, "CharacterInspect", OnAddon);
        Service.AddonLifecycle.RegisterListener(AddonEvent.PostSetup,
        [
            "CabinetWithdraw", "Shop", "InclusionShop", "CollectablesShop", "FreeCompanyExchange", "FreeCompanyCreditShop",
            "ShopExchangeCurrency", "ShopExchangeItem", "SkyIslandExchange", "TripleTriadCoinExchange", "FreeCompanyChest",
            "MJIDisposeShop", "GrandCompanyExchange", "ReconstructionBuyback", "ShopExchangeCoin",
            "MiragePrismPrismBoxCrystallize", "ItemSearch", "GrandCompanySupplyList",
        ], OnAddon);

        Service.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize,
        [
            "CabinetWithdraw", "CharacterInspect", "MiragePrismPrismBoxCrystallize", "Shop", "InclusionShop",
            "CollectablesShop", "FreeCompanyExchange", "FreeCompanyCreditShop", "ShopExchangeCurrency", "ShopExchangeItem",
            "SkyIslandExchange", "TripleTriadCoinExchange", "FreeCompanyChest", "MJIDisposeShop", "GrandCompanyExchange",
            "ReconstructionBuyback", "ShopExchangeCoin", "ItemSearch", "GrandCompanySupplyList",
        ], OnAddon);

        _CharacterInspectItems.Clear();
    }

    public override void ConfigUI()
    {
        if (ImGui.Checkbox(Service.Lang.GetText("ExpandItemMenuSearch-CollectorSearch"), ref SearchCollector))
            UpdateConfig("SearchCollector", SearchCollector);

        if (SearchCollector)
        {
            ImGui.Indent();
            ImGui.PushID("CollectorSearchGlamour");
            if (ImGui.Checkbox(Service.Lang.GetText("ExpandItemMenuSearch-SearchGlamour"),
                               ref SearchCollectorByGlamour))
                UpdateConfig("SearchCollectorByGlamour", SearchCollectorByGlamour);

            ImGui.PopID();
            ImGui.Unindent();
        }

        if (ImGui.Checkbox(Service.Lang.GetText("ExpandItemMenuSearch-WikiSearch"), ref SearchWiki))
            UpdateConfig("SearchWiki", SearchWiki);

        if (SearchWiki)
        {
            ImGui.Indent();
            ImGui.PushID("WikiSearchGlamour");
            if (ImGui.Checkbox(Service.Lang.GetText("ExpandItemMenuSearch-SearchGlamour"),
                               ref SearchWikiByGlamour))
                UpdateConfig("SearchWikiByGlamour", SearchWikiByGlamour);

            ImGui.PopID();
            ImGui.Unindent();
        }
    }

    private void OnAddon(AddonEvent type, AddonArgs? args)
    {
        switch (type)
        {
            case AddonEvent.PostSetup:
                switch (args.AddonName)
                {
                    case "MiragePrismPrismBoxCrystallize":
                        _IsOnItemHover = true;
                        break;
                    default:
                        _IsOnItemDetail = true;
                        break;
                }

                break;
            case AddonEvent.PostRefresh:
                switch (args.AddonName)
                {
                    case "CharacterInspect":
                        TaskHelper.Enqueue(() =>
                        {
                            if (_CharacterInspectItems.Count != 0) return;
                            var container = InventoryManager.Instance()->GetInventoryContainer(InventoryType.Examine);
                            for (var i = 0; i < container->Size; i++)
                            {
                                var item = container->GetInventorySlot(i);
                                if (item == null || item->ItemId == 0) continue;

                                _CharacterInspectItems.Add(*item);
                            }

                            _IsOnItemHover = true;
                        });

                        break;
                }

                break;
            case AddonEvent.PreFinalize:
                switch (args.AddonName)
                {
                    case "CharacterInspect":
                        TaskHelper.Enqueue(() =>
                        {
                            _IsOnItemHover = false;
                            _LastHoveredItemID = 0;
                            _CharacterInspectItems.Clear();
                        });

                        break;
                    case "MiragePrismPrismBoxCrystallize":
                        _IsOnItemHover = false;
                        break;
                    default:
                        _IsOnItemDetail = false;
                        break;
                }

                break;
        }
    }

    private static void OnHoveredItemChanged(object? sender, ulong id)
    {
        if (!_IsOnItemHover) return;
        var contextMenu = (AtkUnitBase*)Service.Gui.GetAddonByName("ContextMenu");
        if (contextMenu is null || !contextMenu->IsVisible)
        {
            if (id < 2000000) id %= 500000;
            if (id != 0 && _LastHoveredItemID != id)
                _LastHoveredItemID = id;
        }
    }

    private static void OnUpdate(IFramework framework)
    {
        if (!_IsOnItemDetail) return;
        var agent = AgentItemDetail.Instance();
        if (agent == null) return;

        var id = agent->ItemId;
        if (id != 0 && _LastDetailItemID != id)
            _LastDetailItemID = id;
    }

    private static void OnMenuOpened(IMenuOpenedArgs args)
    {
        _LastItem = null;
        _LastGlamourItem = null;

        if (args.Target is MenuTargetInventory { TargetItem: not null } inventoryTarget)
        {
            var itemId = inventoryTarget.TargetItem.Value.ItemId;
            var glamourId = inventoryTarget.TargetItem.Value.GlamourId;

            if (SearchCollector && PresetData.TryGetGear(itemId, out var itemCollector))
            {
                _LastItem = itemCollector;
                if (SearchCollectorByGlamour)
                    _LastGlamourItem = PresetData.TryGetGear(glamourId, out var glamourItem) ? glamourItem : _LastItem;

                args.AddMenuItem(CollectorItem);
            }

            if (SearchWiki && LuminaCache.TryGetRow<Item>(itemId, out var itemWiki))
            {
                _LastItem = itemWiki;
                if (SearchWikiByGlamour)
                    _LastGlamourItem = PresetData.TryGetGear(glamourId, out var glamourItem) ? glamourItem : _LastItem;

                args.AddMenuItem(WikiItem);
            }

            return;
        }

        switch (args.AddonName)
        {
            case null:
                return;
            case "ChatLog":
            {
                var agent = Service.Gui.FindAgentInterface("ChatLog");
                if (agent == nint.Zero || !IsValidChatLogContext(agent)) return;

                var itemID = *(uint*)(agent + ChatLogContextItemId);
                if (SearchCollector && PresetData.Gears.TryGetValue(itemID, out var collectorItem))
                {
                    _LastItem = collectorItem;
                    args.AddMenuItem(CollectorItem);
                }

                if (SearchWiki && LuminaCache.TryGetRow<Item>(itemID, out var wikiItem))
                {
                    _LastItem = wikiItem;
                    args.AddMenuItem(WikiItem);
                }

                break;
            }
            case "MiragePrismMiragePlate":
                var agentDetail = AgentMiragePrismPrismItemDetail.Instance();
                if (agentDetail == null) return;
                if (!PresetData.Gears.TryGetValue(agentDetail->ItemId, out _LastItem)) return;

                if (SearchCollector) args.AddMenuItem(CollectorItem);
                if (SearchWiki) args.AddMenuItem(WikiItem);
                break;
            case "ColorantColoring":
                var agentColoring = AgentColorant.Instance();
                if (agentColoring == null) return;
                if (!PresetData.Dyes.TryGetValue(agentColoring->CharaView.SelectedStain, out _LastItem)) return;

                if (SearchWiki) args.AddMenuItem(WikiItem);
                break;
            case "CabinetWithdraw":
                if (_LastDetailItemID <= 0) return;
                if (!PresetData.Gears.TryGetValue((uint)_LastDetailItemID, out _LastItem)) return;

                if (SearchCollector) args.AddMenuItem(CollectorItem);
                if (SearchWiki) args.AddMenuItem(WikiItem);
                break;
            case "CharacterInspect":
            {
                if (!PresetData.Gears.TryGetValue((uint)_LastHoveredItemID, out var inspectItem)) return;
                var glamourID = _CharacterInspectItems.FirstOrDefault(x => x.ItemId == _LastHoveredItemID).GlamourId;

                _LastItem = inspectItem;
                _LastGlamourItem = PresetData.Gears.GetValueOrDefault(glamourID, _LastItem);

                if (SearchCollector) args.AddMenuItem(CollectorItem);
                if (SearchWiki) args.AddMenuItem(WikiItem);

                break;
            }
            case "MiragePrismPrismBoxCrystallize":
                var itemId = AgentMiragePrismPrismItemDetail.Instance()->ItemId;
                PresetData.Gears.TryGetValue(itemId, out var miragePrismPrismBoxCrystallizeItem);
                PresetData.Gears.TryGetValue((uint)_LastHoveredItemID, out var miragePrismPrismBoxItem);
                if (miragePrismPrismBoxCrystallizeItem == null && miragePrismPrismBoxItem == null) return;

                _LastItem = miragePrismPrismBoxItem;
                _LastPrismBoxItem = miragePrismPrismBoxCrystallizeItem;
                _LastGlamourItem = null;
                if (SearchCollector) args.AddMenuItem(CollectorItem);
                if (SearchWiki) args.AddMenuItem(WikiItem);
                break;
            case "InclusionShop":
            case "CollectablesShop":
            case "FreeCompanyExchange":
            case "ShopExchangeCurrency":
            case "ShopExchangeItem":
            case "FreeCompanyCreditShop":
            case "Shop":
            case "SkyIslandExchange":
            case "TripleTriadCoinExchange":
            case "FreeCompanyChest":
            case "MJIDisposeShop":
            case "GrandCompanyExchange":
            case "ReconstructionBuyback":
            case "ShopExchangeCoin":
            case "ItemSearch":
            case "GrandCompanySupplyList":
                if (_LastDetailItemID <= 0) return;

                if (SearchCollector && PresetData.Gears.TryGetValue((uint)_LastDetailItemID, out _))
                    args.AddMenuItem(CollectorItem);

                if (SearchWiki && LuminaCache.TryGetRow((uint)_LastDetailItemID, out _LastItem))
                    args.AddMenuItem(WikiItem);

                break;
        }
    }

    private static void OnCollector(IMenuItemClickedArgs args)
    {
        if (args.AddonName == "MiragePrismPrismBoxCrystallize" &&
            TryGetAddonByName<AtkUnitBase>("ContextMenu", out var addon) && IsAddonAndNodesReady(addon))
        {
            Util.OpenLink(TryScanContextMenuText(addon, "投影到当前装备上", out var index)
                              ? string.Format(CollectorUrl, _LastPrismBoxItem.Name)
                              : string.Format(CollectorUrl, _LastItem.Name));

            return;
        }

        if (SearchCollectorByGlamour && _LastGlamourItem != null && _LastGlamourItem.Name.ToString().Length != 0)
            Util.OpenLink(string.Format(CollectorUrl, _LastGlamourItem.Name));
        else if (_LastItem != null)
            Util.OpenLink(string.Format(CollectorUrl, _LastItem.Name));
    }

    private static void OnWiki(IMenuItemClickedArgs args)
    {
        if (args.AddonName == "MiragePrismPrismBoxCrystallize" &&
            TryGetAddonByName<AtkUnitBase>("ContextMenu", out var addon) && IsAddonAndNodesReady(addon))
        {
            if (TryScanContextMenuText(addon, "投影到当前装备上", out var index))
                Util.OpenLink(string.Format(WikiUrl, _LastPrismBoxItem.Name));
            else
                Util.OpenLink(string.Format(WikiUrl, _LastItem.Name));

            return;
        }

        if (SearchWikiByGlamour && _LastGlamourItem != null && _LastGlamourItem.Name.ToString().Length != 0)
            Util.OpenLink(string.Format(WikiUrl, _LastGlamourItem.Name));
        else if (_LastItem != null)
            Util.OpenLink(string.Format(WikiUrl, _LastItem.Name));
    }

    private static bool IsValidChatLogContext(nint agent) { return *(uint*)(agent + ChatLogContextItemId + 8) == 3; }

    public override void Uninit()
    {
        _CharacterInspectItems.Clear();
        _LastGlamourItem = null;
        _LastItem = null;

        Service.AddonLifecycle.UnregisterListener(OnAddon);
        Service.Gui.HoveredItemChanged -= OnHoveredItemChanged;
        Service.ContextMenu.OnMenuOpened -= OnMenuOpened;

        base.Uninit();
    }
}
