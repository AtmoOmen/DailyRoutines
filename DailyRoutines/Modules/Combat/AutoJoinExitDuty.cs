using ClickLib.Clicks;
using DailyRoutines.Helpers;
using DailyRoutines.Infos;
using DailyRoutines.Managers;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Command;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace DailyRoutines.Modules;

[ModuleDescription("AutoJoinExitDutyTitle", "AutoJoinExitDutyDescription", ModuleCategories.战斗)]
public unsafe class AutoJoinExitDuty : DailyModuleBase
{
    [Signature("E8 ?? ?? ?? ?? 48 8B 43 28 B1 01")]
    private static AbandonDutyDelagte? AbandonDuty;

    private static AtkUnitBase* ContentsFinder => (AtkUnitBase*)Service.Gui.GetAddonByName("ContentsFinder");

    public override void Init()
    {
        Service.Hook.InitializeFromAttributes(this);

        TaskHelper ??= new TaskHelper { AbortOnTimeout = true, TimeLimitMS = 60000, ShowDebug = false };
        Service.CommandManager.AddSubCommand("joinexitduty",
                                             new CommandInfo(OnCommand)
                                             {
                                                 HelpMessage = Service.Lang.GetText("AutoJoinExitDutyTitle"),
                                                 ShowInHelp = true,
                                             });
    }

    private void OnCommand(string command, string arguments)
    {
        if (Service.PartyList.Length > 0)
        {
            NotifyHelper.NotificationError(Service.Lang.GetText("AutoJoinExitDuty-AlreadyInParty"));
            return;
        }

        if (Flags.BoundByDuty)
        {
            NotifyHelper.NotificationError(Service.Lang.GetText("AutoJoinExitDuty-AlreadyInDutyNotice"));
            return;
        }

        if (!UIState.IsInstanceContentUnlocked(4))
        {
            NotifyHelper.NotificationError(Service.Lang.GetText("AutoJoinExitDuty-DutyLockedNotice"));
            return;
        }

        TaskHelper.Abort();
        EnqueueARound();
    }

    private void EnqueueARound()
    {
        TaskHelper.Enqueue(CheckAndSwitchJob);
        TaskHelper.Enqueue(OpenContentsFinder);
        TaskHelper.Enqueue(CancelSelectedContents);
        TaskHelper.Enqueue(SelectDuty);
        TaskHelper.Enqueue(CommenceDuty);
        TaskHelper.Enqueue(ExitDuty);
        TaskHelper.Enqueue(OpenContentsFinder);
        TaskHelper.Enqueue(ResetContentFinder);
        TaskHelper.Enqueue(CancelSelectedContents);
    }

    private static bool? ResetContentFinder()
    {
        if (!Throttler.Throttle("AutoJoinExitDuty-ResetContentFinder", 100)) return false;
        var instance = FFXIVClientStructs.FFXIV.Client.Game.UI.ContentsFinder.Instance();
        if ( instance == null) return false;
        
        instance->IsExplorerMode = false;
        instance->IsUnrestrictedParty = false;

        return true;
    }

    private bool? CheckAndSwitchJob()
    {
        var localPlayer = Service.ClientState.LocalPlayer;
        if (localPlayer == null)
        {
            TaskHelper.Abort();
            return true;
        }

        if (localPlayer.ClassJob.Id is >= 8 and <= 18)
        {
            var gearsetModule = RaptureGearsetModule.Instance();
            for (var i = 0; i < 100; i++)
            {
                var gearset = gearsetModule->GetGearset(i);
                if (gearset == null) continue;
                if (!gearset->Flags.HasFlag(RaptureGearsetModule.GearsetFlag.Exists)) continue;
                if (gearset->Flags.HasFlag(RaptureGearsetModule.GearsetFlag.MainHandMissing)) continue;
                if (gearset->Id != i) continue;
                if (gearset->ClassJob > 18)
                {
                    ChatHelper.Instance.SendMessage($"/gearset change {gearset->Id + 1}");
                    return true;
                }
            }
        }

        return true;
    }

    private static bool? OpenContentsFinder()
    {
        if (ContentsFinder != null && IsAddonAndNodesReady(ContentsFinder)) return true;

        AgentModule.Instance()->GetAgentContentsFinder()->AgentInterface.Show();
        return true;
    }

    private static bool? CancelSelectedContents()
    {
        if (ContentsFinder == null || !IsAddonAndNodesReady(ContentsFinder)) return false;

        AddonHelper.Callback(ContentsFinder, true, 12, 1);
        var atkValues = ContentsFinder->AtkValues;
        atkValues[7].UInt = 3;
        atkValues[7].Type = ValueType.UInt;
        ContentsFinder->OnRefresh(ContentsFinder->AtkValuesCount, atkValues);
        return true;
    }

    private static bool? SelectDuty()
    {
        if (!Throttler.Throttle("AutoJoinExitDuty-SelectDuty", 100)) return false;
        if (ContentsFinder == null || !IsAddonAndNodesReady(ContentsFinder)) return false;

        var agent = AgentModule.Instance()->GetAgentContentsFinder();
        var instance = FFXIVClientStructs.FFXIV.Client.Game.UI.ContentsFinder.Instance();
        if (agent == null || instance == null) return false;

        if (!instance->IsExplorerMode || !instance->IsUnrestrictedParty)
        {
            instance->IsExplorerMode = true;
            instance->IsUnrestrictedParty = true;
            return false;
        }

        AddonHelper.Callback(ContentsFinder, true, 1, 1);
        AddonHelper.Callback(ContentsFinder, true, 4, 0, true);
        AddonHelper.Callback(ContentsFinder, true, 4, 1, false);
        AddonHelper.Callback(ContentsFinder, true, 4, 2, false);
        AddonHelper.Callback(ContentsFinder, true, 4, 3, false);

        if (ContentsFinder->AtkValues[7].UInt != 0)
        {
            AddonHelper.Callback(ContentsFinder, true, 3, 1U);
            agent->OpenRegularDuty(4);
            return false;
        }

        if (ContentsFinder->AtkValues[7].UInt == 0)
        {
            AddonHelper.Callback(ContentsFinder, true, 12, 0);
            return true;
        }

        return false;
    }

    public bool? CommenceDuty()
    {
        if (Service.ModuleManager.IsModuleEnabled(typeof(AutoCommenceDuty)))
        {
            TaskHelper.InsertDelayNext(500);
            return true;
        }

        if (!TryGetAddonByName<AtkUnitBase>("ContentsFinderConfirm", out var addon) ||
            !IsAddonAndNodesReady(addon)) return false;

        ClickContentsFinderConfirm.Using((nint)addon).Commence();
        return true;
    }

    private static bool? ExitDuty()
    {
        if (Service.Condition[ConditionFlag.WaitingForDutyFinder] ||
            Service.Condition[ConditionFlag.InDutyQueue] || Flags.BetweenAreas) return false;

        AbandonDuty(false);
        return true;
    }

    public override void Uninit()
    {
        Service.CommandManager.RemoveSubCommand("joinexitduty");

        base.Uninit();
    }

    private delegate void AbandonDutyDelagte(bool a1);
}
