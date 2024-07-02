using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using DailyRoutines.Managers;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Hooking;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Memory;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ImGuiNET;

namespace DailyRoutines.Modules;

[ModuleDescription("CustomizeInterfaceTextTitle", "CustomizeInterfaceTextDescription", ModuleCategories.界面优化)]
public unsafe class CustomizeInterfaceText : DailyModuleBase
{
    public enum ReplaceMode
    {
        部分匹配,
        完全匹配,
        正则,
    }

    [Signature("48 89 5C 24 ?? 55 56 57 41 54 41 55 41 56 41 57 48 83 EC ?? 44 0F B6 EA",
               DetourName = nameof(SetPlayerNamePlayerDetour))]
    private static Hook<SetPlayerNamePlateDelegate>? SetPlayerNamePlateHook;

    [Signature("E8 ?? ?? ?? ?? 48 83 C4 ?? 5B C3 CC CC CC CC CC CC CC CC CC CC 40 55 56 57 48 81 EC", DetourName = nameof(TextNodeSetStringDetour))]
    private static Hook<TextNodeSetStringDelegate>? TextNodeSetStringHook;

    private static List<ReplacePattern> ReplacePatterns = [];

    private static string KeyInput = string.Empty;
    private static string ValueInput = string.Empty;
    private static int ReplaceModeInput;

    private static string KeyEditInput = string.Empty;
    private static string ValueEditInput = string.Empty;
    private static int ReplaceModeEditInput;

    public override void Init()
    {
        AddConfig(nameof(ReplacePatterns), ReplacePatterns);
        ReplacePatterns = GetConfig<List<ReplacePattern>>(nameof(ReplacePatterns));

        Service.Hook.InitializeFromAttributes(this);
        TextNodeSetStringHook?.Enable();
        SetPlayerNamePlateHook?.Enable();
    }

