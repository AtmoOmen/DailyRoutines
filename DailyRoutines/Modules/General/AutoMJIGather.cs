using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using DailyRoutines.Helpers;
using DailyRoutines.Infos;
using DailyRoutines.Managers;
using DailyRoutines.Notifications;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Hooking;
using Dalamud.Interface;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Memory;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.MJI;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ImGuiNET;
using Lumina.Excel.GeneratedSheets;
using Newtonsoft.Json;
using ObjectKind = Dalamud.Game.ClientState.Objects.Enums.ObjectKind;

namespace DailyRoutines.Modules;

[PrecedingModule([typeof(AutoAntiAfk)])]
[ModuleDescription("AutoMJIGatherTitle", "AutoMJIGatherDescription", ModuleCategories.一般)]
public class AutoMJIGather : DailyModuleBase
{
    private delegate bool IsPlayerOnDivingDelegate(nint a1);

    [Signature("E8 ?? ?? ?? ?? 84 C0 74 ?? F3 0F 10 35 ?? ?? ?? ?? F3 0F 10 3D ?? ?? ?? ?? F3 44 0F 10 05",
               DetourName = nameof(IsPlayingOnDivingDetour))]
    private static Hook<IsPlayerOnDivingDelegate>? IsPlayerOnDivingHook;

    [Signature("4C 8D 35 ?? ?? ?? ?? 48 8B 09", ScanType = ScanType.Text)]
    private static nint UnknownPtrInTargetSystem;

    private static Config ModuleConfig = null!;

    private static bool IsOnDiving;
    private static int CurrentGatherIndex;
    private static bool IsOnDataCollecting;
    private static List<Vector3> QueuedGatheringList = [];
    private static Dictionary<string, uint> GatheringItemsIcon = [];
    private static List<Vector3> DataCollectWaypoints = [];

    private static Vector2 CheckboxSize = ScaledVector2(20f);

    public override void Init()
    {
        ModuleConfig = LoadConfig<Config>() ?? new();

        Service.Hook.InitializeFromAttributes(this);
        IsPlayerOnDivingHook.Enable();

        TaskHelper ??= new TaskHelper { AbortOnTimeout = true, TimeLimitMS = 15000, ShowDebug = false };

        Service.Chat.ChatMessage += OnChatMessage;

        GatheringItemsIcon = LuminaCache.Get<MJIGatheringObject>()
                                        .Where(x => x.MapIcon != 0)
                                        .ToDictionary(x => x.Name.Value.Singular.RawString, x => x.MapIcon);
    }

    #region UI

