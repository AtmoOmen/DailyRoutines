using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using DailyRoutines.Managers;
using FFXIVClientStructs.FFXIV.Client.System.Memory;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ValueType = FFXIVClientStructs.FFXIV.Component.GUI.ValueType;

namespace DailyRoutines.Helpers;

public unsafe class AddonHelper
{
    public record PartInfo(ushort U, ushort V, ushort Width, ushort Height);

    private delegate byte FireCallbackDelegate(AtkUnitBase* Base, int valueCount, AtkValue* values, byte updateState);
    private static FireCallbackDelegate? FireCallback;

    public delegate int GetAtkValueIntDelegate(nint address);
    public static GetAtkValueIntDelegate? GetAtkValueInt;

    public delegate byte* GetAtkValueStringDelegate(nint address);
    public static GetAtkValueStringDelegate? GetAtkValueString;

    public delegate uint GetAtkValueUIntDelegate(nint address);
    public static GetAtkValueUIntDelegate? GetAtkValueUInt;

    public delegate AtkUnitBase* GetAddonByNodeDelegate(nint atkstageInstance, AtkComponentNode* ownerNode);
    public static GetAddonByNodeDelegate? GetAddonByNode;

    public delegate void SetComponentButtonCheckedDelegate(AtkComponentButton* button, bool isChecked);
    public static SetComponentButtonCheckedDelegate? SetComponentButtonChecked;

    internal static void Init()
    {
        FireCallback ??=
            Marshal.GetDelegateForFunctionPointer<FireCallbackDelegate>(
                Service.SigScanner.ScanText("48 89 5C 24 ?? 48 89 6C 24 ?? 48 89 74 24 ?? 57 48 81 EC ?? ?? ?? ?? 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 44 24 ?? 0F B7 81"));

        GetAtkValueInt ??= Marshal.GetDelegateForFunctionPointer<GetAtkValueIntDelegate>
            (Service.SigScanner.ScanText("F6 01 ?? 75 ?? 33 C0 C3 8B 41 ?? C3 CC CC CC CC CC CC CC CC CC CC CC CC CC CC CC CC CC CC CC CC F6 01 ?? 75 ?? 33 C0"));

        GetAtkValueString ??= Marshal.GetDelegateForFunctionPointer<GetAtkValueStringDelegate>(
            Service.SigScanner.ScanText("E8 ?? ?? ?? ?? 33 D2 48 8D 8B ?? ?? ?? ?? 41 B8 ?? ?? ?? ?? 48 8B F8"));

        GetAtkValueUInt ??=
            Marshal.GetDelegateForFunctionPointer<GetAtkValueUIntDelegate>(
                Service.SigScanner.ScanText("F6 01 ?? 75 ?? 33 C0 C3 8B 41 ?? C3 CC CC CC CC CC CC CC CC CC CC CC CC CC CC CC CC CC CC CC CC F6 01 ?? 75 ?? 33 C0 C3 48 8B 41 ?? C3 CC CC CC CC CC CC CC CC CC CC CC CC CC CC CC CC CC CC CC F6 01 ?? 75 ?? 0F 57 C0"));

        GetAddonByNode ??= Marshal.GetDelegateForFunctionPointer<GetAddonByNodeDelegate>
            (Service.SigScanner.ScanText("48 89 5C 24 ?? 4C 8B D2 4C 8B D9"));

        SetComponentButtonChecked ??= Marshal.GetDelegateForFunctionPointer<SetComponentButtonCheckedDelegate>
            (Service.SigScanner.ScanText("E8 ?? ?? ?? ?? 8D 46 12 ?? ?? ?? ?? ?? ?? ?? ??"));
    }

    public static bool IsScreenReady()
    {
        if (TryGetAddonByName<AtkUnitBase>("NowLoading", out var addon0) && addon0->IsVisible) return false;
        if (TryGetAddonByName<AtkUnitBase>("FadeMiddle", out var addon1) && addon1->IsVisible) return false;
        if (TryGetAddonByName<AtkUnitBase>("FadeBack", out var addon2) && addon2->IsVisible) return false;

        return true;
    }

    public static bool TryGetAddonByName<T>(string addonName, out T* addonPtr) where T : unmanaged
    {
        var a = Service.Gui.GetAddonByName(addonName);
        if (a == nint.Zero)
        {
            addonPtr = null;
            return false;
        }

        addonPtr = (T*)a;
        return true;
    }

