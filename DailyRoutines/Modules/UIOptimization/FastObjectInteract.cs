using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using ClickLib;
using DailyRoutines.Helpers;
using DailyRoutines.Infos;
using DailyRoutines.Managers;
using DailyRoutines.Windows;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Memory;
using Dalamud.Plugin.Services;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ImGuiNET;
using Lumina.Excel.GeneratedSheets;
using ObjectKind = Dalamud.Game.ClientState.Objects.Enums.ObjectKind;

namespace DailyRoutines.Modules;

[ModuleDescription("FastObjectInteractTitle", "FastObjectInteractDescription", ModuleCategories.界面优化)]
public unsafe partial class FastObjectInteract : DailyModuleBase
{
    private delegate nint AgentWorldTravelReceiveEventDelegate(AgentWorldTravel* agent, nint a2, nint a3, nint a4, long eventCase);
    [Signature("40 55 53 56 57 41 54 41 56 41 57 48 8D AC 24 ?? ?? ?? ?? B8")]
    private static AgentWorldTravelReceiveEventDelegate? AgentWorldTravelReceiveEvent;

    private delegate nint WorldTravelSetupInfoDelegate(nint worldTravel, ushort currentWorld, ushort targetWorld);
    [Signature("E8 ?? ?? ?? ?? 48 8D 8E ?? ?? ?? ?? E8 ?? ?? ?? ?? 4C 8B 05 ?? ?? ?? ?? 48 8D 4C 24 ?? 48 8B D0 E8 ?? ?? ?? ?? 48 8B 4E")]
    private static WorldTravelSetupInfoDelegate? WorldTravelSetupInfo;

    private static readonly Dictionary<ObjectKind, string> ObjectKindLoc = new()
    {
        { ObjectKind.BattleNpc, "战斗类 NPC (不建议)" },
        { ObjectKind.EventNpc, "一般类 NPC" },
        { ObjectKind.EventObj, "事件物体 (绝大多数要交互的都属于此类)" },
        { ObjectKind.Treasure, "宝箱" },
        { ObjectKind.Aetheryte, "以太之光" },
        { ObjectKind.GatheringPoint, "采集点" },
        { ObjectKind.MountType, "坐骑 (不建议)" },
        { ObjectKind.Companion, "宠物 (不建议)" },
        { ObjectKind.Retainer, "雇员" },
        { ObjectKind.Area, "地图传送相关" },
        { ObjectKind.Housing, "家具庭具" },
        { ObjectKind.CardStand, "固定类物体 (如无人岛采集点等)" },
        { ObjectKind.Ornament, "时尚配饰 (不建议)" },
    };

    private const string ENPCTiltleText = "[{0}] {1}";
    private static Dictionary<uint, string>? ENpcTitles;
    private static HashSet<uint>? ImportantENPC;

    private static Config ModuleConfig = null!;
    private static readonly Throttler<string> MonitorThrottler = new();

    private static string BlacklistKeyInput = string.Empty;
    private static float WindowWidth;

    private static readonly List<ObjectToSelect> tempObjects = new(596);
    private static TargetSystem* TargetSystem;
    private static readonly Dictionary<nint, ObjectToSelect> ObjectsToSelect = [];

    private static string AethernetShardName = string.Empty;
    private static bool IsInInstancedArea;
    private static int InstancedAreaAmount = 3;

    private static readonly HashSet<uint> WorldTravelValidZones = [132, 129, 130];
    private static Dictionary<uint, string> DCWorlds = [];
    private static bool IsOnWorldTravelling;