    public override void ConfigUI()
    {
        ImGuiOm.DisableZoneWithHelp(() =>
                                    {
                                        ImGuiOm.DisableZoneWithHelp(() =>
                                        {
                                            if (ImGui.Button(Service.Lang.GetText("Start")))
                                            {
                                                TaskHelper.Enqueue(SwitchToGatherMode);
                                                QueuedGatheringList = ModuleConfig.IslandGatherPoints
                                                    .Where(group => group.Enabled)
                                                    .SelectMany(group => group.Points ?? Enumerable.Empty<Vector3>())
                                                    .ToList();

                                                if (!IsPlayerOnDivingHook.IsEnabled) IsPlayerOnDivingHook.Enable();

                                                if (QueuedGatheringList.Count != 0 && QueuedGatheringList.Count > 10)
                                                    Gather(QueuedGatheringList);
                                                else
                                                {
                                                    NotifyHelper.NotificationError(
                                                        Service.Lang.GetText("AutoMJIGather-InsufficientGatherNodes"));
                                                }
                                            }
                                        }, [
                                            new(IsOnDataCollecting,
                                                Service.Lang.GetText("AutoMJIGather-DisableHelp-DataCollecting")),
                                            new(TaskHelper.IsBusy,
                                                Service.Lang.GetText("AutoMJIGather-DisableHelp-Gathering")),
                                            new(Flags.IsOnMount,
                                                Service.Lang.GetText("AutoMJIGather-DisableHelp-Mouting")),
                                        ], Service.Lang.GetText("DisableZoneHeader"));

                                        ImGui.SameLine();
                                        ImGuiOm.DisableZoneWithHelp(() =>
                                                                    {
                                                                        if (ImGui.Button(Service.Lang.GetText("Stop")))
                                                                        {
                                                                            TaskHelper.Abort();
                                                                            IsOnDiving = false;
                                                                            IsPlayerOnDivingHook.Disable();
                                                                        }
                                                                    },
                                                                    [
                                                                        new(IsOnDataCollecting,
                                                                            Service.Lang.GetText(
                                                                                "AutoMJIGather-DisableHelp-DataCollecting")),
                                                                    ],
                                                                    Service.Lang.GetText("DisableZoneHeader"));
                                    },
                                    [
                                        new(Service.ClientState.TerritoryType != 1055,
                                            Service.Lang.GetText("AutoMJIGather-DisableHelp-NotInIsland")),
                                    ],
                                    Service.Lang.GetText("DisableZoneHeader"));

        ImGui.SameLine();
        ImGui.Text(Service.Lang.GetText("AutoMJIGather-GatherProcessInfo",
                                        QueuedGatheringList.Count == 0 ? 0 : CurrentGatherIndex + 1,
                                        QueuedGatheringList.Count));

        ImGui.SameLine();
        if (ImGui.Checkbox(Service.Lang.GetText("AutoMJIGather-StopWhenReachCaps"),
                           ref ModuleConfig.StopWhenReachingCap))
            SaveConfig(ModuleConfig);

        ImGui.Spacing();

        var tableSize = ((ImGui.GetContentRegionAvail() / 3) + ScaledVector2(50f)) with { Y = 0 };
        if (ImGui.BeginTable("GatherPointsTable1", 3, ImGuiTableFlags.Borders, tableSize))
        {
            ImGui.TableSetupColumn("启用", ImGuiTableColumnFlags.WidthFixed, CheckboxSize.X);
            ImGui.TableSetupColumn("名称", ImGuiTableColumnFlags.None, 40);
            ImGui.TableSetupColumn("数量", ImGuiTableColumnFlags.None, 20);

            ImGui.TableNextRow(ImGuiTableRowFlags.Headers);

            ImGui.TableNextColumn();
            DrawDataCollectButton();

            ImGuiOm.TooltipHover(Service.Lang.GetText("AutoMJIGather-CollectGatherPointsHelp"));

            ImGui.TableNextColumn();
            ImGuiOm.Text(Service.Lang.GetText("Name"));

            ImGui.TableNextColumn();
            ImGuiOm.Text(Service.Lang.GetText("Amount"));

            foreach (var gatherPoint in ModuleConfig.IslandGatherPoints.Take(ModuleConfig.IslandGatherPoints.Count / 2))
            {
                ImGui.TableNextRow();

                ImGui.TableNextColumn();
                var isEnabled = gatherPoint.Enabled;
                ImGui.BeginDisabled();
                ImGui.Checkbox($"###{gatherPoint.Name}", ref isEnabled);
                CheckboxSize = ImGui.GetItemRectSize();
                ImGui.EndDisabled();

                ImGui.TableNextColumn();
                ImGui.Image(gatherPoint.MapIcon.Value.ImGuiHandle, ScaledVector2(20f));

                ImGui.SameLine();
                if (ImGui.Selectable(gatherPoint.Name, isEnabled, ImGuiSelectableFlags.SpanAllColumns))
                {
                    gatherPoint.Enabled ^= true;
                    SaveConfig(ModuleConfig);
                }

                ImGui.TableNextColumn();
                ImGui.Text(gatherPoint.Points.Count.ToString());
            }

            ImGui.EndTable();
        }

        ImGui.SameLine();
        if (ImGui.BeginTable("GatherPointsTable2", 3, ImGuiTableFlags.Borders, tableSize))
        {
            ImGui.TableSetupColumn("启用", ImGuiTableColumnFlags.WidthFixed, CheckboxSize.X);
            ImGui.TableSetupColumn("名称", ImGuiTableColumnFlags.None, 40);
            ImGui.TableSetupColumn("数量", ImGuiTableColumnFlags.None, 20);

            ImGui.TableNextRow(ImGuiTableRowFlags.Headers);

            ImGui.TableNextColumn();
            DrawDataCollectButton();

            ImGuiOm.TooltipHover(Service.Lang.GetText("AutoMJIGather-CollectGatherPointsHelp"));

            ImGui.TableNextColumn();
            ImGuiOm.Text(Service.Lang.GetText("Name"));

            ImGui.TableNextColumn();
            ImGuiOm.Text(Service.Lang.GetText("Amount"));

            foreach (var gatherPoint in ModuleConfig.IslandGatherPoints.Skip(ModuleConfig.IslandGatherPoints.Count / 2))
            {
                ImGui.TableNextRow();

                ImGui.TableNextColumn();
                var isEnabled = gatherPoint.Enabled;
                ImGui.BeginDisabled();
                ImGui.Checkbox($"###{gatherPoint.Name}", ref isEnabled);
                CheckboxSize = ImGui.GetItemRectSize();
                ImGui.EndDisabled();

                ImGui.TableNextColumn();
                ImGui.Image(gatherPoint.MapIcon.Value.ImGuiHandle, ScaledVector2(20f));

                ImGui.SameLine();
                if (ImGui.Selectable(gatherPoint.Name, isEnabled, ImGuiSelectableFlags.SpanAllColumns))
                {
                    gatherPoint.Enabled ^= true;
                    SaveConfig(ModuleConfig);
                }

                ImGui.TableNextColumn();
                ImGui.Text(gatherPoint.Points.Count.ToString());
            }

            ImGui.EndTable();
        }
    }

