using System;
using System.Diagnostics;
using System.Numerics;
using DailyRoutines.Infos;
using DailyRoutines.Managers;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.ClientState.Conditions;
using ECommons.Automation;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using ImGuiNET;

namespace DailyRoutines.Modules;

[ModuleDescription("AutoNotifyCutSceneEndTitle", "AutoNotifyCutSceneEndDescription",
                   ModuleCategories.Notice)]
public class AutoNotifyCutSceneEnd : DailyModuleBase
{
    private static bool ConfigOnlyNotifyWhenBackground;
    private static bool IsDutyEnd;

    private static Stopwatch? Stopwatch;

    public override void Init()
    {
        TaskManager ??= new TaskManager { ShowDebug = false, TimeLimitMS = int.MaxValue, AbortOnTimeout = false };
        Stopwatch ??= new Stopwatch();

        AddConfig(this, "OnlyNotifyWhenBackground", true);
        ConfigOnlyNotifyWhenBackground = GetConfig<bool>(this, "OnlyNotifyWhenBackground");

        Service.AddonLifecycle.RegisterListener(AddonEvent.PostRequestedUpdate, "_PartyList", OnPartyList);
        Service.DutyState.DutyCompleted += OnDutyComplete;
        Service.ClientState.TerritoryChanged += OnZoneChanged;
    }

    public override void ConfigUI()
    {
        PreviewImageWithHelpText(Service.Lang.GetText("AutoNotifyCutSceneEnd-NotificationMessageHelp"),
                                 "https://raw.githubusercontent.com/AtmoOmen/DailyRoutines/main/imgs/AutoNotifyCutSceneEnd-1.png",
                                 new Vector2(378, 113));

        if (ImGui.Checkbox(Service.Lang.GetText("AutoNotifyCutSceneEnd-OnlyWhenBackground"),
                           ref ConfigOnlyNotifyWhenBackground))
            UpdateConfig(this, "OnlyNotifyWhenBackground", ConfigOnlyNotifyWhenBackground);
    }

    private unsafe void OnPartyList(AddonEvent type, AddonArgs args)
    {
        if (TaskManager.IsBusy || Service.ClientState.IsPvP || !IsBoundByDuty() || IsDutyEnd) return;

        var isSBInCutScene = false;
        foreach (var member in Service.PartyList)
        {
            var chara = (Character*)member.GameObject.Address;
            if (chara == null) continue;
            if (!Service.DutyState.IsDutyStarted && !member.GameObject.IsTargetable)
            {
                isSBInCutScene = false;
                break;
            }

            if (chara->CharacterData.OnlineStatus == 15) isSBInCutScene = true;
        }

        if (isSBInCutScene)
        {
            Service.Log.Debug("检测到有成员正在过场动画中");
            Stopwatch.Restart();
            TaskManager.Enqueue(IsNoOneWatchingCutscene);
            TaskManager.Enqueue(ShowNoticeMessage);
        }
    }

    private unsafe bool? IsNoOneWatchingCutscene()
    {
        if (IsDutyEnd)
        {
            Stopwatch.Reset();
            TaskManager.Abort();
            return true;
        }

        foreach (var member in Service.PartyList)
        {
            var chara = (Character*)member.GameObject.Address;
            if (chara == null) continue;
            if (chara->CharacterData.OnlineStatus == 15) return false;
        }

        if (Stopwatch.Elapsed < TimeSpan.FromSeconds(5))
        {
            Stopwatch.Reset();
            TaskManager.Abort();
        }

        return true;
    }

    private static bool? ShowNoticeMessage()
    {
        if (ConfigOnlyNotifyWhenBackground)
        {
            if (!HelpersOm.IsGameForeground())
                Service.Notice.Notify("", Service.Lang.GetText("AutoNotifyCutSceneEnd-NotificationMessage"));
            return true;
        }

        Service.Notice.Notify("", Service.Lang.GetText("AutoNotifyCutSceneEnd-NotificationMessage"));
        return true;
    }

    private void OnZoneChanged(ushort zone)
    {
        Stopwatch.Reset();
        TaskManager.Abort();
        IsDutyEnd = false;
    }

    private void OnDutyComplete(object? sender, ushort duty)
    {
        Stopwatch.Reset();
        TaskManager.Abort();
        IsDutyEnd = true;
    }

    public static bool IsBoundByDuty()
    {
        return Service.Condition[ConditionFlag.BoundByDuty] || Service.Condition[ConditionFlag.BoundByDuty56] ||
               Service.Condition[ConditionFlag.BoundByDuty95];
    }

    public override void Uninit()
    {
        Stopwatch.Reset();

        Service.DutyState.DutyCompleted -= OnDutyComplete;
        Service.ClientState.TerritoryChanged -= OnZoneChanged;
        Service.AddonLifecycle.UnregisterListener(OnPartyList);

        base.Uninit();
    }
}