    public override void ConfigUI()
    {
        if (ImGui.BeginCombo("###CustomizeInterfaceTextCombo",
                             Service.Lang.GetText("CustomizeInterfaceText-PatternAmount", ReplacePatterns.Count),
                             ImGuiComboFlags.HeightLarge))
        {
            ImGui.BeginGroup();
            ImGui.AlignTextToFramePadding();
            ImGui.Text($"{Service.Lang.GetText("Key")}:");

            ImGui.SameLine();
            ImGui.SetNextItemWidth(300f * GlobalFontScale);
            ImGui.InputText("###KeyInput", ref KeyInput, 96);


            ImGui.AlignTextToFramePadding();
            ImGui.Text($"{Service.Lang.GetText("Value")}:");

            ImGui.SameLine();
            ImGui.SetNextItemWidth(300f * GlobalFontScale);
            ImGui.InputText("###ValueInput", ref ValueInput, 96);

            ImGui.AlignTextToFramePadding();
            ImGui.Text($"{Service.Lang.GetText("CustomizeInterfaceText-ReplaceMode")}:");

            foreach (var replaceMode in Enum.GetValues<ReplaceMode>())
            {
                ImGui.SameLine();
                ImGui.RadioButton(replaceMode.ToString(), ref ReplaceModeInput, (int)replaceMode);
            }

            ImGui.EndGroup();

            ImGui.SameLine();
            if (ImGuiOm.ButtonIconWithTextVertical(FontAwesomeIcon.Plus, Service.Lang.GetText("Add")) &&
                !string.IsNullOrWhiteSpace(KeyInput))
            {
                var pattern = new ReplacePattern(KeyInput, ValueInput, (ReplaceMode)ReplaceModeInput, true);
                if (ReplaceModeEditInput == (int)ReplaceMode.正则)
                    pattern.Regex = new Regex(pattern.Key);

                if (!ReplacePatterns.Contains(pattern))
                {
                    ReplacePatterns.Add(pattern);
                    KeyInput = ValueInput = string.Empty;

                    UpdateConfig(nameof(ReplacePatterns), ReplacePatterns);
                }
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            if (ImGui.BeginTable("###CustomizeInterfaceTextTable", 4, ImGuiTableFlags.Borders))
            {
                ImGui.TableSetupColumn("启用", ImGuiTableColumnFlags.WidthFixed, 20 * GlobalFontScale);
                ImGui.TableSetupColumn("键", ImGuiTableColumnFlags.WidthStretch, 50);
                ImGui.TableSetupColumn("值", ImGuiTableColumnFlags.WidthStretch, 50);
                ImGui.TableSetupColumn("匹配模式", ImGuiTableColumnFlags.WidthStretch, 15);

                ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
                ImGui.TableNextColumn();
                ImGui.Text("");
                ImGui.TableNextColumn();
                ImGui.Text(Service.Lang.GetText("Key"));
                ImGui.TableNextColumn();
                ImGui.Text(Service.Lang.GetText("Value"));
                ImGui.TableNextColumn();
                ImGui.Text(Service.Lang.GetText("CustomizeInterfaceText-ReplaceMode"));

                var array = ReplacePatterns.ToArray();
                for (var i = 0; i < ReplacePatterns.Count; i++)
                {
                    var replacePattern = array[i];
                    ImGui.TableNextRow();

                    ImGui.TableNextColumn();
                    var enabled = replacePattern.Enabled;
                    if (ImGui.Checkbox($"###{i}_IsEnabled", ref enabled))
                    {
                        ReplacePatterns[i].Enabled = enabled;
                        UpdateConfig(nameof(ReplacePatterns), ReplacePatterns);
                    }

                    ImGui.TableNextColumn();
                    ImGui.Selectable(replacePattern.Key, false);

                    if (ImGui.BeginPopupContextItem($"{replacePattern.Key}_KeyEdit"))
                    {
                        if (ImGui.IsWindowAppearing())
                            KeyEditInput = replacePattern.Key;

                        ImGui.AlignTextToFramePadding();
                        ImGui.Text($"{Service.Lang.GetText("Key")}:");

                        ImGui.SameLine();
                        ImGui.SetNextItemWidth(300f * GlobalFontScale);
                        ImGui.InputText("###KeyEditInput", ref KeyEditInput, 96);

                        if (ImGui.IsItemDeactivatedAfterEdit() && !string.IsNullOrWhiteSpace(KeyEditInput))
                        {
                            var pattern = new ReplacePattern(KeyEditInput, "", 0, replacePattern.Enabled);
                            if (!ReplacePatterns.Contains(pattern))
                            {
                                ReplacePatterns[i].Key = KeyEditInput;
                                if (replacePattern.Mode is ReplaceMode.正则)
                                    ReplacePatterns[i].Regex = new Regex(KeyEditInput);

                                UpdateConfig(nameof(ReplacePatterns), ReplacePatterns);
                            }
                        }

                        ImGui.SameLine();
                        if (ImGui.Button(Service.Lang.GetText("Delete")))
                        {
                            if (ReplacePatterns.Remove(replacePattern))
                                UpdateConfig(nameof(ReplacePatterns), ReplacePatterns);
                        }

                        ImGui.EndPopup();
                    }

                    ImGui.TableNextColumn();
                    ImGui.Selectable(replacePattern.Value);

                    if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                        ValueEditInput = replacePattern.Value;

                    if (ImGui.BeginPopupContextItem($"{replacePattern.Key}_ValueEdit"))
                    {
                        ImGui.AlignTextToFramePadding();
                        ImGui.Text($"{Service.Lang.GetText("Value")}:");

                        ImGui.SameLine();
                        ImGui.SetNextItemWidth(300f * GlobalFontScale);
                        ImGui.InputText("###ValueEditInput", ref ValueEditInput, 96);

                        if (ImGui.IsItemDeactivatedAfterEdit())
                        {
                            ReplacePatterns[i].Value = ValueEditInput;
                            UpdateConfig(nameof(ReplacePatterns), ReplacePatterns);
                        }

                        ImGui.EndPopup();
                    }

                    ImGui.TableNextColumn();
                    ImGui.Selectable(replacePattern.Mode.ToString());

                    if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                        ReplaceModeEditInput = (int)replacePattern.Mode;

                    if (ImGui.BeginPopupContextItem($"{replacePattern.Key}_ModeEdit"))
                    {
                        ImGui.AlignTextToFramePadding();
                        ImGui.Text($"{Service.Lang.GetText("CustomizeInterfaceText-ReplaceMode")}:");

                        foreach (var replaceMode in Enum.GetValues<ReplaceMode>())
                        {
                            ImGui.SameLine();
                            ImGui.RadioButton(replaceMode.ToString(), ref ReplaceModeEditInput, (int)replaceMode);

                            if (ImGui.IsItemDeactivatedAfterEdit())
                            {
                                ReplacePatterns[i].Mode = (ReplaceMode)ReplaceModeEditInput;
                                if ((ReplaceMode)ReplaceModeEditInput is ReplaceMode.正则)
                                    ReplacePatterns[i].Regex = new Regex(replacePattern.Key);

                                UpdateConfig(nameof(ReplacePatterns), ReplacePatterns);
                            }
                        }

                        ImGui.EndPopup();
                    }
                }

                ImGui.EndTable();
            }

            ImGui.EndCombo();
        }
    }

    private static void TextNodeSetStringDetour(AtkTextNode* textNode, nint text)
    {
        var origText = MemoryHelper.ReadSeStringNullTerminated(text);
        if (origText.Payloads.Count == 0)
        {
            TextNodeSetStringHook.Original(textNode, text);
            return;
        }

        var (state, modifiedText) = ApplyTextReplacements(origText);

        if (state)
        {
            var ptr = Marshal.AllocHGlobal(modifiedText.Length + 1);
            Marshal.Copy(modifiedText, 0, ptr, modifiedText.Length);
            Marshal.WriteByte(ptr, modifiedText.Length, 0);

            TextNodeSetStringHook.Original(textNode, ptr);
            Marshal.FreeHGlobal(ptr);

            return;
        }

        TextNodeSetStringHook.Original(textNode, text);
    }

    private static nint SetPlayerNamePlayerDetour(
        nint namePlateObjectPtr, bool isPrefixTitle, bool displayTitle,
        nint titlePtr, nint namePtr, nint fcNamePtr, nint prefix, int iconId)
    {
        var (stateName, newNamePtr) = ReplaceTextAndAllocate(namePtr);
        var (stateTitle, newTitlePtr) = ReplaceTextAndAllocate(titlePtr);
        var (stateFcName, newFcNamePtr) = ReplaceTextAndAllocate(fcNamePtr);

        var anyChanges = stateName || stateTitle || stateFcName;

        if (anyChanges)
        {
            var original = SetPlayerNamePlateHook.Original(namePlateObjectPtr, isPrefixTitle, displayTitle,
                                                           newTitlePtr, newNamePtr, newFcNamePtr, prefix, iconId);

            if (stateName) Marshal.FreeHGlobal(newNamePtr);
            if (stateTitle) Marshal.FreeHGlobal(newTitlePtr);
            if (stateFcName) Marshal.FreeHGlobal(newFcNamePtr);
            return original;
        }

        return SetPlayerNamePlateHook.Original(namePlateObjectPtr, isPrefixTitle, displayTitle,
                                               titlePtr, namePtr, fcNamePtr, prefix, iconId);
    }

    private static (bool state, nint ptr) ReplaceTextAndAllocate(nint originalTextPtr)
    {
        var origText = MemoryHelper.ReadSeStringNullTerminated(originalTextPtr);
        if (origText.Payloads.Count == 0)
            return (false, originalTextPtr);

        var (state, modifiedText) = ApplyTextReplacements(origText);

        if (state)
        {
            var ptr = Marshal.AllocHGlobal(modifiedText.Length + 1);
            Marshal.Copy(modifiedText, 0, ptr, modifiedText.Length);
            Marshal.WriteByte(ptr, modifiedText.Length, 0);
            return (true, ptr);
        }

        return (false, originalTextPtr);
    }

    private static (bool state, byte[]? modifiedText) ApplyTextReplacements(SeString origText)
    {
        var state = false;
        var textPayloads = origText.Payloads
                                   .Where(p => p.Type == PayloadType.RawText)
                                   .Cast<TextPayload>()
                                   .ToList();

        foreach (var pattern in ReplacePatterns)
        {
            if (!pattern.Enabled) continue;

            foreach (var rawTextPayload in textPayloads)
            {
                var originalText = rawTextPayload.Text;

                switch (pattern.Mode)
                {
                    case ReplaceMode.部分匹配:
                        if (originalText.Contains(pattern.Key))
                        {
                            rawTextPayload.Text = originalText.Replace(pattern.Key, pattern.Value);
                            state = true;
                        }

                        break;
                    case ReplaceMode.完全匹配:
                        if (originalText == pattern.Key)
                        {
                            rawTextPayload.Text = pattern.Value;
                            state = true;
                        }

                        break;
                    case ReplaceMode.正则:
                        var regex = pattern.Regex;
                        if (regex != null && regex.IsMatch(originalText))
                        {
                            rawTextPayload.Text = regex.Replace(originalText, pattern.Value);
                            state = true;
                        }

                        break;
                }
            }
        }

        return (state, state ? origText.Encode() : null);
    }

    private delegate nint SetPlayerNamePlateDelegate(
        nint namePlateObjectPtr, bool isPrefixTitle, bool displayTitle,
        nint titlePtr, nint namePtr, nint fcNamePtr, nint prefix, int iconId);

    private delegate void TextNodeSetStringDelegate(AtkTextNode* textNode, nint text);

    public class ReplacePattern : IComparable<ReplacePattern>, IEquatable<ReplacePattern>
    {
        public ReplacePattern() { }

        public ReplacePattern(string key, string value, ReplaceMode mode, bool enabled)
        {
            Key = key;
            Value = value;
            Mode = mode;
            Enabled = enabled;
        }

        public string      Key     { get; set; } = string.Empty;
        public string      Value   { get; set; } = string.Empty;
        public ReplaceMode Mode    { get; set; }
        public bool        Enabled { get; set; }
        public Regex?      Regex   { get; set; }

        public int CompareTo(ReplacePattern? other)
        {
            return other == null ? 1 : string.Compare(Key, other.Key, StringComparison.Ordinal);
        }

        public bool Equals(ReplacePattern? other) { return other != null && Key == other.Key; }

        public override bool Equals(object? obj) { return obj is ReplacePattern other && Equals(other); }

        public override int GetHashCode() { return Key.GetHashCode(); }

        public void Deconstruct(out string key, out string value, out ReplaceMode mode, out bool enabled)
        {
            key = Key;
            value = Value;
            mode = Mode;
            enabled = Enabled;
        }
    }
}
