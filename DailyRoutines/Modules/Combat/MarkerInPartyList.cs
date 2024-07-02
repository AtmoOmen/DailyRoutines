using DailyRoutines.Infos;
using DailyRoutines.Managers;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Hooking;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Client.System.Memory;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ImGuiNET;
using Lumina.Excel;
using Lumina.Excel.GeneratedSheets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;

namespace DailyRoutines.Modules;

[ModuleDescription("MarkerInPartyListTitle", "MarkerInPartyListDescription", ModuleCategories.战斗)]
public unsafe class MarkerInPartyList : DailyModuleBase
{
    public override string Author => "status102";
    private const int DefaultIconId = 61201;
    private static readonly (short X, short Y) BasePosition = (52, 15);
    private static ExcelSheet<Marker>? MarkerSheet;

    [Signature("E8 ?? ?? ?? ?? 4C 8B C5 8B D7 48 8B CB E8", DetourName = nameof(DetourLocalMarkingFunc))]
    public static Hook<LocalMarkingFunc>? LocalMarkingHook;
    public delegate nint LocalMarkingFunc(nint manager, uint markingType, nint objectId, nint a4);

    private static Config _config = null!;
    private List<nint> _imageNodes = new(8);
    private static readonly Dictionary<int, int> _markedObject = new(8);
    private static bool _isBuilt, _needClear;
    private object _lock = new();

