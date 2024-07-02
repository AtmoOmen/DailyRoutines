using DailyRoutines.Helpers;
using DailyRoutines.Infos;
using DailyRoutines.Managers;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Hooking;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Group;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel.GeneratedSheets;

namespace DailyRoutines.Modules;

[ModuleDescription("FastRidePillionTitle", "FastRidePillionDescription", ModuleCategories.一般)]
public unsafe class FastRidePillion : DailyModuleBase
{
    private delegate nint AgentContextReceiveEventDelegate(AgentContext* agent, nint a2, nint a3, uint a4, nint a5);
    [Signature("40 55 53 57 41 54 41 55 41 56 48 8D 6C 24 ?? 48 81 EC ?? ?? ?? ?? 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 45 ?? 48 83 BD", DetourName = nameof(AgentContextReceiveEventDetour))]
    private static Hook<AgentContextReceiveEventDelegate>? AgentContextReceiveEventHook;

    private delegate void RidePillionDelegate(BattleChara* target, int seatIndex);
    [Signature("48 85 C9 0F 84 ?? ?? ?? ?? 48 89 6C 24 ?? 56 48 83 EC")]
    private static RidePillionDelegate? RidePillion;

    public override void Init()
    {
        Service.Hook.InitializeFromAttributes(this);
        AgentContextReceiveEventHook.Enable();

        Service.Condition.ConditionChange += OnCondition;
    }

    private static void OnCondition(ConditionFlag flag, bool value)
    {
        if (flag is not ConditionFlag.Mounted2 || !value) return;

        if (AddonState.ContextMenu != null && IsAddonAndNodesReady(AddonState.ContextMenu))
            AddonState.ContextMenu->Close(true);
    }

    private static nint AgentContextReceiveEventDetour(AgentContext* agent, nint a2, nint a3, uint a4, nint a5)
    {
        if (a5 != 0 || GetAtkValueInt(a3) != 1 || Flags.IsOnMount)
            return AgentContextReceiveEventHook.Original(agent, a2, a3, a4, a5);

        var targetObjectIDGame = agent->TargetObjectId;
        if (targetObjectIDGame.ObjectId == 0 || targetObjectIDGame.Type != 0)
            return AgentContextReceiveEventHook.Original(agent, a2, a3, a4, a5);

        var isInParty = GroupManager.Instance()->GetGroup()->IsEntityIdInParty(targetObjectIDGame.ObjectId);
        if (!isInParty) 
            return AgentContextReceiveEventHook.Original(agent, a2, a3, a4, a5);

        var chara = CharacterManager.Instance()->LookupBattleCharaByEntityId(targetObjectIDGame.ObjectId);
        if (chara == null)
            return AgentContextReceiveEventHook.Original(agent, a2, a3, a4, a5);

        var mount = chara->Character.Mount;
        if (mount.MountObject == null || mount.MountId == 0)
            return AgentContextReceiveEventHook.Original(agent, a2, a3, a4, a5);

        var mountSheet = LuminaCache.GetRow<Mount>(mount.MountId);
        if (mountSheet == null || mountSheet.ExtraSeats == 0)
            return AgentContextReceiveEventHook.Original(agent, a2, a3, a4, a5);

        RidePillion(chara, 10);
        return AgentContextReceiveEventHook.Original(agent, a2, a3, a4, a5);
    }

    public override void Uninit()
    {
        Service.Condition.ConditionChange -= OnCondition;

        base.Uninit();
    }
}