    public static T* GetAddonByName<T>(string addonName) where T : unmanaged
    {
        var a = Service.Gui.GetAddonByName(addonName);
        if (a == nint.Zero) return null;

        return (T*)a;
    }

    public static IntPtr Alloc(ulong size) => new(IMemorySpace.GetUISpace()->Malloc(size, 8UL));

    public static IntPtr Alloc(int size)
    {
        if (size <= 0) throw new ArgumentException("Allocation size must be positive.");
        return Alloc((ulong)size);
    }

    public static void SetSize(AtkResNode* node, int? width, int? height)
    {
        if (width is >= ushort.MinValue and <= ushort.MaxValue) node->Width = (ushort)width.Value;
        if (height is >= ushort.MinValue and <= ushort.MaxValue) node->Height = (ushort)height.Value;
        node->DrawFlags |= 0x1;
    }

    public static void SetPosition(AtkResNode* node, float? x, float? y)
    {
        if (x != null) node->X = x.Value;
        if (y != null) node->Y = y.Value;
        node->DrawFlags |= 0x1;
    }

    public static void SetPosition(AtkUnitBase* atkUnitBase, float? x, float? y)
    {
        if (x is >= short.MinValue and <= short.MaxValue) atkUnitBase->X = (short)x.Value;
        if (y >= short.MinValue && x <= short.MaxValue) atkUnitBase->Y = (short)y.Value;
    }

    public static void SetWindowSize(AtkComponentNode* windowNode, ushort? width, ushort? height)
    {
        if (((AtkUldComponentInfo*)windowNode->Component->UldManager.Objects)->ComponentType !=
            ComponentType.Window) return;

        width ??= windowNode->AtkResNode.Width;
        height ??= windowNode->AtkResNode.Height;

        if (width < 64) width = 64;
        if (height < 16) height = 16;

        SetSize(windowNode, width, height);
        var n = windowNode->Component->UldManager.RootNode;
        SetSize(n, width, height);
        n = n->PrevSiblingNode;
        SetSize(n, (ushort)(width - 14), null);
        n = n->PrevSiblingNode;
        SetSize(n, width, height);
        n = n->PrevSiblingNode;
        SetSize(n, width, height);
        n = n->PrevSiblingNode;
        if (Service.GameConfig.System.GetUInt("ColorThemeType") == 3)
            SetSize(n, width - 8, height - 16);
        else
            SetSize(n, width, height);

        n = n->PrevSiblingNode;
        SetSize(n, (ushort)(width - 5), null); // Header Node
        n = n->ChildNode;
        SetSize(n, (ushort)(width - 20), null); // Header Seperator
        n = n->PrevSiblingNode;
        SetPosition(n, width - 33, 6); // Close Button
        n = n->PrevSiblingNode;
        SetPosition(n, width - 47, 8); // Gear Button
        n = n->PrevSiblingNode;
        SetPosition(n, width - 61, 8); // Help Button

        windowNode->AtkResNode.DrawFlags |= 0x1;
    }

    public static void SetSize<T>(T* node, int? w, int? h) where T : unmanaged => SetSize((AtkResNode*)node, w, h);

    public static void SetPosition<T>(T* node, float? x, float? y) where T : unmanaged =>
        SetPosition((AtkResNode*)node, x, y);

    public static T* CloneNode<T>(T* original) where T : unmanaged => (T*)CloneNode((AtkResNode*)original);

    public static void ExpandNodeList(AtkComponentNode* componentNode, ushort addSize)
    {
        var newNodeList = ExpandNodeList(componentNode->Component->UldManager.NodeList,
                                         componentNode->Component->UldManager.NodeListCount,
                                         (ushort)(componentNode->Component->UldManager.NodeListCount + addSize));

        componentNode->Component->UldManager.NodeList = newNodeList;
    }

    public static void ExpandNodeList(AtkUnitBase* atkUnitBase, ushort addSize)
    {
        var newNodeList = ExpandNodeList(atkUnitBase->UldManager.NodeList, atkUnitBase->UldManager.NodeListCount,
                                         (ushort)(atkUnitBase->UldManager.NodeListCount + addSize));

        atkUnitBase->UldManager.NodeList = newNodeList;
    }

    private static AtkResNode** ExpandNodeList(AtkResNode** originalList, ushort originalSize, ushort newSize = 0)
    {
        if (newSize <= originalSize) newSize = (ushort)(originalSize + 1);
        var oldListPtr = new IntPtr(originalList);
        var newListPtr = Alloc((ulong)((newSize + 1) * 8));
        var clone = new IntPtr[originalSize];
        Marshal.Copy(oldListPtr, clone, 0, originalSize);
        Marshal.Copy(clone, 0, newListPtr, originalSize);
        return (AtkResNode**)newListPtr;
    }

