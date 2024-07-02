using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ClickLib;
using DailyRoutines.Helpers;
using DailyRoutines.Infos;
using DailyRoutines.Managers;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Memory;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ImGuiNET;
using Lumina.Excel.GeneratedSheets;
using Action = System.Action;
using ObjectKind = Dalamud.Game.ClientState.Objects.Enums.ObjectKind;

namespace DailyRoutines.Modules;

[ModuleDescription("AutoGardensWorkTitle", "AutoGardensWorkDescription", ModuleCategories.一般)]
public unsafe class AutoGardensWork : DailyModuleBase
{
    private delegate GameObject* GetGameObjectFromObjectIDDelegate(ulong objectID);

    [Signature("40 53 48 83 EC ?? B8 ?? ?? ?? ?? 0F B6 DA")]
    private static GetGameObjectFromObjectIDDelegate? GetGameObjectFromObjectID;

    private static readonly InventoryType[] InventoryTypes =
    [
        InventoryType.Inventory1, InventoryType.Inventory2, InventoryType.Inventory3,
        InventoryType.Inventory4,
    ];

    private static Config ModuleConfig = null!;

    private static string GardenRenameInput = string.Empty;
    private static string SearchFilterSeed = string.Empty;

    public override void Init()
    {
        ModuleConfig = LoadConfig<Config>() ?? new();
        Service.Hook.InitializeFromAttributes(this);
        TaskHelper ??= new TaskHelper { AbortOnTimeout = true, ShowDebug = false };

        Service.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "HousingGardening", OnAddon);
    }

    public override void ConfigUI()
    {
        ImGui.BeginGroup();
        ImGui.BeginDisabled(TaskHelper.IsBusy);
        ImGui.BeginGroup();

        // 自动种植
        ImGui.PushID("AutoPlant");
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(ImGuiColors.DalamudOrange, $"{Service.Lang.GetText("AutoGardensWork-AutoPlant")}:");

        ImGui.SameLine();
        if (ImGui.Button(Service.Lang.GetText("Start")))
            StartPlant();

        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(ImGuiColors.DalamudOrange, $"{Service.Lang.GetText("AutoGardensWork-Seed")}:");

        ImGui.SameLine();
        ImGui.SetNextItemWidth(150f * GlobalFontScale);
        if (ImGui.BeginCombo("###Seeds",
                             ModuleConfig.SelectedSeed == 0
                                 ? string.Empty
                                 : PresetData.Seeds[ModuleConfig.SelectedSeed].Name.RawString,
                             ImGuiComboFlags.HeightLarge))
        {
            ImGui.InputTextWithHint("###SearchSeedInput", Service.Lang.GetText("PleaseSearch"),
                                    ref SearchFilterSeed, 100);

            foreach (var item in PresetData.Seeds)
            {
                var itemName = item.Value.Name.ExtractText();
                if (!string.IsNullOrWhiteSpace(SearchFilterSeed) &&
                    !itemName.Contains(SearchFilterSeed, StringComparison.OrdinalIgnoreCase)) continue;

                if (ImGui.Selectable($"{itemName}", ModuleConfig.SelectedSeed == item.Key))
                {
                    ModuleConfig.SelectedSeed = item.Key;
                    SaveConfig(ModuleConfig);
                }
            }

            ImGui.EndCombo();
        }

        ImGui.SameLine();
        ImGui.TextColored(ImGuiColors.DalamudOrange, $"{Service.Lang.GetText("AutoGardensWork-Soil")}:");

        ImGui.SameLine();
        ImGui.SetNextItemWidth(150f * GlobalFontScale);
        if (ImGui.BeginCombo("###Soils",
                             ModuleConfig.SelectedSoil == 0
                                 ? string.Empty
                                 : PresetData.Soils[ModuleConfig.SelectedSoil].Name.RawString,
                             ImGuiComboFlags.HeightLarge))
        {
            foreach (var item in PresetData.Soils)
                if (ImGui.Selectable($"{item.Value.Name.ExtractText()}", ModuleConfig.SelectedSoil == item.Key))
                {
                    ModuleConfig.SelectedSoil = item.Key;
                    SaveConfig(ModuleConfig);
                }

            ImGui.EndCombo();
        }

        ImGui.PopID();

        // 自动收获
        ImGui.PushID("AutoGather");
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(ImGuiColors.DalamudOrange, $"{Service.Lang.GetText("AutoGardensWork-AutoGather")}:");

        ImGui.SameLine();
        if (ImGui.Button(Service.Lang.GetText("Start")))
            StartGather();

        ImGui.PopID();

        // 自动施肥
        ImGui.PushID("AutoFertilize");
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(ImGuiColors.DalamudOrange, $"{Service.Lang.GetText("AutoGardensWork-AutoFertilize")}:");

        ImGui.SameLine();
        if (ImGui.Button(Service.Lang.GetText("Start")))
            StartFertilize();

        ImGui.SameLine();
        ImGui.TextColored(ImGuiColors.DalamudOrange, $"{Service.Lang.GetText("AutoGardensWork-Fertilizer")}:");

        ImGui.SameLine();
        ImGui.SetNextItemWidth(150f * GlobalFontScale);
        if (ImGui.BeginCombo("###Fertilizer",
                             ModuleConfig.SelectedFertilizer == 0
                                 ? string.Empty
                                 : PresetData.Fertilizers[ModuleConfig.SelectedFertilizer].Name.RawString,
                             ImGuiComboFlags.HeightLarge))
        {
            foreach (var item in PresetData.Fertilizers)
                if (ImGui.Selectable($"{item.Value.Name.ExtractText()}", ModuleConfig.SelectedFertilizer == item.Key))
                {
                    ModuleConfig.SelectedFertilizer = item.Key;
                    SaveConfig(ModuleConfig);
                }

            ImGui.EndCombo();
        }

        // 自动护理
        ImGui.PushID("AutoTend");
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(ImGuiColors.DalamudOrange, $"{Service.Lang.GetText("AutoGardensWork-AutoTend")}:");

        ImGui.SameLine();
        if (ImGui.Button(Service.Lang.GetText("Start")))
            StartTend();

        ImGui.PopID();

        ImGui.EndGroup();
        ImGui.EndDisabled();

        var groupSize = ImGui.GetItemRectSize();

        ImGui.SameLine();
        ImGui.Spacing();

        ImGui.SameLine();
        if (ImGui.Button(Service.Lang.GetText("Stop"), groupSize with { X = 80f * GlobalFontScale }))
            TaskHelper.Abort();

        ImGui.SameLine();
        if (ImGui.Button(Service.Lang.GetText("AutoGardensWork-ObtainGardensAround"),
                         groupSize with { X = 100f * GlobalFontScale }))
            ObtainGardensAround();

        ImGui.EndGroup();

        ImGui.Spacing();

        var tableSize = ImGui.GetItemRectSize() with { Y = 0 };
        var twoCharaSize = ImGui.CalcTextSize("测试");
        if (ImGui.BeginTable("GardensTable", 6, ImGuiTableFlags.Borders, tableSize))
        {
            ImGui.TableSetupColumn("名称", ImGuiTableColumnFlags.None, 20);
            ImGui.TableSetupColumn("ObjectID", ImGuiTableColumnFlags.None, 20);
            ImGui.TableSetupColumn("地点", ImGuiTableColumnFlags.None, 15);
            ImGui.TableSetupColumn("房区", ImGuiTableColumnFlags.WidthFixed, twoCharaSize.X);
            ImGui.TableSetupColumn("地皮", ImGuiTableColumnFlags.WidthFixed, twoCharaSize.X);
            ImGui.TableSetupColumn("操作", ImGuiTableColumnFlags.None, 10);

            ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
            ImGui.TableNextColumn();
            ImGui.Text(Service.Lang.GetText("Name"));

            ImGui.TableNextColumn();
            ImGui.Text("ObjectID");

            ImGui.TableNextColumn();
            ImGui.Text(Service.Lang.GetText("Zone"));

            ImGui.TableNextColumn();
            ImGui.Text(Service.Lang.GetText("Ward"));

            ImGui.TableNextColumn();
            ImGui.Text(Service.Lang.GetText("Plot"));

            foreach (var garden in ModuleConfig.Gardens.ToList())
            {
                ImGui.PushID($"{garden.ObjectID}");
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Selectable(garden.Name);

                if (ImGui.BeginPopupContextItem())
                {
                    if (ImGui.IsWindowAppearing())
                        GardenRenameInput = garden.Name;

                    ImGui.BeginGroup();
                    ImGui.AlignTextToFramePadding();
                    ImGui.TextColored(ImGuiColors.DalamudOrange, $"{Service.Lang.GetText("Rename")}:");

                    ImGui.SameLine();
                    ImGui.InputText("###GardenRenameInput", ref GardenRenameInput, 128);
                    ImGui.EndGroup();

                    var buttonSize = ImGui.GetItemRectSize() with { Y = 24f * GlobalFontScale };

                    ImGui.Spacing();
                    if (ImGui.Button(Service.Lang.GetText("AutoGardensWork-ConfirmRename"), buttonSize))
                    {
                        garden.Name = GardenRenameInput;

                        ModuleConfig.Gardens = [.. ModuleConfig.Gardens.OrderBy(x => x.Name)];
                        SaveConfig(ModuleConfig);
                    }

                    ImGui.EndPopup();
                }

                ImGui.TableNextColumn();
                ImGui.Text(garden.ObjectID.ToString());

                ImGui.TableNextColumn();
                ImGui.Text(LuminaCache.GetRow<TerritoryType>(garden.ZoneID).ExtractPlaceName());

                ImGui.TableNextColumn();
                ImGui.Text($"{garden.Ward + 1}");

                ImGui.TableNextColumn();
                ImGui.Text(garden.Plot.ToString());

                ImGui.TableNextColumn();
                if (ImGuiOm.ButtonIcon("Target", FontAwesomeIcon.MousePointer, Service.Lang.GetText("ToTarget")))
                {
                    var gameObj = GetGameObjectFromObjectID(garden.ObjectID);
                    if (gameObj == null)
                        NotifyHelper.NotificationError(Service.Lang.GetText("AutoGardensWork-GardenNotFound"));
                    else
                        TargetSystem.Instance()->Target = gameObj;
                }

                ImGui.SameLine();
                if (ImGuiOm.ButtonIcon("Delete", FontAwesomeIcon.TrashAlt, Service.Lang.GetText("Delete")))
                {
                    ModuleConfig.Gardens.Remove(garden);
                    SaveConfig(ModuleConfig);
                }

                ImGui.PopID();
            }

            ImGui.EndTable();
        }
    }

    private void OnAddon(AddonEvent type, AddonArgs args)
    {
        if (ModuleConfig.SelectedSeed == 0 || ModuleConfig.SelectedSoil == 0) return;
        var inventoryManager = InventoryManager.Instance();
        if (inventoryManager->GetInventoryItemCount(ModuleConfig.SelectedSeed) == 0 ||
            inventoryManager->GetInventoryItemCount(ModuleConfig.SelectedSoil) == 0) return;

        TaskHelper.Enqueue(() => AgentHelper.SendEvent(AgentId.HousingPlant, 0, 2, 0U, 0, 0, 1U), null, 2);

        TaskHelper.DelayNext(10, false, 2);
        TaskHelper.Enqueue(() => FillContextMenu(PresetData.Soils[ModuleConfig.SelectedSoil].Name.RawString), null, 2);

        TaskHelper.DelayNext(10, false, 2);
        TaskHelper.Enqueue(() => AgentHelper.SendEvent(AgentId.HousingPlant, 0, 2, 1U, 0, 0, 1U), null, 2);

        TaskHelper.DelayNext(10, false, 2);
        TaskHelper.Enqueue(() => FillContextMenu(PresetData.Seeds[ModuleConfig.SelectedSeed].Name.RawString), null, 2);

        TaskHelper.DelayNext(10, false, 2);
        TaskHelper.Enqueue(() => AgentHelper.SendEvent(AgentId.HousingPlant, 0, 0, 0, 0, 0, 0), null, 2);

        TaskHelper.DelayNext(10, false, 2);
        TaskHelper.Enqueue(() => Click.TrySendClick("select_yes"), null, 2);
    }

    private void StartAction(string action, Action extraAction = null)
    {
        var localPlayer = (GameObject*)Service.ClientState.LocalPlayer.Address;
        if (localPlayer == null) return;

        var manager = HousingManager.Instance();
        var ward = manager->GetCurrentWard();
        var plot = manager->GetCurrentPlot();
        if (ward == -1 || plot == -1)
        {
            NotifyHelper.NotificationError(Service.Lang.GetText("AutoGardensWork-NotInValidWardOrPlot"));
            return;
        }

        foreach (var garden in ModuleConfig.Gardens.Where(x => 
                                                              x.ZoneID == Service.ClientState.TerritoryType &&
                                                              x.Ward == ward && x.Plot == plot))
        {
            var gameObj = GetGameObjectFromObjectID(garden.ObjectID);
            if (gameObj == null) continue;

            var objDistance = Vector3.Distance(localPlayer->Position, gameObj->Position);
            if (objDistance > 5) continue;

            TaskHelper.Enqueue(() => InteractWithGarden(gameObj));
            TaskHelper.Enqueue(() => ClickEntryByText(action));

            extraAction?.Invoke();
        }
    }

    private void StartGather() => StartAction("收获");

    private void StartTend() => StartAction("护理");

    private void StartPlant() =>
        StartAction(LuminaCache.GetRow<Addon>(6410).Text.RawString, () => TaskHelper.DelayNext(250));

    private void StartFertilize()
        => StartAction(LuminaCache.GetRow<Addon>(6423).Text.RawString, () =>
        {
            TaskHelper.Enqueue(CheckFertilizerState);
            TaskHelper.Enqueue(ClickFertilizer);
            TaskHelper.Enqueue(() => !Service.Condition[ConditionFlag.OccupiedInQuestEvent]);
        });


    private void ObtainGardensAround()
    {
        var manager = HousingManager.Instance();
        var ward = manager->GetCurrentWard();
        var plot = manager->GetCurrentPlot();
        if (ward == -1 || plot == -1)
        {
            NotifyHelper.NotificationError(Service.Lang.GetText("AutoGardensWork-NotInValidWardOrPlot"));
            return;
        }

        foreach (var gameObj in Service.ObjectTable.Where(x => x is { ObjectKind: ObjectKind.EventObj, DataId: 2003757 }))
        {
            if (Vector3.Distance(gameObj.Position, Service.ClientState.LocalPlayer.Position) > 20) continue;

            var objectID = gameObj.GameObjectId;
            var zoneID = Service.ClientState.TerritoryType;
            var zoneName = LuminaCache.GetRow<TerritoryType>(zoneID).ExtractPlaceName();
            var garden = new GameGarden($"{zoneName}_{objectID}", objectID, zoneID, ward, plot);
            if (!ModuleConfig.Gardens.Contains(garden)) ModuleConfig.Gardens.Add(garden);
        }

        SaveConfig(ModuleConfig);
    }

    private static bool? CheckFertilizerState()
    {
        var selectString = Service.Gui.GetAddonByName("SelectString");
        var inventory = (AtkUnitBase*)Service.Gui.GetAddonByName("Inventory");
        var inventoryLarge = (AtkUnitBase*)Service.Gui.GetAddonByName("InventoryLarge");
        var inventoryExpansion = (AtkUnitBase*)Service.Gui.GetAddonByName("InventoryExpansion");

        if (selectString != nint.Zero) return false;

        if (inventory->IsVisible || inventoryLarge->IsVisible || inventoryExpansion->IsVisible ||
            !Service.Condition[ConditionFlag.OccupiedInQuestEvent])
            return true;

        return false;
    }

    private bool? ClickFertilizer()
    {
        if (Service.Gui.GetAddonByName("SelectString") != nint.Zero) return false;

        if (!Service.Condition[ConditionFlag.OccupiedInQuestEvent]) return true;

        if (ModuleConfig.SelectedFertilizer == 0)
        {
            TaskHelper.Abort();
            return true;
        }

        if (Service.Gui.GetAddonByName("Inventory") == nint.Zero) return false;
        var agent = AgentInventoryContext.Instance();
        if (agent == null) return false;

        var inventoryManager = InventoryManager.Instance();
        if (inventoryManager == null) return false;

        InventoryType? foundType = null;
        foreach (var type in InventoryTypes)
            if (inventoryManager->GetItemCountInContainer(ModuleConfig.SelectedFertilizer, type) != 0)
            {
                foundType = type;
                break;
            }

        if (foundType == null) return false;

        var container = inventoryManager->GetInventoryContainer((InventoryType)foundType);
        if (container == null) return false;

        int? foundSlot = null;
        for (var i = 0; i < container->Size; i++)
        {
            var slot = container->GetInventorySlot(i);
            if (slot->ItemId == ModuleConfig.SelectedFertilizer)
            {
                foundSlot = i;
                break;
            }
        }

        if (foundSlot == null) return false;

        var agentInventory = AgentModule.Instance()->GetAgentByInternalId(AgentId.Inventory);
        if (agentInventory == null) return false;

        agent->OpenForItemSlot((InventoryType)foundType, (int)foundSlot, agentInventory->AddonId);

        TaskHelper.InsertDelayNext(20);
        TaskHelper.Insert(() => ClickContextMenuByText("施肥"));

        return true;
    }

    private static bool? ClickContextMenuByText(string text)
    {
        if (!Service.Condition[ConditionFlag.OccupiedInQuestEvent]) return true;

        if (TryGetAddonByName<AtkUnitBase>("ContextMenu", out var addon) && IsAddonAndNodesReady(addon))
        {
            if (!TryScanContextMenuText(addon, text, out var index))
            {
                addon->FireCloseCallback();
                addon->Close(true);
                return true;
            }

            Callback(addon, true, 0, index, 0U, 0, 0);
        }

        return false;
    }

    private bool? FillContextMenu(string itemNameToSelect)
    {
        if (!TryGetAddonByName<AtkUnitBase>("ContextIconMenu", out var addon) ||
            !IsAddonAndNodesReady(addon)) return false;

        var validAtkValuesCount = addon->AtkValuesCount - 11;
        if (validAtkValuesCount % 6 != 0) return false;
        var entryAmount = addon->AtkValuesCount / 6;

        for (var i = 0; i < entryAmount; i++)
        {
            var iconID = addon->AtkValues[11 + (i * 6)].UInt;
            var itemName = MemoryHelper.ReadSeStringNullTerminated((nint)addon->AtkValues[13 + (i * 6)].String)
                                       .ExtractText();

            if (itemName == itemNameToSelect)
            {
                Callback(addon, true, 0, i, iconID, 0U, 0);
                return true;
            }
        }

        TaskHelper.Abort();
        return true;
    }

    private static bool? InteractWithGarden(GameObject* gameObj)
    {
        if (Flags.OccupiedInEvent) return false;

        var targetSystem = TargetSystem.Instance();
        targetSystem->Target = gameObj;
        targetSystem->InteractWithObject(gameObj);
        targetSystem->OpenObjectInteraction(gameObj);

        return true;
    }

    private static bool? ClickEntryByText(string text)
    {
        if (!TryGetAddonByName<AtkUnitBase>("SelectString", out var addon) || !IsAddonAndNodesReady(addon))
            return false;

        if (!TryScanSelectStringText(addon, text, out var index))
        {
            TryScanSelectStringText(addon, "取消", out index);
            return Click.TrySendClick($"select_string{index + 1}");
        }

        return Click.TrySendClick($"select_string{index + 1}");
    }

    public override void Uninit()
    {
        Service.AddonLifecycle.UnregisterListener(OnAddon);

        base.Uninit();
    }

    private class GameGarden : IEquatable<GameGarden>
    {
        public string Name     { get; set; } = string.Empty;
        public ulong  ObjectID { get; set; }
        public ushort ZoneID   { get; set; }
        public sbyte  Ward     { get; set; }
        public sbyte  Plot     { get; set; }

        public GameGarden() { }

        public GameGarden(string name, ulong objectID, ushort zoneID, sbyte ward, sbyte plot)
        {
            Name = name;
            ObjectID = objectID;
            ZoneID = zoneID;
            Ward = ward;
            Plot = plot;
        }

        public bool Equals(GameGarden? other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;

            return ObjectID == other.ObjectID && ZoneID == other.ZoneID;
        }

        public override bool Equals(object? obj)
        {
            if (obj is null) return false;
            if (ReferenceEquals(this, obj)) return true;

            return obj.GetType() == GetType() && Equals((GameGarden)obj);
        }

        public override int GetHashCode() => HashCode.Combine(ObjectID, ZoneID);
    }

    private class Config : ModuleConfiguration
    {
        public List<GameGarden> Gardens = [];

        public uint SelectedSeed;
        public uint SelectedSoil;
        public uint SelectedFertilizer;
    }
}
