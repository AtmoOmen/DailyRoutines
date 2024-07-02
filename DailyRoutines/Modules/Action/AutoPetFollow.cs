using DailyRoutines.Managers;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace DailyRoutines.Modules;

[ModuleDescription("AutoPetFollowTitle", "AutoPetFollowDescription", ModuleCategories.技能)]
public class AutoPetFollow : DailyModuleBase
{
    public override void Init()
    {
        Service.Condition.ConditionChange += OnConditionChanged;
    }

    private static unsafe void OnConditionChanged(ConditionFlag flag, bool value)
    {
        if (flag is not ConditionFlag.InCombat || value) return;

        var player = Service.ClientState.LocalPlayer;
        if (player == null) return;

        var isPetSummoned = CharacterManager.Instance()->LookupPetByOwnerObject((BattleChara*)player.Address) != null;
        if (!isPetSummoned) return;

        ActionManager.Instance()->UseAction(ActionType.PetAction, 2);
    }

    public override void Uninit()
    {
        Service.Condition.ConditionChange -= OnConditionChanged;
    }
}
