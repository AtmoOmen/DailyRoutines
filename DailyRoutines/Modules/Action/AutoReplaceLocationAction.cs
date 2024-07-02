using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DailyRoutines.Helpers;
using DailyRoutines.Infos;
using DailyRoutines.Managers;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Hooking;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Memory;
using Dalamud.Plugin.Services;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using ImGuiNET;
using Lumina.Excel.GeneratedSheets;

namespace DailyRoutines.Modules;

[ModuleDescription("AutoReplaceLocationActionTitle", "AutoReplaceLocationActionDescription", ModuleCategories.技能)]
public class AutoReplaceLocationAction : DailyModuleBase
{
    // 返回值为 GameObject*, 无对象则为 0
    private delegate nint ParseActionCommandArgDelegate(nint a1, nint arg, bool a3, bool a4);
    [Signature("E8 ?? ?? ?? ?? 45 33 E4 4C 8B F8 48 85 C0 74 ?? 48 8B 00", DetourName = nameof(ParseActionCommandArgDetour))]
    private static Hook<ParseActionCommandArgDelegate>? ParseActionCommandArgHook;

    private static Config ModuleConfig = null!;
    private static Throttler<string> Throttler = new();

    private static uint CurrentMapID;
    private static readonly Dictionary<MapMarker, Vector2> ZoneMapMarkers = [];
    private static bool IsNeedToModify;
    private static Vector3? ModifiedLocation;

    private static string ContentSearchInput = string.Empty;

    private static Vector2 CheckboxSize = ScaledVector2(20f);

    public override void Init()
    {
        ModuleConfig = LoadConfig<Config>() ?? new();

        Service.FrameworkManager.Register(OnUpdate);
        Service.UseActionManager.Register(OnPreUseActionLocation);
        Service.UseActionManager.Register(OnPostUseActionLocation);
        Service.UseActionManager.Register(OnPreUseActionPetMove);

        Service.Hook.InitializeFromAttributes(this);
        ParseActionCommandArgHook.Enable();
    }

    public override void ConfigUI()
    {
        ImGui.TextColored(ImGuiColors.DalamudOrange, $"{Service.Lang.GetText("WorkTheory")}:");
        ImGuiOm.HelpMarker(Service.Lang.GetText("AutoReplaceLocationAction-TheoryHelp"), 30f);

        ImGui.Spacing();

        if (ImGui.Checkbox(Service.Lang.GetText("AutoReplaceLocationAction-SendMessage"), ref ModuleConfig.SendMessage))
            SaveConfig(ModuleConfig);

        if (ImGui.Checkbox(Service.Lang.GetText("AutoReplaceLocationAction-EnableCenterArg"), ref ModuleConfig.EnableCenterArgument))
            SaveConfig(ModuleConfig);

        ImGuiOm.HelpMarker(Service.Lang.GetText("AutoReplaceLocationAction-EnableCenterArgHelp"));

        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(ImGuiColors.DalamudOrange, $"{Service.Lang.GetText("AutoReplaceLocationAction-AdjustDistance")}:");

        ImGui.SameLine();
        ImGui.SetNextItemWidth(80f * GlobalFontScale);
        ImGui.InputFloat("###AdjustDistanceInput", ref ModuleConfig.AdjustDistance, 0, 0, "%.1f");
        if (ImGui.IsItemDeactivatedAfterEdit())
            SaveConfig(ModuleConfig);

        ImGuiOm.HelpMarker(Service.Lang.GetText("AutoReplaceLocationAction-AdjustDistanceHelp"));

        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(ImGuiColors.DalamudOrange, $"{Service.Lang.GetText("AutoReplaceLocationAction-BlacklistContents")}:");

        ImGui.SameLine();
        ImGui.SetNextItemWidth(200f * GlobalFontScale);
        if (ContentSelectCombo(ref ModuleConfig.BlacklistContent, ref ContentSearchInput)) SaveConfig(ModuleConfig);

        var tableSize = new Vector2(ImGui.GetContentRegionAvail().X / 3, 0);
        if (ImGui.BeginTable("ActionEnableTable", 2, ImGuiTableFlags.BordersInner, tableSize))
        {
            ImGui.TableSetupColumn("启用", ImGuiTableColumnFlags.WidthFixed, CheckboxSize.X);
            ImGui.TableSetupColumn("名称");

            foreach (var actionPair in ModuleConfig.EnabledActions)
            {
                var action = LuminaCache.GetRow<Action>(actionPair.Key);

                ImGui.TableNextRow();

                ImGui.TableNextColumn();
                var state = actionPair.Value;
                if (ImGui.Checkbox($"###{actionPair.Key}_{action.Name.RawString}", ref state))
                {
                    ModuleConfig.EnabledActions[actionPair.Key] = state;
                    SaveConfig(ModuleConfig);
                }
                CheckboxSize = ImGui.GetItemRectSize();

                ImGui.TableNextColumn();
                ImGuiOm.TextImage(action.Name.RawString, ImageHelper.GetIcon(action.Icon).ImGuiHandle,
                                  ScaledVector2(20f));
            }

            foreach (var actionPair in ModuleConfig.EnabledPetActions)
            {
                var action = LuminaCache.GetRow<PetAction>(actionPair.Key);

                ImGui.TableNextRow();

                ImGui.TableNextColumn();
                var state = actionPair.Value;
                if (ImGui.Checkbox($"###{actionPair.Key}_{action.Name.RawString}", ref state))
                {
                    ModuleConfig.EnabledPetActions[actionPair.Key] = state;
                    SaveConfig(ModuleConfig);
                }

                ImGui.TableNextColumn();
                ImGuiOm.TextImage(action.Name.RawString, ImageHelper.GetIcon((uint)action.Icon).ImGuiHandle,
                                  ScaledVector2(20f));
            }

            ImGui.EndTable();
        }
    }

