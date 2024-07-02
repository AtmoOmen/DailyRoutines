using System;
using ClickLib;
using DailyRoutines.Helpers;
using DailyRoutines.Managers;
using Dalamud;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Hooking;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ImGuiNET;
using Lumina.Excel.GeneratedSheets;

namespace DailyRoutines.Modules;

[ModuleDescription("AutoCutSceneSkipTitle", "AutoCutSceneSkipDescription", ModuleCategories.系统)]
public class AutoCutSceneSkip : DailyModuleBase
{
    private const string ConditionSig = "75 11 BA ?? ?? ?? ?? 48 8B CF E8 ?? ?? ?? ?? 84 C0 74 4C";
    private static nint ConditionAddress;

    private delegate byte CutsceneHandleInputDelegate(nint a1, float a2);

    [Signature(
        "48 89 5C 24 ?? 48 89 6C 24 ?? 48 89 74 24 ?? 57 48 83 EC 40 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 44 24 ?? 80 79 27 00",
        DetourName = nameof(CutsceneHandleInputDetour))]
    private static Hook<CutsceneHandleInputDelegate>? CutsceneHandleInputHook;

    private delegate nint GetCutSceneRowDelegate(uint row);

    [Signature(
        "E8 ?? ?? ?? ?? 48 85 C0 74 ?? 0F B7 00 66 85 C0 74 ?? 44 8B C0 83 E0 ?? 3D ?? ?? ?? ?? 73 ?? 41 8B C0 41 8B C8 48 C1 E8 ?? 83 E1 ?? BA ?? ?? ?? ?? D3 E2 84 94 38",
        DetourName = nameof(GetCutSceneRowDetour))]
    private static Hook<GetCutSceneRowDelegate>? GetCutSceneRowHook;

    private static uint CurrentCutscene;
    private static bool ProhibitSkippingUnseenCutscene;

    public override void Init()
    {
        Service.Hook.InitializeFromAttributes(this);
        ConditionAddress = Service.SigScanner.ScanText(ConditionSig);
        CutsceneHandleInputHook?.Enable();
        GetCutSceneRowHook?.Enable();

        AddConfig(nameof(ProhibitSkippingUnseenCutscene), false);
        ProhibitSkippingUnseenCutscene = GetConfig<bool>(nameof(ProhibitSkippingUnseenCutscene));

        TaskHelper ??= new TaskHelper { AbortOnTimeout = true, TimeLimitMS = 5000, ShowDebug = false };
    }

    public override void ConfigUI()
    {
        if (ImGui.Checkbox(Service.Lang.GetText("AutoCutSceneSkip-ProhibitSkippingUnseenCutscene"),
                           ref ProhibitSkippingUnseenCutscene))
            UpdateConfig(nameof(ProhibitSkippingUnseenCutscene), ProhibitSkippingUnseenCutscene);

        ImGuiOm.HelpMarker(Service.Lang.GetText("AutoCutSceneSkip-ProhibitSkippingUnseenCutsceneHelp"));
    }

    private unsafe byte CutsceneHandleInputDetour(nint a1, float a2)
    {
        if (!Service.Condition[ConditionFlag.OccupiedInCutSceneEvent])
            return CutsceneHandleInputHook!.Original(a1, a2);

        if (ProhibitSkippingUnseenCutscene && CurrentCutscene != 0)
        {
            if (LuminaCache.GetRow<CutsceneWorkIndex>(CurrentCutscene).WorkIndex != 0 &&
                !UIState.Instance()->IsCutsceneSeen(CurrentCutscene))
                return CutsceneHandleInputHook!.Original(a1, a2);
        }

        try
        {
            var skippable = *(IntPtr*)(a1 + 56) != IntPtr.Zero;
            if (skippable)
            {
                SafeMemory.WriteBytes(ConditionAddress, [0xEB]);
                var ret = CutsceneHandleInputHook!.Original(a1, a2);
                SafeMemory.WriteBytes(ConditionAddress, [0x75]);

                TaskHelper.Enqueue(() =>
                {
                    if (!TryGetAddonByName<AtkUnitBase>("SelectString", out var addon) || !IsAddonAndNodesReady(addon))
                        return false;

                    if (addon->GetTextNodeById(2)->
                            NodeText.ExtractText().Contains(LuminaCache.GetRow<Addon>(281).Text.RawString))
                    {
                        if (Click.TrySendClick("select_string1"))
                        {
                            TaskHelper.Abort();
                            return true;
                        }

                        return false;
                    }

                    return false;
                });

                return ret;
            }
        }
        catch (Exception ex)
        {
            NotifyHelper.Debug(string.Empty, ex);
        }

        return CutsceneHandleInputHook!.Original(a1, a2);
    }

    private static nint GetCutSceneRowDetour(uint row)
    {
        CurrentCutscene = row;
        return GetCutSceneRowHook.Original(row);
    }
}
