using System.Collections.Generic;
using System.Linq;
using DailyRoutines.Helpers;
using DailyRoutines.Infos;
using DailyRoutines.Managers;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface.Colors;
using FFXIVClientStructs.FFXIV.Client.Game;
using ImGuiNET;

namespace DailyRoutines.Modules;

[ModuleDescription("AutoPreventDuplicateStatusTitle", "AutoPreventDuplicateStatusDescription", ModuleCategories.技能)]
public unsafe class AutoPreventDuplicateStatus : DailyModuleBase
{
    private static readonly Dictionary<DetectType, string> DetectTypeLoc = new()
    {
        { DetectType.Self, Service.Lang.GetText("AutoPreventDuplicateStatus-Self") },
        { DetectType.Member, Service.Lang.GetText("AutoPreventDuplicateStatus-Member") },
        { DetectType.Target, Service.Lang.GetText("AutoPreventDuplicateStatus-Target") },
    };

    private static readonly Dictionary<uint, DuplicateActionInfo> DuplicateActions = new()
    {
        // 牵制
        { 7549, new(7549, DetectType.Target, [1195], []) },
        // 昏乱
        { 7560, new(7560, DetectType.Target, [1203], []) },
        // 抗死
        { 25857, new(25857, DetectType.Self, [2707], []) },
        // 武装解除
        { 2887, new(2887, DetectType.Target, [860], []) },
        // 策动，防守之桑巴, 行吟
        { 16889, new(16889, DetectType.Self, [1951, 1934, 1826], []) },
        { 16012, new(16012, DetectType.Self, [1951, 1934, 1826], []) },
        { 7405, new(7405, DetectType.Self, [1951, 1934, 1826], []) },
        // 大地神的抒情恋歌
        { 7408, new(7408, DetectType.Self, [1202], []) },
        // 雪仇
        { 7535, new(7535, DetectType.Target, [1193], []) },
        // 摆脱
        { 7388, new(7388, DetectType.Self, [1457], []) },
        // 圣光幕帘
        { 3540, new(3540, DetectType.Self, [1362], []) },
        // 干预
        { 7382, new(7382, DetectType.Target, [1174], []) },
        // 献奉
        { 25754, new(25754, DetectType.Target, [2682], []) },
        // 至黑之夜
        { 7393, new(7393, DetectType.Target, [1178], []) },
        // 光之心
        { 16160, new(16160, DetectType.Self, [1839], []) },
        // 刚玉之心
        { 25758, new(25758, DetectType.Target, [2683], []) },
        // 极光
        { 16151, new(16151, DetectType.Target, [1835], []) },
        // 神祝祷
        { 7432, new(7432, DetectType.Target, [1218], []) },
        // 水流幕
        { 25861, new(25861, DetectType.Target, [2708], []) },
        // 无中生有
        { 7430, new(7430, DetectType.Self, [1217], []) },
        // 擢升
        { 25873, new(25873, DetectType.Target, [2717], []) },
        // 扫腿，下踢，盾牌猛击
        { 7863, new(7863, DetectType.Target, [2], []) },
        { 7540, new(7540, DetectType.Target, [2], []) },
        { 16, new(16, DetectType.Target, [2], []) },
        // 插言, 伤头
        { 7538, new(7538, DetectType.Target, [1], []) },
        { 7551, new(7551, DetectType.Target, [1], []) },
        // 真北
        { 7546, new(7546, DetectType.Self, [1250], []) },
        // 亲疏自行 (战士)
        { 7548, new(7548, DetectType.Self, [2663], []) },
        // 战斗连祷
        { 3557, new(3557, DetectType.Self, [786], []) },
        // 龙剑
        { 83, new(83, DetectType.Self, [116], []) },
        // 震脚
        { 69, new(69, DetectType.Self, [110], []) },
        // 义结金兰
        { 7396, new(7396, DetectType.Self, [1182, 1185], []) },
        // 夺取
        { 2248, new(2248, DetectType.Target, [638], []) },
        // 神秘环
        { 24405, new(24405, DetectType.Self, [2599], []) },
        // 明镜止水
        { 7499, new(7499, DetectType.Self, [1233], []) },
        // 灼热之光
        { 25801, new(25801, DetectType.Self, [2703], []) },
        // 三连咏唱
        { 7421, new(7421, DetectType.Self, [1211], []) },
        // 激情咏唱
        { 3574, new(3574, DetectType.Self, [867], []) },
        // 促进
        { 7518, new(7518, DetectType.Self, [1238], []) },
        // 必灭之炎
        { 34579, new(34579, DetectType.Target, [3643], []) },
        // 魔法吐息
        { 34567, new(34567, DetectType.Target, [3712], []) },
        // 战斗之声
        { 118, new(118, DetectType.Self, [141], []) },
        // 技巧舞步
        { 15998, new(15998, DetectType.Self, [1822], [2698]) },
        // 连环计
        { 7436, new(7436, DetectType.Target, [1221], []) },
        // 占卜
        { 16552, new(16552, DetectType.Self, [1878], []) },
        // 抽卡
        { 3590, new(3590, DetectType.Self, [913, 914, 915, 916, 917, 918], []) },
        // 复活，复生，生辰，复苏，赤复活，天使低语
        { 125, new(125, DetectType.Target, [148], []) },
        { 173, new(173, DetectType.Target, [148], []) },
        { 3603, new(3603, DetectType.Target, [148], []) },
        { 24287, new(24287, DetectType.Target, [148], []) },
        { 7523, new(7523, DetectType.Target, [148], []) },
        { 18317, new(18317, DetectType.Target, [148], []) },
    };

