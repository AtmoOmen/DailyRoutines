using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DailyRoutines.Helpers;
using DailyRoutines.Infos;
using DailyRoutines.Managers;
using Dalamud.Hooking;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Plugin.Services;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ImGuiNET;
using Lumina.Excel.GeneratedSheets;

namespace DailyRoutines.Modules;

[ModuleDescription("AutoCheckFoodUsageTitle", "AutoCheckFoodUsageDescription", ModuleCategories.战斗)]
public class AutoCheckFoodUsage : DailyModuleBase
{
    public delegate nint CountdownInitDelegate(nint a1, nint a2);
    [Signature("E9 ?? ?? ?? ?? 48 83 C4 ?? 5B C3 CC CC CC CC CC CC CC CC CC CC CC CC CC CC CC CC CC CC 40 53 48 83 EC ?? 48 8B 0D", 
               DetourName = nameof(CountdownInitDetour))]
    private static Hook<CountdownInitDelegate>? CountdownInitHook;

    private static Config ModuleConfig = null!;


    private static uint SelectedItem;
    private static string SelectItemSearch = string.Empty;
    private static bool SelectItemIsHQ = true;
    private static string ZoneSearch = string.Empty;

    private static Vector2 CheckboxSize = ScaledVector2(20f);

    public override void Init()
    {
        ModuleConfig = LoadConfig<Config>() ?? new();
        foreach (var checkPoint in Enum.GetValues<FoodCheckpoint>())
            ModuleConfig.EnabledCheckpoints.TryAdd(checkPoint, false);

        TaskHelper ??= new TaskHelper { AbortOnTimeout = true, TimeLimitMS = 60000, ShowDebug = false };

        Service.Hook.InitializeFromAttributes(this);
        CountdownInitHook?.Enable();
        Service.ClientState.TerritoryChanged += OnZoneChanged;
    }

