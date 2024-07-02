using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using DailyRoutines.Helpers;
using DailyRoutines.Infos;
using DailyRoutines.Managers;
using DailyRoutines.Notifications;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Hooking;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Memory;
using Dalamud.Utility.Signatures;


using FFXIVClientStructs.FFXIV.Client.Game.Character;
using ImGuiNET;
using Lumina.Excel.GeneratedSheets;
using GameObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;

namespace DailyRoutines.Modules;

[ModuleDescription("AutoNotifySPPlayersTitle", "AutoNotifySPPlayersDescription", ModuleCategories.通知)]
public unsafe class AutoNotifySPPlayers : DailyModuleBase
{
    private static Config ModuleConfig = null!;

    private static Dictionary<uint, OnlineStatus>? OnlineStatuses;

    private static string ZoneSearchInput = string.Empty;

    private static readonly HashSet<uint> SelectedOnlineStatus = [];
    private static readonly HashSet<uint> SelectedZone = [];
    private static string SelectName = string.Empty;
    private static string SelectCommand = string.Empty;

    private static readonly Dictionary<nint, long> NoticeTimeInfo = [];

    [Signature("0F B6 91 ?? ?? ?? ?? F6 C2 ?? 74 ?? F6 C2 ?? 74", DetourName = nameof(IsTargetableDetour))]
    private readonly Hook<IsTargetableDelegate>? IsTargetableHook;


    public override void Init()
    {
        OnlineStatuses ??= LuminaCache.Get<OnlineStatus>().Where(x => x.RowId != 0 && x.RowId != 47)
                                      .ToDictionary(x => x.RowId, x => x);

        ModuleConfig = LoadConfig<Config>() ?? new();

        Service.Hook.InitializeFromAttributes(this);
        IsTargetableHook?.Enable();
    }

    public override void ConfigUI()
    {
        ImGui.TextColored(ImGuiColors.DalamudOrange, Service.Lang.GetText("WorkTheory"));

        ImGuiOm.HelpMarker(Service.Lang.GetText("AutoNotifySPPlayers-WorkTheoryHelp"));

        var tableSize = new Vector2(ImGui.GetContentRegionAvail().X / 4 * 3, 0);
        if (ImGui.BeginTable("###AutoNotifySPPlayersTable", 2, ImGuiTableFlags.None, tableSize))
        {
            ImGui.TableSetupColumn("Lable", ImGuiTableColumnFlags.WidthStretch, 10);
            ImGui.TableSetupColumn("Content", ImGuiTableColumnFlags.WidthStretch, 60);

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text($"{Service.Lang.GetText("Name")}:");

            ImGui.TableNextColumn();
            ImGui.SetNextItemWidth(-1f);
            ImGui.InputTextWithHint("###NameInput", Service.Lang.GetText("AutoNotifySPPlayers-NameInputHint"),
                                    ref SelectName, 64);

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text($"{Service.Lang.GetText("AutoNotifySPPlayers-OnlineStatus")}:");

            ImGui.TableNextColumn();
            ImGui.SetNextItemWidth(-1f);
            if (ImGui.BeginCombo("###OnlineStatusCombo",
                                 Service.Lang.GetText("AutoNotifySPPlayers-SelectedEntryAmount",
                                                      SelectedOnlineStatus.Count), ImGuiComboFlags.HeightLarge))
            {
                foreach (var statusPair in OnlineStatuses)
                {
                    ImGui.PushID($"{statusPair.Value.Name.RawString}_{statusPair.Value.RowId}");
                    if (ImGuiOm.SelectableImageWithText(ImageHelper.GetIcon(statusPair.Value.Icon).ImGuiHandle,
                                                        ScaledVector2(20f),
                                                        statusPair.Value.Name.RawString,
                                                        SelectedOnlineStatus.Contains(statusPair.Key),
                                                        ImGuiSelectableFlags.DontClosePopups))
                    {
                        if (!SelectedOnlineStatus.Remove(statusPair.Key))
                            SelectedOnlineStatus.Add(statusPair.Key);
                    }

                    ImGui.PopID();
                }

                ImGui.EndCombo();
            }

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text($"{Service.Lang.GetText("Zone")}:");

            ImGui.TableNextColumn();
            ImGui.SetNextItemWidth(-1f);
            if (ImGui.BeginCombo("###ZoneCombo",
                                 Service.Lang.GetText("AutoNotifySPPlayers-SelectedEntryAmount", SelectedZone.Count),
                                 ImGuiComboFlags.HeightLarge))
            {
                ImGui.InputTextWithHint("###ZoneSearchInput", Service.Lang.GetText("PleaseSearch"),
                                        ref ZoneSearchInput, 32);

                ImGui.Separator();

                foreach (var zonePair in PresetData.Zones)
                {
                    var zoneName = zonePair.Value.PlaceName.Value.Name.RawString;
                    if (!string.IsNullOrWhiteSpace(ZoneSearchInput) && !zoneName.Contains(ZoneSearchInput))
                        continue;

                    ImGui.PushID($"{zonePair.Value.Name.RawString}_{zonePair.Value.RowId}");
                    if (ImGuiOm.Selectable(zoneName, SelectedZone.Contains(zonePair.Key),
                                           ImGuiSelectableFlags.DontClosePopups))
                    {
                        if (!SelectedZone.Remove(zonePair.Key))
                            SelectedZone.Add(zonePair.Key);
                    }

                    ImGui.PopID();
                }

                ImGui.EndCombo();
            }

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text($"{Service.Lang.GetText("AutoNotifySPPlayers-ExtraCommand")}:");

            ImGui.TableNextColumn();
            ImGui.SetNextItemWidth(-1f);
            ImGui.InputTextWithHint("###CommandInput",
                                    Service.Lang.GetText("AutoNotifySPPlayers-ExtraCommandInputHint"),
                                    ref SelectCommand, 64);

            ImGui.EndTable();
        }

        ImGui.SameLine();
        if (ImGuiOm.ButtonIconWithTextVertical(FontAwesomeIcon.Plus, Service.Lang.GetText("Add")))
        {
            if (!(string.IsNullOrWhiteSpace(SelectName) &&
                  SelectedOnlineStatus.Count <= 0 && SelectedZone.Count <= 0))
            {
                var preset = new NotifiedPlayers
                {
                    Name = SelectName,
                    OnlineStatus = SelectedOnlineStatus,
                    Zone = SelectedZone,
                    Command = SelectCommand,
                };

                ModuleConfig.NotifiedPlayer.Add(preset);
                SaveConfig(ModuleConfig);
            }
        }

        ImGui.Separator();
        ImGui.Separator();

        foreach (var config in ModuleConfig.NotifiedPlayer.ToArray())
        {
            ImGui.Selectable(Service.Lang.GetText("AutoNotifySPPlayers-DisplayInfo", config.Name,
                                                  string.Join(",", config.OnlineStatus.Take(3)),
                                                  string.Join(",", config.Zone.Take(3)), config.Command));

            if (ImGui.BeginPopupContextItem(
                    $"{config.Name}-{string.Join(",", config.OnlineStatus.Take(3))}-{string.Join(",", config.Zone.Take(3))}-{config.Command}"))
            {
                if (ImGuiOm.ButtonSelectable($"{Service.Lang.GetText("Delete")}"))
                {
                    ModuleConfig.NotifiedPlayer.Remove(config);
                    SaveConfig(ModuleConfig);
                }

                ImGui.EndPopup();
            }

            ImGui.Separator();
        }
    }

