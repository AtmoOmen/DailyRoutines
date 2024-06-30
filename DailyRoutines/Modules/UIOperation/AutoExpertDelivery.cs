using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using ClickLib;
using ClickLib.Clicks;
using DailyRoutines.Infos;
using DailyRoutines.Infos.Clicks;
using DailyRoutines.Managers;
using DailyRoutines.Windows;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Interface.Colors;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ImGuiNET;

namespace DailyRoutines.Modules;

[ModuleDescription("AutoExpertDeliveryTitle", "AutoExpertDeliveryDescription", ModuleCategories.界面操作)]
public unsafe class AutoExpertDelivery : DailyModuleBase
{
    private class Config : ModuleConfiguration
    {
        public bool SkipWhenHQ;
        public bool AutoSwitchWhenOpen = true;
    }

    private static readonly List<InventoryType> ValidInventoryTypes =
    [
        InventoryType.Inventory1, InventoryType.Inventory2, InventoryType.Inventory3,
        InventoryType.Inventory4, InventoryType.ArmoryBody, InventoryType.ArmoryEar, InventoryType.ArmoryFeets,
        InventoryType.ArmoryHands, InventoryType.ArmoryHead, InventoryType.ArmoryLegs, InventoryType.ArmoryRings,
        InventoryType.ArmoryNeck, InventoryType.ArmoryWrist, InventoryType.ArmoryRings, InventoryType.ArmoryMainHand,
        InventoryType.ArmoryOffHand,
    ];

    private static Config ModuleConfig = null!;

    private static HashSet<uint> HQItems = [];

