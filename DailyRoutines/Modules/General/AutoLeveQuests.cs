using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using ClickLib;
using DailyRoutines.Helpers;
using DailyRoutines.Infos;
using DailyRoutines.Managers;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Hooking;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Application.Network.WorkDefinitions;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ImGuiNET;
using Lumina.Excel.GeneratedSheets;

namespace DailyRoutines.Modules;

[PrecedingModule([typeof(AutoRequestItemSubmit), typeof(AutoTalkSkip), typeof(AutoQuestComplete)])]
[ModuleDescription("AutoLeveQuestsTitle", "AutoLeveQuestsDescription", ModuleCategories.一般)]
public unsafe class AutoLeveQuests : DailyModuleBase
{
    [Signature("E8 ?? ?? ?? ?? 83 FE 4F", DetourName = nameof(GameObjectRotateDetour))]
    private static Hook<GameObjectRotateDelegate>? GameObjectRotateHook;

    private const string LeveAllowancesSig = "88 05 ?? ?? ?? ?? 0F B7 41 06";

    private static Dictionary<uint, Leve> LeveQuests = [];

    private static Leve? SelectedLeve;
    private static GameObject* LeveMete;
    private static GameObject* LeveReceiver;
    private static string SearchString = string.Empty;

    private static byte LeveAllowancesDisplay;
    private static float RandomRotation;
    private static AtkUnitBase* SelectString     => (AtkUnitBase*)Service.Gui.GetAddonByName("SelectString");
    private static AtkUnitBase* SelectIconString => (AtkUnitBase*)Service.Gui.GetAddonByName("SelectIconString");
    private static AtkUnitBase* GuildLeve        => (AtkUnitBase*)Service.Gui.GetAddonByName("GuildLeve");

    public override void Init()
    {
        Service.Hook.InitializeFromAttributes(this);
        Service.ClientState.TerritoryChanged += OnZoneChanged;
        TaskHelper ??= new TaskHelper { AbortOnTimeout = true, TimeLimitMS = 30000, ShowDebug = false };

        RandomRotation = new Random().Next(0, 360);
    }

    public override void ConfigUI()
    {
        ImGui.TextColored(ImGuiColors.DalamudOrange, Service.Lang.GetText("AutoLeveQuests-NoDelayHelp"));

        ImGui.Spacing();

        var childSize = ScaledVector2(420f, 300f);
        ImGuiOm.DisableZoneWithHelp(() =>
                                    {
                                        if (ImGui.BeginChild("LeveSelectChild", childSize, true))
                                        {
                                            if (LeveQuests.Count == 0)
                                            {
                                                if (ImGuiOm.SelectableTextCentered(
                                                        Service.Lang.GetText("AutoLeveQuests-GetAreaLeveData")))
                                                    GetMapLeveQuests();
                                            }
                                            else
                                            {
                                                ImGui.SetNextItemWidth(-1f);
                                                ImGui.InputTextWithHint("##AutoLeveQuests-SearchLeveQuest",
                                                                        Service.Lang.GetText("PleaseSearch"),
                                                                        ref SearchString, 128);
                                            }

                                            ImGui.Separator();

                                            foreach (var leve in LeveQuests)
                                            {
                                                var leveName = leve.Value.Name.RawString;
                                                var jobName = leve.Value.ClassJobCategory.Value.Name.RawString;
                                                var leveID = leve.Value.RowId.ToString();

                                                if (!string.IsNullOrWhiteSpace(SearchString) &&
                                                    !leveName.Contains(SearchString) && !jobName.Contains(SearchString) &&
                                                    !leveID.Contains(SearchString))
                                                    continue;

                                                if (ImGui.Selectable(
                                                        $"{jobName}{leveName[leveName.IndexOf('：')..]} ({leveID})",
                                                        SelectedLeve == leve.Value))
                                                    SelectedLeve = leve.Value;
                                            }

                                            ImGui.EndChild();
                                        }
                                    },
                                    [
                                        new(TaskHelper.IsBusy,
                                            Service.Lang.GetText("AutoLeveQuests-DisableHelp-Delivering")),
                                    ],
                                    Service.Lang.GetText("DisableZoneHeader"));

        ImGui.SameLine();
        ImGui.BeginGroup();

        ScaledDummy(3f);

        ImGuiOm.DisableZoneWithHelp(() =>
                                    {
                                        if (ImGui.Button(Service.Lang.GetText("AutoLeveQuests-ObtainLevemete")))
                                            LeveMete = TargetSystem.Instance()->Target;

                                        ImGui.SameLine();
                                        ImGui.Text(LeveMete == null
                                                       ? Service.Lang.GetText("AutoLeveQuests-ObtainHelp")
                                                       : $"{LeveMete->NameString} ({LeveMete->BaseId})");

                                        ScaledDummy(2f);

                                        if (ImGui.Button(Service.Lang.GetText("AutoLeveQuests-ObtainLeveClient")))
                                            LeveReceiver = TargetSystem.Instance()->Target;

                                        ImGui.SameLine();
                                        ImGui.Text(LeveReceiver == null
                                                       ? Service.Lang.GetText("AutoLeveQuests-ObtainHelp")
                                                       : $"{LeveReceiver->NameString} ({LeveReceiver->BaseId})");
                                    },
                                    [
                                        new(TaskHelper.IsBusy,
                                            Service.Lang.GetText("AutoLeveQuests-DisableHelp-Delivering")),
                                    ],
                                    Service.Lang.GetText("DisableZoneHeader"));

        ScaledDummy(5f);

        ImGui.BeginGroup();
        ImGuiOm.DisableZoneWithHelp(() =>
        {
            if (ImGuiOm.ButtonIconWithTextVertical(FontAwesomeIcon.Play, Service.Lang.GetText("Start"),
                                                   ScaledVector2(167f, 60f)))
            {
                Service.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "SelectYesno", AlwaysYes);
                Service.ExecuteCommandManager.Register(OnPreExecuteCommand);
                GameObjectRotateHook?.Enable();
                EnqueueARound();
            }
        }, [
            new(LeveMete == null, Service.Lang.GetText("AutoLeveQuests-DisableHelp-NullMete")),
            new(LeveReceiver == null, Service.Lang.GetText("AutoLeveQuests-DisableHelp-NullReceiver")),
            new(LeveMete == LeveReceiver, Service.Lang.GetText("AutoLeveQuests-DisableHelp-SameNPC")),
            new(SelectedLeve == null, Service.Lang.GetText("AutoLeveQuests-DisableHelp-NullLeve")),
            new(TaskHelper.IsBusy, Service.Lang.GetText("AutoLeveQuests-DisableHelp-Delivering")),
        ], Service.Lang.GetText("DisableZoneHeader"));