    private static void OnUpdate(IFramework framework)
    {
        if (!Throttler.Throttle("AutoReplaceLocationAction", 1000)) return;
        if (!Flags.BoundByDuty) return;

        if (CurrentMapID == Service.ClientState.MapId) return;

        CurrentMapID = Service.ClientState.MapId;
        var currentMap = LuminaCache.GetRow<Map>(CurrentMapID);

        ZoneMapMarkers.Clear();
        MapHelper.GetMapMarkers(CurrentMapID)
                 .ForEach(x =>
                 {
                     if (x.Icon == 60442)
                         ZoneMapMarkers.TryAdd(x, MapHelper.TextureToWorld(x.GetPosition(), currentMap));
                 });
    }

    private static void OnPreUseActionLocation(
        ref bool isPrevented, ref ActionType type, ref uint actionID,
        ref ulong targetID, ref Vector3 location, ref uint a4)
    {
        if (ModuleConfig.BlacklistContent.Contains(Service.ClientState.TerritoryType)) return;
        if (!IsNeedToModify && (type != ActionType.Action ||
                                !ModuleConfig.EnabledActions.TryGetValue(actionID, out var isEnabled) ||
                                !isEnabled)) return;

        var resultLocation = ZoneMapMarkers.Values
                                           .Select(x => x.ToVector3() as Vector3?)
                                           .FirstOrDefault(
                                               x => x.HasValue &&
                                                    Vector3.Distance(
                                                        x.Value, Service.ClientState.LocalPlayer.Position) < 25);

        if (resultLocation != null)
            UpdateLocationIfClose(ref location, resultLocation.Value, ModuleConfig.AdjustDistance);
        else
            HandleAlternativeLocation(ref location);
    }

