using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;
using ClickLib;
using ClickLib.Clicks;
using DailyRoutines.Helpers;
using DailyRoutines.Infos.Clicks;
using DailyRoutines.Managers;
using DailyRoutines.Windows;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Network.Structures;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Hooking;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Memory;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ImGuiNET;
using Lumina.Excel.GeneratedSheets;
using Microsoft.Extensions.Caching.Memory;

namespace DailyRoutines.Modules;

[PrecedingModule([typeof(AutoTalkSkip)])]
[ModuleDescription("AutoRetainerWorkTitle", "AutoRetainerWorkDescription", ModuleCategories.界面操作)]
public unsafe class AutoRetainerWork : DailyModuleBase
{
    private delegate nint InfoProxyItemSearchAddPageDelegate(byte* a1, byte* a2);

    [Signature(
        "48 89 5C 24 ?? 57 48 81 EC ?? ?? ?? ?? 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 84 24 ?? ?? ?? ?? 0F B6 82 ?? ?? ?? ?? 48 8B FA 48 8B D9 38 41 19 74 54",
        DetourName = nameof(InfoProxyItemSearchAddPageDetour))]
    private static Hook<InfoProxyItemSearchAddPageDelegate>? InfoProxyItemSearchAddPageHook;

    private delegate nint MarketboardPacketDelegate(nint a1, nint packetData);

    [Signature(
        "40 53 48 83 EC ?? 48 8B 0D ?? ?? ?? ?? 48 8B DA E8 ?? ?? ?? ?? 48 85 C0 74 ?? 4C 8B 00 48 8B C8 41 FF 90 ?? ?? ?? ?? 48 8B C8 BA ?? ?? ?? ?? E8 ?? ?? ?? ?? 48 85 C0 74 ?? 48 8D 53 ?? 41 B8 ?? ?? ?? ?? 48 8B C8 48 83 C4 ?? 5B E9 ?? ?? ?? ?? 48 83 C4 ?? 5B C3 CC CC CC CC CC CC CC CC CC CC 40 53",
        DetourName = nameof(MarketboardHistorDetour))]
    private static Hook<MarketboardPacketDelegate>? MarketboardHistoryHook;

    private static Config ModuleConfig = null!;

    private static Dictionary<string, Item>? ItemNames;
    private static Dictionary<string, Item> _ItemNames = [];
    private static Dictionary<uint, uint>? ItemsSellPrice;

    private static string PresetSearchInput = string.Empty;
    private static string ItemSearchInput = string.Empty;
    private static uint NewConfigItemID;
    private static bool NewConfigItemHQ;
    private static AbortCondition CondtionInput = AbortCondition.低于最小值;
    private static AbortBehavior BehaviorInput = AbortBehavior.无;
    private static Vector2 ChildSizeLeft;
    private static Vector2 ChildSizeRight;

    private static ItemConfig? SelectedItemConfig;
    private static ItemKey? CurrentItem;
    private static int CurrentItemIndex = -1;
    private static List<IMarketBoardHistoryListing>? ItemHistoryList;
    private static List<IMarketBoardItemListing>? ItemSearchList;

    private static readonly HashSet<ulong> PlayerRetainers = [];