    private static Config ModuleConfig = null!;

    public override void Init()
    {
        ModuleConfig = LoadConfig<Config>() ?? new();

        DuplicateActions.Keys.Except(ModuleConfig.EnabledActions.Keys).ToList()
                        .ForEach(key => ModuleConfig.EnabledActions[key] = true);

        ModuleConfig.EnabledActions.Keys.Except(DuplicateActions.Keys).ToList()
                    .ForEach(key => ModuleConfig.EnabledActions.Remove(key));

        SaveConfig(ModuleConfig);

        Service.UseActionManager.Register(OnPreUseAction);
    }

    public override void ConfigUI()
    {
        if (ImGui.BeginCombo("###ActionEnabledCombo", 
                             Service.Lang.GetText("AutoPreventDuplicateStatus-EnabledActionAmount",
                                                  ModuleConfig.EnabledActions.Count(x => x.Value)), ImGuiComboFlags.HeightLarge))
        {
            if (ImGui.BeginTable("###ActionTable", 3, ImGuiTableFlags.Borders))
            {
                ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
                ImGui.TableNextColumn();
                ImGui.Text(Service.Lang.GetText("AutoPreventDuplicateStatus-Action"));
                ImGui.TableNextColumn();
                ImGui.Text(Service.Lang.GetText("AutoPreventDuplicateStatus-DetectType"));
                ImGui.TableNextColumn();
                ImGui.Text(Service.Lang.GetText("AutoPreventDuplicateStatus-RelatedStatus"));

                foreach (var info in DuplicateActions)
                {
                    if (!PresetData.PlayerActions.TryGetValue(info.Key, out var result)) continue;
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    var isActionEnabled = ModuleConfig.EnabledActions[info.Key];
                    if (ImGui.Checkbox($"###Is{info.Key}Enabled", ref isActionEnabled))
                    {
                        ModuleConfig.EnabledActions[info.Key] ^= true;
                        SaveConfig(ModuleConfig);
                    }

                    ImGui.SameLine();
                    ImGui.Spacing();

                    ImGui.SameLine();
                    ImGuiOm.TextImage(result.Name.RawString, ImageHelper.GetIcon(result.Icon).ImGuiHandle, ScaledVector2(20f));

                    ImGui.TableNextColumn();
                    ImGui.Text($"    {DetectTypeLoc[info.Value.DetectType]}    ");

                    ImGui.TableNextColumn();
                    ImGui.Spacing();
                    foreach (var status in info.Value.StatusID)
                    {
                        if (info.Key is 7551 or 7538) continue;
                        var statusResult = PresetData.Statuses[status];
                        ImGui.SameLine();
                        ImGui.Image(ImageHelper.GetIcon(statusResult.Icon).ImGuiHandle, ScaledVector2(24f));

                        ImGuiOm.TooltipHover(statusResult.Name.ExtractText());
                    }
                }

                ImGui.EndTable();
            }

            ImGui.EndCombo();
        }

        ScaledDummy(2f);

        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(ImGuiColors.DalamudOrange, $"{Service.Lang.GetText("AutoPreventDuplicateStatus-OverlapThreshold")}:");

        ImGui.SameLine();
        ImGui.SetNextItemWidth(150f * GlobalFontScale);
        ImGui.InputFloat("###OverlapThreshold", ref ModuleConfig.OverlapThreshold, 0, 0, "%.1f");
        if (ImGui.IsItemDeactivatedAfterEdit())
            SaveConfig(ModuleConfig);
        
        ImGuiOm.HelpMarker(Service.Lang.GetText("AutoPreventDuplicateStatus-OverlapThresholdHelp"));
    }

