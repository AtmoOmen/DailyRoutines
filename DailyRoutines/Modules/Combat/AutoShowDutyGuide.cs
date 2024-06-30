using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using DailyRoutines.Helpers;
using DailyRoutines.Infos;
using DailyRoutines.Managers;
using DailyRoutines.Windows;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Utility;
using ImGuiNET;

namespace DailyRoutines.Modules;

[ModuleDescription("AutoShowDutyGuideTitle", "AutoShowDutyGuideDescription", ModuleCategories.战斗)]
public class AutoShowDutyGuide : DailyModuleBase
{
    private const string FF14OrgLinkBase = "https://gh.atmoomen.top/novice-network/master/docs/duty/{0}.md";

    private static Config ModuleConfig = null!;

    private static readonly HttpClient client = new();
    private static uint CurrentDuty;
    private static IDalamudTextureWrap? NoviceIcon;

    private static string HintText = string.Empty;
    private static List<string> GuideText = [];

    private static bool IsOnDebug;

    private readonly Dictionary<ushort, Func<bool?>> HintsContent = new()
    {
        { 1036, GetSastashaHint },
    };

    public override void Init()
    {
        ModuleConfig = LoadConfig<Config>() ?? new();

        NoviceIcon ??= ImageHelper.GetIcon(61523);

        TaskHelper ??= new TaskHelper { AbortOnTimeout = true, TimeLimitMS = 60000, ShowDebug = false };

        Overlay ??= new Overlay(this);
        Overlay.Flags &= ~ImGuiWindowFlags.NoTitleBar;
        Overlay.Flags &= ~ImGuiWindowFlags.AlwaysAutoResize;
        Overlay.Flags |= ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoNavInputs;
        Overlay.ShowCloseButton = false;

        Service.ClientState.TerritoryChanged += OnZoneChange;
        if (Flags.BoundByDuty)
            OnZoneChange(Service.ClientState.TerritoryType);
    }

    public override void ConfigUI()
    {
        ImGui.TextColored(ImGuiColors.DalamudOrange, $"{Service.Lang.GetText("WorkTheory")}:");
        ImGuiOm.HelpMarker(Service.Lang.GetText("AutoShowDutyGuide-TheoryHelp"), 30f);

        ImGui.SetNextItemWidth(80f * GlobalFontScale);
        ImGui.InputFloat(Service.Lang.GetText("AutoShowDutyGuide-FontScale"), ref ModuleConfig.FontScale);
        if (ImGui.IsItemDeactivatedAfterEdit())
            SaveConfig(ModuleConfig);

        ImGui.BeginDisabled(Flags.BoundByDuty);
        if (ImGui.Checkbox(Service.Lang.GetText("AutoShowDutyGuide-DebugMode"), ref IsOnDebug))
        {
            if (IsOnDebug) OnZoneChange(172);
            else
            {
                HintText = string.Empty;
                GuideText.Clear();
                CurrentDuty = 0;
            }
        }

        ImGui.EndDisabled();

        ImGuiOm.TooltipHover(Service.Lang.GetText("AutoShowDutyGuide-DebugModeHelp"));
    }

    public override void OverlayOnOpen() => ImGui.SetScrollHereY();

    public override void OverlayPreDraw()
    {
        if (!IsOnDebug && (!Flags.BoundByDuty || GuideText.Count <= 0))
        {
            Overlay.IsOpen = false;
            GuideText.Clear();
            HintText = string.Empty;
            return;
        }

        if (GuideText.Count > 0)
            Overlay.WindowName = $"{GuideText[0]}###AutoShowDutyGuide-GuideWindow";
    }

    public override void OverlayUI()
    {
        using (FontManager.GetUIFont(ModuleConfig.FontScale).Push())
        {
            if (ImGuiOm.SelectableImageWithText(NoviceIcon.ImGuiHandle, ScaledVector2(24f),
                                                Service.Lang.GetText("AutoShowDutyGuide-Source"), false))
                Util.OpenLink($"https://ff14.org/duty/{CurrentDuty}.htm");

            ImGui.Separator();

            ImGui.PushTextWrapPos(ImGui.GetWindowWidth());
            if (!string.IsNullOrWhiteSpace(HintText))
            {
                using (FontManager.GetUIFont(ModuleConfig.FontScale * 0.8f).Push())
                {
                    ImGui.Text($"{Service.Lang.GetText("AutoShowDutyGuide-DutyExtraGuide")}:");
                }
                ImGui.Text($"{HintText}");
                ImGui.Separator();
            }

            for (var i = 1; i < GuideText.Count; i++)
            {
                var text = GuideText[i];
                ImGui.PushID($"DutyGuideLine-{i}");
                ImGui.Text(text);
                if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                {
                    ImGui.SetClipboardText(text);
                    NotifyHelper.NotificationSuccess(Service.Lang.GetText("AutoShowDutyGuide-CopyNotice"));
                }

                ImGui.PopID();

                ImGui.NewLine();
            }
            ImGui.PopTextWrapPos();
        }
    }

    private void OnZoneChange(ushort territory)
    {
        if (!PresetData.Contents.TryGetValue(territory, out var content))
        {
            CurrentDuty = 0;
            HintText = string.Empty;
            GuideText.Clear();
            Overlay.IsOpen = false;
            return;
        }

        if (HintsContent.TryGetValue(territory, out var func))
        {
            TaskHelper.DelayNext(500);
            TaskHelper.Enqueue(func);
        }

        Task.Run(async () => await GetDutyGuide(content.RowId));
    }

    private static bool? GetSastashaHint()
    {
        if (Flags.BetweenAreas) return false;
        if (!Flags.BoundByDuty) return true;

        var blueObj =
            Service.ObjectTable.FirstOrDefault(x => x.IsValid() && x.IsTargetable && x.DataId == (uint)Sastasha.蓝珊瑚);

        var redObj = Service.ObjectTable.FirstOrDefault(
            x => x.IsValid() && x.IsTargetable && x.DataId == (uint)Sastasha.红珊瑚);

        var greenObj =
            Service.ObjectTable.FirstOrDefault(x => x.IsValid() && x.IsTargetable && x.DataId == (uint)Sastasha.绿珊瑚);

        if (blueObj == null && redObj == null && greenObj == null) return false;

        if (blueObj != null) HintText = $"正确机关: {Sastasha.蓝珊瑚}";
        if (redObj != null) HintText = $"正确机关: {Sastasha.红珊瑚}";
        if (greenObj != null) HintText = $"正确机关: {Sastasha.绿珊瑚}";

        return true;
    }

    private async Task GetDutyGuide(uint dutyID)
    {
        CurrentDuty = dutyID;
        var originalText = await client.GetStringAsync(string.Format(FF14OrgLinkBase, dutyID));

        var plainText = MarkdownToPlainText(originalText);
        if (!string.IsNullOrWhiteSpace(plainText))
        {
            GuideText = [.. plainText.Split('\n')];
            Overlay.IsOpen = true;
        }
    }

    public override void Uninit()
    {
        Service.ClientState.TerritoryChanged -= OnZoneChange;

        base.Uninit();
    }

    private enum Sastasha
    {
        蓝珊瑚 = 2000212,
        红珊瑚 = 2001548,
        绿珊瑚 = 2001549,
    }

    private class Config : ModuleConfiguration
    {
        public float FontScale = 1f;
    }
}
