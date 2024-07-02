using System;
using System.Runtime.InteropServices;
using DailyRoutines.Helpers;
using DailyRoutines.Managers;
using Dalamud.Hooking;
using Dalamud.Utility.Signatures;

using FFXIVClientStructs.FFXIV.Component.GUI;

namespace DailyRoutines.Modules;

[ModuleDescription("AutoNumericInputMaxTitle", "AutoNumericInputMaxDescription", ModuleCategories.界面优化)]
public unsafe class AutoNumericInputMax : DailyModuleBase
{
    private delegate nint UldUpdateDelegate(AtkComponentNumericInput* component);

    [Signature("40 53 48 83 EC ?? 48 8B D9 48 83 C1 ?? E8 ?? ?? ?? ?? 80 BB ?? ?? ?? ?? ?? 74 ?? 48 8B CB", DetourName = nameof(UldUpdateDetour))]
    private readonly Hook<UldUpdateDelegate>? UldUpdateHook;

    private static Throttler<nint> Throttler = new();

    private static long _LastInterruptTime;

    public override void Init()
    {
        Service.Hook.InitializeFromAttributes(this);
        UldUpdateHook?.Enable();
    }

    public override void ConfigUI()
    {
        ConflictKeyText();

        ImGuiOm.HelpMarker(Service.Lang.GetText("AutoNumericInputMax-InterruptHelp"));
    }

    private nint UldUpdateDetour(AtkComponentNumericInput* component)
    {
        var result = UldUpdateHook.Original(component);

        // 一些界面切换 Tab 后也会刷新输入状态
        if (Service.KeyState[Service.Config.ConflictKey])
            _LastInterruptTime = Environment.TickCount64;

        if (Environment.TickCount64 - _LastInterruptTime > 10000)
        {
            if (Throttler.Throttle((nint)component, 250))
            {
                var value = Marshal.ReadInt32((nint)component + 504);
                var nodeFlags = component->AtkComponentInputBase.AtkComponentBase.OwnerNode->AtkResNode.NodeFlags;
                if (value == 1 && nodeFlags.HasFlag(NodeFlags.Enabled | NodeFlags.Visible) && component->Data.Max < 9999)
                    component->SetValue(component->Data.Max);
            }
        }

        return result;
    }
}