    private static void OnPreUseAction(
        ref bool isPrevented, ref ActionType actionType, ref uint actionID, ref ulong targetID, ref uint a4,
        ref uint queueState, ref uint a6)
    {
        if (actionType != ActionType.Action || !DuplicateActions.TryGetValue(actionID, out var info) ||
            !ModuleConfig.EnabledActions[actionID]) return;

        if (actionID is 7551 or 7538 &&
            Service.Target.Target is not IBattleChara { IsCasting: true, IsCastInterruptible: true } chara)
        {
            isPrevented = true;
            return;
        }

        StatusManager* statusManager = null;
        switch (info.DetectType)
        {
            case DetectType.Self:
                statusManager = ((FFXIVClientStructs.FFXIV.Client.Game.Character.BattleChara*)
                                    Service.ClientState.LocalPlayer.Address)->GetStatusManager();

                break;
            case DetectType.Target:
                if (Service.Target.Target != null && Service.Target.Target is IBattleChara)
                {
                    statusManager = ((FFXIVClientStructs.FFXIV.Client.Game.Character.BattleChara*)
                                        Service.Target.Target.Address)->GetStatusManager();
                }

                if (Service.Target.Target == null && targetID == 0xE000_0000)
                {
                    statusManager =
                        ((FFXIVClientStructs.FFXIV.Client.Game.Character.BattleChara*)
                            Service.ClientState.LocalPlayer.Address)->GetStatusManager();
                }

                break;
        }

        if (statusManager != null)
        {
            if (info.SecondStatusID.Length > 0)
            {
                foreach (var secondStatus in info.SecondStatusID)
                    if (statusManager->HasStatus(secondStatus))
                        return;
            }

            foreach (var status in info.StatusID)
            {
                var statusIndex = statusManager->GetStatusIndex(status);
                if (statusIndex != -1 &&
                    (PresetData.Statuses[status].IsPermanent ||
                     statusManager->Status[statusIndex].RemainingTime > ModuleConfig.OverlapThreshold))
                {
                    isPrevented = true;
                    return;
                }
            }
        }
    }

    public override void Uninit()
    {
        Service.UseActionManager.Unregister(OnPreUseAction);

        base.Uninit();
    }

    private enum DetectType
    {
        Self = 0,
        Member = 1,
        Target = 2,
    }

    private sealed record DuplicateActionInfo(uint ActionID, DetectType DetectType, uint[] StatusID, uint[] SecondStatusID);

    private class Config : ModuleConfiguration
    {
        public Dictionary<uint, bool> EnabledActions = [];
        public float OverlapThreshold = 3.5f;
    }
}