    public static AtkResNode* CloneNode(AtkResNode* original)
    {
        var size = original->Type switch
        {
            NodeType.Res => sizeof(AtkResNode),
            NodeType.Image => sizeof(AtkImageNode),
            NodeType.Text => sizeof(AtkTextNode),
            NodeType.NineGrid => sizeof(AtkNineGridNode),
            NodeType.Counter => sizeof(AtkCounterNode),
            NodeType.Collision => sizeof(AtkCollisionNode),
            _ => throw new Exception($"Unsupported Type: {original->Type}"),
        };

        var allocation = Alloc((ulong)size);
        var bytes = new byte[size];
        Marshal.Copy(new IntPtr(original), bytes, 0, bytes.Length);
        Marshal.Copy(bytes, 0, allocation, bytes.Length);

        var newNode = (AtkResNode*)allocation;
        newNode->ParentNode = null;
        newNode->ChildNode = null;
        newNode->ChildCount = 0;
        newNode->PrevSiblingNode = null;
        newNode->NextSiblingNode = null;
        return newNode;
    }

    #region Callback

    public static void CallbackRaw(AtkUnitBase* Base, int valueCount, AtkValue* values, byte updateState = 0)
    {
        if (FireCallback == null) Init();
        FireCallback(Base, valueCount, values, updateState);
    }

    public static void Callback(AtkUnitBase* Base, bool updateState, params object[] values)
    {
        if (Base == null) throw new Exception("Null UnitBase");
        var atkValues = CreateAtkValueArray(values);
        if (atkValues == null) return;

        try
        {
            CallbackRaw(Base, values.Length, atkValues, (byte)(updateState ? 1 : 0));
        } finally
        {
            for (var i = 0; i < values.Length; i++)
                if (atkValues[i].Type == ValueType.String)
                    Marshal.FreeHGlobal(new nint(atkValues[i].String));

            Marshal.FreeHGlobal(new nint(atkValues));
        }
    }

    public static AtkValue* CreateAtkValueArray(params object[] values)
    {
        var atkValues = (AtkValue*)Marshal.AllocHGlobal(values.Length * sizeof(AtkValue));
        if (atkValues == null) return null;
        try
        {
            for (var i = 0; i < values.Length; i++)
            {
                var v = values[i];
                switch (v)
                {
                    case uint uintValue:
                        atkValues[i].Type = ValueType.UInt;
                        atkValues[i].UInt = uintValue;
                        break;
                    case int intValue:
                        atkValues[i].Type = ValueType.Int;
                        atkValues[i].Int = intValue;
                        break;
                    case float floatValue:
                        atkValues[i].Type = ValueType.Float;
                        atkValues[i].Float = floatValue;
                        break;
                    case bool boolValue:
                        atkValues[i].Type = ValueType.Bool;
                        atkValues[i].Byte = (byte)(boolValue ? 1 : 0);
                        break;
                    case string stringValue:
                    {
                        atkValues[i].Type = ValueType.String;
                        var stringBytes = Encoding.UTF8.GetBytes(stringValue);
                        var stringAlloc = Marshal.AllocHGlobal(stringBytes.Length + 1);
                        Marshal.Copy(stringBytes, 0, stringAlloc, stringBytes.Length);
                        Marshal.WriteByte(stringAlloc, stringBytes.Length, 0);
                        atkValues[i].String = (byte*)stringAlloc;
                        break;
                    }
                    default:
                        throw new ArgumentException($"Unable to convert type {v.GetType()} to AtkValue");
                }
            }
        }
        catch
        {
            return null;
        }

        return atkValues;
    }

    public static string DecodeValues(int cnt, AtkValue* values)
    {
        var atkValueList = new List<string>();
        try
        {
            for (var i = 0; i < cnt; i++) atkValueList.Add(DecodeValue(values[i]));
        }
        catch (Exception e)
        {
            NotifyHelper.Error("", e);
        }

        return atkValueList.Join("\n");
    }

    public static string DecodeValue(AtkValue a)
    {
        var str = new StringBuilder(a.Type.ToString()).Append(": ");
        switch (a.Type)
        {
            case ValueType.Int:
            {
                str.Append(a.Int);
                break;
            }
            case ValueType.String:
            {
                str.Append(Marshal.PtrToStringUTF8(new nint(a.String)));
                break;
            }
            case ValueType.UInt:
            {
                str.Append(a.UInt);
                break;
            }
            case ValueType.Bool:
            {
                str.Append(a.Byte != 0);
                break;
            }
            default:
            {
                str.Append($"Unknown Type: {a.Int}");
                break;
            }
        }

        return str.ToString();
    }