    public override void ConfigUI()
    {
        ImGui.TextColored(ImGuiColors.DalamudOrange, $"{Service.Lang.GetText("AutoCheckFoodUsage-Checkpoint")}:");

        ImGui.Indent();
        ImGui.Dummy(Vector2.One);

        foreach (var checkPoint in Enum.GetValues<FoodCheckpoint>())
        {
            ImGui.SameLine();
            var state = ModuleConfig.EnabledCheckpoints[checkPoint];
            if (ImGui.Checkbox(checkPoint.ToString(), ref state))
            {
                ModuleConfig.EnabledCheckpoints[checkPoint] = state;
                SaveConfig(ModuleConfig);
            }
        }
        ImGui.Unindent();

        ImGui.TextColored(ImGuiColors.DalamudOrange, $"{Service.Lang.GetText("Settings")}:");

        ImGui.Indent();
        ImGui.Dummy(Vector2.One);

        ImGui.SetNextItemWidth(50f * GlobalFontScale);
        ImGui.InputInt(Service.Lang.GetText("AutoCheckFoodUsage-RefreshThreshold"), ref ModuleConfig.RefreshThreshold, 0, 0);
        if (ImGui.IsItemDeactivatedAfterEdit())
            SaveConfig(ModuleConfig);
        
        ImGui.SameLine();
        if (ImGui.Checkbox(Service.Lang.GetText("AutoCheckFoodUsage-SendNotice"), ref ModuleConfig.SendNotice))
            SaveConfig(ModuleConfig);

        ImGuiOm.HelpMarker(Service.Lang.GetText("AutoCheckFoodUsage-RefreshThresholdHelp"));

        ImGui.Unindent();

        var tableSize = (ImGui.GetContentRegionAvail() - ScaledVector2(100f)) with { Y = 0 };
        if (ImGui.BeginTable("FoodPreset", 4, ImGuiTableFlags.Borders, tableSize))
        {
            ImGui.TableSetupColumn("添加", ImGuiTableColumnFlags.WidthFixed, CheckboxSize.X);
            ImGui.TableSetupColumn("名称", ImGuiTableColumnFlags.None, 30);
            ImGui.TableSetupColumn("地区", ImGuiTableColumnFlags.None, 30);
            ImGui.TableSetupColumn("职业", ImGuiTableColumnFlags.None, 30);

            ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
            ImGui.TableNextColumn();
            if (ImGuiOm.ButtonIconSelectable("AddNewPreset", FontAwesomeIcon.Plus))
                ImGui.OpenPopup("AddNewPresetPopup");

            if (ImGui.BeginPopup("AddNewPresetPopup"))
            {
                ImGui.TextColored(ImGuiColors.DalamudOrange, $"{Service.Lang.GetText("AutoCheckFoodUsage-AddNewPreset")}:");

                ImGui.Indent();
                ImGui.Dummy(Vector2.One);

                ImGui.SameLine();
                ImGui.AlignTextToFramePadding();
                ImGui.Text($"{Service.Lang.GetText("Food")}:");

                ImGui.SameLine();
                ImGui.SetNextItemWidth(200f * GlobalFontScale);
                SingleSelectCombo(PresetData.Food, ref SelectedItem, ref SelectItemSearch, x => $"{x.Name.RawString} ({x.RowId})",
                    [new("物品", ImGuiTableColumnFlags.WidthStretch, 0)],
                    [x => () =>
                    {
                        var icon = ImageHelper.GetIcon(x.Icon, SelectItemIsHQ);

                        if (ImGuiOm.SelectableImageWithText(icon.ImGuiHandle, ScaledVector2(20f),
                                                            x.Name, x.RowId == SelectedItem,
                                                            ImGuiSelectableFlags.DontClosePopups))
                        {
                            SelectedItem = SelectedItem == x.RowId ? 0 : x.RowId;
                        }
                    }], [x => x.Name.RawString, x => x.RowId.ToString()], true);

                ImGui.SameLine();
                ImGui.Checkbox("HQ", ref SelectItemIsHQ);

                ImGui.SameLine();
                ImGui.BeginDisabled(SelectedItem == 0);
                if (ImGui.Button(Service.Lang.GetText("Add")))
                {
                    var preset = new FoodUsagePreset(SelectedItem, SelectItemIsHQ);
                    if (ModuleConfig.Presets.Contains(preset)) return;

                    ModuleConfig.Presets.Add(preset);
                    SaveConfig(ModuleConfig);
                }
                ImGui.EndDisabled();
                ImGui.Unindent();

                ImGui.EndPopup();
            }

            ImGui.TableNextColumn();
            ImGui.Text(Service.Lang.GetText("Food"));

            ImGui.TableNextColumn();
            ImGui.Text(Service.Lang.GetText("AutoCheckFoodUsage-ZoneRestrictions"));

            ImGui.TableNextColumn();
            ImGui.Text(Service.Lang.GetText("AutoCheckFoodUsage-JobRestrictions"));

            foreach (var preset in ModuleConfig.Presets.ToArray())
            {
                ImGui.PushID($"{preset.ItemID}_{preset.IsHQ}");
                ImGui.TableNextRow();

                ImGui.TableNextColumn();
                var isEnabled = preset.Enabled;
                if (ImGui.Checkbox("", ref isEnabled))
                {
                    preset.Enabled = isEnabled;
                    SaveConfig(ModuleConfig);
                }
                CheckboxSize = ImGui.GetItemRectSize();

                ImGui.TableNextColumn();
                ImGui.Selectable($"{LuminaCache.GetRow<Item>(preset.ItemID).Name.RawString} {(preset.IsHQ ? "(HQ)" : "")}");

                if (ImGui.BeginPopupContextItem())
                {
                    if (ImGui.MenuItem($"{Service.Lang.GetText("AutoCheckFoodUsage-ChangeTo")} {(preset.IsHQ ? "NQ" : "HQ")}"))
                    {
                        preset.IsHQ ^= true;
                        SaveConfig(ModuleConfig);
                    }

                    if (ImGui.MenuItem(Service.Lang.GetText("Delete")))
                    {
                        ModuleConfig.Presets.Remove(preset);
                        SaveConfig(ModuleConfig);
                    }
                    ImGui.EndPopup();
                }

                ImGui.TableNextColumn();
                var zones = preset.Zones;
                ImGui.SetNextItemWidth(-1f);
                ImGui.PushID("ZonesSelectCombo");
                if (MultiSelectCombo(PresetData.Zones, ref zones, ref ZoneSearch, 
                                     [  new("区域", ImGuiTableColumnFlags.WidthStretch, 0), 
                                        new("副本", ImGuiTableColumnFlags.WidthStretch, 0)  ], 
                                     [x => () =>
                                     {
                                         if (ImGui.Selectable($"{x.ExtractPlaceName()}##{x.RowId}", zones.Contains(x.RowId), 
                                                              ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.DontClosePopups))
                                         {
                                             if (!zones.Remove(x.RowId))
                                                 zones.Add(x.RowId);
                                             SaveConfig(ModuleConfig);
                                         }
                                     }, 
                                     x => () =>
                                     {
                                         var contentName = x.ContentFinderCondition?.Value?.Name?.RawString ?? "";
                                         ImGui.Text(contentName);
                                     }], 
                                     [x => x.ExtractPlaceName(), x => x.ContentFinderCondition?.Value?.Name?.RawString ?? ""], true))
                {
                    preset.Zones = zones;
                    SaveConfig(ModuleConfig);
                }
                ImGuiOm.TooltipHover(Service.Lang.GetText("AutoCheckFoodUsage-NoZoneSelectHelp"));
                ImGui.PopID();

                ImGui.TableNextColumn();
                var jobs = preset.ClassJobs;
                ImGui.SetNextItemWidth(-1f);
                if (JobSelectCombo(ref jobs, ref ZoneSearch))
                {
                    preset.ClassJobs = jobs;
                    SaveConfig(ModuleConfig);
                }
                
                ImGui.PopID();
            }

            ImGui.EndTable();
        }
    }

