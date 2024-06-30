using System;
using System.Collections.Generic;
using System.Numerics;
using DailyRoutines.Helpers;
using DailyRoutines.Infos;
using DailyRoutines.Managers;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ImGuiNET;
using Lumina.Excel.GeneratedSheets;

namespace DailyRoutines.Modules;

[ModuleDescription("PlayerTargetInfoExpandTitle", "PlayerTargetInfoExpandDescription", ModuleCategories.界面优化)]
public unsafe class PlayerTargetInfoExpand : DailyModuleBase
{
    private static readonly List<Payload> Payloads =
    [
        new Payload("/Name/", "名称", c => c.Name.TextValue),
        new Payload("/Job/", "职业",
                    c => c.ClassJob.GameData?.Name.RawString ?? LuminaCache.GetRow<ClassJob>(0).Name.RawString),
        new Payload("/Level/", "等级", c => c.Level.ToString()),
        new Payload("/FCTag/", "部队", c => c.CompanyTag.TextValue),
        new Payload("/OnlineStatus/", "在线状态",
                    c => string.IsNullOrWhiteSpace(c.OnlineStatus.GameData?.Name.RawString)
                             ? LuminaCache.GetRow<OnlineStatus>(47).Name.RawString
                             : c.OnlineStatus.GameData?.Name.RawString),
        new Payload("/Mount/", "坐骑", c => LuminaCache.GetRow<Mount>(c.ToCharacterStruct()->Mount.MountId).Singular.RawString),
        new Payload("/HomeWorld/", "原始服务器", c => LuminaCache.GetRow<World>(c.ToCharacterStruct()->HomeWorld).Name.RawString),
        new Payload("/Emote/", "情感动作", c => LuminaCache.GetRow<Emote>(c.ToCharacterStruct()->EmoteController.EmoteId).Name.RawString),
        new Payload("/TargetsTarget/", "目标的目标", c => c.TargetObject?.Name.TextValue ?? ""),
        new Payload("/ShieldValue/", "盾值 (百分比)", c => c.ShieldPercentage.ToString()),
        new Payload("/CurrentHP/", "当前生命值", c => c.CurrentHp.ToString()),
        new Payload("/MaxHP/", "最大生命值", c => c.MaxHp.ToString()),
        new Payload("/CurrentMP/", "当前魔力", c => c.CurrentMp.ToString()),
        new Payload("/MaxMP/", "最大魔力", c => c.MaxMp.ToString()),
        new Payload("/MaxCP/", "最大制作力", c => c.MaxCp.ToString()),
        new Payload("/CurrentCP/", "当前制作力", c => c.CurrentCp.ToString()),
        new Payload("/MaxCP/", "最大制作力", c => c.MaxCp.ToString()),
        new Payload("/CurrentGP/", "当前采集力", c => c.CurrentGp.ToString()),
        new Payload("/MaxGP/", "最大采集力", c => c.MaxGp.ToString()),
    ];

    private static Config ModuleConfig = null!;

    public override void Init()
    {
        ModuleConfig = LoadConfig<Config>() ?? new();

        Service.AddonLifecycle.RegisterListener(AddonEvent.PostRequestedUpdate, "_TargetInfo", UpdateTargetInfo);
        Service.AddonLifecycle.RegisterListener(AddonEvent.PostRequestedUpdate, "_TargetInfoMainTarget",
                                                UpdateTargetInfoMainTarget);

        Service.AddonLifecycle.RegisterListener(AddonEvent.PostRequestedUpdate, "_FocusTargetInfo", UpdateFocusTargetInfo);
    }

    public override void ConfigUI()
    {
        ImGui.BeginGroup();
        var tableSize = new Vector2(ImGui.GetContentRegionAvail().X / 2, 0);
        DrawInputAndPreviewText(Service.Lang.GetText("Target"), ref ModuleConfig.TargetPattern);
        DrawInputAndPreviewText(Service.Lang.GetText("PlayerTargetInfoExpand-TargetsTarget"),
                                ref ModuleConfig.TargetsTargetPattern);

        DrawInputAndPreviewText(Service.Lang.GetText("PlayerTargetInfoExpand-FocusTarget"),
                                ref ModuleConfig.FocusTargetPattern);
        ImGui.EndGroup();

        ImGui.SameLine();
        if (ImGui.BeginTable("PayloadDisplay", 2, ImGuiTableFlags.Borders, tableSize / 1.5f))
        {
            ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
            ImGui.TableNextColumn();
            ImGui.Text(Service.Lang.GetText("PlayerTargetInfoExpand-AvailablePayload"));
            ImGui.TableNextColumn();
            ImGui.Text(Service.Lang.GetText("Description"));

            foreach (var payload in Payloads)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text(payload.Placeholder);
                ImGui.TableNextColumn();
                ImGui.Text(payload.Description);
            }

            ImGui.EndTable();
        }