    #endregion

    #region NodeManagement

    public static AtkTextNode* MakeTextNode(uint id) { return !TryMakeTextNode(id, out var textNode) ? null : textNode; }

    public static AtkImageNode* MakeImageNode(uint id, PartInfo partInfo)
    {
        if (!TryMakeImageNode(id, 0, 0, 0, 0, out var imageNode))
        {
            Service.Log.Error("Failed to alloc memory for AtkImageNode.");
            return null;
        }

        if (!TryMakePartsList(0, out var partsList))
        {
            Service.Log.Error("Failed to alloc memory for AtkUldPartsList.");
            FreeImageNode(imageNode);
            return null;
        }

        if (!TryMakePart(partInfo.U, partInfo.V, partInfo.Width, partInfo.Height, out var part))
        {
            Service.Log.Error("Failed to alloc memory for AtkUldPart.");
            FreePartsList(partsList);
            FreeImageNode(imageNode);
            return null;
        }

        if (!TryMakeAsset(0, out var asset))
        {
            Service.Log.Error("Failed to alloc memory for AtkUldAsset.");
            FreePart(part);
            FreePartsList(partsList);
            FreeImageNode(imageNode);
        }

        AddAsset(part, asset);
        AddPart(partsList, part);
        AddPartsList(imageNode, partsList);

        return imageNode;
    }

    public static bool TryMakeTextNode(uint id, [NotNullWhen(true)] out AtkTextNode* textNode)
    {
        textNode = IMemorySpace.GetUISpace()->Create<AtkTextNode>();

        if (textNode is not null)
        {
            textNode->AtkResNode.Type = NodeType.Text;
            textNode->AtkResNode.NodeId = id;
            return true;
        }

        return false;
    }


    public static bool TryMakeImageNode(
        uint id, NodeFlags resNodeFlags, uint resNodeDrawFlags, byte wrapMode, byte imageNodeFlags,
        [NotNullWhen(true)] out AtkImageNode* imageNode)
    {
        imageNode = IMemorySpace.GetUISpace()->Create<AtkImageNode>();

        if (imageNode is not null)
        {
            imageNode->AtkResNode.Type = NodeType.Image;
            imageNode->AtkResNode.NodeId = id;
            imageNode->AtkResNode.NodeFlags = resNodeFlags;
            imageNode->AtkResNode.DrawFlags = resNodeDrawFlags;
            imageNode->WrapMode = wrapMode;
            imageNode->Flags = imageNodeFlags;
            return true;
        }

        return false;
    }

    public static bool TryMakePartsList(uint id, [NotNullWhen(true)] out AtkUldPartsList* partsList)
    {
        partsList = (AtkUldPartsList*)IMemorySpace.GetUISpace()->Malloc((ulong)sizeof(AtkUldPartsList), 8);

        if (partsList is not null)
        {
            partsList->Id = id;
            partsList->PartCount = 0;
            partsList->Parts = null;
            return true;
        }

        return false;
    }

    public static bool TryMakePart(
        ushort u, ushort v, ushort width, ushort height, [NotNullWhen(true)] out AtkUldPart* part)
    {
        part = (AtkUldPart*)IMemorySpace.GetUISpace()->Malloc((ulong)sizeof(AtkUldPart), 8);

        if (part is not null)
        {
            part->U = u;
            part->V = v;
            part->Width = width;
            part->Height = height;
            return true;
        }

        return false;
    }

    public static bool TryMakeAsset(uint id, [NotNullWhen(true)] out AtkUldAsset* asset)
    {
        asset = (AtkUldAsset*)IMemorySpace.GetUISpace()->Malloc((ulong)sizeof(AtkUldAsset), 8);

        if (asset is not null)
        {
            asset->Id = id;
            asset->AtkTexture.Ctor();
            return true;
        }

        return false;
    }

    public static void AddPartsList(AtkImageNode* imageNode, AtkUldPartsList* partsList)
    {
        imageNode->PartsList = partsList;
    }

    public static void AddPartsList(AtkCounterNode* counterNode, AtkUldPartsList* partsList)
    {
        counterNode->PartsList = partsList;
    }

