using System.Collections.Generic;
using System.Linq;
using DailyRoutines.Helpers;
using DailyRoutines.Infos;
using DailyRoutines.Managers;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using ImGuiNET;

namespace DailyRoutines.Modules;

[ModuleDescription("AutoTankStanceTitle", "AutoTankStanceDescription", ModuleCategories.技能)]
public class AutoTankStance : DailyModuleBase
{
    private static bool ConfigOnlyAutoStanceWhenOneTank = true;

    private static HashSet<uint>? ContentsWithOneTank;
    private static readonly uint[] TankStanceStatuses = [79, 91, 743, 1833];

    private static readonly Dictionary<uint, uint> TankStanceActions = new()
    {
        // 剑术师 / 骑士
        { 1, 28 },
        { 19, 28 },
        // 斧术师 / 战士
        { 3, 48 },
        { 21, 48 },
        // 暗黑骑士
        { 32, 3629 },
        // 绝枪战士
        { 37, 16142 },
    };

    public override void Init()
    {
        AddConfig("OnlyAutoStanceWhenOneTank", true);
        ConfigOnlyAutoStanceWhenOneTank = GetConfig<bool>("OnlyAutoStanceWhenOneTank");

        TaskHelper ??= new TaskHelper { AbortOnTimeout = true, TimeLimitMS = 30000, ShowDebug = false };

        ContentsWithOneTank ??= PresetData.Contents
                                          .Where(x => (uint)x.Value.ContentMemberType.Value.TanksPerParty == 1)
                                          .Select(x => x.Key)
                                          .ToHashSet();

        Service.ClientState.TerritoryChanged += OnZoneChanged;
        Service.DutyState.DutyRecommenced += OnDutyRecommenced;
    }

    public override void ConfigUI()
    {
        if (ImGui.Checkbox(Service.Lang.GetText("AutoTankStance-OnlyAutoStanceWhenOneTank"),
                           ref ConfigOnlyAutoStanceWhenOneTank))
            UpdateConfig("OnlyAutoStanceWhenOneTank", ConfigOnlyAutoStanceWhenOneTank);

        ImGuiOm.HelpMarker(Service.Lang.GetText("AutoTankStance-OnlyAutoStanceWhenOneTankHelp"));
    }

    private void OnZoneChanged(ushort zone)
    {
        if (Service.ClientState.IsPvP) return;
        if ((ConfigOnlyAutoStanceWhenOneTank && ContentsWithOneTank.Contains(zone)) ||
            (!ConfigOnlyAutoStanceWhenOneTank && PresetData.Contents.ContainsKey(zone)))
        {
            TaskHelper.Abort();
            TaskHelper.DelayNext(1000);
            TaskHelper.Enqueue(CheckCurrentJob);
        }
    }

    private void OnDutyRecommenced(object? sender, ushort e)
    {
        TaskHelper.Abort();
        TaskHelper.Enqueue(CheckCurrentJob);
    }

    private static unsafe bool? CheckCurrentJob()
    {
        if (Flags.BetweenAreas) return false;
        if (!IsScreenReady()) return false;

        var player = Service.ClientState.LocalPlayer;
        if (player == null || player.ClassJob.Id == 0 || !player.IsTargetable) return false;

        var job = player.ClassJob.Id;
        if (!TankStanceActions.TryGetValue(job, out var actionID)) return true;

        if (Flags.OccupiedInEvent) return false;

        var battlePlayer = (BattleChara*)player.Address;
        foreach (var status in TankStanceStatuses)
            if (battlePlayer->GetStatusManager()->HasStatus(status))
                return true;

        return ActionManager.Instance()->UseAction(ActionType.Action, actionID);
    }

    public override void Uninit()
    {
        Service.ClientState.TerritoryChanged -= OnZoneChanged;
        Service.DutyState.DutyRecommenced -= OnDutyRecommenced;

        base.Uninit();
    }
}