    public override void Init()
    {
        ModuleConfig = LoadConfig<Config>() ?? new()
        {
            SelectedKinds =
            [
                ObjectKind.EventNpc, ObjectKind.EventObj, ObjectKind.Treasure, ObjectKind.Aetheryte,
                ObjectKind.GatheringPoint,
            ],
        };

        Service.Hook.InitializeFromAttributes(this);

        TargetSystem = FFXIVClientStructs.FFXIV.Client.Game.Control.TargetSystem.Instance();
        TaskHelper ??= new TaskHelper { AbortOnTimeout = true, TimeLimitMS = 5000, ShowDebug = false };

        ENpcTitles ??= LuminaCache.Get<ENpcResident>()
                                  .Where(x => x.Unknown10 && !string.IsNullOrWhiteSpace(x.Title.RawString))
                                  .ToDictionary(x => x.RowId, x => x.Title.RawString);

        ImportantENPC ??= LuminaCache.Get<ENpcResident>()
                                     .Where(x => x.Unknown10)
                                     .Select(x => x.RowId)
                                     .ToHashSet();

        AethernetShardName = LuminaCache.GetRow<EObjName>(2000151).Singular.RawString;

        Overlay ??= new Overlay(this, $"Daily Routines {Service.Lang.GetText("FastObjectInteractTitle")}");
        Overlay.Flags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.AlwaysAutoResize |
                        ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoCollapse;

        if (ModuleConfig.LockWindow) Overlay.Flags |= ImGuiWindowFlags.NoMove;
        else Overlay.Flags &= ~ImGuiWindowFlags.NoMove;

        Service.ClientState.TerritoryChanged += OnZoneChanged;
        Service.ClientState.Login += OnLogin;
        Service.FrameworkManager.Register(OnUpdate);

        OnZoneChanged(1);
        OnLogin();
    }

    private static void OnLogin()
    {
        var agent = AgentLobby.Instance();
        if (agent == null) return;

        var homeWorld = agent->LobbyData.HomeWorldId;
        if (homeWorld <= 0) return;

        var dataCenter = LuminaCache.GetRow<World>(homeWorld).DataCenter.Row;
        if (dataCenter <= 0) return;

        DCWorlds.Clear();
        DCWorlds = LuminaCache.Get<World>()
                              .Where(x => x.DataCenter.Row == dataCenter && !string.IsNullOrWhiteSpace(x.Name.RawString) &&
                                          !string.IsNullOrWhiteSpace(x.InternalName.RawString) &&
                                          IsChineseString(x.Name.RawString))
                              .ToDictionary(x => x.RowId, x => x.Name.RawString);
    }

    public override void ConfigUI()
    {
        ImGui.AlignTextToFramePadding();
        ImGui.Text($"{Service.Lang.GetText("FastObjectInteract-FontScale")}:");

        ImGui.SameLine();
        ImGui.SetNextItemWidth(80f * GlobalFontScale);
        ImGui.InputFloat("###FontScaleInput", ref ModuleConfig.FontScale, 0f, 0f,
                         ModuleConfig.FontScale.ToString(CultureInfo.InvariantCulture));

        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            ModuleConfig.FontScale = Math.Max(0.1f, ModuleConfig.FontScale);
            SaveConfig(ModuleConfig);
        }

        ImGui.AlignTextToFramePadding();
        ImGui.Text($"{Service.Lang.GetText("FastObjectInteract-MinButtonWidth")}:");

