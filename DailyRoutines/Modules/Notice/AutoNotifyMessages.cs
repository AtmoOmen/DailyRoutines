using System;
using System.Collections.Generic;
using DailyRoutines.Managers;
using DailyRoutines.Notifications;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using ImGuiNET;

namespace DailyRoutines.Modules;

[ModuleDescription("AutoNotifyMessagesTitle", "AutoNotifyMessagesDescription", ModuleCategories.通知)]
public class AutoNotifyMessages : DailyModuleBase
{
    private static bool ConfigOnlyNotifyWhenBackground;
    private static bool ConfigBlockOwnMessages;
    private static HashSet<XivChatType> ConfigValidChatTypes = [];

    private static string SearchChatTypesContent = string.Empty;

    private static readonly Dictionary<XivChatType, string> ChatTypesLoc = new()
    {
        { XivChatType.Notice, "系统通知" },
        { XivChatType.SystemError, "系统错误" },
        { XivChatType.SystemMessage, "系统消息" },
        { XivChatType.ErrorMessage, "错误消息" },
        { XivChatType.RetainerSale, "雇员出售信息" },
        { XivChatType.Say, "说话" },
        { XivChatType.Yell, "呼喊" },
        { XivChatType.Shout, "喊话" },
        { XivChatType.TellIncoming, "悄悄话" },
        { XivChatType.Party, "小队" },
        { XivChatType.CrossParty, "跨服小队" },
        { XivChatType.Alliance, "团队" },
        { XivChatType.FreeCompany, "部队" },
        { XivChatType.PvPTeam, "战队" },
        { XivChatType.Echo, "默语" },
        { XivChatType.NoviceNetwork, "新人频道" },
        { XivChatType.StandardEmote, "情感动作" },
        { XivChatType.CustomEmote, "自定义情感动作" },
        { XivChatType.Ls1, "通讯贝1" },
        { XivChatType.Ls2, "通讯贝2" },
        { XivChatType.Ls3, "通讯贝3" },
        { XivChatType.Ls4, "通讯贝4" },
        { XivChatType.Ls5, "通讯贝5" },
        { XivChatType.Ls6, "通讯贝6" },
        { XivChatType.Ls7, "通讯贝7" },
        { XivChatType.Ls8, "通讯贝8" },
        { XivChatType.CrossLinkShell1, "跨服贝1" },
        { XivChatType.CrossLinkShell2, "跨服贝2" },
        { XivChatType.CrossLinkShell3, "跨服贝3" },
        { XivChatType.CrossLinkShell4, "跨服贝4" },
        { XivChatType.CrossLinkShell5, "跨服贝5" },
        { XivChatType.CrossLinkShell6, "跨服贝6" },
        { XivChatType.CrossLinkShell7, "跨服贝7" },
        { XivChatType.CrossLinkShell8, "跨服贝8" },
    };

    public override void Init()
    {
        AddConfig("OnlyNotifyWhenBackground", true);
        ConfigOnlyNotifyWhenBackground = GetConfig<bool>("OnlyNotifyWhenBackground");

        AddConfig("ValidChatTypes", new HashSet<XivChatType> { XivChatType.TellIncoming });
        ConfigValidChatTypes = GetConfig<HashSet<XivChatType>>("ValidChatTypes");

        AddConfig("BlockOwnMessages", true);
        ConfigBlockOwnMessages = GetConfig<bool>("BlockOwnMessages");

        Service.Chat.ChatMessage += OnChatMessage;
    }

    public override void ConfigUI()
    {
        PreviewImageWithHelpText(Service.Lang.GetText("AutoNotifyMessages-NotificationMessageHelp"),
                                 "https://gh.atmoomen.top/DailyRoutines/main/Assets/Images/AutoNotifyMessages-1.png");

        if (ImGui.Checkbox(Service.Lang.GetText("OnlyNotifyWhenBackground"),
                           ref ConfigOnlyNotifyWhenBackground))
            UpdateConfig("OnlyNotifyWhenBackground", ConfigOnlyNotifyWhenBackground);

        if (ImGui.Checkbox(Service.Lang.GetText("AutoNotifyMessages-BlockOwnMessages"), ref ConfigBlockOwnMessages))
            UpdateConfig("BlockOwnMessages", ConfigBlockOwnMessages);

        ImGui.SetNextItemWidth(300f * GlobalFontScale);
        if (ImGui.BeginCombo("###SelectChatTypesCombo",
                             Service.Lang.GetText("AutoNotifyMessages-SelectedTypesAmount", ConfigValidChatTypes.Count),
                             ImGuiComboFlags.HeightLarge))
        {
            ImGui.SetNextItemWidth(-1f);
            ImGui.InputTextWithHint("###ChatTypeSelectInput", $"{Service.Lang.GetText("PleaseSearch")}...",
                                    ref SearchChatTypesContent, 50);

            ImGui.Separator();

            foreach (var chatType in ChatTypesLoc)
            {
                if (!string.IsNullOrEmpty(SearchChatTypesContent) &&
                    !chatType.Value.Contains(SearchChatTypesContent, StringComparison.OrdinalIgnoreCase)) continue;

                var existed = ConfigValidChatTypes.Contains(chatType.Key);
                if (ImGui.Checkbox(chatType.Value, ref existed))
                {
                    if (!ConfigValidChatTypes.Remove(chatType.Key))
                        ConfigValidChatTypes.Add(chatType.Key);

                    UpdateConfig("ValidChatTypes", ConfigValidChatTypes);
                }
            }

            ImGui.EndCombo();
        }
    }

    private static unsafe void OnChatMessage(XivChatType type, int timestamp, ref SeString sender, ref SeString message, ref bool ishandled)
    {
        if (!ConfigValidChatTypes.Contains(type)) return;

        var locState = ChatTypesLoc.TryGetValue(type, out var prefix);
        var isSendByOwn = sender.ExtractText().Contains(Service.ClientState.LocalPlayer?.Name.ExtractText());
        if (ConfigOnlyNotifyWhenBackground && !Framework.Instance()->WindowInactive &&
            !(ConfigBlockOwnMessages && isSendByOwn))
            WinToast.Notify($"[{(locState ? prefix : type)}]  {sender.ExtractText()}", message.ExtractText());
    }

    public override void Uninit()
    {
        Service.Chat.ChatMessage -= OnChatMessage;

        base.Uninit();
    }
}