    private byte IsTargetableDetour(GameObject* potentialTarget)
    {
        var original = IsTargetableHook.Original(potentialTarget);

        var targetAddress = (nint)potentialTarget;
        if (Throttler.Throttle($"AutoNotifySPPlayers-{targetAddress}", 3000))
        {
            var currentTime = Environment.TickCount64;
            NoticeTimeInfo.TryAdd(targetAddress, currentTime);
            if (!NoticeTimeInfo.TryGetValue(targetAddress, out var lastNoticeTime))
            {
                NoticeTimeInfo[targetAddress] = currentTime;
                CheckGameObject(potentialTarget);
            }
            else
            {
                switch (currentTime - lastNoticeTime)
                {
                    case < 15000:
                        CheckGameObject(potentialTarget);
                        break;
                    case > 300000:
                        NoticeTimeInfo[targetAddress] = currentTime;
                        CheckGameObject(potentialTarget);
                        break;
                }
            }
        }

        return original;
    }

    private static void CheckGameObject(GameObject* obj)
    {
        if (ModuleConfig.NotifiedPlayer.Count == 0 ||
            Service.ClientState.LocalPlayer == null ||
            Flags.BetweenAreas) return;

        if ((ObjectKind)obj->ObjectKind != ObjectKind.Player || !obj->IsCharacter()) return;
        var chara = (Character*)obj;

        foreach (var config in ModuleConfig.NotifiedPlayer)
        {
            bool[] checks = [true, true, true];
            var playerName = obj->NameString;

            if (!string.IsNullOrWhiteSpace(config.Name))
            {
                try
                {
                    checks[0] = config.Name.StartsWith('/')
                                    ? new Regex(config.Name).IsMatch(playerName)
                                    : playerName == config.Name;
                }
                catch (ArgumentException)
                {
                    checks[0] = false;
                }
            }

            if (config.OnlineStatus.Count > 0)
                checks[1] = config.OnlineStatus.Contains(chara->CharacterData.OnlineStatus);

            if (config.Zone.Count > 0) checks[2] = config.Zone.Contains(Service.ClientState.TerritoryType);

            if (checks.All(x => x))
            {
                var message = Service.Lang.GetText("AutoNotifySPPlayers-NoticeMessage", playerName);
                Service.Chat.Print(message);
                WinToast.Notify(message, message);

                if (!string.IsNullOrWhiteSpace(config.Command)) ChatHelper.Instance.SendMessage(config.Command);
            }
        }
    }

    private class NotifiedPlayers
    {
        public string        Name         { get; set; } = string.Empty;
        public string        Command      { get; set; } = string.Empty;
        public HashSet<uint> Zone         { get; set; } = [];
        public HashSet<uint> OnlineStatus { get; set; } = [];
    }

    private class Config : ModuleConfiguration
    {
        public readonly List<NotifiedPlayers> NotifiedPlayer = [];
    }

    private delegate byte IsTargetableDelegate(GameObject* gameObj);
}
