using System;
using System.Collections.Generic;
using System.Linq;
using DailyRoutines.Helpers;
using DailyRoutines.Infos;
using DailyRoutines.Managers;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Memory;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ImGuiNET;
using Lumina.Excel.GeneratedSheets;

namespace DailyRoutines.Modules;

[ModuleDescription("AutoPlayerCommendTitle", "AutoPlayerCommendDescription", ModuleCategories.战斗)]
public unsafe class AutoPlayerCommend : DailyModuleBase
{
    private static World? SelectedWorld;
    private static ContentFinderCondition? SelectedContent;

    private static string WorldSearchInput = string.Empty;
    private static string PlayerNameInput = string.Empty;
    private static string ContentSearchInput = string.Empty;

    private static bool IsNeedToCommend;

    private Config? ModuleConfig;

    public override void Init()
    {
        ModuleConfig = LoadConfig<Config>() ?? new();

        TaskHelper ??= new TaskHelper { AbortOnTimeout = true, TimeLimitMS = 10000, ShowDebug = false };
        Service.DutyState.DutyCompleted += OnDutyComplete;

        Service.AddonLifecycle.RegisterListener(AddonEvent.PostDraw, "VoteMvp", OnAddonList);
        Service.AddonLifecycle.RegisterListener(AddonEvent.PostDraw, "BannerMIP", OnAddonList);
    }

    public override void ConfigUI()
    {
        ImGui.AlignTextToFramePadding();
        ImGui.Text($"{Service.Lang.GetText("AutoPlayerCommend-BlacklistPlayers")}:");

        ImGui.SameLine();
        ImGui.SetNextItemWidth(200f * GlobalFontScale);
        if (ImGui.BeginCombo("###BlacklistPlayerInfoCombo",
                             Service.Lang.GetText("AutoPlayerCommend-BlacklistPlayersAmount",
                                                  ModuleConfig.BlacklistPlayers.Count), ImGuiComboFlags.HeightLarge))
        {
            ImGui.BeginGroup();
            ImGui.AlignTextToFramePadding();
            ImGui.Text($"{Service.Lang.GetText("AutoPlayerCommend-World")}:");

            ImGui.SameLine();
            ImGui.SetNextItemWidth(120f * GlobalFontScale);
            CNWorldSelectCombo(ref SelectedWorld, ref WorldSearchInput);

            ImGui.AlignTextToFramePadding();
            ImGui.Text($"{Service.Lang.GetText("AutoPlayerCommend-PlayerName")}:");

            ImGui.SameLine();
            ImGui.SetNextItemWidth(120f * GlobalFontScale);
            ImGui.InputText("###PlayerNameInput", ref PlayerNameInput, 100);
            ImGui.EndGroup();

            ImGui.SameLine();
            if (ImGuiOm.ButtonIconWithTextVertical(FontAwesomeIcon.Plus, Service.Lang.GetText("Add")))
            {
                if (SelectedWorld == null || string.IsNullOrWhiteSpace(PlayerNameInput)) return;
                var info = new PlayerInfo(PlayerNameInput, SelectedWorld.RowId);
                if (ModuleConfig.BlacklistPlayers.Add(info))
                    SaveConfig(ModuleConfig);
            }

            ImGui.SameLine();
            if (ImGuiOm.ButtonIconWithTextVertical(FontAwesomeIcon.Sync,
                                                   Service.Lang.GetText("AutoPlayerCommend-SyncBlacklist")))
            {
                var blacklist =
                    GetBlacklistInfo(
                        (InfoProxyBlacklist*)InfoModule.Instance()->GetInfoProxyById(InfoProxyId.Blacklist));

                foreach (var player in blacklist)
                    ModuleConfig.BlacklistPlayers.Add(player);

                SaveConfig(ModuleConfig);
            }

            ImGuiOm.TooltipHover(Service.Lang.GetText("AutoPlayerCommend-SyncBlacklistHelp"));

            ImGui.Separator();
            ImGui.Separator();

            foreach (var player in ModuleConfig.BlacklistPlayers)
            {
                if (!PresetData.TryGetCNWorld(player.WorldID, out var world)) continue;
                ImGui.Selectable($"{world.Name.RawString} / {player.PlayerName}");

                if (ImGui.BeginPopupContextItem($"DeleteBlacklistPlayer_{player.PlayerName}_{player.WorldID}"))
                {
                    if (ImGui.Selectable(Service.Lang.GetText("Delete")))
                    {
                        ModuleConfig.BlacklistPlayers.Remove(player);
                        SaveConfig(ModuleConfig);
                    }

                    ImGui.EndPopup();
                }
            }

            ImGui.EndCombo();
        }

        ImGui.AlignTextToFramePadding();
        ImGui.Text($"{Service.Lang.GetText("AutoPlayerCommend-BlacklistContents")}:");

        ImGui.SameLine();
        ImGui.SetNextItemWidth(200f * GlobalFontScale);

        if (ImGui.BeginCombo("###BlacklistContentsCombo",
                             Service.Lang.GetText("AutoPlayerCommend-BlacklistContentsAmount",
                                                  ModuleConfig.BlacklistContents.Count)))
        {
            ImGui.AlignTextToFramePadding();
            ImGui.Text($"{Service.Lang.GetText("Content")}:");

            ImGui.SameLine();
            ImGui.SetNextItemWidth(200f * GlobalFontScale);
            ContentSelectCombo(ref SelectedContent, ref ContentSearchInput);

            ImGui.SameLine();
            if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Plus, Service.Lang.GetText("Add")))
            {
                if (SelectedContent != null)
                {
                    var info = new ContentInfo
                        { ContentID = SelectedContent.RowId, TerritoryID = SelectedContent.TerritoryType.Row };

                    if (ModuleConfig.BlacklistContents.Add(info))
                        SaveConfig(ModuleConfig);
                }
            }

