using DailyRoutines.Helpers;
using DailyRoutines.Infos;
using DailyRoutines.Managers;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace DailyRoutines.Modules;

[ModuleDescription("AutoDrawACardTitle", "AutoDrawACardDescription", ModuleCategories.技能)]
public class AutoDrawACard : DailyModuleBase
{
    public override void Init()
    {
        TaskHelper ??= new TaskHelper { AbortOnTimeout = true, TimeLimitMS = 30000, ShowDebug = false };

        Service.ClientState.TerritoryChanged += OnZoneChanged;
        Service.DutyState.DutyRecommenced += OnDutyRecommenced;
    }

    private void OnDutyRecommenced(object? sender, ushort e)
    {
        TaskHelper.Abort();
        TaskHelper.Enqueue(CheckCurrentJob);
    }

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

        if (job != 33 || player.Level < 30)
        {
            TaskHelper.Abort();
            return true;
        }
        
        if (Flags.OccupiedInEvent) return false;

        return ActionManager.Instance()->UseAction(ActionType.Action, 3590);
    }

    public override void Uninit()
    {
        Service.ClientState.TerritoryChanged -= OnZoneChanged;
        Service.DutyState.DutyRecommenced -= OnDutyRecommenced;

        base.Uninit();
    }
}
