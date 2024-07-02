using System.Collections.Generic;
using System.Runtime.InteropServices;
using DailyRoutines.Helpers;
using DailyRoutines.Infos;
using DailyRoutines.Managers;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel.GeneratedSheets;

namespace DailyRoutines.Modules;

[ModuleDescription("AutoCheckItemLevelTitle", "AutoCheckItemLevelDescription", ModuleCategories.战斗)]
public unsafe class AutoCheckItemLevel : DailyModuleBase
{
    private static readonly HashSet<uint> ValidContentJobCategories = [108, 142, 146];
    private static readonly HashSet<uint> HaveOffHandJobCategories = [2, 7, 8, 20];

    private static HudPartyMember? CurrentMember;

    public override void Init()
    {
        TaskHelper ??= new TaskHelper { AbortOnTimeout = true, TimeLimitMS = 20000 };

        Service.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "CharacterInspect", OnAddonInspect);
        Service.ClientState.TerritoryChanged += OnZoneChanged;
    }

    private void OnZoneChanged(ushort zone)
    {
        CurrentMember = null;

        if (Service.ClientState.IsPvP) return;
        if (!PresetData.TryGetContent(zone, out var content) || content.PvP ||
            !ValidContentJobCategories.Contains(content.AcceptClassJobCategory.Row)) return;

        NotifyHelper.Chat(new SeStringBuilder().Append($"{Service.Lang.GetText("AutoCheckItemLevel-ILRequired")}: ")
                                               .AddUiForeground(content.ItemLevelRequired.ToString(), 34).Build());

        TaskHelper.Enqueue(() => !Flags.BetweenAreas && IsScreenReady() && Service.ClientState.LocalPlayer != null,
                           "WaitForEnteringDuty", 2);

        TaskHelper.Enqueue(CheckMembersItemLevel);
    }

    private bool? CheckMembersItemLevel()
    {
        if (Service.PartyList.Length == 0)
        {
            TaskHelper.Abort();
            return true;
        }

        var pfArray = AgentHUD.Instance()->PartyMembers.ToArray();
        foreach (var member in pfArray)
        {
            if (member.Object->GetGameObjectId().ObjectId == Service.ClientState.LocalPlayer.GameObjectId) continue;

            TaskHelper.Enqueue(() =>
            {
                if (!Throttler.Throttle("AutoCheckItemLevel-WaitExamineUI", 1000)) return false;

                CurrentMember ??= member;
                AgentInspect.Instance()->ExamineCharacter(member.Object->GetGameObjectId().ObjectId);
                return AddonState.CharacterInspect != null;
            });

            TaskHelper.DelayNext($"Delay_ForeachPF_{member.Object->GetGameObjectId().ObjectId}", 1000);
        }

        return true;
    }

    private void OnAddonInspect(AddonEvent type, AddonArgs args)
    {
        if (AddonState.CharacterInspect == null) return;
        if (CurrentMember == null) return;

        var member = CurrentMember.Value;
        TaskHelper.DelayNext("Delay_AddonInspect", 50, false, 2);
        TaskHelper.Enqueue(() =>
        {
            var container = InventoryManager.Instance()->GetInventoryContainer(InventoryType.Examine);
            if (container == null || container->Loaded != 1)
            {
                AgentInspect.Instance()->ExamineCharacter(member.Object->GetGameObjectId().ObjectId);
                return false;
            }

            uint totalIL = 0U, lowestIL = uint.MaxValue;
            var itemSlotAmount = 11;
            for (var i = 0; i < 13; i++)
            {
                switch (i)
                {
                    case 0:
                    {
                        var mainHand = LuminaCache.GetRow<Item>(container->GetInventorySlot(i)->ItemId);
                        var category = mainHand.ClassJobCategory.Row;
                        if (HaveOffHandJobCategories.Contains(category))
                            itemSlotAmount++;

                        break;
                    }
                    case 1 when itemSlotAmount != 12:
                    case 5: // 腰带
                        continue;
                }

                var slot = container->GetInventorySlot(i);
                if (slot == null) continue;

                var itemID = slot->ItemId;
                var item = LuminaCache.GetRow<Item>(itemID);

                if (item.LevelItem.Row < lowestIL)
                    lowestIL = item.LevelItem.Row;

                totalIL += item.LevelItem.Row;
            }

            var avgItemLevel = totalIL / itemSlotAmount;

            var content = PresetData.Contents[Service.ClientState.TerritoryType];
            var ssb = new SeStringBuilder();
            ssb.AddUiForeground(25);
            ssb.Add(new PlayerPayload(Marshal.PtrToStringUTF8((nint)member.Name), member.Object->Character.HomeWorld));
            ssb.AddUiForegroundOff();
            ssb.Append(
                $" ({LuminaCache.GetRow<ClassJob>(member.Object->Character.CharacterData.ClassJob).Name.RawString})");

            ssb.Append($" {Service.Lang.GetText("Level")}: ").AddUiForeground(
                member.Object->Character.CharacterData.Level.ToString(),
                (ushort)(member.Object->Character.CharacterData.Level >= content.ClassJobLevelSync ? 43 : 17));

            ssb.Add(new NewLinePayload());
            ssb.Append($" {Service.Lang.GetText("AutoCheckItemLevel-ILAverage")}: ")
               .AddUiForeground(avgItemLevel.ToString(), (ushort)(avgItemLevel > content.ItemLevelSync ? 43 : 17));

            ssb.Append($" {Service.Lang.GetText("AutoCheckItemLevel-ILMinimum")}: ")
               .AddUiForeground(lowestIL.ToString(), (ushort)(lowestIL > content.ItemLevelRequired ? 43 : 17));

            ssb.Add(new NewLinePayload());

            NotifyHelper.Chat(ssb.Build());
            AgentInspect.Instance()->AgentInterface.Hide();

            CurrentMember = null;
            return true;
        }, "CheckMemberIL", 2);
    }

    public override void Uninit()
    {
        Service.ClientState.TerritoryChanged -= OnZoneChanged;
        Service.AddonLifecycle.UnregisterListener(OnAddonInspect);

        base.Uninit();
    }
}
