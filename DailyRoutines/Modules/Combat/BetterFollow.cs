using System.Collections.Generic;
using System.Numerics;
using DailyRoutines.Helpers;
using DailyRoutines.Infos;
using DailyRoutines.IPC;
using DailyRoutines.Managers;
using Dalamud;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.Command;
using Dalamud.Hooking;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Plugin.Services;
using Dalamud.Utility.Signatures;

using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using ImGuiNET;
using GameObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;

namespace DailyRoutines.Modules;

[ModuleDescription("BetterFollowTitle", "BetterFollowDescription", ModuleCategories.战斗)]
public unsafe class BetterFollow : DailyModuleBase
{
    public const string CommandStr = "/pdrfollow";

    // 状态
    private static bool _FollowStatus;
    private static bool _enableReFollow;
    private static string _LastFollowObjectName = "无";
    private static bool _LastFollowObjectStatus;

    // 数据
    private static nint _LastFollowObjectAddress;
    private static uint _LastFollowObjectId;
    private static nint _a1;
    private static nint _a1_data;
    private static nint _v5;
    private static nint _d1;
    private static nint _v8;


    private static vnavmeshIPC? vnavmesh;

    private static readonly Dictionary<MoveTypeList, string> MoveTypeLoc = new()
    {
        { MoveTypeList.System, Service.Lang.GetText("BetterFollow-SystemFollow") },
        { MoveTypeList.Navmesh, Service.Lang.GetText("BetterFollow-NvavmeshFollow") },
    };

    [Signature("E8 ?? ?? ?? ?? EB ?? 48 81 C1 ?? ?? ?? ?? E8 ?? ?? ?? ?? EB", DetourName = nameof(FollowData))]
    private readonly Hook<FollowDataDelegate>? FollowDataHook;

    [Signature("48 89 5C 24 ?? 57 48 83 EC ?? 48 8B F9 48 8B DA 48 8B CA E8 ?? ?? ?? ?? 44 8B 00")]
    private readonly delegate* unmanaged<nint, nint, ulong> FollowDataPush;

    [Signature(
        "40 53 48 81 EC ?? ?? ?? ?? 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 84 24 ?? ?? ?? ?? 48 8B D9 48 8D 4C 24 ?? E8 ?? ?? ?? ?? 48 8B D0 48 8D 8B ?? ?? ?? ?? E8 ?? ?? ?? ?? 48 8D 4C 24 ?? E8 ?? ?? ?? ?? 48 8B 4C 24 ?? BA ?? ?? ?? ?? 41 B8 ?? ?? ?? ?? E8 ?? ?? ?? ?? 48 8B 8C 24 ?? ?? ?? ?? 48 33 CC E8 ?? ?? ?? ?? 48 81 C4 ?? ?? ?? ?? 5B C3 CC CC CC CC CC CC CC CC CC CC CC CC CC CC CC CC CC 48 89 5C 24")]
    private readonly delegate* unmanaged<nint, uint, nint, nint, ulong> FollowStart;

    private Config ModuleConfig = null!;
    public override string? Author { get; set; } = "HSS";

    public override void Init()
    {
        ModuleConfig = LoadConfig<Config>() ?? new();

        #region Data

        SafeMemory.Write(_a1 + 1189, 0);
        SafeMemory.Write(_a1 + 1369, 0);
        _FollowStatus = false;
        _enableReFollow = false;
        _LastFollowObjectAddress = 0;
        _LastFollowObjectId = 0;
        _a1 = Service.SigScanner.GetStaticAddressFromSig(
            "48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? EB ?? 48 8D 15 ?? ?? ?? ?? 48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 84 DB");

        _v5 = *(nint*)Service.SigScanner.GetStaticAddressFromSig("4C 8B 35 ?? ?? ?? ?? 48 3B D0");
        _a1_data = _a1 + 0x450;
        _d1 = _a1 + 1369;
        _v8 = *(nint*)(*(ulong*)Service.SigScanner.GetStaticAddressFromSig(
                           "48 8B 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 48 8B C8 48 8B 10 FF 52 ?? 4C 8B CE") + 0x2B60) +
              0x19c8;

        #endregion

        TaskHelper ??= new TaskHelper { AbortOnTimeout = true, TimeLimitMS = 5000, ShowDebug = false };

        Service.Hook.InitializeFromAttributes(this);
        FollowDataHook?.Enable();
        vnavmesh ??= Service.IPCManager.Load<vnavmeshIPC>(this);
        if (vnavmesh == null) ModuleConfig.MoveType = MoveTypeList.System;
        Service.IPCManager.RegisterPluginChanged(OnPluginsChanged);

        Service.FrameworkManager.Register(OnFramework);

        if (ModuleConfig.ForcedFollow)
        {
            Service.CommandManager.AddCommand(CommandStr,
                                              new CommandInfo(OnCommand)
                                              {
                                                  HelpMessage =
                                                      Service.Lang.GetText("BetterFollow-CommandDesc", CommandStr),
                                              });
        }
    }

