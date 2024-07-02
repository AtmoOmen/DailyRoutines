using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using DailyRoutines.Helpers;
using DailyRoutines.Infos;
using DailyRoutines.Managers;
using Dalamud.Hooking;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ImGuiNET;

namespace DailyRoutines.Modules;

[ModuleDescription("AutoSwitchIMEWhenInputTitle", "AutoSwitchIMEWhenInputDescription", ModuleCategories.系统)]
public unsafe class AutoSwitchIMEWhenInput : DailyModuleBase
{
    [Signature(
        "40 55 53 57 41 56 41 57 48 8D AC 24 ?? ?? ?? ?? 48 81 EC ?? ?? ?? ?? 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 85 ?? ?? ?? ?? 48 8B 9D",
        DetourName = nameof(TextInputReceiveEventDetour))]
    private static Hook<TextInputReceiveEventDelegate>? TextInputReceiveEventHook;

    private static Config ModuleConfig = null!;

    private static readonly List<SavedIME> InstalledIME = [];

    private static SavedIME? CurrentIME;

    [DllImport("imm32.dll")]
    public static extern nint ImmGetContext(nint hWnd);

    [DllImport("imm32.dll")]
    public static extern bool ImmGetConversionStatus(nint hIMC, ref int mode, ref int sentence);

    [DllImport("imm32.dll")]
    public static extern bool ImmSetConversionStatus(nint hIMC, int mode, int sentence);

    public override void Init()
    {
        ModuleConfig = LoadConfig<Config>() ?? new();
        RefreshIME();

        Service.Hook.InitializeFromAttributes(this);
        TextInputReceiveEventHook?.Enable();
    }

    public override void ConfigUI()
    {
        if (Throttler.Throttle("AutoSwitchIMEWhenInput-RefreshIME", 1000)) RefreshIME();

        ImGui.Spacing();

        if (InstalledIME.Count <= 1)
            ImGui.TextColored(ImGuiColors.DalamudOrange, Service.Lang.GetText("AutoSwitchIMEWhenInput-InadequateIMEHelp"));

        var tableSize = ((ImGui.GetContentRegionAvail() / 2) - ScaledVector2(20f)) with { Y = 0 };
        if (ImGui.BeginTable("IMESelectTable", 4, ImGuiTableFlags.Borders, tableSize))
        {
            ImGui.TableSetupColumn("单选框", ImGuiTableColumnFlags.WidthFixed, 20f * GlobalFontScale);
            ImGui.TableSetupColumn("布局", ImGuiTableColumnFlags.None, 40);
            ImGui.TableSetupColumn("模式", ImGuiTableColumnFlags.None, 20);
            ImGui.TableSetupColumn("句式", ImGuiTableColumnFlags.None, 20);

            ImGui.TableNextRow(ImGuiTableRowFlags.Headers);

            ImGui.TableNextColumn();
            if (ImGuiOm.ButtonIconSelectable("Refresh", FontAwesomeIcon.Sync, Service.Lang.GetText("Refresh")))
                RefreshIME();

            ImGui.TableNextColumn();
            ImGuiOm.Text(Service.Lang.GetText("AutoSwitchIMEWhenInput-Layout"));

            ImGui.TableNextColumn();
            ImGuiOm.Text(Service.Lang.GetText("AutoSwitchIMEWhenInput-InputMode"));

            ImGui.TableNextColumn();
            ImGuiOm.Text(Service.Lang.GetText("AutoSwitchIMEWhenInput-SentenceMode"));

            foreach (var ime in InstalledIME)
            {
                ImGui.TableNextRow();

                ImGui.TableNextColumn();
                ImGui.PushItemWidth(20f * GlobalFontScale);
                if (ImGui.RadioButton($"##{ime.Name}-{ime.Mode}-{ime.Sentence}", ime.Equals(ModuleConfig.SavedIME)))
                {
                    ModuleConfig.SavedIME = ime;
                    SaveConfig(ModuleConfig);
                    SwitchIME(ime);
                }
                ImGui.PopItemWidth();

                ImGui.TableNextColumn();
                ImGui.Text($"{ime.Name}");

                ImGui.TableNextColumn();
                ImGui.Text($"{ime.Mode}");

                ImGui.TableNextColumn();
                ImGui.Text($"{ime.Sentence}");
            }

            ImGui.EndTable();
        }

        ImGui.Spacing();

        var saved = ModuleConfig.SavedIME;
        ImGui.Text(Service.Lang.GetText("AutoSwitchIMEWhenInput-SavedIME",
                                        saved == null ? Service.Lang.GetText("None") : $"{saved.Name} ({saved.Mode}/{saved.Sentence})"));

        if (ImGui.Checkbox(Service.Lang.GetText("AutoSwitchIMEWhenInput-RestoreWhenFocusStop"),
                           ref ModuleConfig.RestoreWhenFocusStop))
            SaveConfig(ModuleConfig);
    }

    private static nint TextInputReceiveEventDetour(
        AtkComponentTextInput* component, ushort eventCase, uint a3, nint a4, ushort* a5)
    {
        var original = TextInputReceiveEventHook.Original(component, eventCase, a3, a4, a5);
        switch (eventCase)
        {
            case 18:
            {
                CurrentIME = GetCurrentIME();
                if (ModuleConfig.SavedIME != null) SwitchIME(ModuleConfig.SavedIME);
                break;
            }
            case 19:
                if (ModuleConfig.RestoreWhenFocusStop && CurrentIME != null) SwitchIME(CurrentIME);
                CurrentIME = null;
                break;
        }

        return original;
    }