        ImGui.SameLine();
        if (ImGuiOm.ButtonIconWithTextVertical(FontAwesomeIcon.Pause, Service.Lang.GetText("Stop"),
                                               ScaledVector2(167f, 60f)))
        {
            TaskHelper.Abort();
            Service.AddonLifecycle.UnregisterListener(AlwaysYes);
            GameObjectRotateHook?.Disable();
            Service.ExecuteCommandManager.Unregister(OnPreExecuteCommand);
        }

        if (Throttler.Throttle("AutoLeveQuests-GetLeveAllowancesDisplay", 1000))
            LeveAllowancesDisplay = *(byte*)Service.SigScanner.GetStaticAddressFromSig(LeveAllowancesSig);

        var text = Service.Lang.GetText("AutoLeveQuests-CurrentLeveAllowances", LeveAllowancesDisplay);
        using (FontManager.UIFont160.Push())
        {
            ImGui.Text(text);
        }
        ImGui.EndGroup();

        ImGui.EndGroup();
    }

    public void EnqueueARound()
    {
        LeveAllowancesDisplay = *(byte*)Service.SigScanner.GetStaticAddressFromSig(LeveAllowancesSig);
        if (SelectedLeve == null || LeveAllowancesDisplay <= 0)
        {
            TaskHelper.Abort();
            Service.AddonLifecycle.UnregisterListener(AlwaysYes);
            GameObjectRotateHook?.Disable();
            Service.ExecuteCommandManager.Unregister(OnPreExecuteCommand);

            NotifyHelper.ToastInfo(Service.Lang.GetText("AutoLeveQuests-CompleteHelp"));
            return;
        }

        // 与理符发行人交互
        TaskHelper.Enqueue(InteractWithMete);
        // 点击对应理符类别
        TaskHelper.Enqueue(ClickLeveGenre);
        // 接取对应理符任务
        TaskHelper.Enqueue(AcceptLeveQuest);
        // 退出理符任务界面
        TaskHelper.Enqueue(ExitLeveInterface);
        // 与理符委托人交互
        TaskHelper.Enqueue(InteractWithReceiver);
        // 检查是否有多个理符待提交
        TaskHelper.Enqueue(CheckIfMultipleLevesToSubmit);
    }

    private static bool? InteractWithMete()
    {
        var continueToSubmit = LuminaCache.GetRow<CraftLeveClient>(1).Text.RawString;
        if (SelectString != null && IsAddonAndNodesReady(SelectString) &&
            TryScanSelectStringText(SelectString, continueToSubmit, out _))
        {
            ClickHelper.SelectString(continueToSubmit);
            return false;
        }

        if (Flags.OccupiedInEvent) return false;
        TargetSystem.Instance()->Target = LeveMete;
        TargetSystem.Instance()->InteractWithObject(LeveMete);
        return true;
    }

    private static bool? ClickLeveGenre()
    {
        if (SelectString == null || !IsAddonAndNodesReady(SelectString)) return false;

        var fieldcraft = LuminaCache.GetRow<Addon>(595).Text.RawString;
        var tradecraft = LuminaCache.GetRow<Addon>(596).Text.RawString;
        return ClickHelper.SelectString(SelectedLeve.ClassJobCategory.Row is 19 ? fieldcraft : tradecraft);
    }

    private static bool? AcceptLeveQuest()
    {
        if (GuildLeve == null || !IsAddonAndNodesReady(GuildLeve)) return false;

        AgentHelper.SendEvent(AgentId.LeveQuest, 0, 3, SelectedLeve.RowId);

        return QuestManager.Instance()->LeveQuests.ToArray().Select(x => (LeveWork?)x)
                                                      .FirstOrDefault(
                                                          x => x.HasValue && x.Value.LeveId == SelectedLeve.RowId) != null;
    }

    private static bool? ExitLeveInterface()
    {
        if (GuildLeve != null && IsAddonAndNodesReady(GuildLeve))
        {
            GuildLeve->FireCloseCallback();
            GuildLeve->Close(true);

            return false;
        }

        var cancel = LuminaCache.GetRow<Addon>(581).Text.RawString;
        if (SelectString != null && IsAddonAndNodesReady(SelectString))
            return ClickHelper.SelectString(cancel);

        return false;
    }

    private static bool? InteractWithReceiver()
    {
        if (Flags.OccupiedInEvent) return false;
        TargetSystem.Instance()->Target = LeveReceiver;
        TargetSystem.Instance()->InteractWithObject(LeveReceiver);
        return true;
    }

    private bool? CheckIfMultipleLevesToSubmit()
    {
        var levesSpan = QuestManager.Instance()->LeveQuests;
        var qualifiedCount = 0;

        // 判断是否为当前地图的理符
        foreach (var leve in levesSpan)
            if (LeveQuests.ContainsKey(leve.LeveId))
                qualifiedCount++;

        if (qualifiedCount > 1)
        {
            if (SelectIconString != null && IsAddonAndNodesReady(SelectIconString))
            {
                if (!ClickHelper.SelectIconString(SelectedLeve.Name.RawString)) return false;

                EnqueueARound();
                return true;
            }

            return false;
        }

        EnqueueARound();
        return true;
    }

    private static void GetMapLeveQuests()
    {
        var currentZonePlaceNameID = LuminaCache.GetRow<TerritoryType>(Service.ClientState.TerritoryType)?.PlaceName.Row ?? 0;

        if (Service.ClientState.TerritoryType == 0 || currentZonePlaceNameID == 0)
        {
            NotifyHelper.NotificationError(Service.Lang.GetText("AutoLeveQuests-GetMapLevesFailed"));
            return;
        }

        LeveQuests = LuminaCache.Get<Leve>()
                                .Where(x => x.PlaceNameIssued.Row == currentZonePlaceNameID &&
                                            !string.IsNullOrEmpty(x.Name.RawString) &&
                                            x.ClassJobCategory.Row is (>= 9 and <= 16) or 19)
                                .Reverse()
                                .OrderBy(x => x.ClassJobCategory.Value.Name != (Service.ClientState.LocalPlayer?.ClassJob.GameData.Name ?? string.Empty))                                
                                .ToDictionary(x => x.RowId, x => x);

        if (LeveQuests.Count <= 0)
            NotifyHelper.NotificationError(Service.Lang.GetText("AutoLeveQuests-NoLevesHelp"));
    }

    private void OnPreExecuteCommand
        (ref bool isPrevented, ref int command, ref int param1, ref int param2, ref int param3, ref int param4)
    {
        if (command != 3 || !TaskHelper.IsBusy) return;

        param1 = int.MinValue / 4;
    }

    private static void GameObjectRotateDetour(GameObject* obj, float value)
    {
        if ((nint)obj != Service.ClientState.LocalPlayer.Address)
            GameObjectRotateHook.Original(obj, value);

        GameObjectRotateHook.Original(obj, RandomRotation);
    }

    private static void OnZoneChanged(ushort zone)
    {
        LeveQuests.Clear();
        SelectedLeve = null;
        LeveMete = null;
        LeveReceiver = null;
    }

    private void AlwaysYes(AddonEvent type, AddonArgs args)
    {
        if (!TaskHelper.IsBusy) return;
        Click.SendClick("select_yes");
    }

    public override void Uninit()
    {
        Service.ClientState.TerritoryChanged -= OnZoneChanged;
        Service.AddonLifecycle.UnregisterListener(AlwaysYes);
        Service.ExecuteCommandManager.Unregister(OnPreExecuteCommand);
        LeveMete = null;
        LeveReceiver = null;
        LeveQuests.Clear();

        base.Uninit();
    }

    private delegate void GameObjectRotateDelegate(GameObject* obj, float value);
}
