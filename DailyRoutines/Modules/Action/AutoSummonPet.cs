using System.Collections.Generic;
using DailyRoutines.Helpers;
using DailyRoutines.Infos;
using DailyRoutines.Managers;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;

namespace DailyRoutines.Modules;

[ModuleDescription("AutoSummonPetTitle", "AutoSummonPetDescription", ModuleCategories.技能)]
public class AutoSummonPet : DailyModuleBase
{
    private static readonly Dictionary<uint, uint> SummonActions = new()
    {
        // 学者
        { 28, 17215 },
        // 秘术师 / 召唤师
        { 26, 25798 },
        { 27, 25798 },
    };

    public override void Init()
    {
        TaskHelper ??= new TaskHelper { AbortOnTimeout = true, TimeLimitMS = 30000, ShowDebug = false };

        Service.ClientState.TerritoryChanged += OnZoneChanged;
        Service.DutyState.DutyRecommenced += OnDutyRecommenced;
    }

    // 重新挑战
    private void OnDutyRecommenced(object? sender, ushort e)
    {
        TaskHelper.Abort();
        TaskHelper.Enqueue(CheckCurrentJob);
    }

    // 进入副本
    private void OnZoneChanged(ushort zone)
    {
        TaskHelper.Abort();
        if (!PresetData.Contents.ContainsKey(zone) || Service.ClientState.IsPvP) return;

        TaskHelper.DelayNext(1000);
        TaskHelper.Enqueue(CheckCurrentJob);
    }

    private unsafe bool? CheckCurrentJob()
    {
        if (Flags.BetweenAreas || !IsScreenReady()) return false;

        var player = Service.ClientState.LocalPlayer;
        var job = player?.ClassJob.Id ?? 0;
        if (player == null || job == 0 || !player.IsTargetable) return false;

        if (!SummonActions.TryGetValue(job, out var actionID))
        {
            TaskHelper.Abort();
            return true;
        }

        if (Flags.OccupiedInEvent) return false;

        var state = CharacterManager.Instance()->LookupPetByOwnerObject((BattleChara*)player.Address) != null;
        if (state) return true;

        return ActionManager.Instance()->UseAction(ActionType.Action, actionID);
    }

    public override void Uninit()
    {
        Service.DutyState.DutyRecommenced -= OnDutyRecommenced;
        Service.ClientState.TerritoryChanged -= OnZoneChanged;

        base.Uninit();
    }
}
