using System.Collections.Generic;
using System.Linq;
using DailyRoutines.Infos;
using DailyRoutines.Managers;
using ECommons.Automation;
using ECommons.GameFunctions;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ImGuiNET;

namespace DailyRoutines.Modules;

[ModuleDescription("AutoTankStanceTitle", "AutoTankStanceDescription", ModuleCategories.Combat)]
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
        { 37, 16142 }
    };

    public override void Init()
    {
        AddConfig(this, "OnlyAutoStanceWhenOneTank", true);
        ConfigOnlyAutoStanceWhenOneTank = GetConfig<bool>(this, "OnlyAutoStanceWhenOneTank");

        TaskManager ??= new TaskManager { AbortOnTimeout = true, TimeLimitMS = 30000, ShowDebug = false };

        ContentsWithOneTank ??= Service.PresetData.Contents
                                       .Where(x => (uint)x.Value.ContentMemberType.Value.TanksPerParty == 1)
                                       .Select(x => x.Key)
                                       .ToHashSet();

        Service.ClientState.TerritoryChanged += OnZoneChanged;
    }

    public override void ConfigUI()
    {
        if (ImGui.Checkbox(Service.Lang.GetText("AutoTankStance-OnlyAutoStanceWhenOneTank"),
                           ref ConfigOnlyAutoStanceWhenOneTank))
            UpdateConfig(this, "OnlyAutoStanceWhenOneTank", ConfigOnlyAutoStanceWhenOneTank);

        ImGuiOm.HelpMarker(Service.Lang.GetText("AutoTankStance-OnlyAutoStanceWhenOneTankHelp"));
    }

    private void OnZoneChanged(ushort zone)
    {
        if (Service.ClientState.IsPvP) return;
        if ((ConfigOnlyAutoStanceWhenOneTank && ContentsWithOneTank.Contains(zone)) ||
            (!ConfigOnlyAutoStanceWhenOneTank && Service.PresetData.Contents.ContainsKey(zone)))
        {
            TaskManager.Abort();
            TaskManager.Enqueue(CheckCurrentJob);
        }
    }

    private static unsafe bool? CheckCurrentJob()
    {
        if (TryGetAddonByName<AtkUnitBase>("NowLoading", out var addon) && HelpersOm.IsAddonAndNodesReady(addon))
            return false;

        var player = Service.ClientState.LocalPlayer;
        if (player == null || player.ClassJob.Id == 0) return false;

        var job = player.ClassJob.Id;
        if (!TankStanceActions.TryGetValue(job, out var actionID)) return true;

        if (IsOccupied()) return false;
        foreach (var status in TankStanceStatuses)
            if (player.BattleChara()->GetStatusManager->HasStatus(status))
                return true;
        return ActionManager.Instance()->UseAction(ActionType.Action, actionID);
    }

    public override void Uninit()
    {
        Service.ClientState.TerritoryChanged -= OnZoneChanged;

        base.Uninit();
    }
}