    public static void AddPart(AtkUldPartsList* partsList, AtkUldPart* part)
    {
        var oldPartArray = partsList->Parts;

        var newSize = partsList->PartCount + 1;
        var newArray = (AtkUldPart*)IMemorySpace.GetUISpace()->Malloc((ulong)sizeof(AtkUldPart) * newSize, 8);

        if (oldPartArray is not null)
        {
            foreach (var index in Enumerable.Range(0, (int)partsList->PartCount))
                Buffer.MemoryCopy(oldPartArray + index, newArray + index, sizeof(AtkUldPart), sizeof(AtkUldPart));

            IMemorySpace.Free(oldPartArray, (ulong)sizeof(AtkUldPart) * partsList->PartCount);
        }

        Buffer.MemoryCopy(part, newArray + (newSize - 1), sizeof(AtkUldPart), sizeof(AtkUldPart));
        partsList->Parts = newArray;
        partsList->PartCount = newSize;
    }

    public static void AddAsset(AtkUldPart* part, AtkUldAsset* asset) { part->UldAsset = asset; }

    public static void FreeImageNode(AtkImageNode* node)
    {
        node->AtkResNode.Destroy(false);
        IMemorySpace.Free(node, (ulong)sizeof(AtkImageNode));
    }

    public static void FreeTextNode(AtkTextNode* node)
    {
        node->AtkResNode.Destroy(false);
        IMemorySpace.Free(node, (ulong)sizeof(AtkTextNode));
    }

    public static void FreePartsList(AtkUldPartsList* partsList)
    {
        foreach (var index in Enumerable.Range(0, (int)partsList->PartCount))
        {
            var part = &partsList->Parts[index];

            FreeAsset(part->UldAsset);
            FreePart(part);
        }

        IMemorySpace.Free(partsList, (ulong)sizeof(AtkUldPartsList));
    }

    public static void FreePart(AtkUldPart* part) { IMemorySpace.Free(part, (ulong)sizeof(AtkUldPart)); }

    public static void FreeAsset(AtkUldAsset* asset) { IMemorySpace.Free(asset, (ulong)sizeof(AtkUldAsset)); }

    public static void LinkNodeAtEnd(AtkResNode* imageNode, AtkUnitBase* parent)
    {
        var node = parent->RootNode->ChildNode;
        while (node->PrevSiblingNode != null) node = node->PrevSiblingNode;

        node->PrevSiblingNode = imageNode;
        imageNode->NextSiblingNode = node;
        imageNode->ParentNode = node->ParentNode;

        parent->UldManager.UpdateDrawNodeList();
    }

    public static void UnlinkNode<T>(T* atkNode, AtkComponentNode* componentNode) where T : unmanaged
    {
        var node = (AtkResNode*)atkNode;
        if (node == null) return;

        if (node->ParentNode->ChildNode == node) node->ParentNode->ChildNode = node->NextSiblingNode;

        if (node->NextSiblingNode != null && node->NextSiblingNode->PrevSiblingNode == node)
            node->NextSiblingNode->PrevSiblingNode = node->PrevSiblingNode;

        if (node->PrevSiblingNode != null && node->PrevSiblingNode->NextSiblingNode == node)
            node->PrevSiblingNode->NextSiblingNode = node->NextSiblingNode;

        componentNode->Component->UldManager.UpdateDrawNodeList();
    }

    public static void UnlinkAndFreeImageNode(AtkImageNode* node, AtkUnitBase* parent)
    {
        if (node->AtkResNode.PrevSiblingNode is not null)
            node->AtkResNode.PrevSiblingNode->NextSiblingNode = node->AtkResNode.NextSiblingNode;

        if (node->AtkResNode.NextSiblingNode is not null)
            node->AtkResNode.NextSiblingNode->PrevSiblingNode = node->AtkResNode.PrevSiblingNode;

        parent->UldManager.UpdateDrawNodeList();

        FreePartsList(node->PartsList);
        FreeImageNode(node);
    }

    public static void UnlinkAndFreeTextNode(AtkTextNode* node, AtkUnitBase* parent)
    {
        if (node->AtkResNode.PrevSiblingNode is not null)
            node->AtkResNode.PrevSiblingNode->NextSiblingNode = node->AtkResNode.NextSiblingNode;

        if (node->AtkResNode.NextSiblingNode is not null)
            node->AtkResNode.NextSiblingNode->PrevSiblingNode = node->AtkResNode.PrevSiblingNode;

        parent->UldManager.UpdateDrawNodeList();
        FreeTextNode(node);
    }

    #endregion
}
