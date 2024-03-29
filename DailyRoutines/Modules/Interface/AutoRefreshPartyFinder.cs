using System;
using System.Numerics;
using System.Timers;
using DailyRoutines.Infos;
using DailyRoutines.Managers;
using DailyRoutines.Windows;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Interface.Utility;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ImGuiNET;
using Timer = System.Timers.Timer;

namespace DailyRoutines.Modules;

[ModuleDescription("AutoRefreshPartyFinderTitle", "AutoRefreshPartyFinderDescription", ModuleCategories.Interface)]
public class AutoRefreshPartyFinder : DailyModuleBase
{
    private static int ConfigRefreshInterval = 10; // 秒
    private static bool ConfigOnlyInactive = true;

    private static Vector2 WindowPos = new(1000);

    private static Timer? PFRefreshTimer;

    public override void Init()
    {
        AddConfig(this, "RefreshInterval", ConfigRefreshInterval);
        AddConfig(this, "OnlyInactive", ConfigOnlyInactive);

        ConfigRefreshInterval = GetConfig<int>(this, "RefreshInterval");
        ConfigOnlyInactive = GetConfig<bool>(this, "OnlyInactive");

        Overlay ??= new Overlay(this);

        PFRefreshTimer ??= new Timer(TimeSpan.FromSeconds(ConfigRefreshInterval));
        PFRefreshTimer.AutoReset = true;
        PFRefreshTimer.Elapsed += OnRefreshTimer;

        if (Service.Gui.GetAddonByName("LookingForGroup") != nint.Zero)
            OnAddonPF(AddonEvent.PostSetup, null);
        Service.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "LookingForGroup", OnAddonPF);
        Service.AddonLifecycle.RegisterListener(AddonEvent.PostRefresh, "LookingForGroup", OnAddonPF);
        Service.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, "LookingForGroup", OnAddonPF);
    }

    public override unsafe void OverlayUI()
    {
        var addon = (AtkUnitBase*)Service.Gui.GetAddonByName("LookingForGroup");
        var refreshButton = addon->GetButtonNodeById(47)->AtkComponentBase.AtkResNode;
        if (addon == null || refreshButton == null) return;

        Overlay.Position = WindowPos;

        ImGui.BeginGroup();
        ImGui.SetNextItemWidth(80f * ImGuiHelpers.GlobalScale);
        if (ImGui.InputInt(Service.Lang.GetText("AutoRefreshPartyFinder-RefreshInterval"), ref ConfigRefreshInterval, 1,
                           1, ImGuiInputTextFlags.EnterReturnsTrue))
        {
            ConfigRefreshInterval = Math.Max(1, ConfigRefreshInterval);
            UpdateConfig(this, "RefreshInterval", ConfigRefreshInterval);

            PFRefreshTimer.Stop();
            PFRefreshTimer.Interval = ConfigRefreshInterval * 1000;
            if (Service.Gui.GetAddonByName("LookingForGroup") != nint.Zero)
                PFRefreshTimer.Start();
        }

        ImGui.SameLine();
        if (ImGui.Checkbox(Service.Lang.GetText("AutoRefreshPartyFinder-OnlyInactive"), ref ConfigOnlyInactive))
            UpdateConfig(this, "OnlyInactive", ConfigOnlyInactive);
        ImGui.EndGroup();

        var contentSize = ImGui.GetItemRectSize();
        var framePadding = ImGui.GetStyle().FramePadding;

        WindowPos = new Vector2(refreshButton->ScreenX - contentSize.X - (4 * framePadding.X),
                                refreshButton->ScreenY - framePadding.Y);
    }

    private void OnAddonPF(AddonEvent type, AddonArgs? args)
    {
        switch (type)
        {
            case AddonEvent.PostSetup:
                PFRefreshTimer.Restart();
                Overlay.IsOpen = true;
                break;
            case AddonEvent.PostRefresh:
                if (ConfigOnlyInactive)
                    PFRefreshTimer.Restart();
                break;
            case AddonEvent.PreFinalize:
                PFRefreshTimer.Stop();
                Overlay.IsOpen = false;
                break;
        }
    }

    private static unsafe void OnRefreshTimer(object? sender, ElapsedEventArgs e)
    {
        if (TryGetAddonByName<AtkUnitBase>("LookingForGroup", out var addon) && HelpersOm.IsAddonAndNodesReady(addon))
        {
            AddonManager.Callback(addon, true, 17);
            return;
        }

        PFRefreshTimer.Stop();
    }

    public override void Uninit()
    {
        Service.AddonLifecycle.UnregisterListener(OnAddonPF);

        if (PFRefreshTimer != null)
        {
            PFRefreshTimer.Elapsed -= OnRefreshTimer;
            PFRefreshTimer.Stop();
        }
        PFRefreshTimer?.Dispose();
        PFRefreshTimer = null;

        base.Uninit();
    }
}