        return;

        void DrawInputAndPreviewText(string categoryTitle, ref string config)
        {
            if (ImGui.BeginTable(categoryTitle, 2, ImGuiTableFlags.BordersOuter, tableSize))
            {
                ImGui.TableSetupColumn("###Category", ImGuiTableColumnFlags.WidthFixed, ImGui.CalcTextSize("真得要六个字").X);
                ImGui.TableSetupColumn("###Content", ImGuiTableColumnFlags.None, 50);

                ImGui.TableNextRow();

                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                ImGui.Text($"{categoryTitle}:");

                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(-1f);
                if (ImGui.InputText($"###{categoryTitle}", ref config, 64))
                    SaveConfig(ModuleConfig);

                if (Service.ClientState.LocalPlayer != null && Service.ClientState.LocalPlayer is ICharacter chara)
                {
                    ImGui.TableNextRow();

                    ImGui.TableNextColumn();
                    ImGui.AlignTextToFramePadding();
                    ImGui.Text($"{Service.Lang.GetText("Example")}:");

                    ImGui.TableNextColumn();
                    ImGui.Text(ReplacePatterns(config, Payloads, chara));
                }

                ImGui.EndTable();
            }

            ImGui.Spacing();
        }
    }

    private static void UpdateTargetInfo(AddonEvent type, AddonArgs args)
    {
        var addon = (AtkUnitBase*)args.Addon;
        if (addon == null || !addon->IsVisible) return;

        // 目标
        var target = Service.Target.Target;
        var node0 = addon->GetTextNodeById(16);
        if (node0 != null && target is ICharacter { ObjectKind: ObjectKind.Player } chara0)
            node0->SetText(ReplacePatterns(ModuleConfig.TargetPattern, Payloads, chara0));

        // 目标的目标
        var targetsTarget = Service.Target.Target?.TargetObject;
        var node1 = addon->GetTextNodeById(7);
        if (node1 != null && targetsTarget is ICharacter { ObjectKind: ObjectKind.Player } chara1)
            node1->SetText(ReplacePatterns(ModuleConfig.TargetsTargetPattern, Payloads, chara1));
    }

    private static void UpdateTargetInfoMainTarget(AddonEvent type, AddonArgs args)
    {
        var addon = (AtkUnitBase*)args.Addon;
        if (addon == null || !addon->IsVisible) return;

        // 目标
        var target = Service.Target.Target;
        var node0 = addon->GetTextNodeById(10);
        if (node0 != null && target is ICharacter { ObjectKind: ObjectKind.Player } chara0)
            node0->SetText(ReplacePatterns(ModuleConfig.TargetPattern, Payloads, chara0));

        // 目标的目标
        var targetsTarget = Service.Target.Target?.TargetObject;
        var node1 = addon->GetTextNodeById(7);
        if (node1 != null && targetsTarget is ICharacter { ObjectKind: ObjectKind.Player } chara1)
            node1->SetText(ReplacePatterns(ModuleConfig.TargetsTargetPattern, Payloads, chara1));
    }

    private static void UpdateFocusTargetInfo(AddonEvent type, AddonArgs args)
    {
        var addon = (AtkUnitBase*)args.Addon;
        if (addon == null || !addon->IsVisible) return;

        // 焦点目标
        var target = Service.Target.FocusTarget;
        var node0 = addon->GetTextNodeById(10);
        if (node0 != null && target is ICharacter { ObjectKind: ObjectKind.Player } chara0)
            node0->SetText(ReplacePatterns(ModuleConfig.FocusTargetPattern, Payloads, chara0));
    }

    private static string ReplacePatterns(string input, IEnumerable<Payload> payloads, ICharacter chara)
    {
        foreach (var payload in payloads)
            input = input.Replace(payload.Placeholder, payload.ValueFunc(chara));

        return input;
    }

    public override void Uninit()
    {
        Service.AddonLifecycle.UnregisterListener(UpdateTargetInfo);
        Service.AddonLifecycle.UnregisterListener(UpdateTargetInfoMainTarget);
        Service.AddonLifecycle.UnregisterListener(UpdateFocusTargetInfo);

        base.Uninit();
    }

    private class Payload(string placeholder, string description, Func<ICharacter, string> valueFunc)
    {
        public string                  Placeholder { get; } = placeholder;
        public string                  Description { get; } = description;
        public Func<ICharacter, string> ValueFunc   { get; } = valueFunc;
    }

    private class Config : ModuleConfiguration
    {
        public string FocusTargetPattern = "/Level/级 /Name/";
        public string TargetPattern = "/Name/ [/Job/] «/FCTag/»";
        public string TargetsTargetPattern = "/Name/";
    }
}
