using System.Net.Http;
using System.Threading.Tasks;
using DailyRoutines.Helpers;
using DailyRoutines.Infos;
using DailyRoutines.Managers;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using ImGuiNET;
using Newtonsoft.Json;

namespace DailyRoutines.Modules;

[ModuleDescription("ExpandPlayerMenuSearchTitle", "ExpandPlayerMenuSearchDescription", ModuleCategories.界面优化)]
public class ExpandPlayerMenuSearch : DailyModuleBase
{
    private const string RisingStoneSearchAPI =
        "https://apiff14risingstones.web.sdo.com/api/common/search?type=6&keywords={0}&page={1}&limit=50";

    private const string RisingStonePlayerInfo = "https://ff14risingstones.web.sdo.com/pc/index.html#/me/info?uuid={0}";
    private const string FFLogsSearch = "https://cn.fflogs.com/character/CN/{0}/{1}";
    private const string TiebaSearch = "https://tieba.baidu.com/f/search/res?ie=utf-8&kw=ff14&qw={0}";


    private static readonly MenuItem RisingStoneItem = new()
    {
        IsEnabled = true,
        IsReturn = false,
        UseDefaultPrefix = true,
        Name = new SeStringBuilder().Append(DRPrefix)
                                    .Append(Service.Lang.GetText("ExpandPlayerMenuSearch-RisingStoneSearch")).Build(),
        OnClicked = OnClickRisingStone,
        IsSubmenu = false,
        PrefixColor = 34,
    };

    private static readonly MenuItem FFLogsItem = new()
    {
        IsEnabled = true,
        IsReturn = false,
        UseDefaultPrefix = true,
        Name = new SeStringBuilder().Append(DRPrefix).Append(Service.Lang.GetText("ExpandPlayerMenuSearch-FFLogsSearch"))
                                    .Build(),
        OnClicked = OnClickFFLogs,
        IsSubmenu = false,
        PrefixColor = 34,
    };

    private static readonly MenuItem TiebaItem = new()
    {
        IsEnabled = true,
        IsReturn = false,
        UseDefaultPrefix = true,
        Name = new SeStringBuilder().Append(DRPrefix).Append(Service.Lang.GetText("ExpandPlayerMenuSearch-TiebaSearch"))
                                    .Build(),
        OnClicked = OnClickTieba,
        IsSubmenu = false,
        PrefixColor = 34,
    };

    private static readonly HttpClient client = new();
    private static CharacterSearchInfo? _TargetChara;

    private static bool RisingStoneEnabled;
    private static bool FFLogsEnabled;
    private static bool TiebaEnabled;

    public override void Init()
    {
        AddConfig("RisingStoneEnabled", true);
        RisingStoneEnabled = GetConfig<bool>("RisingStoneEnabled");

        AddConfig("FFLogsEnabled", true);
        FFLogsEnabled = GetConfig<bool>("FFLogsEnabled");

        AddConfig("TiebaEnabled", true);
        TiebaEnabled = GetConfig<bool>("TiebaEnabled");

        Service.ContextMenu.OnMenuOpened += OnMenuOpen;
    }

    public override void ConfigUI()
    {
        if (ImGui.Checkbox(Service.Lang.GetText("ExpandPlayerMenuSearch-RisingStoneSearch"),
                           ref RisingStoneEnabled))
            UpdateConfig("RisingStoneEnabled", RisingStoneEnabled);

        ImGuiOm.HelpMarker(Service.Lang.GetText("ExpandPlayerMenuSearch-RisingStoneSearchHelp"));

        if (ImGui.Checkbox(Service.Lang.GetText("ExpandPlayerMenuSearch-FFLogsSearch"), ref FFLogsEnabled))
            UpdateConfig("FFLogsEnabled", FFLogsEnabled);

        if (ImGui.Checkbox(Service.Lang.GetText("ExpandPlayerMenuSearch-TiebaSearch"), ref TiebaEnabled))
            UpdateConfig("TiebaEnabled", TiebaEnabled);
    }

    private static void OnMenuOpen(IMenuOpenedArgs args)
    {
        if (!IsValidAddon(args)) return;
        if (args.MenuType != ContextMenuType.Default) return;

        if (RisingStoneEnabled) args.AddMenuItem(RisingStoneItem);
        if (FFLogsEnabled) args.AddMenuItem(FFLogsItem);
        if (TiebaEnabled) args.AddMenuItem(TiebaItem);
    }

