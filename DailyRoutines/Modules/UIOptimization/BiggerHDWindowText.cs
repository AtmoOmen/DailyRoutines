using System;
using System.Collections.Generic;
using DailyRoutines.Managers;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Hooking;
using Dalamud.Interface.Colors;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ImGuiNET;

namespace DailyRoutines.Modules;

[ModuleDescription("BiggerHDWindowTextTitle", "BiggerHDWindowTextDescription", ModuleCategories.界面优化)]
public unsafe class BiggerHDWindowText : DailyModuleBase
{
    [Signature("40 55 53 57 41 56 41 57 48 8D AC 24 ?? ?? ?? ?? 48 81 EC ?? ?? ?? ?? 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 85 ?? ?? ?? ?? 48 8B 9D",
               DetourName = nameof(TextInputReceiveEventDetour))]
    private static Hook<TextInputReceiveEventDelegate>? TextInputReceiveEventHook;

    private static readonly Dictionary<string, TextNodeInfo> TextWindows = new()
    {
        { "LookingForGroupDetail", new TextNodeInfo(20) },
        { "LookingForGroupCondition", new TextNodeInfo(22, 16) { TextFlags1 = 224, TextFlags2 = 1 } },
    };

    private static float FontScale = 2f;

    public override void Init()
    {
        AddConfig("FontScale", FontScale);
        FontScale = GetConfig<float>("FontScale");

        Service.Hook.InitializeFromAttributes(this);
        TextInputReceiveEventHook?.Enable();

        Service.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, TextWindows.Keys, OnTextAddon);
    }

    public override void ConfigUI()
    {
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(ImGuiColors.DalamudOrange, $"{Service.Lang.GetText("BiggerHDWindowText-FontScale")}:");

        ImGui.SameLine();
        ImGui.SetNextItemWidth(100f * GlobalFontScale);
        ImGui.InputFloat("###FontScaleInput", ref FontScale, 0, 0, "%.1f");
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            FontScale = (float)Math.Clamp(FontScale, 0.1, 5f);
            UpdateConfig(nameof(FontScale), FontScale);
        }
    }

    private static void OnTextAddon(AddonEvent type, AddonArgs args)
    {
        var addon = (AtkUnitBase*)args.Addon;
        if (addon == null) return;

        if (!TextWindows.TryGetValue(args.AddonName, out var info)) return;

        ModifyTextNode(addon, info.NodeID, true);
    }

    private static nint TextInputReceiveEventDetour
        (AtkComponentTextInput* component, ushort eventCase, uint a3, nint a4, ushort* a5)
    {
        var original = TextInputReceiveEventHook.Original(component, eventCase, a3, a4, a5);

        if (eventCase == 18)
            ModifyTextInputComponent(component);

        return original;
    }

    private static void ModifyTextInputComponent(AtkComponentTextInput* component)
    {
        if (component == null) return;

        var imeBackground = component->AtkComponentInputBase.AtkComponentBase.UldManager.SearchNodeById(4);
        if (imeBackground == null) return;

        imeBackground->SetScale(FontScale, FontScale);
    }

    private static void ModifyTextNode(AtkUnitBase* addon, uint[] nodeID, bool isAdd)
    {
        if (addon == null) return;
        var node = addon->GetNodeById(nodeID[0]);
        foreach (var id in nodeID[1..])
        {
            if (node is null)
                return;

            node = node->GetComponent()->UldManager.SearchNodeById(id);
        }

        var textNode = node->GetAsAtkTextNode();
        if (textNode is null)
            return;

        if (!TextWindows.TryGetValue(addon->NameString, out var info)) return;

        if (!info.Modified)
        {
            info.TextFlag1Original = textNode->TextFlags;
            info.TextFlag2Original = textNode->TextFlags2;
            info.FontSize = textNode->FontSize;
        }

        if (isAdd)
        {
            textNode->TextFlags = info.TextFlags1;
            textNode->TextFlags2 = info.TextFlags2;
            info.Modified = true;
        }
        else
        {
            if (info.TextFlag1Original != null)
                textNode->TextFlags = (byte)info.TextFlag1Original;

            if (info.TextFlag2Original != null)
                textNode->TextFlags2 = (byte)info.TextFlag2Original;

            if (info.FontSize != null)
                textNode->FontSize = (byte)info.FontSize;

            info.Modified = false;
        }
    }

    public override void Uninit()
    {
        Service.AddonLifecycle.UnregisterListener(OnTextAddon);

        foreach (var window in TextWindows)
        {
            var addon = (AtkUnitBase*)Service.Gui.GetAddonByName(window.Key);
            if (addon == null) continue;

            ModifyTextNode(addon, window.Value.NodeID, false);
        }

        base.Uninit();
    }

    private class TextNodeInfo(uint nodeId, params uint[] nodeIds)
    {
        public uint[] NodeID            { get; set; } = [nodeId, .. nodeIds];
        public byte?  TextFlag1Original { get; set; }
        public byte?  TextFlag2Original { get; set; }
        public byte   TextFlags1        { get; set; } = 195;
        public byte   TextFlags2        { get; set; }
        public byte?  FontSize          { get; set; }
        public bool   Modified          { get; set; }
    }

    private delegate nint TextInputReceiveEventDelegate
        (AtkComponentTextInput* component, ushort eventCase, uint a3, nint a4, ushort* a5);
}