            ImGui.Separator();
            ImGui.Separator();

            foreach (var contentInfo in ModuleConfig.BlacklistContents)
            {
                if (!LuminaCache.TryGetRow<ContentFinderCondition>(contentInfo.ContentID, out var content)) continue;
                ImGui.Selectable($"{content.Name.RawString}");

                if (ImGui.BeginPopupContextItem(
                        $"DeleteBlacklistContent_{contentInfo.ContentID}_{contentInfo.TerritoryID}"))
                {
                    if (ImGui.Selectable(Service.Lang.GetText("Delete")))
                    {
                        if (ModuleConfig.BlacklistContents.Remove(contentInfo))
                            SaveConfig(ModuleConfig);
                    }

                    ImGui.EndPopup();
                }
            }

            ImGui.EndCombo();
        }
    }

    private void OnDutyComplete(object? sender, ushort dutyZoneID)
    {
        IsNeedToCommend = false;
        if (InterruptByConflictKey()) return;
        if (ModuleConfig.BlacklistContents.FirstOrDefault(x => x.TerritoryID == dutyZoneID) is not null) return;

        TaskHelper.Enqueue(OpenCommendWindow);
    }

    private static bool? OpenCommendWindow()
    {
        if (Flags.OccupiedInEvent) return false;

        var notification = (AtkUnitBase*)Service.Gui.GetAddonByName("_Notification");
        var notificationMvp = (AtkUnitBase*)Service.Gui.GetAddonByName("_NotificationIcMvp");
        if (notification == null && notificationMvp == null) return true;

        IsNeedToCommend = true;
        AddonHelper.Callback(notification, true, 0, 11);
        return true;
    }

    private void ProcessCommendation(string addonName, int voteOffset, int nameOffset, int callbackIndex)
    {
        TaskHelper.Abort();

        var localPlayer = Service.ClientState.LocalPlayer;
        var localPlayerInfo = new PlayerInfo(localPlayer.Name.ExtractText(), localPlayer.HomeWorld.GameData.RowId)
        {
            JobID = localPlayer.ClassJob.GameData.RowId,
            Role = GetCharacterJobRole(localPlayer.ClassJob.GameData.Role),
        };

        var allies = Service.PartyList.Select(x => new PlayerInfo(x.Name.ExtractText(), x.World.GameData.RowId)
                            {
                                Role = GetCharacterJobRole(x.ClassJob.GameData.Role),
                                JobID = x.ClassJob.GameData.RowId,
                            })
                            .Where(x => x != localPlayerInfo && !ModuleConfig.BlacklistPlayers.Contains(x)).ToList();

        if (allies.Count == 0) return;
        var playersToCommend = allies
                               .OrderByDescending(player => player.Role == localPlayerInfo.Role)
                               .ThenByDescending(player =>
                               {
                                   return localPlayerInfo.Role switch
                                   {
                                       PlayerRole.Tank or PlayerRole.Healer => player.Role is PlayerRole.Tank
                                                                                   or PlayerRole.Healer
                                                                                   ? 1
                                                                                   : 0,
                                       PlayerRole.DPS => player.Role switch
                                       {
                                           PlayerRole.DPS => 3,
                                           PlayerRole.Healer => 2,
                                           PlayerRole.Tank => 1,
                                           _ => 0,
                                       },
                                       _ => 0,
                                   };
                               });

        if (TryGetAddonByName<AtkUnitBase>(addonName, out var addon) && IsAddonAndNodesReady(addon))
        {
            foreach (var player in playersToCommend)
                for (var i = 0; i < allies.Count; i++)
                    if (Convert.ToBoolean(addon->AtkValues[i + voteOffset].Byte))
                    {
                        var playerNameInAddon =
                            MemoryHelper.ReadStringNullTerminated((nint)addon->AtkValues[i + nameOffset].String);

                        if (playerNameInAddon == player.PlayerName)
                        {
                            AddonHelper.Callback(addon, true, callbackIndex, i);
                            var job = LuminaCache.GetRow<ClassJob>(player.JobID);
                            var message = new SeStringBuilder().Append(DRPrefix).Append(" ")
                                                               .Append(Service.Lang.GetSeString(
                                                                           "AutoPlayerCommend-NoticeMessage",
                                                                           job.ToBitmapFontIcon(), job.Name.RawString,
                                                                           player.PlayerName)).Build();

                            Service.Chat.Print(message);
                            return;
                        }
                    }
        }
    }

    private void OnAddonList(AddonEvent type, AddonArgs args)
    {
        if (!IsNeedToCommend) return;

        switch (args.AddonName)
        {
            case "VoteMvp":
                ProcessCommendation("VoteMvp", 16, 9, 0);
                break;
            case "BannerMIP":
                ProcessCommendation("BannerMIP", 29, 22, 12);
                break;
        }

        IsNeedToCommend = false;
    }

    private static PlayerRole GetCharacterJobRole(byte rawRole)
    {
        return rawRole switch
        {
            1 => PlayerRole.Tank,
            2 or 3 => PlayerRole.DPS,
            4 => PlayerRole.Healer,
            _ => PlayerRole.None,
        };
    }

    private static List<PlayerInfo> GetBlacklistInfo(InfoProxyBlacklist* blacklist)
    {
        var list = new List<PlayerInfo>();
        var stringArray = (nint*)AtkStage.Instance()->AtkArrayDataHolder->StringArrays[14]->StringArray;
        for (var num = 0u; num < blacklist->InfoProxyPageInterface.InfoProxyInterface.EntryCount; num++)
        {
            var playerName = MemoryHelper.ReadStringNullTerminated(stringArray[num]);
            var worldName = MemoryHelper.ReadStringNullTerminated(stringArray[200 + num]);
            var world = PresetData.CNWorlds.Values.FirstOrDefault(x => x.Name.RawString == worldName);
            if (world == null || string.IsNullOrWhiteSpace(playerName)) continue;

            var player = new PlayerInfo(playerName, world.RowId);
            list.Add(player);
        }

        return list;
    }

    public override void Uninit()
    {
        Service.AddonLifecycle.UnregisterListener(OnAddonList);
        Service.DutyState.DutyCompleted -= OnDutyComplete;

        base.Uninit();
    }

    private enum PlayerRole
    {
        Tank,
        Healer,
        DPS,
        None,
    }

    private class PlayerInfo : IEquatable<PlayerInfo>
    {
        public PlayerInfo() { }

        public PlayerInfo(string name, uint world)
        {
            PlayerName = name;
            WorldID = world;
        }

        public string      PlayerName { get; set; } = string.Empty;
        public uint        WorldID    { get; set; }
        public PlayerRole? Role       { get; set; } = PlayerRole.None;
        public uint        JobID      { get; set; }

        public bool Equals(PlayerInfo? other)
        {
            if (other is null || GetType() != other.GetType())
                return false;

            return PlayerName == other.PlayerName && WorldID == other.WorldID;
        }

        public override bool Equals(object? obj) { return Equals(obj as PlayerInfo); }

        public override int GetHashCode() { return HashCode.Combine(PlayerName, WorldID); }

        public static bool operator ==(PlayerInfo? lhs, PlayerInfo? rhs)
        {
            if (lhs is null) return rhs is null;
            return lhs.Equals(rhs);
        }

        public static bool operator !=(PlayerInfo lhs, PlayerInfo rhs) { return !(lhs == rhs); }
    }

    private class ContentInfo : IEquatable<ContentInfo>
    {
        public uint ContentID   { get; set; }
        public uint TerritoryID { get; set; }

        public bool Equals(ContentInfo? other)
        {
            if (other is null || GetType() != other.GetType())
                return false;

            return ContentID == other.ContentID && TerritoryID == other.TerritoryID;
        }

        public override bool Equals(object? obj) { return Equals(obj as ContentInfo); }

        public override int GetHashCode() { return HashCode.Combine(ContentID, TerritoryID); }

        public static bool operator ==(ContentInfo? lhs, ContentInfo? rhs)
        {
            if (lhs is null) return rhs is null;
            return lhs.Equals(rhs);
        }

        public static bool operator !=(ContentInfo lhs, ContentInfo rhs) { return !(lhs == rhs); }
    }

    private class Config : ModuleConfiguration
    {
        public HashSet<ContentInfo> BlacklistContents = [];
        public HashSet<PlayerInfo> BlacklistPlayers = [];
    }
}