    private void DrawDataCollectButton()
    {
        ImGuiOm.DisableZoneWithHelp(() =>
                                    {
                                        if (ImGuiOm.SelectableIconCentered(
                                                "DataCollect", FontAwesomeIcon.Play, IsOnDataCollecting))
                                        {
                                            IsOnDataCollecting ^= true;
                                            if (IsOnDataCollecting)
                                            {
                                                if (DataCollectWaypoints.Count == 0)
                                                {
                                                    DataCollectWaypoints = MergeAndTransform(
                                                        LuminaCache.Get<MJIGatheringItem>()
                                                                   .Where(x => x.Item.Value != null)
                                                                   .Select(item => new Vector2(item.X, item.Y))
                                                                   .ToList(), 50);
                                                }

                                                IsPlayerOnDivingHook.Enable();
                                                DataCollect(DataCollectWaypoints);
                                            }
                                            else
                                            {
                                                TaskHelper.Abort();
                                                IsOnDiving = false;
                                                IsPlayerOnDivingHook.Disable();
                                                SaveConfig(ModuleConfig);
                                            }
                                        }
                                    },
                                    [
                                        new(Service.ClientState.TerritoryType != 1055,
                                            Service.Lang.GetText("AutoMJIGather-DisableHelp-NotInIsland")),
                                    ],
                                    Service.Lang.GetText("DisableZoneHeader"));
    }

    #endregion

    #region Queues

    private bool? Gather(IReadOnlyList<Vector3> nodes)
    {
        if (Flags.IsOnMount || Flags.OccupiedInEvent) return false;

        TaskHelper.Enqueue(SwitchToGatherMode);
        TaskHelper.Enqueue(() => { IsOnDiving = nodes[CurrentGatherIndex].Y < 0; });
        TaskHelper.Enqueue(() =>
        {
            var node = nodes[CurrentGatherIndex];
            return node.Y switch
            {
                < 0 when !IsPlayerOnDivingHook.Original(Service.ClientState.LocalPlayer.Address + 528) =>
                    Teleport(node),
                > 0 when IsPlayerOnDivingHook.Original(Service.ClientState.LocalPlayer.Address + 528) =>
                    TeleportWhenDiving(node),
                _ => node.Y > 0 ? Teleport(node) : TeleportWhenDiving(node),
            };
        });

        TaskHelper.DelayNext(100);
        TaskHelper.Enqueue(() => InteractWithNearestObject(nodes[CurrentGatherIndex]));

        TaskHelper.DelayNext(100);
        TaskHelper.Enqueue(() => Gather(QueuedGatheringList));

        TaskHelper.DelayNext(100);
        if (CurrentGatherIndex + 1 >= nodes.Count)
        {
            TaskHelper.Abort();
            TaskHelper.Enqueue(() => CurrentGatherIndex = 0);
            TaskHelper.Enqueue(() => Gather(QueuedGatheringList));
        }
        else TaskHelper.Enqueue(() => CurrentGatherIndex++);

        return true;
    }

