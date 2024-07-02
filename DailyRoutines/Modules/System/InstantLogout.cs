using System;
using System.Runtime.InteropServices;
using DailyRoutines.Helpers;
using DailyRoutines.Managers;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Hooking;
using Dalamud.Interface.Colors;
using Dalamud.Memory;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using ImGuiNET;
using Lumina.Excel.GeneratedSheets;
using Action = System.Action;

namespace DailyRoutines.Modules;

[ModuleDescription("InstantLogoutTitle", "InstantLogoutDescription", ModuleCategories.系统)]
public unsafe class InstantLogout : DailyModuleBase
{
    private delegate nint SendLogoutDelegate();
    [Signature("40 53 48 83 EC ?? 48 8B 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 48 8B D8 48 85 C0 74 ?? 48 8B 0D")]
    private static SendLogoutDelegate? SendLogout;

    private delegate nint SystemMenuExecuteDelegate(AgentHUD* agentHud, int a2, uint a3, int a4, nint a5);
    [Signature("48 89 5C 24 ?? 55 57 41 55 41 56 41 57 48 8D 6C 24 ?? 48 81 EC ?? ?? ?? ?? 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 45 ?? 80 B9",
               DetourName = nameof(SystemMenuExecuteDetour))]
    private static Hook<SystemMenuExecuteDelegate>? SystemMenuExecuteHook;

    private delegate byte ProcessSendedChatDelegate(nint uiModule, byte** message, nint a3);
    [Signature("E8 ?? ?? ?? ?? FE 86 ?? ?? ?? ?? C7 86 ?? ?? ?? ?? ?? ?? ?? ??", DetourName = nameof(ProcessSendedChatDetour))]
    private static Hook<ProcessSendedChatDelegate>? ProcessSendedChatHook;

    private static readonly Lazy<TextCommand> LogoutLine = new(() => LuminaCache.GetRow<TextCommand>(172));
    private static readonly Lazy<TextCommand> ShutdownLine = new(() => LuminaCache.GetRow<TextCommand>(173));

    public override void Init()
    {
        Service.Hook.InitializeFromAttributes(this);
        SystemMenuExecuteHook.Enable();
        ProcessSendedChatHook.Enable();
    }

    public override void ConfigUI()
    {
        ImGui.TextColored(ImGuiColors.DalamudOrange, Service.Lang.GetText("InstantLogout-ManualOperation"));
        ImGui.Separator();

        if (ImGui.Button(Service.Lang.GetText("InstantLogout-Logout"))) Logout();
        

        ImGui.SameLine();
        if (ImGui.Button(Service.Lang.GetText("InstantLogout-Shutdown"))) Shutdown();
    }

    private static nint SystemMenuExecuteDetour(AgentHUD* agentHud, int a2, uint a3, int a4, nint a5)
    {
        if (a2 is 1 && a4 is -1)
        {
            switch (a3)
            {
                case 23:
                    Logout();
                    return 0;
                case 24:
                    Shutdown();
                    return 0;
            }
        }
        
        var original = SystemMenuExecuteHook.Original(agentHud, a2, a3, a4, a5);
        return original;
    }

    private byte ProcessSendedChatDetour(nint uiModule, byte** message, nint a3)
    {
        var messageDecode = MemoryHelper.ReadSeStringNullTerminated((nint)(*message)).ToString();

        if (string.IsNullOrWhiteSpace(messageDecode) || !messageDecode.StartsWith('/'))
            return ProcessSendedChatHook.Original(uiModule, message, a3);

        CheckCommand(messageDecode, LogoutLine.Value, Logout);
        CheckCommand(messageDecode, ShutdownLine.Value, Shutdown);

        return ProcessSendedChatHook.Original(uiModule, message, a3);
    }

    private static void CheckCommand(string message, TextCommand command, Action action)
    {
        if (message == command.Command.RawString || message == command.Alias.RawString) action();
    }

    private static void Logout()
    {
        for (var i = 0; i < Service.Condition.MaxEntries; i++)
            Marshal.WriteByte(Service.Condition.Address + i, 0);

        foreach (var addon in RaptureAtkUnitManager.Instance()->AtkUnitManager.AllLoadedUnitsList.Entries)
        {
            if (addon.Value == null || !addon.Value->IsVisible) continue;
            addon.Value->Close(true);
        }

        SendLogout();
    }

    private static void Shutdown() => ChatHelper.Instance.SendMessage("/xlkill");
}