    private static void OnClickRisingStone(IMenuItemClickedArgs args)
    {
        Task.Run(async () =>
        {
            if (_TargetChara == null) return;

            var page = 1;
            var isFound = false;
            const int delayBetweenRequests = 1000;

            while (!isFound)
            {
                var url = string.Format(RisingStoneSearchAPI, _TargetChara.Name, page);
                var response = await client.GetStringAsync(url);
                var result = JsonConvert.DeserializeObject<FileFormat.RSPlayerSearchResult>(response);

                if (result.data.Count == 0)
                {
                    NotifyHelper.NotificationError(Service.Lang.GetText("ExpandPlayerMenuSearch-RisingStoneInfoNotFound"));
                    break;
                }

                foreach (var player in result.data)
                    if (player.character_name == _TargetChara.Name && player.group_name == _TargetChara.World)
                    {
                        var uuid = player.uuid;
                        Util.OpenLink(string.Format(RisingStonePlayerInfo, uuid));
                        isFound = true;
                        break;
                    }

                if (!isFound)
                {
                    NotifyHelper.NotificationInfo(Service.Lang.GetText("ExpandPlayerMenuSearch-NextPageMessage", 0));
                    await Task.Delay(delayBetweenRequests);
                    page++;
                }
                else break;
            }
        });
    }

    private static void OnClickFFLogs(IMenuItemClickedArgs args)
    {
        if (_TargetChara == null) return;
        Util.OpenLink(string.Format(FFLogsSearch, _TargetChara.World, _TargetChara.Name));
    }

    private static void OnClickTieba(IMenuItemClickedArgs args)
    {
        if (_TargetChara == null) return;
        Util.OpenLink(string.Format(TiebaSearch, $"{_TargetChara.Name}@{_TargetChara.World}"));
    }

    private static unsafe bool IsValidAddon(IMenuArgs args)
    {
        if (args.Target is MenuTargetInventory) return false;
        var menuTarget = (MenuTargetDefault)args.Target;
        var agent = Service.Gui.FindAgentInterface("ChatLog");
        if (agent != nint.Zero && *(uint*)(agent + 0x948 + 8) == 3) return false;

        var judgeCriteria0 = menuTarget.TargetCharacter != null;
        var judgeCriteria1 = !string.IsNullOrWhiteSpace(menuTarget.TargetName) &&
                             menuTarget.TargetHomeWorld.GameData != null &&
                             menuTarget.TargetHomeWorld.GameData.RowId != 0;

        var judgeCriteria2 = menuTarget.TargetObject is ICharacter && judgeCriteria1;

        switch (args.AddonName)
        {
            default:
                return false;
            case "BlackList":
                var agentBlackList = AgentBlacklist.Instance();

                if ((nint)agentBlackList != nint.Zero && agentBlackList->AgentInterface.IsAgentActive())
                {
                    var playerName = agentBlackList->SelectedPlayerName.ExtractText();
                    var serverName = agentBlackList->SelectedPlayerFullName.ExtractText()
                                                                           .TrimStart(playerName.ToCharArray());

                    _TargetChara = new() { Name = playerName, World = serverName };
                    return true;
                }

                return false;
            case "FreeCompany":
                if (menuTarget.TargetContentId == 0) return false;

                _TargetChara = new() { Name = menuTarget.TargetName, World = menuTarget.TargetHomeWorld.GameData.Name };
                return true;
            case "LinkShell":
            case "CrossWorldLinkshell":
                return menuTarget.TargetContentId != 0 && GeneralJudge();
            case null:
            case "ChatLog":
            case "LookingForGroup":
            case "PartyMemberList":
            case "FriendList":
            case "SocialList":
            case "ContactList":
            case "_PartyList":
            case "BeginnerChatList":
            case "ContentMemberList":
                return GeneralJudge();
        }

        bool GeneralJudge()
        {
            if (judgeCriteria0)
                _TargetChara = menuTarget.TargetCharacter.ToCharacterSearchInfo();
            else if (menuTarget.TargetObject is ICharacter chara && judgeCriteria1)
                _TargetChara = chara.ToCharacterSearchInfo();
            else if (judgeCriteria1)
            {
                _TargetChara = new()
                    { Name = menuTarget.TargetName, World = menuTarget.TargetHomeWorld.GameData.Name.RawString };
            }

            return judgeCriteria0 || judgeCriteria2 || judgeCriteria1;
        }
    }

    public override void Uninit()
    {
        Service.ContextMenu.OnMenuOpened -= OnMenuOpen;

        base.Uninit();
    }

    public class CharacterSearchInfo
    {
        public string Name  { get; set; } = null!;
        public string World { get; set; } = null!;
    }
}
