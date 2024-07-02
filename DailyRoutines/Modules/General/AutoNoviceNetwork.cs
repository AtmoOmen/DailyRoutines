using System;
using System.Runtime.InteropServices;
using System.Timers;
using DailyRoutines.Helpers;
using DailyRoutines.Infos;
using DailyRoutines.Managers;
using Dalamud.Interface.Colors;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using ImGuiNET;

namespace DailyRoutines.Modules;

[ModuleDescription("AutoNoviceNetworkTitle", "AutoNoviceNetworkDescription", ModuleCategories.一般)]
public unsafe class AutoNoviceNetwork : DailyModuleBase
{
    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint LastInputTickCount;
    }

    private delegate byte TryJoinNoviceNetworkDelegate(InfoProxyInterface* infoProxy20);
    [Signature("E8 ?? ?? ?? ?? 45 33 F6 41 B4")]
    private static TryJoinNoviceNetworkDelegate? TryJoinNoviceNetwork;

    private static Timer? AfkTimer;
    private static int TryTimes;
    private static bool IsTryJoinWhenInactive;
    private static bool IsInNoviceNetworkDisplay;

    [DllImport("User32.dll")]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);

    public override void Init()
    {
        Service.Hook.InitializeFromAttributes(this);

        AddConfig("IsTryJoinWhenInactive", false);
        IsTryJoinWhenInactive = GetConfig<bool>("IsTryJoinWhenInactive");

        AfkTimer ??= new Timer(10000);
        AfkTimer.Elapsed += OnAfkStateCheck;
        AfkTimer.AutoReset = true;
        AfkTimer.Enabled = true;

        TaskHelper ??= new TaskHelper { AbortOnTimeout = true, TimeLimitMS = 5000, ShowDebug = false };
    }

    public override void ConfigUI()
    {
        ImGui.BeginDisabled(TaskHelper.IsBusy);
        if (ImGui.Button(Service.Lang.GetText("Start")))
        {
            Service.Hook.InitializeFromAttributes(this);

            TryTimes = 0;
            TaskHelper.Enqueue(EnqueueARound);
        }
        ImGui.EndDisabled();

        ImGui.SameLine();
        if (ImGui.Button(Service.Lang.GetText("Stop")))
            TaskHelper.Abort();

        ImGui.SameLine();
        ImGui.TextWrapped($"{Service.Lang.GetText("AutoNoviceNetwork-AttemptedTimes")}:");

        ImGui.SameLine();
        ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudYellow);
        ImGui.TextWrapped(TryTimes.ToString());
        ImGui.PopStyleColor();

        if (ImGui.Checkbox(Service.Lang.GetText("AutoNoviceNetwork-TryJoinWhenInactive"),
                           ref IsTryJoinWhenInactive))
            UpdateConfig("IsTryJoinWhenInactive", IsTryJoinWhenInactive);

        ImGuiOm.HelpMarker(Service.Lang.GetText("AutoNoviceNetwork-TryJoinWhenInactiveHelp"));

        ImGui.SameLine();
        ImGui.Text($"{Service.Lang.GetText("AutoNoviceNetwork-JoinState")}:");

        if (Throttler.Throttle("AutoNoviceNetwork", 1000))
            IsInNoviceNetworkDisplay = IsInNoviceNetwork();

        ImGui.SameLine();
        ImGui.TextColored(IsInNoviceNetworkDisplay ? ImGuiColors.HealerGreen : ImGuiColors.DPSRed,
                          IsInNoviceNetworkDisplay
                              ? Service.Lang.GetText("AutoNoviceNetwork-HaveJoined")
                              : Service.Lang.GetText("AutoNoviceNetwork-HaveNotJoined"));
    }

    private void EnqueueARound()
    {
        TaskHelper.Enqueue(() =>
        {
            if (!PlayerState.Instance()->IsPlayerStateFlagSet(PlayerStateFlag.IsNoviceNetworkAutoJoinEnabled))
                ChatHelper.Instance.SendMessage("/beginnerchannel on");
        });

        TaskHelper.Enqueue(TryJoin);

        TaskHelper.DelayNext(250);
        TaskHelper.Enqueue(() => TryTimes++);

        TaskHelper.Enqueue(() =>
        {
            if (IsInNoviceNetwork())
            {
                TaskHelper.Abort();
                return;
            }

            EnqueueARound();
        });
    }

    private static void TryJoin() => TryJoinNoviceNetwork(InfoModule.Instance()->GetInfoProxyById(InfoProxyId.CrossRealmParty));

    private static bool IsInNoviceNetwork()
    {
        var infoProxy = InfoModule.Instance()->GetInfoProxyById(InfoProxyId.CrossRealmParty);
        return ((int)infoProxy[1].VirtualTable & 1) != 0;
    }

    private void OnAfkStateCheck(object? sender, ElapsedEventArgs e)
    {
        if (!IsTryJoinWhenInactive || IsInNoviceNetwork() || TaskHelper.IsBusy) return;
        if (Flags.BoundByDuty || Flags.OccupiedInEvent) return;

        var idleTime = GetIdleTime();
        if (idleTime > TimeSpan.FromSeconds(10) || Framework.Instance()->WindowInactive)
            TryJoin();
    }

    public static TimeSpan GetIdleTime()
    {
        var lastInputInfo = new LastInputInfo { Size = (uint)Marshal.SizeOf(typeof(LastInputInfo)) };
        GetLastInputInfo(ref lastInputInfo);

        return TimeSpan.FromMilliseconds(Environment.TickCount - (int)lastInputInfo.LastInputTickCount);
    }

    public override void Uninit()
    {
        AfkTimer?.Stop();
        if (AfkTimer != null) AfkTimer.Elapsed -= OnAfkStateCheck;
        AfkTimer?.Dispose();
        AfkTimer = null;

        TryTimes = 0;
        base.Uninit();
    }
}