        ImGui.SameLine();
        ImGui.SetNextItemWidth(80f * GlobalFontScale);
        ImGui.InputFloat("###MinButtonWidthInput", ref ModuleConfig.MinButtonWidth, 0, 0,
                         ModuleConfig.MinButtonWidth.ToString(CultureInfo.InvariantCulture));

        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            ModuleConfig.MinButtonWidth = Math.Max(1, ModuleConfig.MinButtonWidth);
            SaveConfig(ModuleConfig);
        }

        ImGui.AlignTextToFramePadding();
        ImGui.Text($"{Service.Lang.GetText("FastObjectInteract-MaxDisplayAmount")}:");

        ImGui.SameLine();
        ImGui.SetNextItemWidth(80f * GlobalFontScale);
        ImGui.InputInt("###MaxDisplayAmountInput", ref ModuleConfig.MaxDisplayAmount, 0, 0);
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            ModuleConfig.MaxDisplayAmount = Math.Max(1, ModuleConfig.MaxDisplayAmount);
            SaveConfig(ModuleConfig);
        }

        ImGui.AlignTextToFramePadding();
        ImGui.Text($"{Service.Lang.GetText("FastObjectInteract-SelectedObjectKinds")}:");

        ImGui.SameLine();
        ImGui.SetNextItemWidth(300f * GlobalFontScale);
        if (ImGui.BeginCombo("###ObjectKindsSelection",
                             Service.Lang.GetText("FastObjectInteract-SelectedObjectKindsAmount", ModuleConfig.SelectedKinds.Count),
                             ImGuiComboFlags.HeightLarge))
        {
            foreach (var kind in Enum.GetValues<ObjectKind>())
            {
                if (!ObjectKindLoc.TryGetValue(kind, out var loc)) continue;

                var state = ModuleConfig.SelectedKinds.Contains(kind);
                if (ImGui.Checkbox(loc, ref state))
                {
                    if (!ModuleConfig.SelectedKinds.Remove(kind))
                        ModuleConfig.SelectedKinds.Add(kind);

                    SaveConfig(ModuleConfig);
                }
            }

            ImGui.EndCombo();
        }

        ImGui.AlignTextToFramePadding();
        ImGui.Text($"{Service.Lang.GetText("FastObjectInteract-BlacklistKeysList")}:");

        ImGui.SameLine();
        ImGui.SetNextItemWidth(300f * GlobalFontScale);
        if (ImGui.BeginCombo("###BlacklistObjectsSelection",
                             Service.Lang.GetText("FastObjectInteract-BlacklistKeysListAmount",
                                                  ModuleConfig.BlacklistKeys.Count), ImGuiComboFlags.HeightLarge))
        {
            ImGui.SetNextItemWidth(250f * GlobalFontScale);
            ImGui.InputTextWithHint("###BlacklistKeyInput",
                                    $"{Service.Lang.GetText("FastObjectInteract-BlacklistKeysListInputHelp")}",
                                    ref BlacklistKeyInput, 100);

            ImGui.SameLine();
            if (ImGuiOm.ButtonIcon("###BlacklistKeyInputAdd", FontAwesomeIcon.Plus,
                                   Service.Lang.GetText("FastObjectInteract-Add")))
            {
                if (!ModuleConfig.BlacklistKeys.Add(BlacklistKeyInput)) return;

                SaveConfig(ModuleConfig);
            }

            ImGui.Separator();

            foreach (var key in ModuleConfig.BlacklistKeys)
            {
                if (ImGuiOm.ButtonIcon(key, FontAwesomeIcon.TrashAlt, Service.Lang.GetText("FastObjectInteract-Remove")))
                {
                    ModuleConfig.BlacklistKeys.Remove(key);
                    SaveConfig(ModuleConfig);
                }

                ImGui.SameLine();
                ImGui.Text(key);
            }

            ImGui.EndCombo();
        }

        if (ImGui.Checkbox(Service.Lang.GetText("FastObjectInteract-WindowInvisibleWhenInteract"),
                           ref ModuleConfig.WindowInvisibleWhenInteract))
            SaveConfig(ModuleConfig);

        if (ImGui.Checkbox(Service.Lang.GetText("FastObjectInteract-LockWindow"), ref ModuleConfig.LockWindow))
        {
            SaveConfig(ModuleConfig);

            if (ModuleConfig.LockWindow)
                Overlay.Flags |= ImGuiWindowFlags.NoMove;
            else
                Overlay.Flags &= ~ImGuiWindowFlags.NoMove;
        }

        if (ImGui.Checkbox(Service.Lang.GetText("FastObjectInteract-OnlyDisplayInViewRange"),
                           ref ModuleConfig.OnlyDisplayInViewRange))
            SaveConfig(ModuleConfig);

        if (ImGui.Checkbox(Service.Lang.GetText("FastObjectInteract-AllowClickToTarget"),
                           ref ModuleConfig.AllowClickToTarget))
            SaveConfig(ModuleConfig);
    }

    public override void OverlayUI()
    {
        using (FontManager.GetUIFont(ModuleConfig.FontScale).Push())
        {
            ObjectToSelect? instanceChangeObject = null;
            ObjectToSelect? worldTravelObject = null;

            ImGui.BeginGroup();
            foreach (var objectToSelect in ObjectsToSelect.Values)
            {
                if (objectToSelect.GameObject == nint.Zero) continue;

                if (IsInInstancedArea && objectToSelect.Kind == ObjectKind.Aetheryte)
                {
                    var gameObj = (GameObject*)objectToSelect.GameObject;
                    if (gameObj->NameString != AethernetShardName)
                        instanceChangeObject = objectToSelect;
                }

                if (!IsOnWorldTravelling && WorldTravelValidZones.Contains(Service.ClientState.TerritoryType) &&
                    objectToSelect.Kind == ObjectKind.Aetheryte)
                {
                    var gameObj = (GameObject*)objectToSelect.GameObject;
                    if (gameObj->NameString != AethernetShardName)
                        worldTravelObject = objectToSelect;
                }

                if (ModuleConfig.AllowClickToTarget)
                {
                    if (objectToSelect.ButtonToTarget())
                        SaveConfig(ModuleConfig);
                }
                else
                {
                    if (objectToSelect.ButtonNoTarget())
                        SaveConfig(ModuleConfig);
                }
            }

            ImGui.EndGroup();

            ImGui.SameLine();
            if (instanceChangeObject != null)
                InstanceZoneChangeWidget(instanceChangeObject);

            if (worldTravelObject != null)
                WorldChangeWidget(worldTravelObject);

            WindowWidth = Math.Max(ModuleConfig.MinButtonWidth, ImGui.GetItemRectSize().X);
        }
    }

    private void OnUpdate(IFramework framework)
    {
        if (!MonitorThrottler.Throttle("Monitor", 250)) return;

        var localPlayer = Service.ClientState.LocalPlayer;
        if (localPlayer == null)
        {
            ObjectsToSelect.Clear();
            WindowWidth = 0f;
            Overlay.IsOpen = false;
            return;
        }

        tempObjects.Clear();
        IsInInstancedArea = UIState.Instance()->PublicInstance.IsInstancedArea();
        IsOnWorldTravelling = localPlayer.OnlineStatus.Id == 25;

        foreach (var obj in Service.ObjectTable.ToArray())
        {
            if (!obj.IsTargetable || obj.IsDead) continue;

            var objName = obj.Name.TextValue;
            if (ModuleConfig.BlacklistKeys.Contains(objName)) continue;

            var objKind = obj.ObjectKind;
            if (!ModuleConfig.SelectedKinds.Contains(objKind)) continue;

            var dataID = obj.DataId;
            var gameObj = (GameObject*)obj.Address;
            if (objKind == ObjectKind.EventNpc)
            {
                if (ImportantENPC.Contains(dataID))
                {
                    if (ENpcTitles.TryGetValue(dataID, out var ENPCTitle))
                        objName = string.Format(ENPCTiltleText, ENPCTitle, obj.Name);
                }
                else if (gameObj->NamePlateIconId == 0) continue;
            }

            if (ModuleConfig.OnlyDisplayInViewRange)
            {
                if (!TargetSystem_IsObjectInViewRange((nint)TargetSystem, (nint)gameObj)) continue;
            }

            var objDistance = Vector3.Distance(localPlayer.Position, obj.Position);
            if (objDistance > 20 || localPlayer.Position.Y - gameObj->Position.Y > 4) continue;

            if (tempObjects.Count > ModuleConfig.MaxDisplayAmount) break;
            tempObjects.Add(new ObjectToSelect((nint)gameObj, objName, objKind, objDistance));
        }

        tempObjects.Sort((a, b) => a.Distance.CompareTo(b.Distance));

        ObjectsToSelect.Clear();
        foreach (var tempObj in tempObjects) ObjectsToSelect.Add(tempObj.GameObject, tempObj);

        if (Overlay == null) return;
        if (!IsWindowShouldBeOpen())
        {
            Overlay.IsOpen = false;
            WindowWidth = 0f;
        }
        else
            Overlay.IsOpen = true;
    }

    private static void OnZoneChanged(ushort zone)
    {
        if (zone == 0 || zone == Service.ClientState.TerritoryType) return;

        InstancedAreaAmount = 3;
    }

    private void InstanceZoneChangeWidget(ObjectToSelect objectToSelect)
    {
        var gameObject = (GameObject*)objectToSelect.GameObject;
        var instance = UIState.Instance()->PublicInstance;

        ImGui.BeginGroup();
        for (var i = 1; i < InstancedAreaAmount + 1; i++)
        {
            if (i == instance.InstanceId) continue;

            ImGui.BeginDisabled(!objectToSelect.IsReacheable());
            if (ButtonCenterText($"InstanceChangeWidget_{i}",
                                 Service.Lang.GetText("FastObjectInteract-InstanceAreaChange", i)))
                ChangeInstanceZone(gameObject, i);

            ImGui.EndDisabled();
        }

        ImGui.EndGroup();

        return;

        void ChangeInstanceZone(GameObject* obj, int zone)
        {
            TaskHelper.Abort();

            TaskHelper.Enqueue(() => InteractWithObject(obj, ObjectKind.Aetheryte));

            TaskHelper.Enqueue(() =>
            {
                if (!TryGetAddonByName<AtkUnitBase>("SelectString", out var addon) ||
                    !IsAddonAndNodesReady(addon)) return false;

                return ClickHelper.SelectString("切换副本区");
            });

            TaskHelper.Enqueue(() =>
            {
                if (!TryGetAddonByName<AtkUnitBase>("SelectString", out var addon) ||
                    !IsAddonAndNodesReady(addon)) return false;

                if (!MemoryHelper.ReadSeStringNullTerminated((nint)addon->AtkValues[2].String).TextValue
                                 .Contains("为了缓解服务器压力")) return false;

                InstancedAreaAmount = ((AddonSelectString*)addon)->PopupMenu.PopupMenu.EntryCount - 2;
                return Click.TrySendClick($"select_string{zone + 1}");
            });
        }
    }

    private static void WorldChangeWidget(ObjectToSelect _)
    {
        var lobbyData = AgentLobby.Instance()->LobbyData;
        ImGui.BeginGroup();
        ImGui.BeginDisabled(Flags.IsOnWorldTravel);
        foreach (var worldPair in DCWorlds)
        {
            if (worldPair.Key == lobbyData.CurrentWorldId) continue;

            if (ButtonCenterText($"WorldTravelWidget_{worldPair.Key}", worldPair.Value))
            {
                var agent = AgentWorldTravel.Instance();
                agent->WorldToTravel = worldPair.Key;
                WorldTravelSetupInfo((nint)agent, AgentLobby.Instance()->LobbyData.CurrentWorldId, (ushort)worldPair.Key);
                var a2 = 1;
                var a3 = 0;
                var a4 = 1;
                const int a5 = 1;
                AgentWorldTravelReceiveEvent(agent, (nint)(&a2), (nint)(&a3), (nint)(&a4), a5);
            }
        }
        ImGui.EndDisabled();
        ImGui.EndGroup();
    }

    private void InteractWithObject(GameObject* obj, ObjectKind kind)
    {
        TaskHelper.RemoveAllTasks(2);

        if (Flags.IsOnMount)
            TaskHelper.Enqueue(() => Service.ExecuteCommandManager.ExecuteCommand(ExecuteCommandFlag.Dismount, 1), "DismountInteract", 2);

        TaskHelper.Enqueue(() =>
        {
            if (Flags.IsOnMount) return false;

            FFXIVClientStructs.FFXIV.Client.Game.Control.TargetSystem.Instance()->Target = obj;
            return FFXIVClientStructs.FFXIV.Client.Game.Control.TargetSystem.Instance()->InteractWithObject(obj) != 0;
        }, "Interact", 2);

        if (kind is ObjectKind.EventObj)
            TaskHelper.Enqueue(
                () => FFXIVClientStructs.FFXIV.Client.Game.Control.TargetSystem.Instance()->OpenObjectInteraction(obj),
                "OpenInteraction", 2);
    }

    private static bool IsWindowShouldBeOpen()
        => ObjectsToSelect.Count != 0 && (!ModuleConfig.WindowInvisibleWhenInteract || !Flags.OccupiedInEvent);

    public static bool ButtonCenterText(string id, string text)
    {
        ImGui.PushID(id);
        var textSize = ImGui.CalcTextSize(text);

        var cursorPos = ImGui.GetCursorScreenPos();
        var padding = ImGui.GetStyle().FramePadding;
        var buttonWidth = Math.Max(WindowWidth, textSize.X + (padding.X * 2));
        var result = ImGui.Button(string.Empty, new Vector2(buttonWidth, textSize.Y + (padding.Y * 2)));

        ImGui.GetWindowDrawList()
             .AddText(new Vector2(cursorPos.X + ((buttonWidth - textSize.X) / 2), cursorPos.Y + padding.Y),
                      ImGui.GetColorU32(ImGuiCol.Text), text);
        ImGui.PopID();

        return result;
    }

    public static bool TargetSystem_IsObjectInViewRange(nint targetSystem, nint targetGameObject)
    {
        if (targetGameObject == nint.Zero) return false;

        var objectCount = *(int*)(targetSystem + 328);
        if (objectCount <= 0) return false;

        var i = (nint*)(targetSystem + 336);
        for (var index = 0; index < objectCount; index++, i++)
        {
            if (*i == targetGameObject)
                return true;
        }

        return false;
    }

    public override void Uninit()
    {
        base.Uninit();

        Service.ClientState.Login -= OnLogin;
        Service.ClientState.TerritoryChanged -= OnZoneChanged;
        ObjectsToSelect.Clear();
    }

    [GeneratedRegex("\\[.*?\\]")]
    private static partial Regex FastObjectInteractTitleRegex();

    [StructLayout(LayoutKind.Explicit)]
    private struct AgentWorldTravel
    {
        [FieldOffset(0)]
        public AgentInterface AgentInterface;

        [FieldOffset(76)]
        public uint WorldToTravel;

        public static AgentWorldTravel* Instance() =>
            (AgentWorldTravel*)AgentModule.Instance()->GetAgentByInternalId(AgentId.WorldTravel);
    }

    private sealed record ObjectToSelect(nint GameObject, string Name, ObjectKind Kind, float Distance)
    {
        public bool ButtonToTarget()
        {
            var colors = ImGui.GetStyle().Colors;

            if (!IsReacheable())
            {
                ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.5f);
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, colors[(int)ImGuiCol.HeaderActive]);
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, colors[(int)ImGuiCol.HeaderHovered]);
            }

            ButtonCenterText(GameObject.ToString(), Name);

            if (!IsReacheable())
            {
                ImGui.PopStyleColor(2);
                ImGui.PopStyleVar();
            }

            if (ImGui.IsItemClicked(ImGuiMouseButton.Left) && IsReacheable())
            {
                ((FastObjectInteract)Service.ModuleManager.Modules[typeof(FastObjectInteract)]).InteractWithObject((GameObject*)GameObject, Kind);
            }
            else if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
                FFXIVClientStructs.FFXIV.Client.Game.Control.TargetSystem.Instance()->Target = (GameObject*)GameObject;

            return AddToBlacklist();
        }

        public bool ButtonNoTarget()
        {
            ImGui.BeginDisabled(!IsReacheable());
            if (ButtonCenterText(GameObject.ToString(), Name))
                ((FastObjectInteract)Service.ModuleManager.Modules[typeof(FastObjectInteract)]).InteractWithObject((GameObject*)GameObject, Kind);

            ImGui.EndDisabled();

            return AddToBlacklist();
        }

        private bool AddToBlacklist()
        {
            var state = false;
            if (ImGui.BeginPopupContextItem($"{GameObject}_{Name}"))
            {
                if (ImGui.MenuItem(Service.Lang.GetText("FastObjectInteract-AddToBlacklist")))
                {
                    if (ModuleConfig.BlacklistKeys.Add(FastObjectInteractTitleRegex().Replace(Name, "").Trim()))
                        state = true;
                }

                ImGui.EndPopup();
            }

            return state;
        }

        public bool IsReacheable() =>
            Kind switch
            {
                ObjectKind.EventObj => Distance < 4.7999999,
                ObjectKind.EventNpc => Distance < 6.9999999,
                ObjectKind.Aetheryte => Distance < 11.0,
                ObjectKind.GatheringPoint => Distance < 3.0,
                _ => Distance < 6.0,
            };

        public bool Equals(ObjectToSelect? other)
        {
            if (other is null || GetType() != other.GetType())
                return false;

            return GameObject == other.GameObject;
        }

        public override int GetHashCode() { return HashCode.Combine(GameObject); }
    }

    private class Config : ModuleConfiguration
    {
        public HashSet<string> BlacklistKeys = [];
        public HashSet<ObjectKind> SelectedKinds = [];

        public bool AllowClickToTarget;
        public float FontScale = 1f;
        public bool LockWindow;
        public int MaxDisplayAmount = 5;
        public float MinButtonWidth = 300f;
        public bool OnlyDisplayInViewRange;
        public bool WindowInvisibleWhenInteract = true;
    }
}