    private unsafe void DataCollect(IReadOnlyList<Vector3> nodes)
    {
        for (var i = 0; i < nodes.Count; i++)
        {
            var index = i;
            var node = nodes[i];
            TaskHelper.Enqueue(() => { IsOnDiving = node.Y < 0; });
            TaskHelper.Enqueue(() =>
            {
                return node.Y switch
                {
                    < 0 when !IsPlayerOnDivingHook.Original(Service.ClientState.LocalPlayer.Address + 528) =>
                        Teleport(node),
                    > 0 when IsPlayerOnDivingHook.Original(Service.ClientState.LocalPlayer.Address + 528) =>
                        TeleportWhenDiving(node),
                    _ => node.Y > 0 ? Teleport(node) : TeleportWhenDiving(node),
                };
            });

            TaskHelper.Enqueue(() =>
            {
                foreach (var obj in Service.ObjectTable)
                {
                    // 排除非采集点和特定耕地
                    if (obj.ObjectKind != ObjectKind.CardStand ||
                        obj.DataId == 0 || obj.DataId == 2013159) continue;

                    if (string.IsNullOrWhiteSpace(obj.Name.TextValue)) continue;

                    var newGatherPoint = new IslandGatherPoint(obj.Name.TextValue);
                    if (!ModuleConfig.IslandGatherPoints.Contains(newGatherPoint))
                    {
                        ModuleConfig.IslandGatherPoints.Add(newGatherPoint);
                        newGatherPoint.Points.Add(obj.Position);
                    }
                    else
                    {
                        var existingPoint = ModuleConfig.IslandGatherPoints.Find(p => p.Equals(newGatherPoint));
                        if (!existingPoint.Points.Contains(obj.Position))
                            existingPoint.Points.Add(obj.Position);
                    }
                }
            });

            TaskHelper.Enqueue(() => { NotifyHelper.NotificationInfo($"({index + 1}/{nodes.Count}) 扫描 {node} 周围环境中"); });
            TaskHelper.DelayNext(1000);
        }

        TaskHelper.Enqueue(() =>
        {
            TaskHelper.Abort();
            IsOnDiving = false;
            IsPlayerOnDivingHook.Disable();
            SaveConfig(ModuleConfig);
            IsOnDataCollecting = false;
        });

        TaskHelper.Enqueue(() => Service.UseActionManager.UseAction(ActionType.GeneralAction, 27));
    }

    #endregion

    #region Utils

    private unsafe bool? SwitchToGatherMode()
    {
        if (MJIManager.Instance()->CurrentMode == 1) return true;

        var addon = (AtkUnitBase*)Service.Gui.GetAddonByName("MJIHud");
        if (addon == null)
        {
            NotifyHelper.NotificationError(Service.Lang.GetText("AutoMJIGather-HUDNoFound"));
            TaskHelper.Abort();
            return true;
        }

        AddonHelper.Callback(addon, true, 11, 0);
        AgentHelper.SendEvent(AgentId.MJIHud, 2, 0, 1, 82042U, 0U, 0);
        return MJIManager.Instance()->CurrentMode == 1;
    }

    private bool? Teleport(Vector3 pos)
    {
        if (IsOnGathering()) return false;
        if (Service.ClientState.TerritoryType != 1055)
        {
            TaskHelper.Abort();

            NotifyHelper.NotificationError(Service.Lang.GetText("AutoMJIGather-NotInIslandMessage"));
            WinToast.Notify("", Service.Lang.GetText("AutoMJIGather-NotInIslandMessage"), ToolTipIcon.Warning);
            return true;
        }

        var localPlayer = Service.ClientState.LocalPlayer;
        if (localPlayer == null) return false;

        var address = localPlayer.Address + 176;
        MemoryHelper.Write(address, pos.X);
        MemoryHelper.Write(address + 4, pos.Y);
        MemoryHelper.Write(address + 8, pos.Z);

        return true;
    }

    private bool? TeleportWhenDiving(Vector3 pos)
    {
        if (IsOnGathering()) return false;
        if (Service.ClientState.TerritoryType != 1055)
        {
            TaskHelper.Abort();

            NotifyHelper.NotificationError(Service.Lang.GetText("AutoMJIGather-NotInIslandMessage"));
            WinToast.Notify("", Service.Lang.GetText("AutoMJIGather-NotInIslandMessage"), ToolTipIcon.Warning);
            return true;
        }

        var ptr = UnknownPtrInTargetSystem;
        var address = Marshal.ReadInt32(ptr + 3) + ptr + 22151;

        MemoryHelper.Write(address, pos.X);
        MemoryHelper.Write(address + 4, pos.Y);
        MemoryHelper.Write(address + 8, pos.Z);
        return true;
    }

