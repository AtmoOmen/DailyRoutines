using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DailyRoutines.Helpers;
using DailyRoutines.Managers;
using Dalamud.Game.Gui.PartyFinder.Types;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using ImGuiNET;

namespace DailyRoutines.Modules;

[ModuleDescription("PartyFinderFilterTitle", "PartyFinderFilterDescription", ModuleCategories.界面优化)]
public class PartyFinderFilter : DailyModuleBase
{
    public override string? Author => "status102";

    private int batchIndex;
    private readonly HashSet<string> descriptionSet = [];
    private static Config ModuleConfig = null!;

    public override void Init()
    {
        ModuleConfig = LoadConfig<Config>() ?? new Config();
        Service.PartyFinder.ReceiveListing += OnReceiveListing;
    }

    public override void ConfigUI()
    {
        ImGui.TextColored(ImGuiColors.DalamudOrange, $"{Service.Lang.GetText("WorkTheory")}:");
        ImGuiOm.HelpMarker(Service.Lang.GetText("PartyFinderFilter-WorkTheoryHelp"));

        ImGui.Spacing();

        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(ImGuiColors.DalamudOrange, $"{Service.Lang.GetText("PartyFinderFilter-CurrentMode")}:");

        ImGui.SameLine();
        if (ImGuiComponents.ToggleButton("ModeToggle", ref ModuleConfig.IsWhiteList))
            SaveConfig(ModuleConfig);

        ImGui.SameLine();
        ImGui.Text(ModuleConfig.IsWhiteList ? Service.Lang.GetText("Whitelist") : Service.Lang.GetText("Blacklist"));

        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Plus, Service.Lang.GetText("PartyFinderFilter-AddPreset")))
            ModuleConfig.BlackList.Add(new(true, string.Empty));

        DrawBlacklistEditor();
    }

    private void DrawBlacklistEditor()
    {
        var index = 0;
        foreach (var item in ModuleConfig.BlackList.ToList())
        {
            var enableState = item.Key;
            if (ImGui.Checkbox($"##available{index}", ref enableState))
            {
                ModuleConfig.BlackList[index] = new(enableState, item.Value);
                SaveConfig(ModuleConfig);
            }

            ImGui.SameLine();
            if (DrawBlacklistItemText(index, item))
                index++;
        }
    }

    private bool DrawBlacklistItemText(int index, KeyValuePair<bool, string> item)
    {
        var value = item.Value;
        ImGui.InputText($"##{index}", ref value, 500);

        if (ImGui.IsItemDeactivatedAfterEdit())
            HandleRegexUpdate(index, item.Key, value);

        ImGui.SameLine();
        if (ImGuiOm.ButtonIcon($"##delete{index}", FontAwesomeIcon.Trash))
            ModuleConfig.BlackList.RemoveAt(index);
        return true;
    }

    private void HandleRegexUpdate(int index, bool key, string value)
    {
        try
        {
            _ = new Regex(value);
            ModuleConfig.BlackList[index] = new(key, value);
            SaveConfig(ModuleConfig);
        }
        catch (ArgumentException)
        {
            NotifyHelper.NotificationWarning(Service.Lang.GetText("PartyFinderFilter-RegexError"));
            ModuleConfig = LoadConfig<Config>() ?? new Config();
        }
    }

    private void OnReceiveListing(IPartyFinderListing listing, IPartyFinderListingEventArgs args)
    {
        if (batchIndex != args.BatchNumber)
        {
            batchIndex = args.BatchNumber;
            descriptionSet.Clear();
        }

        args.Visible = args.Visible && Verify(listing);
    }

    private bool Verify(IPartyFinderListing listing)
    {
        var description = listing.Description.ToString();
        if (!string.IsNullOrEmpty(description) && !descriptionSet.Add(description))
            return false;

        var isMatch = ModuleConfig.BlackList
                                  .Where(i => i.Key)
                                  .Any(item => Regex.IsMatch(listing.Name.ToString(), item.Value) ||
                                               Regex.IsMatch(description, item.Value));

        return ModuleConfig.IsWhiteList ? isMatch : !isMatch;
    }


    public override void Uninit()
    {
        Service.PartyFinder.ReceiveListing -= OnReceiveListing;
        base.Uninit();
    }

    private class Config : ModuleConfiguration
    {
        public List<KeyValuePair<bool, string>> BlackList = [];
        public bool IsWhiteList;
    }
}
