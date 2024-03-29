using DailyRoutines.Infos;
using DailyRoutines.Managers;
using ECommons.Automation;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace DailyRoutines.Modules;

[ModuleDescription("AutoDrawACardTitle", "AutoDrawACardDescription", ModuleCategories.Combat)]
public class AutoDrawACard : DailyModuleBase
{
    public override void Init()
    {
        TaskManager ??= new TaskManager { AbortOnTimeout = true, TimeLimitMS = 30000, ShowDebug = false };

        Service.ClientState.TerritoryChanged += OnZoneChanged;
    }

    private void OnZoneChanged(ushort zone)
    {
        if (!Service.PresetData.Contents.ContainsKey(zone) || Service.ClientState.IsPvP) return;
        TaskManager.Abort();
        TaskManager.Enqueue(CheckCurrentJob);
    }

    private static unsafe bool? CheckCurrentJob()
    {
        if (TryGetAddonByName<AtkUnitBase>("NowLoading", out var addon) && HelpersOm.IsAddonAndNodesReady(addon))
            return false;

        var player = Service.ClientState.LocalPlayer;
        if (player == null || player.ClassJob.Id == 0) return false;

        if (player.ClassJob.Id != 33 || player.Level < 30) return true;
        if (IsOccupied()) return false;

        return ActionManager.Instance()->UseAction(ActionType.Action, 3590);
    }

    public override void Uninit()
    {
        Service.ClientState.TerritoryChanged -= OnZoneChanged;

        base.Uninit();
    }
}
