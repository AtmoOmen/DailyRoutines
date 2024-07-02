using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ClickLib;
using DailyRoutines.Helpers;
using DailyRoutines.Infos;
using DailyRoutines.Managers;
using DailyRoutines.Windows;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Command;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Interface.Colors;
using Dalamud.Memory;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ImGuiNET;
using Lumina.Excel.GeneratedSheets;
using ObjectKind = Dalamud.Game.ClientState.Objects.Enums.ObjectKind;

namespace DailyRoutines.Modules;

[PrecedingModule([typeof(AutoCutSceneSkip)])]
[ModuleDescription("AutoSubmarineCollectTitle", "AutoSubmarineCollectDescription", ModuleCategories.界面操作)]
public unsafe class AutoSubmarineCollect : DailyModuleBase
{
    private static Throttler<string> Throttler = new();
    private static TaskHelper? RepairTaskHelper;
    private static Config ModuleConfig = null!;

    private static AtkUnitBase* SelectString => (AtkUnitBase*)Service.Gui.GetAddonByName("SelectString");
    private static AtkUnitBase* SelectYesno => (AtkUnitBase*)Service.Gui.GetAddonByName("SelectYesno");
    // 航行结果
    private static AtkUnitBase* AirShipExplorationResult =>
        (AtkUnitBase*)Service.Gui.GetAddonByName("AirShipExplorationResult");
    // 出发详情
    private static AtkUnitBase* AirShipExplorationDetail =>
        (AtkUnitBase*)Service.Gui.GetAddonByName("AirShipExplorationDetail");
    private static AtkUnitBase* CompanyCraftSupply => (AtkUnitBase*)Service.Gui.GetAddonByName("CompanyCraftSupply");

    private static Lazy<string> RequisiteMaterialsName => new(() => LuminaCache.GetRow<Item>(10373).Name.RawString);
    private static uint WorkshopTerritory 
        => Service.AetheryteList.FirstOrDefault(x => x.AetheryteData.GameData.PlaceName.Row == 1145).TerritoryId;

    private const string Command = "submarine";
    private static readonly HashSet<uint> CompanyWorkshopZones = [423, 424, 425, 653, 984];
    private static int? RequisiteMaterials;

