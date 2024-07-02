// 待更新
/*
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using DailyRoutines.Helpers;
using DailyRoutines.Infos;
using DailyRoutines.Managers;
using Dalamud.Hooking;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ImGuiNET;

namespace DailyRoutines.Modules;

[ModuleDescription("MarkerInPartyListTitle", "MarkerInPartyListDescription", ModuleCategories.战斗)]
public unsafe class MarkerInPartyList : DailyModuleBase
{
    public override string Author => "status102";

    [Signature("E8 ?? ?? ?? ?? 4C 8B C5 8B D7 48 8B CB E8", DetourName = nameof(DetourLocalMarkingFunc))]
    public static Hook<LocalMarkingFunc>? LocalMarkingHook;
    public delegate nint LocalMarkingFunc(nint manager, uint markingType, nint objectId, nint a4);

    private const ImGuiWindowFlags Flags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoSavedSettings |
                                           ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoMouseInputs |
                                           ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoBackground |
                                           ImGuiWindowFlags.NoNav;

    private static Config ModuleConfig = null!;

    private static Dictionary<MarkIcon, IDalamudTextureWrap> _markIcon = [];
    private static readonly Dictionary<MarkIcon, int> _markedObject = new(8);
    private static bool _needClear;

    public override void Init()
    {
        ModuleConfig = LoadConfig<Config>() ?? new();

        _markIcon = Enum.GetValues<MarkIcon>().ToDictionary(x => x, x => ImageHelper.GetIcon((uint)x)!);

        Service.Hook.InitializeFromAttributes(this);
        LocalMarkingHook?.Enable();
        Service.ClientState.TerritoryChanged += ResetmarkedObject;

        Overlay ??= new(this);
        Overlay.IsOpen = true;
        Overlay.Flags = Flags;
    }

    public override void ConfigUI()
    {
        ImGui.SetNextItemWidth(200f * GlobalFontScale);
        ImGui.InputFloat2(Service.Lang.GetText("MarkerInPartyList-IconOffset"), ref ModuleConfig.PartyListIconOffset, "%.1f");
        if (ImGui.IsItemDeactivatedAfterEdit())
            SaveConfig(ModuleConfig);

        ImGui.SetNextItemWidth(200f * GlobalFontScale);
        ImGui.InputFloat(Service.Lang.GetText("MarkerInPartyList-IconScale"), ref ModuleConfig.PartyListIconScale, 0, 0, "%.1f");
        if (ImGui.IsItemDeactivatedAfterEdit())
            SaveConfig(ModuleConfig);

        if (ImGui.Checkbox(Service.Lang.GetText("MarkerInPartyList-HidePartyListIndexNumber"), ref ModuleConfig.HidePartyListIndexNumber))
        { 
            SaveConfig(ModuleConfig);
            ResetPartyMemberList();
        }
    }

    public override void OverlayUI()
    {
        if (_markedObject.Count != 0) { }
        else if (_needClear)
        {
            ResetPartyMemberList();
            _needClear = false;
            return;
        }
        else
            return;

        if (AddonState.PartyList is null || !IsAddonAndNodesReady(AddonState.PartyList) ||
            Service.ClientState.LocalPlayer is null)
            return;

        ModifyPartyMemberNumber(AddonState.PartyList, false);

        ImGuiHelpers.ForceNextWindowMainViewport();
        ImGui.SetNextWindowPos(ImGui.GetMainViewport().Pos);
        ImGui.SetNextWindowSize(ImGui.GetMainViewport().Size);
        if (ImGui.Begin("##MarkOverlayWindow", Flags))
        {
            foreach (var icon in _markedObject)
                DrawOnPartyList(icon.Value, icon.Key, AddonState.PartyList, ImGui.GetWindowDrawList());

            ImGui.End();
        }
    }

    private static void ResetmarkedObject(ushort obj)
    {
        _markedObject.Clear();
        ResetPartyMemberList();
    }

    private static void ResetPartyMemberList(AtkUnitBase* partylist = null)
    {
        if (partylist is null) partylist = AddonState.PartyList;
        if (partylist is not null && IsAddonAndNodesReady(partylist))
            ModifyPartyMemberNumber(partylist, true);
    }

    private static void DrawOnPartyList(int listIndex, MarkIcon markIcon, AtkUnitBase* pPartyList, ImDrawListPtr drawList)
    {
        if (listIndex is < 0 or > 7)
            return;

        var partyMemberNodeIndex = 10 + listIndex;
        var partyAlign = pPartyList->UldManager.SearchNodeById(2)->Y;

        var pPartyMemberNode = (AtkComponentNode*)pPartyList->UldManager.SearchNodeById((uint)partyMemberNodeIndex);
        if (pPartyMemberNode is null)
            return;

        var pIconNode = pPartyMemberNode->Component->UldManager.SearchNodeById(19);
        if (pIconNode is null)
            return;

        //	Note: sub-nodes don't scale, so we have to account for the addon's scale.
        var iconOffset = (new Vector2(5, -5) + ModuleConfig.PartyListIconOffset) * pPartyList->Scale;
        var iconSize = new Vector2(pIconNode->Width / 2, pIconNode->Height / 2) * ModuleConfig.PartyListIconScale * 0.9f *
                       pPartyList->Scale;

        var iconPos = new Vector2(
            pPartyList->X + (pPartyMemberNode->AtkResNode.X * pPartyList->Scale) + (pIconNode->X * pPartyList->Scale) +
            (pIconNode->Width * pPartyList->Scale / 2),
            pPartyList->Y + partyAlign + (pPartyMemberNode->AtkResNode.Y * pPartyList->Scale) +
            (pIconNode->Y * pPartyList->Scale) + (pIconNode->Height * pPartyList->Scale / 2));

        iconPos += iconOffset;
        drawList.AddImage(_markIcon[markIcon].ImGuiHandle, iconPos, iconPos + iconSize);
    }

    private static void ModifyPartyMemberNumber(AtkUnitBase* pPartyList, bool visible)
    {
        if (pPartyList is null || (!ModuleConfig.HidePartyListIndexNumber && !visible))
            return;

        var memberIdList = Enumerable.Range(10, 17).ToList();
        foreach (var id in memberIdList)
        {
            var member = pPartyList->GetNodeById((uint)id);
            if (member is null || member->GetComponent() is null)
                continue;

            if (!member->IsVisible())
                break;

            var textNode = member->GetComponent()->UldManager.SearchNodeById(15);
            if (textNode != null && textNode->IsVisible() != visible)
                textNode->ToggleVisibility(visible);
        }
    }


    private static void ProcMarkIconSetted(MarkType markType, uint objectId)
    {
        var icon = Enum.Parse<MarkIcon>(Enum.GetName(markType) ?? string.Empty);
        switch (objectId)
        {
            case 0xE000_0000 or 0xE00_0000:
            {
                _markedObject.Remove(icon);
                if (_markedObject.Count == 0)
                    _needClear = true;

                return;
            }
        }

        if (AgentHUD.Instance() is null || InfoProxyCrossRealm.Instance() is null)
            return;

        var pAgentHUD = AgentHUD.Instance();
            for (var i = 0; i < 8; ++i)
            {
                var offset = i * Marshal.SizeOf<PartyListCharInfo>();
                var pCharData = pAgentHUD->PartyMembers[i];
                if (objectId == pCharData.Object->GetGameObjectId())
                {
                    if (_markedObject.ContainsValue(i))
                        _markedObject.Remove(_markedObject.First(x => x.Value == i).Key);

                    _needClear = false;
                    _markedObject[icon] = i;
                    return;
                }
        }

        if (InfoProxyCrossRealm.Instance()->IsCrossRealm > 0)
        {
            var pGroupMember = InfoProxyCrossRealm.GetMemberByObjectId(objectId);
            if (pGroupMember is not null && pGroupMember->GroupIndex == 0)
        {
                if (_markedObject.ContainsValue(pGroupMember->MemberIndex))
            {
                    _markedObject.Remove(_markedObject.First(x => x.Value == pGroupMember->MemberIndex).Key);
                }
                _needClear = false;
                _markedObject[icon] = pGroupMember->MemberIndex;
                return;
            }

        }

        _markedObject.Remove(icon);
        if (_markedObject.Count == 0)
        {
            _needClear = true;
        }
    }

    private static nint DetourLocalMarkingFunc(nint manager, uint markingType, nint objectId, nint a4)
    {
        ProcMarkIconSetted((MarkType)markingType, (uint)objectId);

        return LocalMarkingHook!.Original(manager, markingType, objectId, a4);
    }

    public override void Uninit()
    {
        Service.ClientState.TerritoryChanged -= ResetmarkedObject;
        ResetPartyMemberList();

        base.Uninit();
    }

    private class Config : ModuleConfiguration
    {
        public Vector2 PartyListIconOffset = new(0, 0);
        public float PartyListIconScale = 1f;
        public bool HidePartyListIndexNumber = true;
    }


    [StructLayout(LayoutKind.Explicit, Size = 0x20)]
    internal struct PartyListCharInfo
    {
        [FieldOffset(0x00)]
        internal IntPtr ObjectAddress;

        [FieldOffset(0x08)]
        internal IntPtr ObjectNameAddress;

        [FieldOffset(0x10)]
        internal ulong ContentID;

        [FieldOffset(0x18)]
        internal uint ObjectID;

        [FieldOffset(0x1C)]
        internal uint Unknown;

        internal string GetName()
        {
            if (ObjectAddress == IntPtr.Zero || ObjectNameAddress == IntPtr.Zero)
                return "";

            return Marshal.PtrToStringUTF8(ObjectNameAddress) ?? "";
        }
    }

    public enum MarkType : byte
    {
        Attack1 = 0,
        Attack2,
        Attack3,
        Attack4,
        Attack5,
        Bind1,
        Bind2,
        Bind3,
        Stop1,
        Stop2,
        Square,
        Circle,
        Cross,
        Triangle,
        Attack6,
        Attack7,
        Attack8,
    }

    public enum MarkIcon : uint
    {
        Attack1 = 61201,
        Attack2,
        Attack3,
        Attack4,
        Attack5,
        Attack6,
        Attack7,
        Attack8,
        Bind1 = 61211,
        Bind2,
        Bind3,
        Stop1 = 61221,
        Stop2,
        Square = 61231,
        Circle,
        Cross,
        Triangle,
    }
}
*/