    public override void Init()
    {
        ModuleConfig = LoadConfig<Config>() ?? new();

        TaskHelper ??= new() { AbortOnTimeout = true };
        Overlay ??= new Overlay(this);

        Service.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "GrandCompanySupplyList", OnAddonSupplyList);
        Service.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, "GrandCompanySupplyList", OnAddonSupplyList);
        Service.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "GrandCompanySupplyReward", OnAddonSupplyReward);
        Service.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "SelectYesno", OnAddonYesno);
    }

    public override void OverlayUI()
    {
        var addon = (AtkUnitBase*)Service.Gui.GetAddonByName("GrandCompanySupplyList");
        if (addon == null) return;

        var pos = new Vector2(addon->GetX() - ImGui.GetWindowSize().X, addon->GetY() + 6);
        ImGui.SetWindowPos(pos);

        ImGui.TextColored(ImGuiColors.DalamudYellow, Service.Lang.GetText("AutoExpertDeliveryTitle"));
        ImGui.Separator();

        if (ImGui.Checkbox(Service.Lang.GetText("AutoExpertDelivery-AutoSwitch"), ref ModuleConfig.AutoSwitchWhenOpen))
        {
            if (ModuleConfig.AutoSwitchWhenOpen)
                ClickGrandCompanySupplyList.Using((nint)addon).ExpertDelivery();
            SaveConfig(ModuleConfig);
        }

        ImGui.BeginDisabled(TaskHelper.IsBusy);
        if (ImGui.Checkbox(Service.Lang.GetText("AutoExpertDelivery-SkipHQ"), ref ModuleConfig.SkipWhenHQ))
            SaveConfig(ModuleConfig);

        if (ImGui.Button(Service.Lang.GetText("Start")))
            EnqueueARound();
        ImGui.EndDisabled();

        ImGui.SameLine();
        if (ImGui.Button(Service.Lang.GetText("Stop")))
            TaskHelper.Abort();
    }

    private void EnqueueARound()
    {
        TaskHelper.Enqueue(ClickCategory);
        TaskHelper.Enqueue(CheckIfToReachCap);
        TaskHelper.Enqueue(ClickListUI);
        TaskHelper.Enqueue(EnqueueARound);
    }

    private void CheckIfToReachCap()
    {
        if (AddonState.GrandCompanySupplyList == null || !IsAddonAndNodesReady(AddonState.GrandCompanySupplyList)) return;

        var addon = (AddonGrandCompanySupplyList*)AddonState.GrandCompanySupplyList;
        if (addon == null) return;

        if (addon->ExpertDeliveryList->ListLength == 0)
        {
            TaskHelper.Abort();
            MakeSureAddonsClosed();
            return;
        }

        var parts = Marshal.PtrToStringUTF8((nint)AtkStage.Instance()->GetStringArrayData()[32]->StringArray[2])
                           .Split('/');

        var capAmount = int.Parse(parts[1].Replace(",", ""));

        var grandCompany = UIState.Instance()->PlayerState.GrandCompany;
        if ((GrandCompany)grandCompany == GrandCompany.None)
        {
            TaskHelper.Abort();
            MakeSureAddonsClosed();
            return;
        }

        var companySeals = InventoryManager.Instance()->GetCompanySeals(grandCompany);

        var firstItemAmount = AddonState.GrandCompanySupplyList->AtkValues[265].UInt;
        if (firstItemAmount + companySeals > capAmount)
        {
            TaskHelper.Abort();
            MakeSureAddonsClosed();
        }
    }

    private void ClickListUI()
    {
        if (AddonState.GrandCompanySupplyList == null || !IsAddonAndNodesReady(AddonState.GrandCompanySupplyList)) return;

        var addon = (AddonGrandCompanySupplyList*)AddonState.GrandCompanySupplyList;
        if (addon == null) return;

        if (ModuleConfig.SkipWhenHQ)
        {
            HQItems = InventoryScanner(ValidInventoryTypes);

            var onlyHQLeft = true;
            for (var i = 0; i < addon->ExpertDeliveryList->ListLength; i++)
            {
                var itemID = addon->AtkUnitBase.AtkValues[i + 425].UInt;
                var isHQItem = HQItems.Contains(itemID);
                if (isHQItem) continue;

                ClickGrandCompanySupplyList.Using((nint)addon).ItemEntry(i);
                onlyHQLeft = false;
                break;
            }

            if (onlyHQLeft) TaskHelper.Abort();
        }
        else
            ClickGrandCompanySupplyList.Using((nint)addon).ItemEntry(0);
    }

    private static void ClickCategory()
    {
        var addon = AddonState.GrandCompanySupplyList;
        if (addon == null) return;

        ClickGrandCompanySupplyList.Using((nint)addon).ExpertDelivery();
    }

    private static void MakeSureAddonsClosed()
    {
        if (AddonState.GrandCompanySupplyReward != null)
            AddonState.GrandCompanySupplyReward->Close(true);

        if (AddonState.SelectYesno != null)
            AddonState.SelectYesno->Close(true);
    }

    public static HashSet<uint> InventoryScanner(IEnumerable<InventoryType> inventories)
    {
        var inventoryManager = InventoryManager.Instance();

        var list = new HashSet<uint>();
        if (inventoryManager == null) return list;

        foreach (var inventory in inventories)
        {
            var container = inventoryManager->GetInventoryContainer(inventory);
            if (container == null) continue;

            for (var i = 0; i < container->Size; i++)
            {
                var slot = container->GetInventorySlot(i);
                if (slot == null) continue;

                var item = slot->ItemID;
                if (item == 0) continue;

                if (!slot->Flags.HasFlag(InventoryItem.ItemFlags.HQ)) continue;

                list.Add(item);
            }
        }

        return list;
    }

    private void OnAddonSupplyList(AddonEvent type, AddonArgs args)
    {
        Overlay.IsOpen = type switch
        {
            AddonEvent.PostSetup => true,
            AddonEvent.PreFinalize => false,
            _ => Overlay.IsOpen,
        };

        if (ModuleConfig.AutoSwitchWhenOpen && type == AddonEvent.PostSetup)
            ClickGrandCompanySupplyList.Using(args.Addon).ExpertDelivery();
    }

    private void OnAddonSupplyReward(AddonEvent type, AddonArgs args)
    {
        TaskHelper.Enqueue(() =>
        {
            if (AddonState.GrandCompanySupplyList != null)
                AddonState.GrandCompanySupplyList->Close(false);
        }, "ConfirmListAddonClosed", 2);
        TaskHelper.Enqueue(() => ClickGrandCompanySupplyReward.Using(args.Addon).Deliver(), "ConfirmSupplyReward", 2);
        TaskHelper.DelayNext("Delay_ConfirmSupplyReward", 20, false, 2);
    }

    private void OnAddonYesno(AddonEvent type, AddonArgs args)
    {
        if (!TaskHelper.IsBusy) return;
        TaskHelper.Enqueue(() => Click.SendClick(ModuleConfig.SkipWhenHQ ? "select_no" : "select_yes"), "ConfirmHQ", 2);
        TaskHelper.DelayNext("Delay_ConfirmHQ", 20, false, 2);
    }

    public override void Uninit()
    {
        Service.AddonLifecycle.UnregisterListener(OnAddonSupplyList);
        Service.AddonLifecycle.UnregisterListener(OnAddonSupplyReward);
        Service.AddonLifecycle.UnregisterListener(OnAddonYesno);

        base.Uninit();
    }
}