    public override void Init()
    {
        Service.Hook.InitializeFromAttributes(this);
        ModuleConfig = LoadConfig<Config>() ?? new();

        TaskHelper ??= new TaskHelper { AbortOnTimeout = true, TimeLimitMS = 30000, ShowDebug = false };
        RepairTaskHelper ??= new TaskHelper { AbortOnTimeout = true, TimeLimitMS = 10000, ShowDebug = false };

        Overlay ??= new Overlay(this);
        Overlay.Flags |= ImGuiWindowFlags.NoMove;

        Service.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "SelectYesno", AlwaysYes);
        Service.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "AirShipExplorationResult", OnExplorationResult);
        Service.AddonLifecycle.RegisterListener(AddonEvent.PostDraw, "SelectString", OnAddonSelectString);
        Service.LogMessageManager.Register(OnLogMessages);

        if (ModuleConfig.AddCommand)
            Service.CommandManager.AddSubCommand(Command, new CommandInfo(OnCommand)
            {
                HelpMessage = Service.Lang.GetText("AutoSubmarineCollect-AddCommandHelp"),
            });
    }

    public override void ConfigUI()
    {
        ConflictKeyText();

        ImGui.Spacing();

        if (ImGui.Checkbox(Service.Lang.GetText("AutoSubmarineCollect-AddCommand", Command), ref ModuleConfig.AddCommand))
        {
            SaveConfig(ModuleConfig);

            if (ModuleConfig.AddCommand)
                Service.CommandManager.AddSubCommand(Command, new CommandInfo(OnCommand)
                {
                    HelpMessage = Service.Lang.GetText("AutoSubmarineCollect-AddCommandHelp"),
                });
            else
                Service.CommandManager.RemoveSubCommand(Command);
        }

        ImGuiOm.HelpMarker(Service.Lang.GetText("AutoSubmarineCollect-AddCommandHelp"));
    }

    public override void OverlayUI()
    {
        if (SelectString == null)
        {
            Overlay.IsOpen = false;
            return;
        }

        var pos = new Vector2(SelectString->GetX() + 6, SelectString->GetY() - ImGui.GetWindowSize().Y + 6);
        ImGui.SetWindowPos(pos);

        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(ImGuiColors.DalamudYellow, Service.Lang.GetText("AutoSubmarineCollectTitle"));

        ImGui.SameLine();

        ImGui.BeginDisabled(TaskHelper.IsBusy);
        if (ImGui.Button(Service.Lang.GetText("Start"))) GetSubmarineInfos();
        ImGui.EndDisabled();

        ImGui.SameLine();
        if (ImGui.Button(Service.Lang.GetText("Stop"))) TaskHelper.Abort();

        RequisiteMaterials ??= InventoryManager.Instance()->GetInventoryItemCount(10373);

        ImGui.SameLine();
        ImGui.Text($"{RequisiteMaterialsName.Value}:");

        ImGui.SameLine();
        ImGui.TextColored(RequisiteMaterials < 20 ? ImGuiColors.DPSRed : ImGuiColors.HealerGreen,
                          RequisiteMaterials.ToString());

        if (Throttler.Throttle("AutoSubmarineCollectOverlay-RequestItemAmount", 1000))
        {
            var inventoryManager = InventoryManager.Instance();
            RequisiteMaterials = inventoryManager->GetInventoryItemCount(10373);
        }
    }

    private void OnCommand(string command, string arguments) => EnqueueTeleportTasks();

    private void EnqueueTeleportTasks()
    {
        TaskHelper.Abort();
        if (WorkshopTerritory == 0) return;

        var housingManager = HousingManager.Instance();
        var currentZone = Service.ClientState.TerritoryType;

        // 就在工房内
        if (CompanyWorkshopZones.Contains(currentZone))
        {
            TaskHelper.Enqueue(TeleportToPanel);
            TaskHelper.Enqueue(GetSubmarineInfos);
            return;
        }

        // 不在房区且不在室内
        if (currentZone != WorkshopTerritory && !housingManager->IsInside())
        {
            TaskHelper.Enqueue(TeleportToHouseZone);
            TaskHelper.Enqueue(TeleportToHouseEntry);
            TaskHelper.Enqueue(TeleportToRoomSelect);
            TaskHelper.Enqueue(TeleportToPanel);
            TaskHelper.Enqueue(GetSubmarineInfos);
            return;
        }

        // 正在房区但不在室内
        if (currentZone == WorkshopTerritory && !housingManager->IsInside())
        {
            TaskHelper.Enqueue(TeleportToHouseEntry);
            TaskHelper.Enqueue(TeleportToRoomSelect);
            TaskHelper.Enqueue(TeleportToPanel);
            TaskHelper.Enqueue(GetSubmarineInfos);
            return;
        }

        // 正在室内
        if (housingManager->IsInside())
        {
            TaskHelper.Enqueue(TeleportToRoomSelect);
            TaskHelper.Enqueue(TeleportToPanel);
            TaskHelper.Enqueue(GetSubmarineInfos);
        }
    }

    private static bool? TeleportToHouseZone()
    {
        if (!Throttler.Throttle("TeleportToHouseZone")) return false;
        if (WorkshopTerritory == 0) return false;

        return Telepo.Instance()->Teleport(96, 0);
    }

    private bool? TeleportToHouseEntry()
    {
        if (!Throttler.Throttle("TeleportToHouseEntry")) return false;
        if (InterruptByConflictKey()) return true;
        if (Flags.BetweenAreas) return false;
        if (Service.ClientState.TerritoryType != WorkshopTerritory) return false;

        var localPlayer = Service.ClientState.LocalPlayer;
        if (localPlayer == null) return false;

        if (Flags.IsOnMount)
        {
            Service.ExecuteCommandManager.ExecuteCommand(ExecuteCommandFlag.Dismount, 1);
            return false;
        }

        var pos = Service.ClientState.LocalPlayer.Position;

        var targetSystem = (nint)TargetSystem.Instance();
        var entry = Service.ObjectTable
                           .Where(x => x.ObjectKind == ObjectKind.EventObj
                                       && x.Name.TextValue == LuminaCache.GetRow<EObjName>(2002737).Singular.RawString
                                       && TargetSystem_IsObjectInViewRange(targetSystem, x.Address))
                           .MinBy(x => Vector3.Distance(pos, x.Position));
        if (entry == null) return false;

        var target = (GameObject*)entry.Address;
        Teleport(target->Position with { Y = target->Position.Y - 1 });
        TargetSystem.Instance()->InteractWithObject(target);

        TaskHelper.InsertDelayNext("WaitEnteringHouse", 500, false, 1);
        return true;
    }

    private bool? TeleportToRoomSelect()
    {
        if (!Throttler.Throttle("TeleportToRoomSelect")) return false;
        if (InterruptByConflictKey()) return true;
        if (!HousingManager.Instance()->IsInside()) return false;

        var localPlayer = Service.ClientState.LocalPlayer;
        if (localPlayer == null) return false;
        var pos = Service.ClientState.LocalPlayer.Position;

        var entry = Service.ObjectTable
                           .Where(x => x.ObjectKind == ObjectKind.EventObj
                                       && x.Name.TextValue == LuminaCache.GetRow<EObjName>(2004353).Singular.RawString)
                           .MinBy(x => Vector3.Distance(pos, x.Position));
        if (entry == null) return false;

        var target = (GameObject*)entry.Address;
        Teleport(target->Position with { Y = target->Position.Y - 1 });
        TargetSystem.Instance()->InteractWithObject(target);

        return ClickHelper.SelectString("移动到部队工房");
    }

    private bool? TeleportToPanel()
    {
        if (!Throttler.Throttle("TeleportToPanel")) return false;
        if (InterruptByConflictKey()) return true;
        if (Flags.BetweenAreas) return false;

        var localPlayer = Service.ClientState.LocalPlayer;
        if (localPlayer == null) return false;
        var pos = Service.ClientState.LocalPlayer.Position;

        var entry = Service.ObjectTable
                           .Where(x => x.ObjectKind == ObjectKind.EventObj
                                       && x.Name.TextValue == LuminaCache.GetRow<EObjName>(2005274).Singular.RawString)
                           .MinBy(x => Vector3.Distance(pos, x.Position));
        if (entry == null) return false;

        var target = (GameObject*)entry.Address;
        Teleport(target->Position with { Y = target->Position.Y - 1 });
        TargetSystem.Instance()->InteractWithObject(target);

        return ClickHelper.SelectString("管理潜水艇");
    }

    private bool? GetSubmarineInfos()
    {
        if (InterruptByConflictKey()) return true;

        // 还在看动画
        if (Service.Condition[ConditionFlag.OccupiedInCutSceneEvent] ||
            Service.Condition[ConditionFlag.WatchingCutscene78]) return false;

        #region CheckNecessaryItems

        var inventoryManager = InventoryManager.Instance();
        if (inventoryManager->GetInventoryItemCount(10373) < 20)
        {
            var message = new SeStringBuilder().Append(DRPrefix).Append(" ")
                                               .Append(Service.Lang.GetSeString(
                                                           "AutoSubmarineCollect-LackSpecificItems",
                                                           SeString.CreateItemLink(10373))).Build();

            Service.Chat.Print(message);

            TaskHelper.Abort();
            return true;
        }

        if (inventoryManager->GetInventoryItemCount(10155) < 15)
        {
            var message = new SeStringBuilder().Append(DRPrefix).Append(" ")
                                               .Append(Service.Lang.GetSeString(
                                                           "AutoSubmarineCollect-LackSpecificItems",
                                                           SeString.CreateItemLink(10155))).Build();

            Service.Chat.Print(message);

            TaskHelper.Abort();
            return true;
        }

        #endregion

        if (SelectString == null || !IsAddonAndNodesReady(SelectString)) return false;
        if (!ClickHelper.SelectString("探索完成"))
        {
            TaskHelper.Abort();
            return true;
        }

        TaskHelper.Abort();
        TaskHelper.DelayNext(2000);
        TaskHelper.Enqueue(CommenceSubmarineVoyage);

        return true;
    }

    private bool? CommenceSubmarineVoyage()
    {
        if (InterruptByConflictKey()) return true;
        if (AirShipExplorationDetail == null || !IsAddonAndNodesReady(AirShipExplorationDetail)) return false;

        Callback(AirShipExplorationDetail, true, 0);
        AirShipExplorationDetail->Close(true);

        TaskHelper.Abort();
        TaskHelper.DelayNext(2000);
        TaskHelper.Enqueue(GetSubmarineInfos);

        return false;
    }

    private bool? ReadyToRepairSubmarines()
    {
        if (AirShipExplorationDetail != null)
        {
            AirShipExplorationDetail->Close(true);
            return false;
        }

        if (AirShipExplorationResult != null)
        {
            AirShipExplorationResult->Close(true);
            return false;
        }

        if (CompanyCraftSupply != null && IsAddonAndNodesReady(CompanyCraftSupply))
        {
            RepairTaskHelper.Enqueue(RepairSubmarines);
            RepairTaskHelper.DelayNext(20);
            RepairTaskHelper.Enqueue(() => Callback(CompanyCraftSupply, true, 5));
            RepairTaskHelper.Enqueue(ClickPreviousVoyageLog);
            return true;
        }

        if (SelectString != null && IsAddonAndNodesReady(SelectString))
        {
            if (!ClickHelper.SelectString("修理")) return false;

            SelectString->Close(true);

            RepairTaskHelper.Enqueue(RepairSubmarines);
            RepairTaskHelper.DelayNext(20);
            RepairTaskHelper.Enqueue(() => Callback(CompanyCraftSupply, true, 5));
            RepairTaskHelper.Enqueue(ClickPreviousVoyageLog);
            RepairTaskHelper.DelayNext(100);
            RepairTaskHelper.Enqueue(CommenceSubmarineVoyage);
            return true;
        }

        return false;
    }

    private static bool? RepairSubmarines()
    {
        if (!Throttler.Throttle("AutoSubmarineCollect-Repair", 200)) return false;
        if (SelectYesno != null) return false;
        if (CompanyCraftSupply == null || !IsAddonAndNodesReady(CompanyCraftSupply)) return false;

        for (var i = 0; i < 4; i++)
        {
            var endurance = CompanyCraftSupply->AtkValues[3 + (8 * i)].UInt;
            if (endurance <= 0)
            {
                AgentHelper.SendEvent(AgentId.SubmersibleParts, 0, 3, 0, i, 0, 0, 0);
                return false;
            }
        }

        return true;
    }

    private bool? ClickPreviousVoyageLog()
    {
        if (AirShipExplorationDetail != null && IsAddonAndNodesReady(AirShipExplorationDetail))
        {
            RepairTaskHelper.Abort();
            RepairTaskHelper.Enqueue(CommenceSubmarineVoyage);

            return true;
        }

        if (SelectString == null || !IsAddonAndNodesReady(SelectString)) return false;

        if (!ClickHelper.SelectString("上次的远航报告")) return false;

        return true;
    }

    public static bool TargetSystem_IsObjectInViewRange(nint targetSystem, nint targetGameObject)
    {
        if (targetGameObject == nint.Zero) return false;

        var objectCount = *(int*)(targetSystem + 328);
        if (objectCount <= 0) return false;

        var i = (nint*)(targetSystem + 336);
        for (var index = 0; index < objectCount; index++, i++)
        {
            if (*i == targetGameObject)
                return true;
        }

        return false;
    }

    private static bool? Teleport(Vector3 pos)
    {
        var localPlayer = Service.ClientState.LocalPlayer;
        if (localPlayer == null) return false;

        var address = localPlayer.Address + 176;
        MemoryHelper.Write(address, pos.X);
        MemoryHelper.Write(address + 4, pos.Y);
        MemoryHelper.Write(address + 8, pos.Z);

        return true;
    }

    #region Events
    private void OnLogMessages(uint logMessageID, ushort logKind)
    {
        switch (logMessageID)
        {
            case 4290:
                TaskHelper.Abort();
                RepairTaskHelper.Abort();
                RepairTaskHelper.Enqueue(ReadyToRepairSubmarines);
                break;
            case 4276:
                TaskHelper.Abort();
                RepairTaskHelper.Abort();
                break;
        }
    }

    private void OnExplorationResult(AddonEvent type, AddonArgs args)
    {
        if (AirShipExplorationResult == null || !IsAddonAndNodesReady(AirShipExplorationResult)) return;

        Callback(AirShipExplorationResult, true, 1);
        if (TaskHelper.IsBusy) AirShipExplorationResult->IsVisible = false;
    }

    private void OnAddonSelectString(AddonEvent type, AddonArgs args)
    {
        if (!Throttler.Throttle("AutoSubmarineCollectOverlay")) return;
        if (!CompanyWorkshopZones.Contains(Service.ClientState.TerritoryType)) return;

        Overlay.IsOpen = false;

        if (SelectString == null) return;
        var title = MemoryHelper.ReadStringNullTerminated((nint)SelectString->AtkValues[2].String);
        if (string.IsNullOrWhiteSpace(title) || !title.Contains("请选择潜水艇")) return;
        Overlay.IsOpen = true;
    }

    private void AlwaysYes(AddonEvent type, AddonArgs args)
    {
        if (!TaskHelper.IsBusy) return;
        Click.SendClick("select_yes");
    }
    #endregion

    public override void Uninit()
    {
        Service.CommandManager.RemoveSubCommand(Command);

        Service.AddonLifecycle.UnregisterListener(OnExplorationResult);
        Service.AddonLifecycle.UnregisterListener(AlwaysYes);
        Service.AddonLifecycle.UnregisterListener(OnAddonSelectString);
        Service.LogMessageManager.Unregister(OnLogMessages);

        RepairTaskHelper?.Abort();
        RepairTaskHelper = null;

        base.Uninit();
    }

    private class Config : ModuleConfiguration
    {
        public bool AddCommand = true;
    }
}
