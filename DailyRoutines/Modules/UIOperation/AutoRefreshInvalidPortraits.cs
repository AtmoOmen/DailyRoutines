using System.Numerics;
using DailyRoutines.Helpers;
using DailyRoutines.Managers;
using DailyRoutines.Windows;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Interface.Colors;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ImGuiNET;

namespace DailyRoutines.Modules;

[ModuleDescription("AutoRefreshInvalidPortraitsTitle", "AutoRefreshInvalidPortraitsDescription",
                   ModuleCategories.界面操作)]
public unsafe class AutoRefreshInvalidPortraits : DailyModuleBase
{
    public override void Init()
    {
        TaskHelper ??= new TaskHelper { AbortOnTimeout = true, TimeLimitMS = 10000, ShowDebug = false };
        Overlay ??= new Overlay(this);

        Service.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "BannerEditor", OnAddonEditor);
        Service.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "BannerList", OnAddonList);
        Service.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, "BannerList", OnAddonList);
    }

    public override void OverlayUI()
    {
        var addon = (AtkUnitBase*)Service.Gui.GetAddonByName("BannerList");
        if (addon == null) return;

        var pos = new Vector2(addon->GetX() + 3, addon->GetY() - ImGui.GetWindowSize().Y);
        ImGui.SetWindowPos(pos);

        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(ImGuiColors.DalamudYellow, Service.Lang.GetText("AutoRefreshInvalidPortraitsTitle"));

        ImGui.BeginDisabled(TaskHelper.IsBusy);
        ImGui.SameLine();
        if (ImGui.Button(Service.Lang.GetText("Start"))) EnqueueARound();
        ImGui.EndDisabled();

        ImGui.SameLine();
        if (ImGui.Button(Service.Lang.GetText("Stop"))) TaskHelper.Abort();
    }

    private void OnAddonEditor(AddonEvent type, AddonArgs args)
    {
        if (!TaskHelper.IsBusy) return;

        var addon = (AddonBannerEditor*)args.Addon;
        if (addon == null) return;

        if (addon->SaveButton->IsEnabled)
        {
            AgentHelper.SendEvent(AgentId.BannerEditor, 0, 0, 9, -1, -1);
            AddonHelper.Callback(&addon->AtkUnitBase, true, 0, 8, 0);

            return;
        }

        AddonHelper.Callback(&addon->AtkUnitBase, true, 0, 8, 0);
    }

    private void OnAddonList(AddonEvent type, AddonArgs args) { Overlay.IsOpen = type == AddonEvent.PostSetup; }

    private void EnqueueARound()
    {
        var module = RaptureGearsetModule.Instance();
        for (var i = 0; i < module->Entries.Length; i++)
        {
            var entry = module->Entries[i];
            if (!entry.Flags.HasFlag(RaptureGearsetModule.GearsetFlag.Exists)) break;

            var il = i;
            TaskHelper.Enqueue(() => ClickBannerListEntry(il));
            TaskHelper.DelayNext(20);
            TaskHelper.Enqueue(() => AgentHelper.SendEvent(AgentId.BannerList, 6, 0, 0, 0U, 0, 0));
            TaskHelper.DelayNext(100);
        }
    }

    private static bool? ClickBannerListEntry(int itemIndex)
    {
        AgentHelper.SendEvent(AgentId.BannerList, 0, 2, itemIndex);
        AgentHelper.SendEvent(AgentId.BannerList, 0, 3, itemIndex);
        return true;
    }

    public override void Uninit()
    {
        Service.AddonLifecycle.UnregisterListener(OnAddonEditor);
        Service.AddonLifecycle.UnregisterListener(OnAddonList);

        base.Uninit();
    }
}