    private unsafe bool? EnqueueFoodRefresh(int zone = -1)
    {
        if (Throttler.Throttle("AutoCheckFoodUsage_EnqueueFoodRefresh", 1000)) return false;

        var actionManager = ActionManager.Instance();
        if (Flags.BetweenAreas || Service.ClientState.LocalPlayer == null ||
               (TryGetAddonByName<AtkUnitBase>("FadeMiddle", out var addon) && addon->IsVisible) ||
               actionManager->GetActionStatus(ActionType.Item, 4650) != 0)
            return false;

        if (zone == -1)
            zone = Service.ClientState.TerritoryType;

        var instance = InventoryManager.Instance();
        var validPresets = ModuleConfig.Presets
                                       .Where(x => x.Enabled && 
                                                   (x.Zones.Count == 0 || x.Zones.Contains((uint)zone)) &&
                                                   (x.ClassJobs.Count == 0 || 
                                                    x.ClassJobs.Contains(Service.ClientState.LocalPlayer.ClassJob.Id)) &&
                                                   instance->GetInventoryItemCount(x.ItemID, x.IsHQ) > 0)
                                       .OrderByDescending(x => x.Zones.Contains((uint)zone))
                                       .ToList();

        if (validPresets.Count == 0)
        {
            TaskHelper.Abort();
            return true;
        }
        TryGetWellFedParam(out var itemFood, out var remainingTime);
        var existedStatus = validPresets.FirstOrDefault(x => ToFoodRowID(x.ItemID) == itemFood);

        if (existedStatus != null)
            if (remainingTime > TimeSpan.FromSeconds(ModuleConfig.RefreshThreshold))
            {
                TaskHelper.Abort();
                return true;
            }

        var fianlPreset = existedStatus ?? validPresets.FirstOrDefault();
        var result = TakeFood(fianlPreset);

        if (ModuleConfig.SendNotice && result)
            NotifyHelper.Chat(Service.Lang.GetText("AutoCheckFoodUsage-NoticeMessage", LuminaCache.GetRow<Item>(fianlPreset.ItemID).Name.RawString, fianlPreset.IsHQ ? "HQ" : "NQ"));

        return result;
    }