    private unsafe bool? InteractWithNearestObject(Vector3 node)
    {
        if (!Throttler.Throttle("AutoMJIGather-InteractWithNearestObject")) return false;

        if (Flags.OccupiedInEvent) return false;

        var nearObjects = Service.ObjectTable
                                 .Where(x => x.ObjectKind is ObjectKind.CardStand &&
                                             Vector3.Distance(x.Position, Service.ClientState.LocalPlayer.Position) <=
                                             2)
                                 .ToArray();

        if (nearObjects.Length == 0)
        {
            NotifyHelper.NotificationWarning("没有找到采集点, 正在尝试重新定位坐标");
            _ = node.Y > 0 ? Teleport(node with { Y = node.Y - 1 }) : TeleportWhenDiving(node with { Y = node.Y - 1 });
            return false;
        }

        if (IsOnGathering()) return false;

        TargetSystem.Instance()->InteractWithObject((GameObject*)nearObjects.FirstOrDefault().Address);
        return true;
    }

    private static bool IsOnGathering()
    {
        return Service.Condition[ConditionFlag.Jumping] ||
               Service.Condition[ConditionFlag.Jumping61] ||
               Service.Condition[ConditionFlag.OccupiedInQuestEvent] ||
               Service.Condition[ConditionFlag.Casting] ||
               Service.Condition[ConditionFlag.BetweenAreas];
    }

    private static List<Vector3> MergeAndTransform(List<Vector2> points, float maxDistance)
    {
        var merged = new List<Vector3>();

        while (points.Count != 0)
        {
            var basePoint = points.First();
            var closePoints = points.Where(p => Vector2.Distance(p, basePoint) <= maxDistance).ToList();
            var midpoint = new Vector2(closePoints.Average(p => p.X), closePoints.Average(p => p.Y));
            points = points.Except(closePoints).ToList();

            merged.AddRange(
            [
                new(midpoint.X, -40, midpoint.Y),
                new(midpoint.X, 10, midpoint.Y),
                new(midpoint.X, 100, midpoint.Y),
                new(midpoint.X, 200, midpoint.Y),
            ]);
        }

        return merged;
    }

    #endregion

    #region Events & Hooks

    private static bool IsPlayingOnDivingDetour(nint a1)
    {
        if (Service.ClientState.TerritoryType != 1055)
        {
            IsPlayerOnDivingHook.Disable();
            return false;
        }

        return IsOnDiving;
    }

    private void OnChatMessage(XivChatType type, int timestamp, ref SeString sender, ref SeString message, ref bool isHandled)
    {
        if (!ModuleConfig.StopWhenReachingCap) return;
        if (!TaskHelper.IsBusy || (ushort)type != 2108) return;

        if (message.TextValue.Contains("持有数量已达到上限"))
        {
            TaskHelper.Abort();
            IsPlayerOnDivingHook.Disable();

            NotifyHelper.Warning(Service.Lang.GetText("AutoMJIGather-ReachCapsMessage"));
            WinToast.Notify("", Service.Lang.GetText("AutoMJIGather-ReachCapsMessage"), ToolTipIcon.Warning);
        }
    }

    #endregion

    public override void Uninit()
    {
        Service.Chat.ChatMessage -= OnChatMessage;
        QueuedGatheringList.Clear();
        IsOnDataCollecting = false;
        CurrentGatherIndex = 0;

        base.Uninit();
    }

    private class IslandGatherPoint : IEquatable<IslandGatherPoint>, IComparable<IslandGatherPoint>
    {
        public IslandGatherPoint()
        {
            MapIcon = new Lazy<IDalamudTextureWrap?>(() => ImageHelper.GetIcon(GatheringItemsIcon[Name]));
        }

        public IslandGatherPoint(string name)
        {
            Name = name;
            MapIcon = new Lazy<IDalamudTextureWrap?>(() => ImageHelper.GetIcon(GatheringItemsIcon[Name]));
        }

        public string        Name    { get; set; } = string.Empty;
        public bool          Enabled { get; set; }
        public List<Vector3> Points  { get; set; } = [];

        [JsonIgnore]
        public Lazy<IDalamudTextureWrap?> MapIcon { get; set; }

        public int CompareTo(IslandGatherPoint? other) =>
            other == null ? 1 : string.Compare(Name, other.Name, StringComparison.Ordinal);

        public bool Equals(IslandGatherPoint? other) => other != null && Name == other.Name;

        public override bool Equals(object? obj) => Equals(obj as IslandGatherPoint);

        public override int GetHashCode() => Name.GetHashCode();
    }

    private class Config : ModuleConfiguration
    {
        public List<IslandGatherPoint> IslandGatherPoints = [];
        public bool StopWhenReachingCap = true;
    }
}
