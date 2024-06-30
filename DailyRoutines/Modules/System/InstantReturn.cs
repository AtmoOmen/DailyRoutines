using DailyRoutines.Infos;
using DailyRoutines.Managers;
using Dalamud.Game.Command;
using Dalamud.Hooking;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using ImGuiNET;

namespace DailyRoutines.Modules;

[ModuleDescription("InstantReturnTitle", "InstantReturnDescription", ModuleCategories.系统)]
public unsafe class InstantReturn : DailyModuleBase
{
    private delegate byte ReturnDelegate(AgentInterface* agentReturn);
    [Signature("48 89 5C 24 ?? 48 89 74 24 ?? 57 48 83 EC ?? 48 8B 3D ?? ?? ?? ?? 48 8B D9 48 8D 0D",
               DetourName = nameof(ReturnDetour))]
    private static Hook<ReturnDelegate>? ReturnHook;

    private const string Command = "instantreturn";
    private static bool IsAddCommand;
    private static bool HookOrigin = true;

    public override void Init()
    {
        AddConfig(nameof(IsAddCommand), false);
        IsAddCommand = GetConfig<bool>(nameof(IsAddCommand));

        AddConfig(nameof(HookOrigin), true);
        HookOrigin = GetConfig<bool>(nameof(HookOrigin));

        Service.Hook.InitializeFromAttributes(this);
        ReturnHook?.Enable();

        if (IsAddCommand)
        {
            Service.CommandManager.AddSubCommand(Command, new CommandInfo(OnCommand)
            {
                HelpMessage = Service.Lang.GetText("InstantReturn-CommandHelp"),
            });
        }
    }

    public override void ConfigUI()
    {
        if (ImGui.Checkbox($"{Service.Lang.GetText("InstantReturn-HookOrigin")}", ref HookOrigin))
            UpdateConfig(nameof(HookOrigin), HookOrigin);

        if (ImGui.Checkbox(
                $"{Service.Lang.GetText("InstantReturn-AddCommand", Command)}: {Service.Lang.GetText("InstantReturn-CommandHelp")}",
                ref IsAddCommand))
        {
            UpdateConfig(nameof(IsAddCommand), IsAddCommand);
            if (IsAddCommand)
            {
                Service.CommandManager.AddSubCommand(Command, new CommandInfo(OnCommand)
                {
                    HelpMessage = Service.Lang.GetText("InstantReturn-CommandHelp"),
                });
            }
            else
                Service.CommandManager.RemoveSubCommand(Command);
        }
    }

    private static void OnCommand(string command, string arguments)
    {
        if (Service.ClientState.IsPvPExcludingDen) return;
        Service.ExecuteCommandManager.ExecuteCommand(ExecuteCommandFlag.InstantReturn);
    }

    private static byte ReturnDetour(AgentInterface* agentReturn)
    {
        if (!HookOrigin ||
            Service.ClientState.IsPvPExcludingDen ||
            ActionManager.Instance()->GetActionStatus(ActionType.GeneralAction, 6) != 0)
            return ReturnHook.Original(agentReturn);

        Service.ExecuteCommandManager.ExecuteCommand(ExecuteCommandFlag.InstantReturn);
        return 1;
    }

    public override void Uninit()
    {
        ReturnHook?.Dispose();
        Service.CommandManager.RemoveSubCommand(Command);

        base.Uninit();
    }
}
