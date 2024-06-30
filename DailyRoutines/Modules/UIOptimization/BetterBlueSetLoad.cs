using System;
using System.Numerics;
using DailyRoutines.Managers;
using DailyRoutines.Windows;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;

using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ImGuiNET;

namespace DailyRoutines.Modules;

[ModuleDescription("BetterBlueSetLoadTitle", "BetterBlueSetLoadDescription", ModuleCategories.界面优化)]
public unsafe class BetterBlueSetLoad : DailyModuleBase
{
    public override void Init()
    {
        Overlay ??= new Overlay(this);
        Overlay.BgAlpha = 0f;

        Service.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "AOZNotebookPresetList", OnAddon);
        Service.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, "AOZNotebookPresetList", OnAddon);
    }

    private void OnAddon(AddonEvent type, AddonArgs args)
    {
        var addon = (AtkUnitBase*)args.Addon;
        if (addon == null || addon->AtkValues[0].UInt == 1) return;

        Overlay.IsOpen = type switch
        {
            AddonEvent.PostSetup => true,
            AddonEvent.PreFinalize => false,
            _ => Overlay.IsOpen,
        };
    }

    public override void OverlayUI()
    {
        var addon = (AtkUnitBase*)Service.Gui.GetAddonByName("AOZNotebookPresetList");
        if (addon == null)
        {
            Overlay.IsOpen = false;
            return;
        }

        for (var i = 4U; i < 9; i++)
        {
            var node = addon->GetNodeById(i);
            if (node == null) continue;

            if (Throttler.Throttle($"BetterBlueSetLoad-{i}"))
            {
                var position = new Vector2(node->ScreenX + (node->Width / 2), node->ScreenY - 4f);
                ImGui.SetNextWindowPos(position);
                ImGui.SetNextWindowBgAlpha(1f);
            }

            if (ImGui.Begin($"ApplyPreset{i}",
                            ImGuiWindowFlags.NoDecoration |
                            ImGuiWindowFlags.AlwaysAutoResize |
                            ImGuiWindowFlags.NoSavedSettings |
                            ImGuiWindowFlags.NoMove |
                            ImGuiWindowFlags.NoDocking |
                            ImGuiWindowFlags.NoFocusOnAppearing |
                            ImGuiWindowFlags.NoNav))
            {
                if (ImGuiOm.ButtonSelectable($"{Service.Lang.GetText("BetterBlueSetLoad-ApplyPreset", i - 3)}"))
                {
                    CompareAndApply((int)i - 4);
                    CompareAndApply((int)i - 4);
                }

                ImGui.End();
            }
        }
    }

    private static void CompareAndApply(int index)
    {
        if (index > 4) return;

        var blueModule = AozNoteModule.Instance();
        var actionManager = ActionManager.Instance();

        Span<uint> presetActions = stackalloc uint[24];
        fixed (uint* actions = blueModule->ActiveSets[index].ActiveActions)
        {
            for (var i = 0; i < 24; i++)
            {
                var action = actions[i];
                if (action == 0) continue;

                presetActions[i] = action;
            }
        }

        Span<uint> currentActions = stackalloc uint[24];
        for (var i = 0; i < 24; i++)
        {
            var action = actionManager->GetActiveBlueMageActionInSlot(i);
            if (action == 0) continue;
            currentActions[i] = action;
        }

        Span<uint> finalActions = stackalloc uint[24];
        presetActions.CopyTo(finalActions);

        for (var i = 0; i < 24; i++)
        {
            if (finalActions[i] == 0) continue;
            for (var j = 0; j < 24; j++)
                if (finalActions[i] == currentActions[j])
                {
                    actionManager->SwapBlueMageActionSlots(i, j);
                    finalActions[i] = 0;
                    break;
                }
        }

        for (var i = 0; i < 24; i++)
        {
            var action = finalActions[i];
            if (action == 0) continue;
            actionManager->AssignBlueMageActionToSlot(i, action);
        }

        blueModule->LoadActiveSetHotBars(index);
    }


    public override void Uninit()
    {
        Service.AddonLifecycle.UnregisterListener(OnAddon);

        base.Uninit();
    }
}
