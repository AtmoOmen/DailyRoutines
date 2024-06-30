using System;
using System.Collections.Generic;
using System.Linq;
using DailyRoutines.Helpers;
using DailyRoutines.Infos;
using DailyRoutines.Managers;
using Dalamud.Game.ClientState.JobGauge.Enums;
using Dalamud.Game.ClientState.JobGauge.Types;
using Dalamud.Game.ClientState.Party;
using Dalamud.Game.Text.SeStringHandling;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using ImGuiNET;
using Action = Lumina.Excel.GeneratedSheets.Action;

namespace DailyRoutines.Modules;

[ModuleDescription("AutoPlayCardsTitle", "AutoPlayCardsDescription", ModuleCategories.技能)]
public unsafe class AutoPlayCards : DailyModuleBase
{
    private static readonly HashSet<uint> CardStatuses = [1882, 1883, 1884, 1885, 1886, 1887];

    private static readonly Dictionary<CardType, (bool IsMelee, string Name)> Cards = new()
    {
        { CardType.BALANCE, (true, LuminaCache.GetRow<Action>(4401).Name.RawString) },
        { CardType.ARROW, (true, LuminaCache.GetRow<Action>(4402).Name.RawString) },
        { CardType.SPEAR, (true, LuminaCache.GetRow<Action>(4403).Name.RawString) },
        { CardType.BOLE, (false, LuminaCache.GetRow<Action>(4403).Name.RawString) },
        { CardType.EWER, (false, LuminaCache.GetRow<Action>(4403).Name.RawString) },
        { CardType.SPIRE, (false, LuminaCache.GetRow<Action>(4403).Name.RawString) },
    };

    private static bool SendMessage = true;
    private static bool UseAantonomasia;
    private static SendInfo? CardSendInfo;

    public override void Init()
    {
        AddConfig("SendMessage", SendMessage);
        SendMessage = GetConfig<bool>("SendMessage");

        AddConfig("UseAantonomasia", UseAantonomasia);
        UseAantonomasia = GetConfig<bool>("UseAantonomasia");

        Service.UseActionManager.Register(OnPreUseAction);
        Service.UseActionManager.Register(OnPostUseAction);
    }

    public override void ConfigUI()
    {
        if (ImGui.Checkbox(Service.Lang.GetText("AutoPlayCards-SendMessage"), ref SendMessage))
            UpdateConfig("SendMessage", SendMessage);

        if (SendMessage)
        {
            ImGui.Indent();
            if (ImGui.Checkbox(Service.Lang.GetText("AutoPlayCards-UseAantonomasia"), ref UseAantonomasia))
                UpdateConfig("UseAantonomasia", UseAantonomasia);

            ImGuiOm.HelpMarker(Service.Lang.GetText("AutoPlayCards-UseAantonomasiaHelp"));
            ImGui.Unindent();
        }
    }

    private static void OnPreUseAction(
        ref bool isPrevented,
        ref ActionType actionType, ref uint actionID, ref ulong targetID, ref uint a4,
        ref uint queueState, ref uint a6)
    {
        if (actionType != ActionType.Action || actionID != 17055) return;

        var localPlayer = Service.ClientState.LocalPlayer;
        var drawnCard = Service.JobGauges.Get<ASTGauge>().DrawnCard;

        if (!Cards.TryGetValue(drawnCard, out var cardInfo)) return;

        var indices = Enumerable.Range(0, Service.PartyList.Count)
                                .Where(i => Service.PartyList[i].GameObject != null &&
                                            Service.PartyList[i].GameObject.IsValid() &&
                                            Service.PartyList[i].GameObject.IsTargetable &&
                                            !Service.PartyList[i].GameObject.IsDead &&
                                            !Service.PartyList[i].Statuses
                                                    .Any(s => CardStatuses.Contains(s.StatusId)) &&
                                            GetGameDistanceFromObject((GameObject*)localPlayer.Address,
                                                                      (GameObject*)Service.PartyList[i].GameObject
                                                                          .Address) <= 30)
                                .ToList();

        IPartyMember? member = null;
        if (indices.Count > 0)
        {
            var rnd = new Random();
            for (var i = indices.Count - 1; i >= 0; i--)
            {
                var j = rnd.Next(i + 1);
                (indices[i], indices[j]) = (indices[j], indices[i]);
            }

            var firstSortedIndex =
                indices.OrderByDescending(index => Service.PartyList[index].ClassJob.GameData.Role is 2 or 3 ? 1 : 0)
                       .ThenByDescending(index =>
                                             Service.PartyList[index].ClassJob.GameData.Role is 1 or 2
                                                 ? cardInfo.IsMelee ? 1 : 0
                                                 : cardInfo.IsMelee
                                                     ? 0
                                                     : 1
                       ).First();

            member = Service.PartyList[firstSortedIndex];
        }

        if (member == null) return;

        targetID = member.ObjectId;
        if (SendMessage) CardSendInfo ??= new(member, cardInfo);
    }

    private static void OnPostUseAction(
        bool result, ActionType actionType, uint actionID, ulong targetID, uint a4, uint queueState, uint a6)
    {
        if (actionType != ActionType.Action || actionID != 17055 || !result || !SendMessage) return;
        if (CardSendInfo == null) return;

        string jobNameText = CardSendInfo.member.ClassJob.GameData.Name.ExtractText(),
               memberNameText = CardSendInfo.member.Name.TextValue;

        var message = new SeStringBuilder().Append(DRPrefix).Append(" ").Append(
            Service.Lang.GetSeString("AutoPlayCards-Message",
                                     UseAantonomasia
                                         ? CardSendInfo.card.IsMelee
                                               ? Service.Lang.GetText("AutoPlayCards-MeleeCard")
                                               : Service.Lang.GetText("AutoPlayCards-RangeCard")
                                         : CardSendInfo.card.Name,
                                     CardSendInfo.member.ClassJob.GameData.ToBitmapFontIcon(), jobNameText,
                                     memberNameText)).Build();

        Service.Chat.Print(message);
        CardSendInfo = null;
    }

    public override void Uninit()
    {
        Service.UseActionManager.Unregister(OnPreUseAction);
        Service.UseActionManager.Unregister(OnPostUseAction);
        CardSendInfo = null;

        base.Uninit();
    }

    private record SendInfo(IPartyMember member, (bool IsMelee, string Name) card);
}