    public override void Init()
    {
        _config = LoadConfig<Config>() ?? new();

        Service.Hook.InitializeFromAttributes(this);
        MarkerSheet ??= Service.Data.GetExcelSheet<Marker>();
        LocalMarkingHook?.Enable();
        Service.ClientState.TerritoryChanged += ResetmarkedObject;
        Service.AddonLifecycle.RegisterListener(AddonEvent.PostDraw, "_PartyList", PartyListDrawHandle);
        Service.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, "_PartyList", PartyListFinalizeHandle);

    }

    public override void Uninit()
    {
        Service.AddonLifecycle.UnregisterListener(AddonEvent.PostDraw, PartyListDrawHandle);
        Service.AddonLifecycle.UnregisterListener(AddonEvent.PreFinalize, PartyListFinalizeHandle);
        Service.ClientState.TerritoryChanged -= ResetmarkedObject;
        LocalMarkingHook?.Dispose();
        ResetPartyMemberList();
        ReleaseImageNodes();

        base.Uninit();
    }

    public override void ConfigUI()
    {
        ImGui.SetNextItemWidth(200f * GlobalFontScale);
        ImGui.InputFloat2(Service.Lang.GetText("MarkerInPartyList-IconOffset"), ref _config.IconOffset, "%.1f");
        if (ImGui.IsItemDeactivatedAfterEdit())
            SaveConfig(_config);

        ImGui.SetNextItemWidth(200f * GlobalFontScale);
        ImGui.InputInt(Service.Lang.GetText("MarkerInPartyList-IconScale"), ref _config.Size, 0, 0);
        if (ImGui.IsItemDeactivatedAfterEdit())
            SaveConfig(_config);

        if (ImGui.Checkbox(Service.Lang.GetText("MarkerInPartyList-HidePartyListIndexNumber"), ref _config.HidePartyListIndexNumber))
        {
            SaveConfig(_config);
            ResetPartyMemberList();
        }
    }

    private static void ResetmarkedObject(ushort obj)
    {
        _markedObject.Clear();
        ResetPartyMemberList();
    }

    private static void ResetPartyMemberList(AtkUnitBase* partylist = null)
    {
        if (partylist is null)
            partylist = AddonState.PartyList;
        if (partylist is not null && IsAddonAndNodesReady(partylist))
            ModifyPartyMemberNumber(partylist, true);
    }

    private static void ModifyPartyMemberNumber(AtkUnitBase* pPartyList, bool visible)
    {
        if (pPartyList is null || (!_config.HidePartyListIndexNumber && !visible))
            return;

        var memberIdList = Enumerable.Range(10, 8).ToList();
        foreach (var id in memberIdList)
        {
            var member = pPartyList->GetNodeById((uint)id);
            if (member is null || member->GetComponent() is null)
                continue;

            if (!member->IsVisible)
                break;

            var textNode = member->GetComponent()->UldManager.SearchNodeById(15);
            if (textNode != null && textNode->IsVisible != visible)
                textNode->ToggleVisibility(visible);
        }
    }

    #region ImageNode

    private unsafe AtkImageNode* GenerateImageNode()
    {
        var newImageNode = (AtkImageNode*)IMemorySpace.GetUISpace()->Malloc((ulong)sizeof(AtkImageNode), 8);
        if (newImageNode == null)
        {
            Service.Log.Error("Failed to allocate memory for image parentNode");
            return null;
        }
        IMemorySpace.Memset(newImageNode, 0, (ulong)sizeof(AtkImageNode));
        newImageNode->Ctor();

        newImageNode->AtkResNode.Type = NodeType.Image;
        newImageNode->AtkResNode.NodeFlags = NodeFlags.AnchorLeft | NodeFlags.AnchorTop;
        newImageNode->AtkResNode.DrawFlags = 0;

        newImageNode->WrapMode = 1;
        newImageNode->Flags |= (byte)ImageNodeFlags.AutoFit;

        var partsList = (AtkUldPartsList*)IMemorySpace.GetUISpace()->Malloc((ulong)sizeof(AtkUldPartsList), 8);
        if (partsList == null)
        {
            Service.Log.Error("Failed to allocate memory for parts list");
            newImageNode->AtkResNode.Destroy(true);
            return null;
        }

        partsList->Id = 0;
        partsList->PartCount = 1;

        var part = (AtkUldPart*)IMemorySpace.GetUISpace()->Malloc((ulong)sizeof(AtkUldPart), 8);
        if (part == null)
        {
            Service.Log.Error("Failed to allocate memory for part");
            IMemorySpace.Free(partsList, (ulong)sizeof(AtkUldPartsList));
            newImageNode->AtkResNode.Destroy(true);
            return null;
        }

        part->U = 0;
        part->V = 0;
        part->Width = 80;
        part->Height = 80;

        partsList->Parts = part;

        var asset = (AtkUldAsset*)IMemorySpace.GetUISpace()->Malloc((ulong)sizeof(AtkUldAsset), 8);
        if (asset == null)
        {
            Service.Log.Error("Failed to allocate memory for asset");
            IMemorySpace.Free(part, (ulong)sizeof(AtkUldPart));
            IMemorySpace.Free(partsList, (ulong)sizeof(AtkUldPartsList));
            newImageNode->AtkResNode.Destroy(true);
            return null;
        }

        asset->Id = 0;
        asset->AtkTexture.Ctor();

        part->UldAsset = asset;

        newImageNode->PartsList = partsList;

        newImageNode->LoadIconTexture(DefaultIconId, 0);
        return newImageNode;
    }

    private void InitImageNodes()
    {
        var partylist = (AtkUnitBase*)Service.Gui.GetAddonByName("_PartyList");
        if (partylist is null)
        {
            Service.Log.Error("Failed to get partylist");
            return;
        }
        lock (_lock)
        {
            if (_isBuilt)
                return;

            foreach (var i in Enumerable.Range(10, 8))
            {
                var parentNode = partylist->GetNodeById((uint)i);
                if (parentNode is null)
                {
                    Service.Log.Error($"Failed to get parentNode-{i}");
                    continue;
                }

                var imageNode = GenerateImageNode();
                if (imageNode is null)
                {
                    Service.Log.Error($"Failed to create image parentNode-{i}");
                    continue;
                }
                imageNode->AtkResNode.NodeID = 114514;
                _imageNodes.Add((nint)imageNode);
                AttachToComponentNode(parentNode, imageNode);
            }
            _isBuilt = true;
        }

    }

    private void ReleaseImageNodes()
    {
        lock (_lock)
        {
            if (!_isBuilt)
                return;

            foreach (var item in _imageNodes)
            {
                Service.Log.Info($"Detach:{item:X}");
                DetachFromComponentNode((AtkImageNode*)item);
            }
            _imageNodes.Clear();
            _isBuilt = false;
        }
    }

    private void ShowImageNode(int i, int iconId)
    {
        var partylist = (AtkUnitBase*)Service.Gui.GetAddonByName("_PartyList");
        if (i is < 0 or > 7 || partylist is null || _imageNodes.Count <= i)
            return;

        var node = (AtkImageNode*)_imageNodes[i];
        if (node is null)
            return;

        node->LoadIconTexture(iconId, 0);
        (float x, float y) = (BasePosition.X + _config.IconOffset.X, BasePosition.Y + _config.IconOffset.Y);
        node->AtkResNode.SetPositionFloat(x, y);
        node->AtkResNode.SetHeight((ushort)_config.Size);
        node->AtkResNode.SetWidth((ushort)_config.Size);
        node->AtkResNode.ToggleVisibility(true);

        ModifyPartyMemberNumber(partylist, false);
    }

    private void HideImageNode(int i)
    {
        if (i is < 0 or > 7)
            return;
        var node = (AtkImageNode*)_imageNodes[i];
        if (node is null)
            return;

        node->AtkResNode.ToggleVisibility(false);
    }

    private static void AttachToComponentNode(AtkResNode* parent, AtkImageNode* node, bool toFront = true)
    {
        if (parent is null || node is null)
            return;

        var lastNode = parent->GetComponent()->UldManager.RootNode;
        node->AtkResNode.ParentNode = parent;
        if (lastNode is null)
            parent->GetComponent()->UldManager.RootNode = &node->AtkResNode;

        else if (toFront)
        {
            while (lastNode->PrevSiblingNode != null)
                lastNode = lastNode->PrevSiblingNode;

            node->AtkResNode.NextSiblingNode = lastNode;
            lastNode->PrevSiblingNode = &node->AtkResNode;
        }
        else
        {
            node->AtkResNode.PrevSiblingNode = lastNode;
            lastNode->NextSiblingNode = &node->AtkResNode;
            parent->GetComponent()->UldManager.RootNode = &node->AtkResNode;
        }
        parent->GetComponent()->UldManager.UpdateDrawNodeList();
    }

    private static void DetachFromComponentNode(AtkImageNode* node)
    {
        if (node is null)
            return;

        if (node->AtkResNode.ParentNode->GetComponent()->UldManager.RootNode == node)
            node->AtkResNode.ParentNode->GetComponent()->UldManager.RootNode = node->AtkResNode.PrevSiblingNode;

        if (node->AtkResNode.NextSiblingNode != null && node->AtkResNode.NextSiblingNode->PrevSiblingNode == node)
            node->AtkResNode.NextSiblingNode->PrevSiblingNode = node->AtkResNode.PrevSiblingNode;

        if (node->AtkResNode.PrevSiblingNode != null && node->AtkResNode.PrevSiblingNode->NextSiblingNode == node)
            node->AtkResNode.PrevSiblingNode->NextSiblingNode = node->AtkResNode.NextSiblingNode;

        node->AtkResNode.ParentNode->GetComponent()->UldManager.UpdateDrawNodeList();
    }

    #endregion

    #region Handle

    private void PartyListDrawHandle(AddonEvent type, AddonArgs args)
    {
        if (!_isBuilt)
            InitImageNodes();

        if (_needClear && _markedObject.Count is 0)
        {
            ResetPartyMemberList((AtkUnitBase*)args.Addon);
            _needClear = false;
        }
    }

    private void PartyListFinalizeHandle(AddonEvent type, AddonArgs args)
    {
        ReleaseImageNodes();
    }

    private void ProcMarkIconSetted(uint markIndex, uint objectId)
    {
        var icon = MarkerSheet.ElementAt((int)(markIndex + 1));
        int outValue;
        if (icon is null)

            return;

        if (objectId is 0xE000_0000 or 0xE00_0000)
        {
            if (_markedObject.Remove(icon.Icon, out outValue))
                HideImageNode(outValue);
            if (_markedObject.Count == 0)
                _needClear = true;
            return;
        }

        if (AgentHUD.Instance() is null || InfoProxyCrossRealm.Instance() is null)
            return;

        var pAgentHUD = AgentHUD.Instance();
        for (var i = 0; i < 8; ++i)
        {
            var offset = i * Marshal.SizeOf<PartyListCharInfo>();
            var pCharData = pAgentHUD->PartyMemberList + offset;
            var charData = *(PartyListCharInfo*)pCharData;
            if (objectId == charData.ObjectID)
            {
                if (_markedObject.ContainsValue(i))
                {
                    _markedObject.Remove(_markedObject.First(x => x.Value == i).Key);
                    HideImageNode(i);
                }
                _markedObject[icon.Icon] = i;
                ShowImageNode(i, icon.Icon);
                _needClear = false;
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
                    HideImageNode(pGroupMember->MemberIndex);
                }
                _markedObject[icon.Icon] = pGroupMember->MemberIndex;
                ShowImageNode(pGroupMember->MemberIndex, icon.Icon);
                _needClear = false;
                return;
            }

        }

        if (_markedObject.Remove(icon.Icon, out outValue))
            HideImageNode(outValue);
        if (_markedObject.Count == 0)
            _needClear = true;
    }

    #endregion

    #region Hook

    private nint DetourLocalMarkingFunc(nint manager, uint markingType, nint objectId, nint a4)
    {
        ProcMarkIconSetted(markingType, (uint)objectId);

        return LocalMarkingHook!.Original(manager, markingType, objectId, a4);
    }

    #endregion

    private class Config : ModuleConfiguration
    {
        public Vector2 IconOffset = new(0, 0);
        public int Size = 27;
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
}