    private static SavedIME? GetCurrentIME()
    {
        SavedIME? result = null;
        if (!TryFindGameWindow(out var gameHandle))
        {
            NotifyHelper.NotificationError($"{Service.Lang.GetText("AutoSwitchIMEWhenInput-FailFindGameWindow")}, {Service.Lang.GetText("AutoSwitchIMEWhenInput-FailObtainIME")}");
            return result;
        }

        var hIMC = ImmGetContext(gameHandle);
        if (hIMC == nint.Zero)
        {
            NotifyHelper.NotificationError($"{Service.Lang.GetText("AutoSwitchIMEWhenInput-FailObtainIMEConvertState")}, {Service.Lang.GetText("AutoSwitchIMEWhenInput-FailObtainIME")}");
            return result;
        }

        var lang = InputLanguage.CurrentInputLanguage;
        var mode = 0;
        var sentence = 0;
        ImmGetConversionStatus(hIMC, ref mode, ref sentence);

        result = new()
        {
            Name = lang.LayoutName,
            Mode = mode,
            Sentence = sentence,
        };

        return result;
    }

    private static void SwitchIME(SavedIME ime)
    {
        if (!TryFindGameWindow(out var gameHandle))
        {
            NotifyHelper.NotificationError($"{Service.Lang.GetText("AutoSwitchIMEWhenInput-FailFindGameWindow")}, {Service.Lang.GetText("AutoSwitchIMEWhenInput-FailSwitchIME")}");
            return;
        }

        // 若本身就是同一输入法则模式应用会失效
        foreach (InputLanguage lang in InputLanguage.InstalledInputLanguages)
        {
            if (lang.LayoutName == ime.Name) continue;

            InputLanguage.CurrentInputLanguage = lang;
            break;
        }

        foreach (InputLanguage lang in InputLanguage.InstalledInputLanguages)
        {
            if (lang.LayoutName != ime.Name) continue;

            InputLanguage.CurrentInputLanguage = lang;
            var ptr = ImmGetContext(gameHandle);
            if (!ImmSetConversionStatus(ptr, ime.Mode, ime.Sentence))
                NotifyHelper.NotificationError($"{Service.Lang.GetText("AutoSwitchIMEWhenInput-FailObtainIMEConvertState")}, {Service.Lang.GetText("AutoSwitchIMEWhenInput-FailSwitchIME")}");

            break;
        }
    }

    private static void RefreshIME()
    {
        InstalledIME.Clear();
        if (!TryFindGameWindow(out var gameHandle))
            NotifyHelper.NotificationError($"{Service.Lang.GetText("AutoSwitchIMEWhenInput-FailFindGameWindow")}, {Service.Lang.GetText("AutoSwitchIMEWhenInput-FailRefreshIME")}");

        var hIMC = ImmGetContext(gameHandle);
        if (hIMC == nint.Zero)
        {
            NotifyHelper.NotificationError($"{Service.Lang.GetText("AutoSwitchIMEWhenInput-FailObtainIMEConvertState")}, {Service.Lang.GetText("AutoSwitchIMEWhenInput-FailRefreshIME")}");
            return;
        }

        foreach (InputLanguage lang in InputLanguage.InstalledInputLanguages)
        {
            var mode = 0;
            var sentence = 0;
            ImmGetConversionStatus(hIMC, ref mode, ref sentence);
            InstalledIME.Add(new()
            {
                Name = lang.LayoutName,
                Mode = mode,
                Sentence = sentence,
            });
        }
    }

    public override void Uninit()
    {
        InstalledIME.Clear();

        base.Uninit();
    }

    private class SavedIME : IEquatable<SavedIME>, IComparable<SavedIME>
    {
        public SavedIME() { }

        public SavedIME(string name, int mode, int sentence)
        {
            Name = name;
            Mode = mode;
            Sentence = sentence;
        }

        public string Name     { get; set; } = null!;
        public int    Mode     { get; set; }
        public int    Sentence { get; set; }

        public int CompareTo(SavedIME? other)
        {
            if (other == null) return 1;

            var nameComparison = string.Compare(Name, other.Name, StringComparison.Ordinal);
            if (nameComparison != 0) return nameComparison;

            var modeComparison = Mode.CompareTo(other.Mode);
            if (modeComparison != 0) return modeComparison;

            return Sentence.CompareTo(other.Sentence);
        }

        public bool Equals(SavedIME? other) =>
            other != null &&
            Name == other.Name &&
            Mode == other.Mode &&
            Sentence == other.Sentence;

        public override bool Equals(object? obj) => Equals(obj as SavedIME);

        public override int GetHashCode() => HashCode.Combine(Name, Mode, Sentence);
    }

    private class Config : ModuleConfiguration
    {
        public bool RestoreWhenFocusStop = true;
        public SavedIME? SavedIME;
    }

    private delegate nint TextInputReceiveEventDelegate
        (AtkComponentTextInput* component, ushort eventCase, uint a3, nint a4, ushort* a5);
}