    public override void Init()
    {
        ChildSizeLeft = ScaledVector2(200, 400);
        ChildSizeRight = ScaledVector2(450, 400);

        ItemsSellPrice ??= LuminaCache.Get<Item>()
                                      .Where(x => !string.IsNullOrEmpty(x.Name.RawString) && x.PriceLow != 0)
                                      .ToDictionary(x => x.RowId, x => x.PriceLow);

        ItemNames ??= LuminaCache.Get<Item>()
                                 .Where(x => !string.IsNullOrEmpty(x.Name.RawString))
                                 .GroupBy(x => x.Name.RawString)
                                 .ToDictionary(x => x.Key, x => x.First());

        _ItemNames = ItemNames.Take(10).ToDictionary(x => x.Key, x => x.Value);

        ModuleConfig = LoadConfig<Config>() ?? new();

        Service.Hook.InitializeFromAttributes(this);
        MarketboardHistoryHook?.Enable();
        InfoProxyItemSearchAddPageHook?.Enable();

        TaskHelper ??= new TaskHelper { AbortOnTimeout = true, TimeLimitMS = 60000, ShowDebug = false };
        Overlay ??= new Overlay(this);

        // 出售品列表
        Service.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "RetainerSellList", OnRetainerSellList);
        Service.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, "RetainerSellList", OnRetainerSellList);
        // 雇员列表
        Service.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "RetainerList", OnRetainerList);
        Service.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, "RetainerList", OnRetainerList);
        // 出售详情
        Service.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "RetainerSell", OnRetainerSell);

        Service.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "RetainerItemTransferList", OnEntrustDupsAddons);
        Service.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "RetainerItemTransferProgress",
                                                OnEntrustDupsAddons);
    }

    #region 游戏界面

    private static AtkUnitBase* SelectString     => (AtkUnitBase*)Service.Gui.GetAddonByName("SelectString");
    private static AtkUnitBase* SelectYesno      => (AtkUnitBase*)Service.Gui.GetAddonByName("SelectYesno");
    private static AtkUnitBase* RetainerList     => (AtkUnitBase*)Service.Gui.GetAddonByName("RetainerList");
    private static AtkUnitBase* RetainerSell     => (AtkUnitBase*)Service.Gui.GetAddonByName("RetainerSell");
    private static AtkUnitBase* RetainerSellList => (AtkUnitBase*)Service.Gui.GetAddonByName("RetainerSellList");
    private static AtkUnitBase* ItemSearchResult => (AtkUnitBase*)Service.Gui.GetAddonByName("ItemSearchResult");
    private static AtkUnitBase* Bank             => (AtkUnitBase*)Service.Gui.GetAddonByName("Bank");

    private static InfoProxyItemSearch* InfoItemSearch =>
        (InfoProxyItemSearch*)InfoModule.Instance()->GetInfoProxyById(InfoProxyId.ItemSearch);

    #endregion

    #region 价格缓存

    private static readonly MemoryCache Cache = new(new MemoryCacheOptions());

    private static string GetPriceCacheKey(uint itemID, bool isHQ) { return $"{itemID}_{(isHQ ? "HQ" : "NQ")}"; }

    private static bool TryGetPriceCache(uint itemID, bool isHQ, out uint price)
    {
        price = 0;

        var cacheKey = GetPriceCacheKey(itemID, isHQ);
        var result = Cache.Get<uint?>(cacheKey);
        if (result != null) price = (uint)result;
        return result != null;
    }

    private static void SetPriceCache(uint itemID, bool isHQ, uint price)
    {
        var cacheKey = GetPriceCacheKey(itemID, isHQ);
        Cache.Set(cacheKey, price, new MemoryCacheEntryOptions { SlidingExpiration = TimeSpan.FromMinutes(5) });
    }

    #endregion

    #region 模块界面

    public override void ConfigUI()
    {
        ConflictKeyText();

        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(ImGuiColors.DalamudYellow, $"{Service.Lang.GetText("AutoRetainersDispatchTitle")}:");
        ImGuiOm.TooltipHover(Service.Lang.GetText("AutoRetainersDispatchDescription"));

        ImGui.SameLine();
        if (ImGui.Button(Service.Lang.GetText("Start"))) EnqueueRetainersDispatch();

        ImGui.SameLine();
        if (ImGui.Button(Service.Lang.GetText("Stop"))) TaskHelper.Abort();

        ImGui.SameLine();
        PreviewImageWithHelpText(Service.Lang.GetText("AutoRetainersDispatch-WhatIsTheList"),
                                 "https://gh.atmoomen.top/DailyRoutines/main/Assets/Images/AutoRetainersDispatch-1.png");

        ImGui.Spacing();

        ImGui.TextColored(ImGuiColors.DalamudYellow, $"{Service.Lang.GetText("AutoRetainerPriceAdjustTitle")}:");

        ItemConfigSelector();

        ImGui.SameLine();
        ItemConfigEditor();
    }

    private void ItemConfigSelector()
    {
        using (ImRaii.Child("ItemConfigSelectorChild", ChildSizeLeft, true))
        {
            if (ImGuiOm.ButtonIcon("AddNewConfig", FontAwesomeIcon.Plus, Service.Lang.GetText("Add")))
                ImGui.OpenPopup("AddNewPreset");

            ImGui.SameLine();
            if (ImGuiOm.ButtonIcon("ImportConfig", FontAwesomeIcon.FileImport,
                                   Service.Lang.GetText("AutoRetainerPriceAdjust-ImportFromClipboard")))
            {
                var itemConfig = ImportItemConfigFromClipboard();
                if (itemConfig != null)
                {
                    var itemKey = new ItemKey(itemConfig.ItemID, itemConfig.IsHQ).ToString();
                    ModuleConfig.ItemConfigs[itemKey] = itemConfig;
                }
            }

            using (var popup0 = ImRaii.Popup("AddNewPreset"))
            {
                if (popup0.Success)
                {
                    ImGui.SetNextItemWidth(150f * GlobalFontScale);
                    ImGui.InputTextWithHint("###GameItemSearchInput", Service.Lang.GetText("PleaseSearch"),
                                            ref ItemSearchInput, 100);

                    if (ImGui.IsItemDeactivatedAfterEdit())
                    {
                        if (!string.IsNullOrWhiteSpace(ItemSearchInput) && ItemSearchInput.Length > 1)
                        {
                            _ItemNames = ItemNames
                                         .Where(x => x.Key.Contains(ItemSearchInput, StringComparison.OrdinalIgnoreCase))
                                         .ToDictionary(x => x.Key, x => x.Value);
                        }
                    }

                    ImGui.SameLine();
                    ImGui.Checkbox("HQ", ref NewConfigItemHQ);

                    ImGui.SameLine();
                    if (ImGui.Button(Service.Lang.GetText("Confirm")))
                    {
                        var newConfigStr = new ItemKey(NewConfigItemID, NewConfigItemHQ).ToString();
                        var newConfig = new ItemConfig(NewConfigItemID, NewConfigItemHQ);
                        if (ModuleConfig.ItemConfigs.TryAdd(newConfigStr, newConfig))
                        {
                            SaveConfig(ModuleConfig);
                            ImGui.CloseCurrentPopup();
                        }
                    }

                    ImGui.Separator();
                    foreach (var (itemName, item) in _ItemNames)
                        if (ImGuiOm.SelectableImageWithText(ImageHelper.GetIcon(item.Icon).ImGuiHandle,
                                                            ScaledVector2(24f), itemName,
                                                            item.RowId == NewConfigItemID,
                                                            ImGuiSelectableFlags.DontClosePopups))
                            NewConfigItemID = item.RowId;
                }
            }

            ImGui.SetNextItemWidth(-1f);
            ImGui.SameLine();
            ImGui.InputTextWithHint("###PresetSearchInput", Service.Lang.GetText("PleaseSearch"), ref PresetSearchInput, 100);

            ImGui.Separator();

            foreach (var itemConfig in ModuleConfig.ItemConfigs.ToList())
            {
                if (!string.IsNullOrWhiteSpace(PresetSearchInput) &&
                    !itemConfig.Value.ItemName.Contains(PresetSearchInput))
                    continue;

                if (ImGui.Selectable($"{itemConfig.Value.ItemName} {(itemConfig.Value.IsHQ ? "(HQ)" : "")}"))
                    SelectedItemConfig = itemConfig.Value;

                var isOpenPopup = false;
                using (var popup1 = ImRaii.ContextPopupItem($"{itemConfig.Value}_{itemConfig.Key}_{itemConfig.Value.ItemID}"))
                {
                    if (popup1.Success)
                    {
                        if (ImGui.MenuItem(Service.Lang.GetText("AutoRetainerPriceAdjust-ExportToClipboard")))
                            ExportItemConfigToClipboard(itemConfig.Value);

                        if (ImGui.MenuItem(Service.Lang.GetText("AutoRetainerWork-CreateNewBaseOnExisted")))
                            isOpenPopup = true;

                        if (itemConfig.Value.ItemID != 0)
                        {
                            if (ImGui.MenuItem(Service.Lang.GetText("Delete")))
                            {
                                ModuleConfig.ItemConfigs.Remove(itemConfig.Key);
                                SaveConfig(ModuleConfig);

                                SelectedItemConfig = null;
                            }
                        }
                    }
                }

                if (isOpenPopup)
                    ImGui.OpenPopup($"AddNewPresetBasedOnExisted_{itemConfig.Key}");

                using (var popup2 = ImRaii.Popup($"AddNewPresetBasedOnExisted_{itemConfig.Key}"))
                {
                    if (popup2.Success)
                    {
                        ImGui.SetNextItemWidth(150f * GlobalFontScale);
                        ImGui.InputTextWithHint("###GameItemSearchInput", Service.Lang.GetText("PleaseSearch"),
                                                ref ItemSearchInput, 100);

                        if (ImGui.IsItemDeactivatedAfterEdit())
                        {
                            if (!string.IsNullOrWhiteSpace(ItemSearchInput) && ItemSearchInput.Length > 1)
                            {
                                _ItemNames = ItemNames
                                             .Where(x => x.Key.Contains(ItemSearchInput, StringComparison.OrdinalIgnoreCase))
                                             .ToDictionary(x => x.Key, x => x.Value);
                            }
                        }

                        ImGui.SameLine();
                        ImGui.Checkbox("HQ", ref NewConfigItemHQ);

                        ImGui.SameLine();
                        if (ImGui.Button(Service.Lang.GetText("Confirm")))
                        {
                            var newConfigStr = new ItemKey(NewConfigItemID, NewConfigItemHQ).ToString();
                            var newConfig = new ItemConfig
                            {
                                ItemID = NewConfigItemID,
                                IsHQ = NewConfigItemHQ,
                                ItemName = LuminaCache.GetRow<Item>(NewConfigItemID).Name.RawString,
                                AbortLogic = itemConfig.Value.AbortLogic,
                                AdjustBehavior = itemConfig.Value.AdjustBehavior,
                                AdjustValues = itemConfig.Value.AdjustValues,
                                PriceExpected = itemConfig.Value.PriceExpected,
                                PriceMaximum = itemConfig.Value.PriceMaximum,
                                PriceMaxReduction = itemConfig.Value.PriceMaxReduction,
                                PriceMinimum = itemConfig.Value.PriceMinimum,
                            };
                            if (ModuleConfig.ItemConfigs.TryAdd(newConfigStr, newConfig))
                            {
                                SaveConfig(ModuleConfig);
                                ImGui.CloseCurrentPopup();
                            }
                        }

                        ImGui.Separator();
                        foreach (var (itemName, item) in _ItemNames)
                            if (ImGuiOm.SelectableImageWithText(ImageHelper.GetIcon(item.Icon).ImGuiHandle,
                                                                ScaledVector2(24f), itemName,
                                                                item.RowId == NewConfigItemID,
                                                                ImGuiSelectableFlags.DontClosePopups))
                                NewConfigItemID = item.RowId;
                    }
                }

                if (itemConfig.Value is { ItemID: 0, IsHQ: true })
                    ImGui.Separator();
            }
        }
    }

    private void ItemConfigEditor()
    {
        ChildSizeRight.X = ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X;
        var itemConfig = SelectedItemConfig;
        if (itemConfig == null)
        {
            if (ImGui.BeginChild("ItemConfigEditorChild", ChildSizeRight, true))
                ImGui.EndChild();

            return;
        }

        // 基本信息获取
        var item = itemConfig.ItemID == 0 ? null : LuminaCache.GetRow<Item>(itemConfig.ItemID);
        var itemName = itemConfig.ItemID == 0
                           ? Service.Lang.GetText("AutoRetainerPriceAdjust-CommonItemPreset")
                           : item.Name.RawString;

        var itemLogo = ImageHelper.GetIcon(itemConfig.ItemID == 0 ? 65002 : (uint)item.Icon, itemConfig.IsHQ);

        var itemBuyingPrice = itemConfig.ItemID == 0 ? 1 : item.PriceLow;

        using (ImRaii.Child("ItemConfigEditorChild", ChildSizeRight, true))
        {
            // 物品基本信息展示
            ImGui.Image(itemLogo.ImGuiHandle, ScaledVector2(48f));

            ImGui.SameLine();
            using (FontManager.UIFont140.Push())
            {
                ImGui.Text(itemName);
            }

            ImGui.SameLine();
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (6f * GlobalFontScale));
            ImGui.Text(itemConfig.IsHQ ? $"({Service.Lang.GetText("HQ")})" : "");

            ImGui.Separator();

            // 改价逻辑配置
            var radioButtonHeight = 24f;
            using (ImRaii.Group())
            {
                foreach (AdjustBehavior behavior in Enum.GetValues(typeof(AdjustBehavior)))
                {
                    if (ImGui.RadioButton(behavior.ToString(), behavior == itemConfig.AdjustBehavior))
                    {
                        itemConfig.AdjustBehavior = behavior;
                        SaveConfig(ModuleConfig);
                    }

                    radioButtonHeight = ImGui.GetItemRectSize().Y;
                }
            }

            ImGui.SameLine();
            using (ImRaii.Group())
            {
                if (itemConfig.AdjustBehavior == AdjustBehavior.固定值)
                {
                    var originalValue = itemConfig.AdjustValues[AdjustBehavior.固定值];
                    ImGui.SetNextItemWidth(100f * GlobalFontScale);
                    ImGui.InputInt(Service.Lang.GetText("AutoRetainerPriceAdjust-ValueReduction"), ref originalValue);

                    if (ImGui.IsItemDeactivatedAfterEdit())
                    {
                        itemConfig.AdjustValues[AdjustBehavior.固定值] = originalValue;
                        SaveConfig(ModuleConfig);
                    }
                }
                else ImGui.Dummy(new(radioButtonHeight));

                if (itemConfig.AdjustBehavior == AdjustBehavior.百分比)
                {
                    var originalValue = itemConfig.AdjustValues[AdjustBehavior.百分比];
                    ImGui.SetNextItemWidth(100f * GlobalFontScale);
                    ImGui.InputInt(Service.Lang.GetText("AutoRetainerPriceAdjust-PercentageReduction"), ref originalValue);

                    if (ImGui.IsItemDeactivatedAfterEdit())
                    {
                        itemConfig.AdjustValues[AdjustBehavior.百分比] = Math.Clamp(originalValue, -99, 99);
                        SaveConfig(ModuleConfig);
                    }
                }
                else ImGui.Dummy(new(radioButtonHeight));
            }

            ImGui.Dummy(ScaledVector2(10f));

            // 最低可接受价格
            var originalMin = itemConfig.PriceMinimum;
            ImGui.SetNextItemWidth(200f * GlobalFontScale);
            ImGui.InputInt(Service.Lang.GetText("AutoRetainerPriceAdjust-PriceMinimum"), ref originalMin);

            if (ImGui.IsItemDeactivatedAfterEdit())
            {
                itemConfig.PriceMinimum = Math.Max(1, originalMin);
                SaveConfig(ModuleConfig);
            }

            ImGui.SameLine();
            using (ImRaii.Disabled(itemConfig.ItemID == 0))
            {
                if (ImGuiOm.ButtonIcon("ObtainBuyingPrice", FontAwesomeIcon.Store,
                                       Service.Lang.GetText("AutoRetainerPriceAdjust-ObtainBuyingPrice")))
                {
                    itemConfig.PriceMinimum = Math.Max(1, (int)itemBuyingPrice);
                    SaveConfig(ModuleConfig);
                }
            }

            // 最高可接受价格
            var originalMax = itemConfig.PriceMaximum;
            ImGui.SetNextItemWidth(200f * GlobalFontScale);
            ImGui.InputInt(Service.Lang.GetText("AutoRetainerPriceAdjust-PriceMaximum"), ref originalMax);
            if (ImGui.IsItemDeactivatedAfterEdit())
            {
                itemConfig.PriceMaximum = Math.Min(int.MaxValue, originalMax);
                SaveConfig(ModuleConfig);
            }

            // 预期价格
            var originalExpected = itemConfig.PriceExpected;
            ImGui.SetNextItemWidth(200f * GlobalFontScale);
            ImGui.InputInt(Service.Lang.GetText("AutoRetainerPriceAdjust-PriceExpected"), ref originalExpected);
            if (ImGui.IsItemDeactivatedAfterEdit())
            {
                itemConfig.PriceExpected = Math.Max(originalMin + 1, originalExpected);
                SaveConfig(ModuleConfig);
            }

            ImGui.SameLine();
            using (ImRaii.Disabled(itemConfig.ItemID == 0))
            {
                if (ImGuiOm.ButtonIcon("OpenUniversalis", FontAwesomeIcon.Globe,
                                       Service.Lang.GetText("AutoRetainerPriceAdjust-OpenUniversalis")))
                    Util.OpenLink($"https://universalis.app/market/{itemConfig.ItemID}");
            }

            // 可接受降价值
            var originalPriceReducion = itemConfig.PriceMaxReduction;
            ImGui.SetNextItemWidth(200f * GlobalFontScale);
            ImGui.InputInt(Service.Lang.GetText("AutoRetainerPriceAdjust-PriceMaxReduction"), ref originalPriceReducion);
            if (ImGui.IsItemDeactivatedAfterEdit())
            {
                itemConfig.PriceMaxReduction = Math.Max(0, originalPriceReducion);
                SaveConfig(ModuleConfig);
            }

            ImGui.Dummy(ScaledVector2(10f));

            // 意外情况
            using (ImRaii.Group())
            {
                ImGui.SetNextItemWidth(250f * GlobalFontScale);
                if (ImGui.BeginCombo("###AddNewLogicConditionCombo", CondtionInput.ToString(), ImGuiComboFlags.HeightLarge))
                {
                    foreach (AbortCondition condition in Enum.GetValues(typeof(AbortCondition)))
                    {
                        if (condition == AbortCondition.无) continue;
                        if (ImGui.Selectable(condition.ToString(), CondtionInput.HasFlag(condition),
                                             ImGuiSelectableFlags.DontClosePopups))
                        {
                            var combinedCondition = CondtionInput;
                            if (CondtionInput.HasFlag(condition))
                                combinedCondition &= ~condition;
                            else
                                combinedCondition |= condition;

                            CondtionInput = combinedCondition;
                        }
                    }

                    ImGui.EndCombo();
                }

                ImGui.SetNextItemWidth(250f * GlobalFontScale);
                if (ImGui.BeginCombo("###AddNewLogicBehaviorCombo", BehaviorInput.ToString(), ImGuiComboFlags.HeightLarge))
                {
                    foreach (AbortBehavior behavior in Enum.GetValues(typeof(AbortBehavior)))
                        if (ImGui.Selectable(behavior.ToString(), BehaviorInput == behavior,
                                             ImGuiSelectableFlags.DontClosePopups))
                            BehaviorInput = behavior;

                    ImGui.EndCombo();
                }
            }

            var groupSize0 = ImGui.GetItemRectSize();

            ImGui.SameLine();
            if (ImGuiOm.ButtonIconWithTextVertical(FontAwesomeIcon.Plus, Service.Lang.GetText("Add"),
                                                   buttonSize:new(ImGui.CalcTextSize("三个字").X, groupSize0.Y)))
            {
                if (CondtionInput != AbortCondition.无)
                {
                    itemConfig.AbortLogic.TryAdd(CondtionInput, BehaviorInput);
                    SaveConfig(ModuleConfig);
                }
            }

            ImGui.Separator();

            foreach (var logic in itemConfig.AbortLogic.ToList())
            {
                // 条件处理 (键)
                var origConditionStr = logic.Key.ToString();
                ImGui.SetNextItemWidth(300f * GlobalFontScale);
                ImGui.InputText($"###Condition_{origConditionStr}", ref origConditionStr, 100,
                                ImGuiInputTextFlags.ReadOnly);

                if (ImGui.IsItemClicked())
                    ImGui.OpenPopup($"###ConditionSelectPopup_{origConditionStr}");

                if (ImGui.BeginPopup($"###ConditionSelectPopup_{origConditionStr}"))
                {
                    foreach (AbortCondition condition in Enum.GetValues(typeof(AbortCondition)))
                        if (ImGui.Selectable(condition.ToString(), logic.Key.HasFlag(condition)))
                        {
                            var combinedCondition = logic.Key;
                            if (logic.Key.HasFlag(condition))
                                combinedCondition &= ~condition;
                            else
                                combinedCondition |= condition;

                            if (!itemConfig.AbortLogic.ContainsKey(combinedCondition))
                            {
                                var origBehavior = logic.Value;
                                itemConfig.AbortLogic[combinedCondition] = origBehavior;
                                itemConfig.AbortLogic.Remove(logic.Key);
                                SaveConfig(ModuleConfig);
                            }
                        }

                    ImGui.EndPopup();
                }

                ImGui.SameLine();
                ImGui.Text("→");

                // 行为处理 (值)
                ImGui.SameLine();
                var origBehaviorStr = logic.Value.ToString();
                ImGui.SetNextItemWidth(300f * GlobalFontScale);
                ImGui.InputText($"###Behavior_{origBehaviorStr}", ref origBehaviorStr, 100,
                                ImGuiInputTextFlags.ReadOnly);

                if (ImGui.IsItemClicked())
                    ImGui.OpenPopup($"###BehaviorSelectPopup_{origBehaviorStr}");

                if (ImGui.BeginPopup($"###BehaviorSelectPopup_{origBehaviorStr}"))
                {
                    foreach (AbortBehavior behavior in Enum.GetValues(typeof(AbortBehavior)))
                        if (ImGui.Selectable(behavior.ToString(), behavior == logic.Value))
                        {
                            itemConfig.AbortLogic[logic.Key] = behavior;
                            SaveConfig(ModuleConfig);
                        }

                    ImGui.EndPopup();
                }

                ImGui.SameLine();
                if (ImGuiOm.ButtonIcon($"Delete_{logic.Key}_{logic.Value}", FontAwesomeIcon.TrashAlt, Service.Lang.GetText("Delete")))
                    itemConfig.AbortLogic.Remove(logic.Key);
            }
        }
    }

    public override void OverlayUI()
    {
        var activeAddon = RetainerSellList != null ? RetainerSellList :
                          RetainerList != null ? RetainerList : null;

        if (activeAddon == null) return;

        var pos = new Vector2(activeAddon->GetX() - ImGui.GetWindowSize().X,
                              activeAddon->GetY() + 6);

        ImGui.SetWindowPos(pos);

        ImGui.Dummy(new(200f * GlobalFontScale, 0f));

        ImGui.BeginDisabled(TaskHelper.IsBusy || activeAddon != RetainerList);

        if (ImGui.CollapsingHeader(Service.Lang.GetText("AutoRetainerRefreshTitle")))
        {
            if (ImGui.Button($"{Service.Lang.GetText("Start")}###Refresh"))
                EnqueueRetainersRefresh();

            ImGui.SameLine();
            if (ImGui.Button($"{Service.Lang.GetText("Stop")}###Refresh"))
                TaskHelper.Abort();
        }

        if (ImGui.CollapsingHeader(Service.Lang.GetText("AutoWithdrawRetainersGilsTitle")))
        {
            if (ImGui.Button($"{Service.Lang.GetText("Start")}###WithDraw"))
                EnqueueRetainersGilWithdraw();

            ImGui.SameLine();
            if (ImGui.Button($"{Service.Lang.GetText("Stop")}###WithDraw"))
                TaskHelper.Abort();
        }

        if (ImGui.CollapsingHeader(Service.Lang.GetText("AutoShareRetainersGilsEvenlyTitle")))
        {
            if (ImGui.Button($"{Service.Lang.GetText("Start")}###Share"))
                EnqueueRetainersGilShare();

            ImGui.SameLine();
            if (ImGui.Button($"{Service.Lang.GetText("Stop")}###Share"))
                TaskHelper.Abort();

            if (ImGui.RadioButton(Service.Lang.GetText("AutoShareRetainersGilsEvenly-Method1"),
                                  ref ModuleConfig.AdjustMethod, 0))
                SaveConfig(ModuleConfig);

            ImGui.SameLine();
            if (ImGui.RadioButton(Service.Lang.GetText("AutoShareRetainersGilsEvenly-Method2"),
                                  ref ModuleConfig.AdjustMethod, 1))
                SaveConfig(ModuleConfig);

            ImGui.SameLine();
            ImGuiOm.HelpMarker(Service.Lang.GetText("AutoShareRetainersGilsEvenly-Help"));
        }

        if (ImGui.CollapsingHeader(Service.Lang.GetText("AutoRetainerEntrustDupsTitle")))
        {
            if (ImGui.Button($"{Service.Lang.GetText("Start")}###Entrust"))
                EnqueueRetainersEntrustDups();

            ImGui.SameLine();
            if (ImGui.Button($"{Service.Lang.GetText("Stop")}###Entrust"))
                TaskHelper.Abort();
        }

        ImGui.EndDisabled();

        // 改价
        if (ImGui.CollapsingHeader(Service.Lang.GetText("AutoRetainerPriceAdjustTitle")))
        {
            ImGui.BeginDisabled(activeAddon == RetainerSellList);
            ImGui.PushID("AdjustPrice-AllRetainers");
            ImGui.TextColored(ImGuiColors.DalamudYellow,
                              Service.Lang.GetText("AutoRetainerPriceAdjust-AdjustForRetainers"));

            ImGui.Separator();

            ImGui.BeginDisabled(TaskHelper.IsBusy);
            if (ImGui.Button(Service.Lang.GetText("Start"))) EnqueueRetainersPriceAdjust();
            ImGui.EndDisabled();

            ImGui.SameLine();
            if (ImGui.Button(Service.Lang.GetText("Stop"))) TaskHelper.Abort();
            ImGui.PopID();
            ImGui.EndDisabled();

            ImGui.BeginDisabled(activeAddon == RetainerList);
            ImGui.PushID("AdjustPrice-SellList");
            ImGui.TextColored(ImGuiColors.DalamudYellow,
                              Service.Lang.GetText("AutoRetainerPriceAdjust-AdjustForListItems"));

            ImGui.Separator();

            ImGui.BeginDisabled(TaskHelper.IsBusy);
            if (ImGui.Button(Service.Lang.GetText("Start"))) EnqueueItemsPriceAdjust();
            ImGui.EndDisabled();

            ImGui.SameLine();
            if (ImGui.Button(Service.Lang.GetText("Stop"))) TaskHelper.Abort();
            ImGui.PopID();
            ImGui.EndDisabled();

            ImGui.Dummy(new(6f * GlobalFontScale));

            if (ImGui.Checkbox(Service.Lang.GetText("AutoRetainerPriceAdjust-SendProcessMessage"),
                               ref ModuleConfig.SendProcessMessage))
                SaveConfig(ModuleConfig);

            ImGui.SetNextItemWidth(50f * GlobalFontScale);
            ImGui.InputInt(Service.Lang.GetText("AutoRetainerPriceAdjust-OperationDelay"),
                           ref ModuleConfig.OperationDelay, 0, 0);

            if (ImGui.IsItemDeactivatedAfterEdit())
                SaveConfig(ModuleConfig);

            if (ImGui.Selectable(Service.Lang.GetText("AutoRetainerPriceAdjust-ClearPriceCache")))
                Cache.Clear();

            ImGuiOm.TooltipHover(Service.Lang.GetText("AutoRetainerPriceAdjust-ClearPriceCacheHelp"));
        }
    }

    #endregion

    #region 点击

    private bool? ClickSpecificRetainer(int index)
    {
        if (InterruptByConflictKey()) return true;
        if (SelectYesno != null && IsAddonAndNodesReady(SelectYesno))
        {
            Click.SendClick("select_yes");
            return false;
        }

        if (RetainerList == null || !IsAddonAndNodesReady(RetainerList)) return false;

        ClickRetainerList.Using((nint)RetainerList).Retainer(index);
        return true;
    }

    // 确认探险完成
    private bool? ClickRetainerFinishedVenture()
    {
        if (InterruptByConflictKey()) return true;
        if (SelectString == null || !IsAddonAndNodesReady(SelectString)) return false;

        if (!TryScanSelectStringText(SelectString, "结束", out var index))
        {
            TaskHelper.Abort();
            TaskHelper.Enqueue(ReturnToRetainerList);
            return true;
        }

        return Click.TrySendClick($"select_string{index + 1}");
    }

    // 进入出售列表
    private bool? ClickToEnterSellList()
    {
        if (InterruptByConflictKey()) return true;

        if (SelectString == null || !IsAddonAndNodesReady(SelectString)) return false;
        if (!TryScanSelectStringText(SelectString, LuminaCache.GetRow<Addon>(2383).Text.RawString,
                                     out var returnIndex) ||
            !TryScanSelectStringText(SelectString, "出售", out var index))
        {
            TaskHelper.Abort();
            if (returnIndex != -1) TaskHelper.Enqueue(() => Click.TrySendClick($"select_string{returnIndex + 1}"));

            return true;
        }

        return Click.TrySendClick($"select_string{index + 1}");
    }

    // 点击出售列表中的物品
    private bool? ClickItemInSellList(int index)
    {
        if (RetainerSellList == null || !IsAddonAndNodesReady(RetainerSellList)) return false;

        CurrentItemIndex = index;
        AgentHelper.SendEvent(AgentId.Retainer, 3, 0, index, 1);
        TaskHelper.Enqueue(ClickAdjustPrice, null, 1);

        return true;
    }

    // 点击右键菜单中的 修改价格
    private bool? ClickAdjustPrice()
    {
        if (InterruptByConflictKey()) return true;
        if (!ClickHelper.ContextMenu(LuminaCache.GetRow<Addon>(6948).Text.RawString)) return false;

        ResetCurrentItemStats(false);
        TaskHelper.Enqueue(ObtainItemData, null, 1);
        return true;
    }

    // 填入价格
    private bool? FillPrice()
    {
        if (InterruptByConflictKey()) return true;

        if (ItemSearchResult != null) return false;
        if (RetainerSell == null || !IsAddonAndNodesReady(RetainerSell)) return false;

        var itemDetails = GetItemDetails();
        if (!TryGetPriceCache(CurrentItem.ItemID, CurrentItem.IsHQ, out var marketPrice) &&
            !TryGetPriceCache(CurrentItem.ItemID, !CurrentItem.IsHQ, out marketPrice))
        {
            if (ModuleConfig.SendProcessMessage)
            {
                var message = new SeStringBuilder().Append(DRPrefix).Append(
                    Service.Lang.GetSeString("AutoRetainerPriceAdjust-NoPriceDataFound",
                                             itemDetails.ItemPayload, itemDetails.RetainerName)).Build();

                Service.Chat.Print(message);
            }

            OperateAndReturn(false);
            return true;
        }

        var modifiedPrice = itemDetails.Preset.AdjustBehavior switch
        {
            AdjustBehavior.固定值 => (uint)((int)marketPrice - itemDetails.Preset.AdjustValues[AdjustBehavior.固定值]),
            AdjustBehavior.百分比 => (uint)(marketPrice *
                                         (1 - (itemDetails.Preset.AdjustValues[AdjustBehavior.百分比] / 100))),
            _ => marketPrice,
        };

        #region 意外情况判断

        // 价格不变
        if (modifiedPrice == itemDetails.OrigPrice)
        {
            OperateAndReturn(false);
            return true;
        }

        // 高于最大值
        if (modifiedPrice > itemDetails.Preset.PriceMaximum &&
            itemDetails.Preset.AbortLogic.Keys.Any(x => x.HasFlag(AbortCondition.高于最大值)))
        {
            var behavior = itemDetails.Preset.AbortLogic.FirstOrDefault(x => x.Key.HasFlag(AbortCondition.高于最大值)).Value;
            NotifyAbortCondition(AbortCondition.高于最大值);
            EnqueueAbortBehavior(behavior);
            return true;
        }

        // 高于预期值
        if (modifiedPrice > itemDetails.Preset.PriceExpected &&
            itemDetails.Preset.AbortLogic.Keys.Any(x => x.HasFlag(AbortCondition.高于预期值)))
        {
            var behavior = itemDetails.Preset.AbortLogic.FirstOrDefault(x => x.Key.HasFlag(AbortCondition.高于预期值)).Value;
            NotifyAbortCondition(AbortCondition.高于预期值);
            EnqueueAbortBehavior(behavior);
            return true;
        }

        // 大于可接受降价值
        if (itemDetails.Preset.PriceMaxReduction != 0 && itemDetails.OrigPrice - modifiedPrice > 0 &&
            itemDetails.OrigPrice - modifiedPrice > itemDetails.Preset.PriceMaxReduction &&
            itemDetails.Preset.AbortLogic.Keys.Any(x => x.HasFlag(AbortCondition.大于可接受降价值)))
        {
            var behavior = itemDetails.Preset.AbortLogic.FirstOrDefault(x => x.Key.HasFlag(AbortCondition.大于可接受降价值))
                                      .Value;

            NotifyAbortCondition(AbortCondition.大于可接受降价值);
            EnqueueAbortBehavior(behavior);
            return true;
        }

        // 低于收购价
        if (ItemsSellPrice.TryGetValue(CurrentItem.ItemID, out var buyingPrice) &&
            modifiedPrice < buyingPrice &&
            itemDetails.Preset.AbortLogic.Keys.Any(x => x.HasFlag(AbortCondition.低于收购价)))
        {
            var behavior = itemDetails.Preset.AbortLogic.FirstOrDefault
                (x => x.Key.HasFlag(AbortCondition.低于收购价)).Value;

            NotifyAbortCondition(AbortCondition.低于收购价);
            EnqueueAbortBehavior(behavior);
            return true;
        }

        // 低于最小值
        if (modifiedPrice < itemDetails.Preset.PriceMinimum &&
            itemDetails.Preset.AbortLogic.Keys.Any(x => x.HasFlag(AbortCondition.低于最小值)))
        {
            var behavior = itemDetails.Preset.AbortLogic.FirstOrDefault(x => x.Key.HasFlag(AbortCondition.低于最小值)).Value;
            NotifyAbortCondition(AbortCondition.低于最小值);
            EnqueueAbortBehavior(behavior);
            return true;
        }

        // 低于预期值
        if (modifiedPrice < itemDetails.Preset.PriceExpected &&
            itemDetails.Preset.AbortLogic.Keys.Any(x => x.HasFlag(AbortCondition.低于预期值)))
        {
            var behavior = itemDetails.Preset.AbortLogic.FirstOrDefault(x => x.Key.HasFlag(AbortCondition.低于预期值)).Value;
            NotifyAbortCondition(AbortCondition.低于预期值);
            EnqueueAbortBehavior(behavior);
            return true;
        }

        #endregion

        OperateAndReturn(true, modifiedPrice);
        return true;

        (ItemConfig Preset, int OrigPrice, SeString ItemPayload, SeString RetainerName) GetItemDetails()
        {
            (ItemConfig Preset, int OrigPrice, SeString ItemPayload, SeString RetainerName) result = new()
            {
                Preset = ModuleConfig.ItemConfigs.TryGetValue(CurrentItem.ToString(), out var itemConfig)
                             ? itemConfig
                             : ModuleConfig.ItemConfigs[new ItemKey(0, CurrentItem.IsHQ).ToString()],
                OrigPrice = RetainerSell->AtkValues[5].Int,
                ItemPayload = new SeStringBuilder().AddItemLink(CurrentItem.ItemID, CurrentItem.IsHQ).Build(),
                RetainerName = new SeStringBuilder()
                               .AddUiForeground(RetainerManager.Instance()->GetActiveRetainer()->NameString, 62).Build(),
            };

            return result;
        }

        void OperateAndReturn(bool isConfirm, uint price = 0)
        {
            var UI = (AddonRetainerSell*)RetainerSell;
            var priceComponent = UI->AskingPrice;
            var handler = new ClickRetainerSell();

            if (isConfirm && price != 0 && itemDetails.OrigPrice != price)
            {
                if (ModuleConfig.SendProcessMessage)
                {
                    var message = Service.Lang.GetSeString("AutoRetainerPriceAdjust-PriceAdjustSuccessfully",
                                                           itemDetails.ItemPayload, itemDetails.RetainerName,
                                                           itemDetails.OrigPrice, price);

                    Service.Chat.Print(new SeStringBuilder().Append(DRPrefix).Append(message).Build());
                }

                priceComponent->SetValue((int)price);
                handler.Confirm();
            }
            else handler.Decline();

            RetainerSell->Close(true);
            ResetCurrentItemStats(true);
        }

        void CloseAddon()
        {
            ClickRetainerSell.Using((nint)RetainerList).Decline();
            RetainerSell->Close(true);
        }

        void EnqueueAbortBehavior(AbortBehavior behavior)
        {
            if (ModuleConfig.SendProcessMessage)
            {
                var behaviorMessage = new SeStringBuilder().AddUiForeground(behavior.ToString(), 67).Build();
                var message = Service.Lang.GetSeString("AutoRetainerPriceAdjust-ConductAbortBehavior", behaviorMessage);
                Service.Chat.Print(message);
            }

            if (behavior == AbortBehavior.无)
            {
                OperateAndReturn(false);
                return;
            }

            switch (behavior)
            {
                case AbortBehavior.改价至最小值:
                    OperateAndReturn(true, (uint)itemDetails.Preset.PriceMinimum);
                    break;
                case AbortBehavior.改价至预期值:
                    OperateAndReturn(true, (uint)itemDetails.Preset.PriceExpected);
                    break;
                case AbortBehavior.改价至最高值:
                    OperateAndReturn(true, (uint)itemDetails.Preset.PriceMaximum);
                    break;
                case AbortBehavior.收回至雇员:
                    CloseAddon();
                    if (CurrentItemIndex != -1)
                    {
                        var copyIndex = CurrentItemIndex;
                        TaskHelper.Enqueue(() =>
                        {
                            if (RetainerSellList == null || !IsAddonAndNodesReady(RetainerSellList)) return false;

                            Callback(RetainerSellList, true, 0, copyIndex, 1);
                            return TryGetAddonByName<AtkUnitBase>("ContextMenu", out var cm) &&
                                   IsAddonAndNodesReady(cm);
                        }, null, 1);

                        TaskHelper.DelayNext(50, false, 1);
                        TaskHelper.Enqueue(
                            () => ClickHelper.ContextMenu(LuminaCache.GetRow<Addon>(958).Text.RawString), null, 1);
                    }

                    TaskHelper.Enqueue(() => OperateAndReturn(false), null, 1);
                    break;
                case AbortBehavior.收回至背包:
                    CloseAddon();
                    if (CurrentItemIndex != -1)
                    {
                        var copyIndex = CurrentItemIndex;
                        TaskHelper.Enqueue(() =>
                        {
                            if (RetainerSellList == null || !IsAddonAndNodesReady(RetainerSellList)) return false;

                            Callback(RetainerSellList, true, 0, copyIndex, 1);
                            return TryGetAddonByName<AtkUnitBase>("ContextMenu", out var cm) &&
                                   IsAddonAndNodesReady(cm);
                        }, null, 1);

                        TaskHelper.DelayNext(50, false, 1);
                        TaskHelper.Enqueue(
                            () => ClickHelper.ContextMenu(LuminaCache.GetRow<Addon>(976).Text.RawString), null, 1);
                    }

                    TaskHelper.Enqueue(() => OperateAndReturn(false), null, 1);
                    break;
                case AbortBehavior.出售至系统商店:
                    CloseAddon();
                    if (CurrentItemIndex != -1)
                    {
                        var copyIndex = CurrentItemIndex;
                        TaskHelper.Enqueue(() =>
                        {
                            if (RetainerSellList == null || !IsAddonAndNodesReady(RetainerSellList)) return false;

                            Callback(RetainerSellList, true, 0, copyIndex, 1);
                            return TryGetAddonByName<AtkUnitBase>("ContextMenu", out var cm) &&
                                   IsAddonAndNodesReady(cm);
                        }, null, 1);

                        TaskHelper.Enqueue(() => ClickHelper.ContextMenu(LuminaCache.GetRow<Addon>(976).Text.RawString), null, 1);
                        TaskHelper.DelayNext(500, false, 1);
                    }

                    TaskHelper.Enqueue(() =>
                    {
                        if (!TrySearchItemInInventory(CurrentItem.ItemID, CurrentItem.IsHQ, out var foundItem) ||
                            foundItem.Count <= 0)
                        {
                            TaskHelper.Enqueue(() => OperateAndReturn(false), null, 2);
                            return true;
                        }

                        TaskHelper.Enqueue(() =>
                        {
                            OpenInventoryItemContext(foundItem[0]);
                            return TryGetAddonByName<AtkUnitBase>("ContextMenu", out var cm) &&
                                   IsAddonAndNodesReady(cm);
                        }, null, 2);

                        TaskHelper.Enqueue(
                            () => ClickHelper.ContextMenu(LuminaCache.GetRow<Addon>(5480).Text.RawString), null, 2);

                        TaskHelper.Enqueue(() => OperateAndReturn(false), null, 2);

                        return true;
                    }, null, 1);

                    break;
            }
        }

        void NotifyAbortCondition(AbortCondition condition)
        {
            if (!ModuleConfig.SendProcessMessage) return;
            var conditionMessage = new SeStringBuilder().AddUiForeground(condition.ToString(), 60).Build();
            var message = Service.Lang.GetSeString("AutoRetainerPriceAdjust-DetectAbortCondition",
                                                   itemDetails.ItemPayload, itemDetails.RetainerName, conditionMessage);

            Service.Chat.Print(new SeStringBuilder().Append(DRPrefix).Append(message).Build());
        }
    }

    private static bool? ExitRetainerInventory()
    {
        var agent = AgentModule.Instance()->GetAgentByInternalId(AgentId.Retainer);
        var agent2 = AgentModule.Instance()->GetAgentByInternalId(AgentId.Inventory);
        if (agent == null || agent2 == null || !agent->IsAgentActive()) return false;

        var addon = RaptureAtkUnitManager.Instance()->GetAddonById((ushort)agent->GetAddonId());
        var addon2 = RaptureAtkUnitManager.Instance()->GetAddonById((ushort)agent2->GetAddonId());

        if (addon != null) addon->Close(true);
        if (addon2 != null) AddonHelper.Callback(addon2, true, -1);
        return true;
    }

    // 返回雇员列表
    private bool? ReturnToRetainerList()
    {
        if (InterruptByConflictKey()) return true;
        if (RetainerList != null && IsAddonAndNodesReady(RetainerList)) return true;

        if (SelectString == null || !IsAddonAndNodesReady(SelectString)) return false;
        return ClickHelper.SelectString(LuminaCache.GetRow<Addon>(2383).Text.RawString);
    }

    #endregion

    #region 队列

    private void EnqueueRetainersCollect()
    {
        if (InterruptByConflictKey()) return;

        var retainerManager = RetainerManager.Instance();
        var serverTime = Framework.GetServerTime();
        for (var i = 0; i < retainerManager->GetRetainerCount(); i++)
        {
            var retainerState = retainerManager->GetRetainerBySortedIndex((uint)i)->VentureComplete;
            if (retainerState == 0) continue;
            if (retainerState - serverTime <= 0)
            {
                var index = i;
                TaskHelper.Enqueue(() => ClickSpecificRetainer(index));
                TaskHelper.Enqueue(ClickRetainerFinishedVenture);
                TaskHelper.Enqueue(() => Click.TrySendClick("retainer_venture_result_reassign"));
                TaskHelper.Enqueue(() => Click.TrySendClick("retainer_venture_ask_assign"));
                TaskHelper.Enqueue(ReturnToRetainerList);
                break;
            }
        }
    }

    private void EnqueueRetainersPriceAdjust()
    {
        var retainerManager = RetainerManager.Instance();

        for (var i = 0; i < PlayerRetainers.Count; i++)
        {
            var index = i;
            var marketItemCount = retainerManager->GetRetainerBySortedIndex((uint)i)->MarketItemCount;
            if (marketItemCount <= 0) continue;

            TaskHelper.Enqueue(() => ClickSpecificRetainer(index));
            TaskHelper.Enqueue(ClickToEnterSellList);
            TaskHelper.Enqueue(() =>
            {
                if (RetainerSellList == null) return false;

                for (var i1 = 0; i1 < marketItemCount; i1++)
                {
                    var index1 = i1;
                    TaskHelper.Insert(() => ClickItemInSellList(index1));
                    if (ModuleConfig.OperationDelay > 0) TaskHelper.InsertDelayNext(ModuleConfig.OperationDelay);
                }

                return true;
            });

            TaskHelper.Enqueue(() =>
            {
                RetainerSellList->FireCloseCallback();
                RetainerSellList->Close(true);
            });

            TaskHelper.Enqueue(ReturnToRetainerList);
        }
    }

    private void EnqueueItemsPriceAdjust()
    {
        var retainerManager = RetainerManager.Instance();
        var marketItemCount = retainerManager->GetActiveRetainer()->MarketItemCount;

        if (marketItemCount == 0)
        {
            TaskHelper.Abort();
            return;
        }

        for (var i = 0; i < marketItemCount; i++)
        {
            var index = i;
            TaskHelper.Enqueue(() => ClickItemInSellList(index));
            if (ModuleConfig.OperationDelay > 0) TaskHelper.DelayNext(ModuleConfig.OperationDelay);
        }
    }

    private void EnqueueRetainersRefresh()
    {
        if (RetainerList == null || !IsAddonAndNodesReady(RetainerList)) return;

        var retainerManager = RetainerManager.Instance();
        for (var i = 0; i < retainerManager->GetRetainerCount(); i++)
        {
            var index = i;
            TaskHelper.Enqueue(() => RetainerList != null && IsAddonAndNodesReady(RetainerList));
            TaskHelper.Enqueue(() => ClickSpecificRetainer(index));
            TaskHelper.Enqueue(() => ClickHelper.SelectString(LuminaCache.GetRow<Addon>(2378).Text.RawString));
            TaskHelper.DelayNext(100);
            TaskHelper.Enqueue(ExitRetainerInventory);
            TaskHelper.Enqueue(() => ClickHelper.SelectString(LuminaCache.GetRow<Addon>(2383).Text.RawString));
        }
    }

    private void EnqueueRetainersGilWithdraw()
    {
        if (RetainerList == null || !IsAddonAndNodesReady(RetainerList)) return;

        var retainerManager = RetainerManager.Instance();
        var retainerCount = retainerManager->GetRetainerCount();

        var totalGilAmount = 0U;
        for (var i = 0U; i < retainerCount; i++) totalGilAmount += retainerManager->GetRetainerBySortedIndex(i)->Gil;

        if (totalGilAmount <= 0) return;

        for (var i = 0; i < retainerCount; i++)
        {
            if (retainerManager->GetRetainerBySortedIndex((uint)i)->Gil == 0) continue;

            var index = i;
            TaskHelper.Enqueue(() => ClickSpecificRetainer(index));

            TaskHelper.Enqueue(() => ClickHelper.SelectString(LuminaCache.GetRow<Addon>(2379).Text.RawString));

            TaskHelper.DelayNext(100);
            TaskHelper.Enqueue(() =>
            {
                if (Bank == null || !IsAddonAndNodesReady(Bank)) return false;

                var retainerGils = Bank->AtkValues[6].Int;
                var handler = new ClickBank();

                if (retainerGils == 0)
                    handler.Cancel();
                else
                {
                    handler.DepositInput((uint)retainerGils);
                    handler.Confirm();
                }

                Bank->Close(true);
                return true;
            });

            TaskHelper.Enqueue(ReturnToRetainerList);
            TaskHelper.DelayNext(100);
        }
    }

    private void EnqueueRetainersGilShare()
    {
        if (RetainerList == null || !IsAddonAndNodesReady(RetainerList)) return;

        var retainerManager = RetainerManager.Instance();
        var retainerCount = retainerManager->GetRetainerCount();

        var totalGilAmount = 0U;
        for (var i = 0U; i < retainerCount; i++) totalGilAmount += retainerManager->GetRetainerBySortedIndex(i)->Gil;

        var avgAmount = (uint)Math.Floor(totalGilAmount / (double)retainerCount);
        Service.Log.Debug($"当前 {retainerCount} 个雇员共有 {totalGilAmount} 金币, 平均每个雇员 {avgAmount} 金币");

        if (avgAmount <= 1) return;

        switch (ModuleConfig.AdjustMethod)
        {
            case 0:
                for (var i = 0; i < retainerCount; i++)
                {
                    EnqueueRetainersGilShareMethodFirst(i, avgAmount);
                    TaskHelper.DelayNext(100);
                }

                break;
            case 1:
                for (var i = 0; i < retainerCount; i++)
                {
                    EnqueueRetainersGilShareMethodSecond(i);
                    TaskHelper.DelayNext(100);
                }

                for (var i = 0; i < retainerCount; i++)
                {
                    EnqueueRetainersGilShareMethodFirst(i, avgAmount);
                    TaskHelper.DelayNext(100);
                }

                break;
        }
    }

    private void EnqueueRetainersGilShareMethodFirst(int index, uint avgAmount)
    {
        // 点击指定雇员
        TaskHelper.Enqueue(() => ClickSpecificRetainer(index));
        // 点击金币管理
        TaskHelper.Enqueue(() => ClickHelper.SelectString(LuminaCache.GetRow<Addon>(2379).Text.RawString));
        // 重新分配金币
        TaskHelper.DelayNext(100);
        TaskHelper.Enqueue(() =>
        {
            if (Bank == null || !IsAddonAndNodesReady(Bank)) return false;

            var retainerGils = Bank->AtkValues[6].Int;
            var handler = new ClickBank();

            if (retainerGils == avgAmount) // 金币恰好相等
            {
                handler.Cancel();
                Bank->Close(true);
                return true;
            }

            if (retainerGils > avgAmount) // 雇员金币多于平均值
            {
                handler.DepositInput((uint)(retainerGils - avgAmount));
                handler.Confirm();
                Bank->Close(true);
                return true;
            }

            // 雇员金币少于平均值
            handler.Switch();
            handler.DepositInput((uint)(avgAmount - retainerGils));
            handler.Confirm();
            Bank->Close(true);
            return true;
        });

        // 回到雇员列表
        TaskHelper.Enqueue(ReturnToRetainerList);
    }

    private void EnqueueRetainersGilShareMethodSecond(int index)
    {
        // 点击指定雇员
        TaskHelper.Enqueue(() => ClickSpecificRetainer(index));
        // 点击金币管理
        TaskHelper.Enqueue(() => ClickHelper.SelectString(LuminaCache.GetRow<Addon>(2379).Text.RawString));
        // 取出所有金币
        TaskHelper.DelayNext(100);
        TaskHelper.Enqueue(() =>
        {
            if (Bank == null || !IsAddonAndNodesReady(Bank)) return false;

            var retainerGils = Bank->AtkValues[6].Int;
            var handler = new ClickBank();

            if (retainerGils == 0)
                handler.Cancel();
            else
            {
                handler.DepositInput((uint)retainerGils);
                handler.Confirm();
            }

            Bank->Close(true);
            return true;
        });

        // 回到雇员列表
        TaskHelper.Enqueue(ReturnToRetainerList);
    }

    private void EnqueueRetainersEntrustDups()
    {
        if (RetainerList == null || !IsAddonAndNodesReady(RetainerList)) return;

        var retainerManager = RetainerManager.Instance();
        for (var i = 0; i < retainerManager->GetRetainerCount(); i++)
        {
            var index = i;
            TaskHelper.Enqueue(() => RetainerList != null && IsAddonAndNodesReady(RetainerList));
            TaskHelper.Enqueue(() => ClickSpecificRetainer(index));
            TaskHelper.Enqueue(() => ClickHelper.SelectString(LuminaCache.GetRow<Addon>(2378).Text.RawString));
            TaskHelper.Enqueue(() =>
            {
                if (!Throttler.Throttle("AutoRetainerEntrustDups", 100)) return false;
                var agent = AgentModule.Instance()->GetAgentByInternalId(AgentId.Retainer);
                if (agent == null || !agent->IsAgentActive()) return false;

                var addon = RaptureAtkUnitManager.Instance()->GetAddonById((ushort)agent->GetAddonId());
                if (addon == null) return false;

                AddonHelper.Callback(addon, true, 0);
                return true;
            });

            TaskHelper.DelayNext(500);
            TaskHelper.Enqueue(ExitRetainerInventory);
            TaskHelper.Enqueue(() => ClickHelper.SelectString(LuminaCache.GetRow<Addon>(2383).Text.RawString));
        }
    }

    private void EnqueueRetainersDispatch()
    {
        var addon = (AddonSelectString*)Service.Gui.GetAddonByName("SelectString");
        if (addon == null) return;

        var entryCount = addon->PopupMenu.PopupMenu.EntryCount;
        if (entryCount - 1 <= 0) return;

        for (var i = 0; i < entryCount - 1; i++)
        {
            var tempI = i;
            TaskHelper.Enqueue(() => Click.TrySendClick($"select_string{tempI + 1}"));
            TaskHelper.DelayNext(20);
            TaskHelper.Enqueue(() => Click.TrySendClick("select_yes"));

            TaskHelper.DelayNext(100);
        }
    }

    #endregion

    #region 数据获取

    private static void ObtainPlayerRetainers()
    {
        var retainerManager = RetainerManager.Instance();
        if (retainerManager == null) return;
        PlayerRetainers.Clear();

        for (var i = 0U; i < retainerManager->GetRetainerCount(); i++)
        {
            var retainer = retainerManager->GetRetainerBySortedIndex(i);
            if (retainer == null) break;

            PlayerRetainers.Add(retainer->RetainerId);
        }
    }

    // 获取物品数据
    private bool? ObtainItemData()
    {
        if (InterruptByConflictKey()) return true;
        if (!TryGetAddonByName<AtkUnitBase>("RetainerSell", out var addon) || !IsAddonAndNodesReady(addon))
            return false;

        var itemNameText = MemoryHelper.ReadSeStringNullTerminated((nint)addon->AtkValues[1].String).TextValue;
        if (string.IsNullOrWhiteSpace(itemNameText)) return false;

        var itemName = itemNameText.TrimEnd(''); // HQ 符号
        if (!ItemNames.TryGetValue(itemName, out var item))
        {
            Service.Chat.PrintError(Service.Lang.GetText("AutoRetainerPriceAdjust-FailObtainItemInfo"),
                                    "Daily Routines");

            TaskHelper.Abort();
            return true;
        }

        CurrentItem ??= new();
        CurrentItem.ItemID = item.RowId;
        CurrentItem.IsHQ = itemNameText.Contains(''); // HQ 符号
        InfoItemSearch->SearchItemId = CurrentItem.ItemID;

        TaskHelper.Enqueue(ObtainMarketData, null, 1);
        return true;
    }

    // 获取市场数据
    private bool? ObtainMarketData()
    {
        InfoItemSearch->ClearData();
        if (!Throttler.Throttle("AutoRetainerPriceAdjust-ObtainMarketData")) return false;
        if (InfoItemSearch->SearchItemId == 0)
        {
            NotifyHelper.ChatError(Service.Lang.GetText("AutoRetainerPriceAdjust-FailObtainItemInfo"));

            TaskHelper.Abort();
            return true;
        }

        if (TryGetPriceCache(CurrentItem.ItemID, CurrentItem.IsHQ, out _))
        {
            TaskHelper.Enqueue(FillPrice, null, 1);
            return true;
        }

        if (ItemHistoryList == null)
        {
            InfoItemSearch->RequestData();
            return false;
        }

        TaskHelper.DelayNext(1000, false, 1);
        TaskHelper.Enqueue(ParseMarketData, null, 1);
        return true;
    }

    // 解析市场数据
    private bool? ParseMarketData()
    {
        // 市场结果为空
        if (ItemSearchList is not { Count: > 0 })
        {
            // 历史结果为空
            if (ItemHistoryList.Count <= 0)
            {
                TaskHelper.Enqueue(FillPrice, null, 1);
                return true;
            }

            var maxPrice = ItemHistoryList.DefaultIfEmpty().Max(x => x.SalePrice);
            var maxHQPrice = ItemHistoryList.Where(x => x is { IsHq: true, OnMannequin: false }).DefaultIfEmpty()
                                            .Max(x => x.SalePrice);

            if (maxPrice != 0)
                SetPriceCache(CurrentItem.ItemID, false, maxPrice);

            if (maxHQPrice != 0)
                SetPriceCache(CurrentItem.ItemID, true, maxHQPrice);

            TaskHelper.Enqueue(FillPrice, null, 1);
            return true;
        }

        var nqItemsList = ItemSearchList
                          .Where(x => !PlayerRetainers.Contains(x.RetainerId) &&
                                      x is { PricePerUnit: > 0, OnMannequin: false })
                          .ToList();

        var minPrice = nqItemsList.Count != 0
                           ? nqItemsList.Min(x => x.PricePerUnit)
                           : 0;

        var hqItemsList = ItemSearchList
                          .Where(x => !PlayerRetainers.Contains(x.RetainerId) &&
                                      x is { PricePerUnit: > 0, IsHq: true, OnMannequin: false })
                          .ToList();

        var minHQPrice = hqItemsList.Count != 0
                             ? hqItemsList.Min(x => x.PricePerUnit)
                             : 0;

        if (minPrice > 0)
            SetPriceCache(CurrentItem.ItemID, false, minPrice);

        if (minHQPrice > 0)
            SetPriceCache(CurrentItem.ItemID, true, minHQPrice);

        TaskHelper.Enqueue(FillPrice, null, 1);
        return true;
    }

    #endregion

    #region 工具

    private static void ExportItemConfigToClipboard(ItemConfig config)
    {
        try
        {
            var json = JsonSerializer.Serialize(config);
            var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));

            Clipboard.SetText(base64);
            Service.Chat.Print(new SeStringBuilder().Append(DRPrefix)
                                                    .Append(
                                                        $" 已成功导出 {config.ItemName} {(config.IsHQ ? "(HQ) " : "")}的配置至剪贴板")
                                                    .Build());
        }
        catch (Exception ex)
        {
            Service.Chat.PrintError($"导出至剪贴板错误: {ex.Message}");
        }
    }

    private static ItemConfig? ImportItemConfigFromClipboard()
    {
        try
        {
            var base64 = Clipboard.GetText();

            if (!string.IsNullOrEmpty(base64))
            {
                var json = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
                var config = JsonSerializer.Deserialize<ItemConfig>(json);
                if (config != null)
                {
                    Service.Chat.Print(new SeStringBuilder().Append(DRPrefix)
                                                            .Append(
                                                                $" 已成功导入 {config.ItemName} {(config.IsHQ ? "(HQ) " : "")}的配置")
                                                            .Build());
                }

                return config;
            }
        }
        catch (Exception ex)
        {
            Service.Chat.PrintError($"从剪贴板导入配置时失败: {ex.Message}");
        }

        return null;
    }

    private static bool TrySearchItemInInventory(uint itemID, bool isHQ, out List<InventoryItem> foundItem)
    {
        foundItem = [];
        var inventoryManager = InventoryManager.Instance();
        if (inventoryManager == null) return false;

        foreach (var type in InventoryTypes)
        {
            var container = inventoryManager->GetInventoryContainer(type);
            if (container == null) return false;

            for (var i = 0; i < container->Size; i++)
            {
                var slot = container->GetInventorySlot(i);
                if (slot == null || slot->ItemId == 0) continue;
                if (slot->ItemId == itemID && (!isHQ || (isHQ && slot->Flags.HasFlag(InventoryItem.ItemFlags.HighQuality))))
                    foundItem.Add(*slot);
            }
        }

        return foundItem.Count > 0;
    }

    private static bool OpenInventoryItemContext(InventoryItem item)
    {
        if (!Throttler.Throttle("AutoDiscard", 100)) return false;
        var agent = AgentInventoryContext.Instance();
        if (agent == null) return false;

        agent->OpenForItemSlot(item.Container, item.Slot, GetActiveInventoryAddonID());
        return true;
    }

    private static uint GetActiveInventoryAddonID()
    {
        var inventory0 = (AtkUnitBase*)Service.Gui.GetAddonByName("Inventory");
        if (inventory0 == null) return 0;

        var inventory1 = (AtkUnitBase*)Service.Gui.GetAddonByName("InventoryLarge");
        if (inventory1 == null) return 0;

        var inventory2 = (AtkUnitBase*)Service.Gui.GetAddonByName("InventoryExpansion");
        if (inventory2 == null) return 0;

        if (IsAddonAndNodesReady(inventory0)) return inventory0->Id;
        if (IsAddonAndNodesReady(inventory1)) return inventory1->Id;
        if (IsAddonAndNodesReady(inventory2)) return inventory2->Id;

        return 0;
    }

    private static void ResetCurrentItemStats(bool isResetIndex)
    {
        InfoItemSearch->SearchItemId = 0;
        InfoItemSearch->ListingCount = 0;
        InfoItemSearch->ClearData();

        ItemHistoryList = null;
        ItemSearchList = null;
        CurrentItem = null;
        if (isResetIndex) CurrentItemIndex = -1;
    }

    #endregion

    #region Hooks

    // 历史交易数据获取
    private nint MarketboardHistorDetour(nint a1, nint packetData)
    {
        if (CurrentItem == null)
            return MarketboardHistoryHook.Original(a1, packetData);

        var data = MarketBoardHistory.Read(packetData);
        if (data.CatalogId != CurrentItem.ItemID)
            return MarketboardHistoryHook.Original(a1, packetData);

        ItemHistoryList ??= data.HistoryListings.ToList();
        return MarketboardHistoryHook.Original(a1, packetData);
    }

    // 当前市场数据获取
    private nint InfoProxyItemSearchAddPageDetour(byte* a1, byte* packetData)
    {
        if (CurrentItem == null)
            return InfoProxyItemSearchAddPageHook.Original(a1, packetData);

        var data = (IMarketBoardCurrentOfferings)MarketBoardCurrentOfferings.Read((nint)packetData);
        if (data.ItemListings.Count > 0 && data.ItemListings[0].ItemId == CurrentItem.ItemID)
        {
            ItemSearchList ??= [];
            ItemSearchList.AddRange(data.ItemListings);
        }

        return InfoProxyItemSearchAddPageHook.Original(a1, packetData);
    }

    #endregion

    #region 界面监控

    // 雇员列表
    private void OnRetainerList(AddonEvent type, AddonArgs args)
    {
        Overlay.IsOpen = type switch
        {
            AddonEvent.PostSetup => true,
            AddonEvent.PreFinalize => false,
            _ => Overlay.IsOpen,
        };

        switch (type)
        {
            case AddonEvent.PostSetup:
                if (InterruptByConflictKey()) return;
                ObtainPlayerRetainers();
                EnqueueRetainersCollect();

                Service.AddonLifecycle.RegisterListener(AddonEvent.PostUpdate, "RetainerList", OnRetainerList);
                break;
            case AddonEvent.PostUpdate:
                if (Throttler.Throttle("AutoRetainerCollect-AFK", 5000))
                {
                    if (InterruptByConflictKey() || TaskHelper.IsBusy ||
                        !IsAddonAndNodesReady((AtkUnitBase*)args.Addon)) return;

                    Service.Framework.RunOnTick(EnqueueRetainersCollect, TimeSpan.FromSeconds(1));
                }

                break;
            case AddonEvent.PreFinalize:
                Service.AddonLifecycle.UnregisterListener(AddonEvent.PostUpdate, "RetainerList", OnRetainerList);
                break;
        }
    }

    // 出售品列表 (悬浮窗控制)
    private void OnRetainerSellList(AddonEvent type, AddonArgs args)
    {
        Overlay.IsOpen = type switch
        {
            AddonEvent.PostSetup => true,
            AddonEvent.PreFinalize => false,
            _ => Overlay.IsOpen,
        };
    }

    // 出售详情界面
    private void OnRetainerSell(AddonEvent eventType, AddonArgs addonInfo)
    {
        switch (eventType)
        {
            case AddonEvent.PostSetup:
                if (InterruptByConflictKey()) return;
                if (TaskHelper.IsBusy) return;

                ResetCurrentItemStats(true);
                TaskHelper.Enqueue(ObtainItemData);
                break;
        }
    }

    private void OnEntrustDupsAddons(AddonEvent type, AddonArgs args)
    {
        if (!TaskHelper.IsBusy) return;
        switch (args.AddonName)
        {
            case "RetainerItemTransferList":
                Callback((AtkUnitBase*)args.Addon, true, 1);
                break;
            case "RetainerItemTransferProgress":
                TaskHelper.Enqueue(() =>
                {
                    if (!Throttler.Throttle("AutoRetainerEntrustDups", 100)) return false;
                    if (!TryGetAddonByName<AtkUnitBase>("RetainerItemTransferProgress", out var addon) ||
                        !IsAddonAndNodesReady(addon)) return false;

                    var progressText = MemoryHelper.ReadSeStringNullTerminated((nint)addon->AtkValues[0].String)
                                                   .ExtractText();

                    if (string.IsNullOrWhiteSpace(progressText)) return false;

                    if (progressText.Contains(LuminaCache.GetRow<Addon>(13528).Text.RawString))
                    {
                        Callback(addon, true, -2);
                        addon->Close(true);
                        return true;
                    }

                    return false;
                }, null, 1);

                break;
        }
    }

    #endregion

    public override void Uninit()
    {
        Service.AddonLifecycle.UnregisterListener(OnRetainerSellList);
        Service.AddonLifecycle.UnregisterListener(OnRetainerList);
        Service.AddonLifecycle.UnregisterListener(OnRetainerSell);
        Service.AddonLifecycle.UnregisterListener(OnEntrustDupsAddons);

        Cache.Clear();

        base.Uninit();
    }

    #region 预定义

    private static readonly InventoryType[] InventoryTypes =
    [
        InventoryType.Inventory1, InventoryType.Inventory2, InventoryType.Inventory3,
        InventoryType.Inventory4,
    ];

    public enum AdjustBehavior
    {
        固定值,
        百分比,
    }

    [Flags]
    public enum AbortCondition
    {
        无 = 1,
        低于最小值 = 2,
        低于预期值 = 4,
        低于收购价 = 8,
        大于可接受降价值 = 16,
        高于预期值 = 32,
        高于最大值 = 64,
    }

    public enum AbortBehavior
    {
        无,
        收回至雇员,
        收回至背包,
        出售至系统商店,
        改价至最小值,
        改价至预期值,
        改价至最高值,
    }

    private class Config : ModuleConfiguration
    {
        public readonly Dictionary<string, ItemConfig> ItemConfigs = new()
        {
            { new ItemKey(0, false).ToString(), new(0, false) },
            { new ItemKey(0, true).ToString(), new(0, true) },
        };

        public int AdjustMethod;
        public int OperationDelay;

        public bool SendProcessMessage = true;
    }

    public class ItemKey : IEquatable<ItemKey>
    {
        public ItemKey() { }

        public ItemKey(uint itemID, bool isHQ)
        {
            ItemID = itemID;
            IsHQ = isHQ;
        }

        public uint ItemID { get; set; }
        public bool IsHQ { get; set; }

        public bool Equals(ItemKey? other)
        {
            if (other is null || GetType() != other.GetType())
                return false;

            return ItemID == other.ItemID && IsHQ == other.IsHQ;
        }

        public override string ToString() { return $"{ItemID}_{(IsHQ ? "HQ" : "NQ")}"; }

        public override bool Equals(object? obj) { return Equals(obj as ItemKey); }

        public override int GetHashCode() { return HashCode.Combine(ItemID, IsHQ); }

        public static bool operator ==(ItemKey? lhs, ItemKey? rhs)
        {
            if (lhs is null) return rhs is null;
            return lhs.Equals(rhs);
        }

        public static bool operator !=(ItemKey lhs, ItemKey rhs) { return !(lhs == rhs); }
    }

    public class ItemConfig : IEquatable<ItemConfig>
    {
        public ItemConfig() { }

        public ItemConfig(uint itemID, bool isHQ)
        {
            ItemID = itemID;
            IsHQ = isHQ;
            ItemName = itemID == 0
                           ? Service.Lang.GetText("AutoRetainerPriceAdjust-CommonItemPreset")
                           : LuminaCache.GetRow<Item>(ItemID).Name.RawString;
        }

        public uint ItemID { get; set; }
        public bool IsHQ { get; set; }
        public string ItemName { get; set; } = string.Empty;

        /// <summary>
        ///     改价行为
        /// </summary>
        public AdjustBehavior AdjustBehavior { get; set; } = AdjustBehavior.固定值;

        /// <summary>
        ///     改价具体值
        /// </summary>
        public Dictionary<AdjustBehavior, int> AdjustValues { get; set; } = new()
        {
            { AdjustBehavior.固定值, 1 },
            { AdjustBehavior.百分比, 10 },
        };

        /// <summary>
        ///     最低可接受价格 (最小值: 1)
        /// </summary>
        public int PriceMinimum { get; set; } = 100;

        /// <summary>
        ///     最大可接受价格
        /// </summary>
        public int PriceMaximum { get; set; } = 100000000;

        /// <summary>
        ///     预期价格 (最小值: PriceMinimum + 1)
        /// </summary>
        public int PriceExpected { get; set; } = 200;

        /// <summary>
        ///     最大可接受降价值 (设置为 0 以禁用)
        /// </summary>
        public int PriceMaxReduction { get; set; }

        /// <summary>
        ///     意外情况逻辑
        /// </summary>
        public Dictionary<AbortCondition, AbortBehavior> AbortLogic { get; set; } = [];

        public bool Equals(ItemConfig? other)
        {
            if (other is null || GetType() != other.GetType())
                return false;

            return ItemID == other.ItemID && IsHQ == other.IsHQ;
        }

        public override bool Equals(object? obj) { return Equals(obj as ItemConfig); }

        public override int GetHashCode() { return HashCode.Combine(ItemID, IsHQ); }

        public static bool operator ==(ItemConfig? lhs, ItemConfig? rhs)
        {
            if (lhs is null) return rhs is null;
            return lhs.Equals(rhs);
        }

        public static bool operator !=(ItemConfig lhs, ItemConfig rhs) { return !(lhs == rhs); }
    }

    #endregion

}
