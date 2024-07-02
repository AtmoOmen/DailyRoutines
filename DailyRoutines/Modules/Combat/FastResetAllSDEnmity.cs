using System.Linq;
using System.Threading.Tasks;
using DailyRoutines.Managers;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Command;
using ImGuiNET;

namespace DailyRoutines.Modules;

[ModuleDescription("FastResetAllSDEnmityTitle", "FastResetAllSDEnmityDescription", ModuleCategories.战斗)]
public class FastResetAllSDEnmity : DailyModuleBase
{
    private const string Command = "resetallsd";
    private static bool IsAddCommand = true;

    public override void Init()
    {
        AddConfig(nameof(IsAddCommand), true);
        IsAddCommand = GetConfig<bool>(nameof(IsAddCommand));

        Service.ExecuteCommandManager.Register(OnResetStrikingDummies);
        if (IsAddCommand)
        {
            Service.CommandManager.AddSubCommand(Command, new CommandInfo(OnCommand)
            {
                HelpMessage = Service.Lang.GetText("FastResetAllSDEnmity-CommandHelp"),
            });
        }
    }

    public override void ConfigUI()
    {
        if (ImGui.Checkbox(
                $"{Service.Lang.GetText("FastResetAllSDEnmity-AddCommand", Command)}: {Service.Lang.GetText("FastResetAllSDEnmity-CommandHelp")}",
                ref IsAddCommand))
        {
            UpdateConfig(nameof(IsAddCommand), IsAddCommand);
            if (IsAddCommand)
            {
                Service.CommandManager.AddSubCommand(Command, new CommandInfo(OnCommand)
                {
                    HelpMessage = Service.Lang.GetText("FastResetAllSDEnmity-CommandHelp"),
                });
            }
            else
                Service.CommandManager.RemoveSubCommand(Command);
        }
    }

    private static void OnCommand(string command, string arguments) => ResetAllStrikingDummies();

    public static void OnResetStrikingDummies(int command, int objectID, int param2, int param3, int param4)
    {
        if (command != 319) return;

        ResetAllStrikingDummies();
    }

    private static void ResetAllStrikingDummies()
    {
        Task.Run(async () =>
        {
            const int iterations = 15;
            const int delay = 100;

            var targets = Service.ObjectTable.Where(x => x.ObjectKind == ObjectKind.BattleNpc &&
                                                         x.TargetObject != null &&
                                                         x is IBattleChara { NameId: 541 })
                                 .Select(x => x.EntityId)
                                 .ToList();

            for (var i = 0; i < iterations; i++)
            {
                foreach (var targetId in targets)
                    Service.ExecuteCommandManager.ExecuteCommand(319, (int)targetId);

                await Task.Delay(delay);
            }
        });
    }

    public override void Uninit()
    {
        Service.ExecuteCommandManager.Unregister(OnResetStrikingDummies);
        Service.CommandManager.RemoveSubCommand(Command);

        base.Uninit();
    }
}