    private static unsafe void OnPreUseActionPetMove(
        ref bool isPrevented, ref int a1, ref Vector3 location, ref int perActionID, ref int a4, ref int a5, ref int a6)
    {
        if (ModuleConfig.BlacklistContent.Contains(Service.ClientState.TerritoryType)) return;
        if (!IsNeedToModify && (!ModuleConfig.EnabledPetActions.TryGetValue(3, out var isEnabled) || !isEnabled)) return;

        var resultLocation = ZoneMapMarkers.Values
                                           .Select(x => x.ToVector3() as Vector3?)
                                           .FirstOrDefault(
                                               x => x.HasValue &&
                                                    Vector3.Distance(x.Value, Service.ClientState.LocalPlayer.Position) <
                                                    25);

        if (resultLocation != null)
            UpdateLocationIfClose(ref location, resultLocation.Value, ModuleConfig.AdjustDistance);
        else
            HandleAlternativeLocation(ref location);

        if (ModifiedLocation != null)
        {
            isPrevented = true;
            var modifiedLocation = (Vector3)ModifiedLocation;
            UseActionManager.UseActionPetMoveHook.Original(1800, &modifiedLocation, 3, 0, 0, 0);

            var message = new SeStringBuilder().Append(DRPrefix)
                                               .Append(Service.Lang.GetText("AutoReplaceLocationAction-RedirectMessage",
                                                                            ModifiedLocation))
                                               .Build();

            Service.Chat.Print(message);
            ModifiedLocation = null;
        }
    }

    private static unsafe nint ParseActionCommandArgDetour(nint a1, nint arg, bool a3, bool a4)
    {
        var original = ParseActionCommandArgHook.Original(a1, arg, a3, a4);
        if (!ModuleConfig.EnableCenterArgument ||
            ModuleConfig.BlacklistContent.Contains(Service.ClientState.TerritoryType)) return original;

        var parsedArg = MemoryHelper.ReadSeStringNullTerminated(arg).TextValue;
        if (!parsedArg.Equals("<center>")) return original;

        IsNeedToModify = true;
        return (nint)Control.GetLocalPlayer();
    }

    private static void UpdateLocationIfClose(
        ref Vector3 currentLocation, Vector3 candidateLocation, float proximityThreshold)
    {
        if (Vector3.Distance(currentLocation, candidateLocation) < proximityThreshold)
        {
            currentLocation = candidateLocation;
            ModifiedLocation ??= candidateLocation;
        }
    }

    private static void HandleAlternativeLocation(ref Vector3 location)
    {
        if (PresetData.TryGetContent(Service.ClientState.TerritoryType, out var content) &&
            content.ContentType.Row is 4 or 5)
        {
            var map = LuminaCache.GetRow<Map>(CurrentMapID);
            var modifiedLocation = MapHelper.MapToWorld(new Vector2(6.125f), map).ToVector3();

            UpdateLocationIfClose(ref location, modifiedLocation, ModuleConfig.AdjustDistance);
        }
    }

    private static void OnPostUseActionLocation(
        bool result, ActionType actionType, uint actionID,
        ulong targetID, Vector3 location, uint a4)
    {
        if (!result || ModifiedLocation == null) return;
        if (!Throttler.Throttle($"{actionType}_{actionID}")) return;

        if (ModuleConfig.SendMessage)
            NotifyHelper.Chat(Service.Lang.GetText("AutoReplaceLocationAction-RedirectMessage", ModifiedLocation));

        ModifiedLocation = null;
        IsNeedToModify = false;
    }

    public override void Uninit()
    {
        Service.UseActionManager.Unregister(OnPreUseActionLocation);
        Service.UseActionManager.Unregister(OnPostUseActionLocation);
        Service.UseActionManager.Unregister(OnPreUseActionPetMove);
        ModifiedLocation = null;
        IsNeedToModify = false;

        base.Uninit();
    }

    private class Config : ModuleConfiguration
    {
        public readonly Dictionary<uint, bool> EnabledActions = new()
        {
            { 7439, true },  // 地星
            { 25862, true }, // 礼仪之铃
            { 3569, true },  // 庇护所
            { 188, true },   // 野战治疗阵
        };

        public readonly Dictionary<uint, bool> EnabledPetActions = new()
        {
            { 3, true }, // 移动
        };

        public float AdjustDistance = 15;
        public HashSet<uint> BlacklistContent = [];
        public bool EnableCenterArgument = true;
        public bool SendMessage = true;
    }
}
