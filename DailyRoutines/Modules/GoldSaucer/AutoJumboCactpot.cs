using System;
using System.Collections.Generic;
using ClickLib;
using DailyRoutines.Infos;
using DailyRoutines.Managers;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Interface.Utility;
using ECommons.Automation;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ImGuiNET;

namespace DailyRoutines.Modules;

[ModuleDescription("AutoJumboCactpotTitle", "AutoJumboCactpotDescription", ModuleCategories.GoldSaucer)]
public class AutoJumboCactpot : DailyModuleBase
{
    private enum Mode
    {
        Random,
        Fixed
    }

    private static readonly Dictionary<Mode, string> NumberModeLoc = new()
    {
        { Mode.Random, Service.Lang.GetText("AutoJumboCactpot-Random") },
        { Mode.Fixed, Service.Lang.GetText("AutoJumboCactpot-Fixed") }
    };

    private static Mode NumberMode = Mode.Random;
    private static int FixedNumber = 1;


    public override void Init()
    {
        AddConfig("NumberMode", Mode.Random);
        NumberMode = GetConfig<Mode>("NumberMode");

        AddConfig("FixedNumber", 1);
        FixedNumber = GetConfig<int>("FixedNumber");

        TaskManager ??= new TaskManager { AbortOnTimeout = true, TimeLimitMS = 5000, ShowDebug = false };

        Service.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "LotteryWeeklyInput", OnAddon);
    }

    public override void ConfigUI()
    {
        ImGui.SetNextItemWidth(100f * ImGuiHelpers.GlobalScale);
        if (ImGui.BeginCombo(Service.Lang.GetText("AutoJumboCactpot-NumberMode"), NumberModeLoc[NumberMode]))
        {
            foreach (var modePair in NumberModeLoc)
            {
                if (ImGui.Selectable(modePair.Value, modePair.Key == NumberMode))
                {
                    NumberMode = modePair.Key;
                    UpdateConfig("NumberMode", NumberMode);
                }
            }

            ImGui.EndCombo();
        }

        if (NumberMode == Mode.Fixed)
        {
            ImGui.SameLine();
            ImGui.SetNextItemWidth(100f * ImGuiHelpers.GlobalScale);
            ImGui.InputInt(Service.Lang.GetText("AutoJumboCactpot-FixedNumber"), ref FixedNumber, 0, 0);

            if (ImGui.IsItemDeactivatedAfterEdit())
            {
                FixedNumber = Math.Clamp(FixedNumber, 0, 9999);
                UpdateConfig("FixedNumber", FixedNumber);
            }
        }
    }

    private unsafe void OnAddon(AddonEvent type, AddonArgs args)
    {
        TaskManager.Abort();

        TaskManager.DelayNext(100);
        TaskManager.Enqueue(() =>
        {
            if (!EzThrottler.Throttle("AutoJumboCactpot", 50)) return false;
            var addon = (AtkUnitBase*)Service.Gui.GetAddonByName("LotteryWeeklyInput");
            if (!IsAddonAndNodesReady(addon)) return false;

            var rnd = new Random();
            var number = NumberMode switch
            {
                Mode.Random => rnd.Next(0, 9999),
                Mode.Fixed => FixedNumber,
                _ => 0
            };

            AddonManager.Callback(addon, true, number);
            return true;
        });

        TaskManager.DelayNext(100);
        TaskManager.Enqueue(() =>
        {
            if (!EzThrottler.Throttle("AutoJumboCactpot", 50)) return false;
            return Click.TrySendClick("select_yes");
        });

        TaskManager.DelayNext(100);
        TaskManager.Enqueue(() =>
        {
            if (!EzThrottler.Throttle("AutoJumboCactpot", 50)) return false;
            return Click.TrySendClick("select_yes");
        });
    }

    public override void Uninit()
    {
        Service.AddonLifecycle.UnregisterListener(OnAddon);
        
        base.Uninit();
    }
}