    private static bool TakeFood(FoodUsagePreset preset) => TakeFood(preset.ItemID, preset.IsHQ);

    private static unsafe bool TakeFood(uint itemID, bool isHQ) 
        => Service.UseActionManager.UseAction(ActionType.Item, isHQ ? itemID + 10_00000 : itemID, 0xE000_0000, 0xFFFF);

    private static unsafe bool TryGetWellFedParam(out uint itemFoodRowID, out TimeSpan remainingTime)
    {
        itemFoodRowID = 0;
        remainingTime = TimeSpan.Zero;

        if (Service.ClientState.LocalPlayer == null) return false;

        var charaStruct = (Character*)Service.ClientState.LocalPlayer.Address;
        var statusManager = charaStruct->GetStatusManager();

        var statusIndex = statusManager->GetStatusIndex(48);
        if (statusIndex == -1) return false;

        var status = statusManager->Status[statusIndex];
        itemFoodRowID = status.Param;
        remainingTime = TimeSpan.FromSeconds(status.RemainingTime);
        return true;
    }

    private nint CountdownInitDetour(nint a1, nint a2)
    {
        var original = CountdownInitHook.Original(a1, a2);

        if (ModuleConfig.EnabledCheckpoints[FoodCheckpoint.倒计时开始时])
            TaskHelper.Enqueue(() => EnqueueFoodRefresh());

        return original;
    }

    private void OnZoneChanged(ushort zone)
    {
        if (!ModuleConfig.EnabledCheckpoints[FoodCheckpoint.区域切换时]) return;

        TaskHelper.Enqueue(() => EnqueueFoodRefresh(zone));
    }

    public override void Uninit()
    {
        Service.ClientState.TerritoryChanged -= OnZoneChanged;

        base.Uninit();
    }

    public class FoodUsagePreset : IEquatable<FoodUsagePreset>
    {
        public FoodUsagePreset() { }

        public FoodUsagePreset(uint itemID) => ItemID = itemID;

        public FoodUsagePreset(uint itemID, bool isHQ)
        {
            ItemID = itemID;
            IsHQ = isHQ;
        }


        public uint          ItemID    { get; set; }
        public bool          IsHQ      { get; set; } = true;
        public HashSet<uint> Zones     { get; set; } = [];
        public HashSet<uint> ClassJobs { get; set; } = [];
        public bool          Enabled   { get; set; } = true;


        public override bool Equals(object? obj) => Equals(obj as FoodUsagePreset);

        public bool Equals(FoodUsagePreset? other)
        {
            if (other == null) return false;
            return ItemID == other.ItemID && IsHQ == other.IsHQ;
        }

        public override int GetHashCode() => HashCode.Combine(ItemID, IsHQ);

        public static bool operator ==(FoodUsagePreset? left, FoodUsagePreset? right) => left?.Equals(right) ?? right is null;

        public static bool operator !=(FoodUsagePreset? left, FoodUsagePreset? right) => !(left == right);
    }

    private class Config : ModuleConfiguration
    {
        public List<FoodUsagePreset> Presets = [];

        public Dictionary<FoodCheckpoint, bool> EnabledCheckpoints = [];
        public int RefreshThreshold = 600; // 秒
        public bool SendNotice = true;
    }

    private enum FoodCheckpoint
    {
        区域切换时,
        倒计时开始时,
    }

    private static uint ToFoodRowID(uint id) => LuminaCache.GetRow<ItemFood>(LuminaCache.GetRow<Item>(id)?.ItemAction?.Value?.Data[1] ?? 0)?.RowId ?? 0;
}