    public override void ConfigUI()
    {
        var disabledFlag = _FollowStatus || vnavmesh == null;
        if (disabledFlag) ImGui.BeginDisabled();

        ImGui.AlignTextToFramePadding();
        ImGui.Text(Service.Lang.GetText("BetterFollow-MoveType"));

        ImGui.SameLine();
        ImGui.SetNextItemWidth(200f);
        if (ImGui.BeginCombo("###MoveTypoCombo", MoveTypeLoc[ModuleConfig.MoveType]))
        {
            foreach (var mode in MoveTypeLoc)
                if (ImGui.Selectable(mode.Value, mode.Key == ModuleConfig.MoveType))
                {
                    ModuleConfig.MoveType = mode.Key;
                    SaveConfig(ModuleConfig);
                }

            ImGui.EndCombo();
        }

        if (ModuleConfig.MoveType == MoveTypeList.Navmesh)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, ImGuiColors.DalamudYellow);
            ImGuiOm.HelpMarker(Service.Lang.GetText("BetterFollow-NvavmeshFollowDesc"));
            ImGui.PopStyleColor();

            ImGui.SameLine();
            ImGui.Dummy(new(12f));
        }

        if (disabledFlag) ImGui.EndDisabled();

        ImGui.SameLine();
        if (ImGuiOm.ButtonIcon("BetterFollow-ReloadIPC", FontAwesomeIcon.Sync,
                               Service.Lang.GetText("BetterFollow-ReloadIPC")))
        {
            if (IPCManager.IsPluginEnabled("vnavmesh"))
                vnavmesh = Service.IPCManager.Load<vnavmeshIPC>(this);
        }

        if (ModuleConfig.MoveType == MoveTypeList.Navmesh)
        {
            ImGui.AlignTextToFramePadding();
            ImGui.Text(Service.Lang.GetText("BetterFollow-FollowDistance"));

            ImGui.SameLine();
            ImGui.SetNextItemWidth(300f);
            ImGui.SliderInt("###BetterFollow-FollowDistance", ref ModuleConfig.FollowDistance, 1, 15);
            if (ImGui.IsItemDeactivatedAfterEdit())
            {
                vnavmesh?.PathSetTolerance(ModuleConfig.FollowDistance);
                SaveConfig(ModuleConfig);
            }
        }

        ImGui.Spacing();

        ImGui.AlignTextToFramePadding();
        ImGui.Text(Service.Lang.GetText("BetterFollow-DelayConfig"));

        ImGui.SameLine();
        ImGui.SetNextItemWidth(300f);
        ImGui.SliderFloat("###BetterFollow-DelayConfig", ref ModuleConfig.Delay, 0.5f, 5f, "%.1f");
        if (ImGui.IsItemDeactivatedAfterEdit())
            SaveConfig(ModuleConfig);

        if (ImGui.Checkbox(Service.Lang.GetText("BetterFollow-MountWhenFollowMount"),
                           ref ModuleConfig.MountWhenFollowMount))
            SaveConfig(ModuleConfig);

        if (ModuleConfig.MountWhenFollowMount)
        {
            ImGui.Indent();
            if (ImGui.Checkbox(Service.Lang.GetText("BetterFollow-FlyingWhenFollowFlying"),
                               ref ModuleConfig.FlyingWhenFollowFlying))
                SaveConfig(ModuleConfig);

            ImGui.Unindent();
        }

        if (ImGui.Checkbox(Service.Lang.GetText("BetterFollow-UnMountWhenFollowUnMount"),
                           ref ModuleConfig.UnMountWhenFollowUnMount))
            SaveConfig(ModuleConfig);

        if (ImGui.Checkbox(Service.Lang.GetText("BetterFollow-AutoReFollowConfig"), ref ModuleConfig.AutoReFollow))
            SaveConfig(ModuleConfig);

        if (ModuleConfig.AutoReFollow)
        {
            ImGui.Indent();
            ConflictKeyText();

            ImGui.Text(Service.Lang.GetText("BetterFollow-Status", _enableReFollow, _LastFollowObjectName,
                                            _LastFollowObjectStatus));

            if (ImGui.Checkbox(Service.Lang.GetText("BetterFollow-OnCombatOverConfig"), ref ModuleConfig.OnCombatOver))
                SaveConfig(ModuleConfig);

            if (ImGui.Checkbox(Service.Lang.GetText("BetterFollow-OnDutyConfig"), ref ModuleConfig.OnDuty))
                SaveConfig(ModuleConfig);

            ImGui.Text(Service.Lang.GetText("BetterFollow-ReFollowDistance"));

            ImGui.SameLine();
            ImGui.SetNextItemWidth(300f);
            ImGui.SliderInt("###BetterFollow-ReFollowDistance", ref ModuleConfig.ReFollowDistance, 1, 15);
            if (ImGui.IsItemDeactivatedAfterEdit())
                SaveConfig(ModuleConfig);

            ImGui.Unindent();
        }

        if (ImGui.Checkbox(Service.Lang.GetText("BetterFollow-ForcedFollowConfig", CommandStr),
                           ref ModuleConfig.ForcedFollow))
        {
            SaveConfig(ModuleConfig);
            if (ModuleConfig.ForcedFollow)
            {
                Service.CommandManager.AddCommand(CommandStr,
                                                  new CommandInfo(OnCommand)
                                                  {
                                                      HelpMessage =
                                                          Service.Lang.GetText("BetterFollow-CommandDesc", CommandStr),
                                                  });
            }
            else
                Service.CommandManager.RemoveCommand(CommandStr);
        }

        if (ModuleConfig.ForcedFollow)
        {
            ImGui.Indent();
            ImGui.Text(Service.Lang.GetText("BetterFollow-CommandDesc"));
            ImGui.Unindent();
        }
    }

    private void OnPluginsChanged()
    {
        if (IPCManager.IsPluginEnabled("vnavmesh"))
            vnavmesh = Service.IPCManager.Load<vnavmeshIPC>(this);
        else
        {
            if (ModuleConfig.MoveType == MoveTypeList.Navmesh)
            {
                _enableReFollow = false;
                _FollowStatus = false;
                _LastFollowObjectAddress = 0;
                _LastFollowObjectId = 0;
                _LastFollowObjectName = "无";
                _LastFollowObjectStatus = false;
                ModuleConfig.MoveType = MoveTypeList.System;
                SaveConfig(ModuleConfig);
            }
        }
    }

    private void OnFramework(IFramework _)
    {
        /*-------------------------------需要实时处理的模块-------------------------------*/
        // 处理打断重新跟随逻辑
        if (_enableReFollow && InterruptByConflictKey())
        {
            _enableReFollow = false;
            _LastFollowObjectName = "无";
            _LastFollowObjectStatus = false;
            return;
        }

        var followObject = Service.ObjectTable.SearchById(_LastFollowObjectId);
        // 处理打断 Vnavmesh 跟随逻辑
        if (ModuleConfig.MoveType == MoveTypeList.Navmesh && _FollowStatus)
        {
            // 目标无了
            if (followObject == null) StopFollow(true);
            // 自行移动了
            if (Service.KeyState[VirtualKey.W] || Service.KeyState[VirtualKey.S] || Service.KeyState[VirtualKey.A] ||
                Service.KeyState[VirtualKey.D]) StopFollow(true);

            // 自己无了
            if (Service.ClientState.LocalPlayer == null) StopFollow(true);
            // 过图了或者死了
            if (Service.ClientState.LocalPlayer.IsDead || Flags.BetweenAreas) StopFollow(true);
            // 进剧情了
            if (Service.ClientState.LocalPlayer.OnlineStatus.GameData.RowId == 15) StopFollow(true);
        }

        // 更新系统跟随状态
        if (ModuleConfig.MoveType == MoveTypeList.System) _FollowStatus = *(int*)_d1 == 4;

        // 如果已经进入飞行状态就别跳了
        if (Service.Condition[ConditionFlag.InFlight]) TaskHelper.Abort();

        // 处理上坐骑和起飞逻辑
        if (_FollowStatus && followObject != null &&
            followObject.ObjectKind == ObjectKind.Player)
        {
            // 跟随目标上坐骑
            if (ModuleConfig.MountWhenFollowMount &&
                ((Character*)followObject.Address)->IsMounted() &&
                Flags.CanMount)
            {
                StopFollow();
                ActionManager.Instance()->UseAction(ActionType.GeneralAction, 9);
                return;
            }

            // 跟随目标起飞
            if (ModuleConfig.FlyingWhenFollowFlying && ((CharacterFlying*)followObject.Address)->IsFlying != 0 &&
                !Service.Condition[ConditionFlag.InFlight] && Service.Condition[ConditionFlag.Mounted])
            {
                TaskHelper.Enqueue(() => ActionManager.Instance()->UseAction(ActionType.GeneralAction, 2));
                TaskHelper.DelayNext(50);
                TaskHelper.Enqueue(() => ActionManager.Instance()->UseAction(ActionType.GeneralAction, 2));
                return;
            }

            // 跟随目标下坐骑
            if (ModuleConfig.UnMountWhenFollowUnMount &&
                !((Character*)followObject.Address)->IsMounted() &&
                Service.Condition[ConditionFlag.Mounted])
            {
                ((BattleChara*)followObject.Address)->GetStatusManager()->
                    RemoveStatus(10);

                ActionManager.Instance()->UseAction(ActionType.GeneralAction, 9);
                return;
            }
        }

        /*-------------------------------不需要实时处理的模块-------------------------------*/
        if (!Throttler.Throttle("BetterFollow", (int)ModuleConfig.Delay * 1000)) return;
        if (_FollowStatus && followObject != null &&
            followObject.ObjectKind == ObjectKind.Player)
        {
            if (ModuleConfig.MountWhenFollowMount &&
                ((Character*)followObject.Address)->IsMounted() &&
                Flags.CanMount) return;

            if (ModuleConfig.FlyingWhenFollowFlying && ((CharacterFlying*)followObject.Address)->IsFlying != 0 &&
                !Service.Condition[ConditionFlag.InFlight] && Service.Condition[ConditionFlag.Mounted]) return;

            if (ModuleConfig.UnMountWhenFollowUnMount &&
                !((Character*)followObject.Address)->IsMounted() &&
                Service.Condition[ConditionFlag.Mounted]) return;
        }

        //处理移动逻辑
        if (ModuleConfig.MoveType == MoveTypeList.Navmesh && _FollowStatus && followObject != null)
        {
            if (Vector3.Distance(Service.ClientState.LocalPlayer.Position, followObject.Position) <
                ModuleConfig.FollowDistance)
            {
                vnavmesh.CancelAllQueries();
                vnavmesh.PathStop();
                return;
            }

            if (Service.ClientState.LocalPlayer.IsCasting) return;
            if (!vnavmesh.NavIsReady()) return;
            if (vnavmesh.PathfindInProgress()) return;
            vnavmesh.PathfindAndMoveTo(followObject.Position, Service.Condition[ConditionFlag.InFlight]);
        }

        // 处理重新跟随逻辑
        if (!_enableReFollow) return;
        if (!ModuleConfig.AutoReFollow || _FollowStatus) return;
        // 在战斗
        if (ModuleConfig.OnCombatOver && Service.Condition[ConditionFlag.InCombat]) return;
        // 在副本里
        if (!ModuleConfig.OnDuty && Flags.BoundByDuty) return;
        //自己无了
        if (Service.ClientState.LocalPlayer == null) return;
        // 过图了或者死了
        if (Service.ClientState.LocalPlayer.IsDead || Flags.BetweenAreas) return;
        // 在读条
        if (Service.ClientState.LocalPlayer.IsCasting) return;
        // 在看剧情
        if (Service.ClientState.LocalPlayer.OnlineStatus.GameData.RowId == 15) return;
        // 在移动
        if (AgentMap.Instance()->IsPlayerMoving == 1) return;
        // 死了
        if (Service.ClientState.LocalPlayer.IsDead) return;
        // 跟随目标无了
        if (Service.ObjectTable.SearchById(_LastFollowObjectId) == null ||
            !Service.ObjectTable.SearchById(_LastFollowObjectId).IsTargetable)
        {
            _LastFollowObjectStatus = false;
            return;
        }

        // 距离不够
        if (Vector3.Distance(Service.ClientState.LocalPlayer.Position,
                             Service.ObjectTable.SearchById(_LastFollowObjectId).Position) <
            ModuleConfig.ReFollowDistance) return;

        // 跟随目标换图了并且换了内存地址
        if (Service.ObjectTable.SearchById(_LastFollowObjectId).Address != _LastFollowObjectAddress)
        {
            // 更新目标的新地址
            _LastFollowObjectAddress = Service.ObjectTable.SearchById(_LastFollowObjectId).Address;
            NewFollow(_LastFollowObjectAddress);
        }
        else
            ReFollow();
    }

    private void OnCommand(string command, string args)
    {
        var localPlayer = Service.ClientState.LocalPlayer;
        if (localPlayer == null || localPlayer.TargetObject == null) return;
        NewFollow(localPlayer.TargetObject.Address);
    }

    private void ReFollow()
    {
        switch (ModuleConfig.MoveType)
        {
            case MoveTypeList.System:
                if (_FollowStatus) return;
                if (_LastFollowObjectAddress == 0) return;
                FollowDataPush(_a1_data, _LastFollowObjectAddress);
                SafeMemory.Write(_d1, 4);
                _FollowStatus = true;
                break;
            case MoveTypeList.Navmesh:
                if (_FollowStatus) return;
                if (_LastFollowObjectAddress == 0) return;
                _FollowStatus = true;
                break;
        }
    }

    private void StopFollow(bool message = false)
    {
        switch (ModuleConfig.MoveType)
        {
            case MoveTypeList.System:
                _FollowStatus = false;
                if (*(int*)_d1 == 4)
                {
                    SafeMemory.Write(_a1 + 1189, 0);
                    SafeMemory.Write(_a1 + 1369, 0);
                }

                break;
            case MoveTypeList.Navmesh:
                _FollowStatus = false;
                vnavmesh.CancelAllQueries();
                vnavmesh.PathStop();
                break;
        }

        if (message) Service.Chat.Print("取消跟随。");
    }

    private void NewFollow(nint objectAddress)
    {
        if (_FollowStatus) return;
        if (objectAddress == 0) return;
        _LastFollowObjectAddress = objectAddress;
        _LastFollowObjectId = (*(GameObject*)objectAddress).GetGameObjectId().ObjectId;
        _LastFollowObjectName = Service.ObjectTable.SearchById((*(GameObject*)objectAddress).GetGameObjectId().ObjectId).Name
                                       .ToString();

        switch (ModuleConfig.MoveType)
        {
            case MoveTypeList.System:
                FollowStart(_v8, 52, _v5, objectAddress);
                FollowDataPush(_a1_data, objectAddress);
                SafeMemory.Write(_d1, 4);
                break;
            case MoveTypeList.Navmesh:
                break;
        }

        _LastFollowObjectStatus = true;
        _enableReFollow = true;
        _FollowStatus = true;
    }

    private void FollowData(ulong a1, nint a2)
    {
        FollowDataHook.Original(a1, a2);

        //获取跟随对象
        _LastFollowObjectAddress = a2;
        _LastFollowObjectId = (*(GameObject*)a2).GetGameObjectId().ObjectId;
        _LastFollowObjectName = Service.ObjectTable.SearchById((*(GameObject*)a2).GetGameObjectId().ObjectId).Name.ToString();
        if (ModuleConfig.MoveType == MoveTypeList.Navmesh)
        {
            //停止系统跟随
            TaskHelper.DelayNext(100);
            TaskHelper.Enqueue(() =>
            {
                SafeMemory.Write(_a1 + 1189, 0);
                SafeMemory.Write(_a1 + 1369, 0);
            });

            Service.Chat.Print($"开始使用 {vnavmesh.InternalName} 跟随");
        }

        _LastFollowObjectStatus = true;
        _enableReFollow = true;
        _FollowStatus = true;
    }

    public override void Uninit()
    {
        Service.CommandManager.RemoveCommand(CommandStr);
        if (vnavmesh != null) Service.IPCManager.Unload<vnavmeshIPC>(this);
        Service.IPCManager.UnregisterPluginChanged(OnPluginsChanged);
        _enableReFollow = false;
        _FollowStatus = false;

        base.Uninit();
    }

    // 配置
    private class Config : ModuleConfiguration
    {
        public bool AutoReFollow = true;
        public float Delay = 0.5f;
        public bool FlyingWhenFollowFlying = true;
        public int FollowDistance = 5;
        public bool ForcedFollow;
        public bool MountWhenFollowMount = true;
        public MoveTypeList MoveType = MoveTypeList.System;
        public bool OnCombatOver = true;
        public bool OnDuty = true;
        public int ReFollowDistance = 3;
        public bool UnMountWhenFollowUnMount;
    }

    // Hook
    private delegate void FollowDataDelegate(ulong a1, nint a2);

    private enum MoveTypeList
    {
        System,
        Navmesh,
    }
}
