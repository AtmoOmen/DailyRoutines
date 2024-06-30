using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DailyRoutines.Helpers;
using DailyRoutines.Infos;
using DailyRoutines.Managers;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using ImGuiNET;
using Lumina.Excel.GeneratedSheets;

namespace DailyRoutines.Modules;

[ModuleDescription("AutoSplitStacksTitle", "AutoSplitStacksDescription", ModuleCategories.一般)]
public unsafe class AutoSplitStacks : DailyModuleBase
{
    private static readonly InventoryType[] InventoryTypes =
    [
        InventoryType.Inventory1, InventoryType.Inventory2, InventoryType.Inventory3,
        InventoryType.Inventory4,
    ];

    private static readonly MenuItem FastSplitItem = new()
    {
        IsEnabled = true,
        IsReturn = false,
        UseDefaultPrefix = true,
        Name = WithDRPrefix(Service.Lang.GetText("AutoSplitStacks-FastSplit")),
        IsSubmenu = false,
        PrefixColor = 34,
    };

    private const string Command = "/pdrsplit";
    private static readonly Vector2 CheckboxSize = ScaledVector2(20f);

    private static Config ModuleConfig = null!;

    private static Dictionary<string, Item>? ItemNames;
    private static Dictionary<string, Item> _ItemNames = [];
    private static Item? SelectedItem;
    private static string ItemSearchInput = string.Empty;
    private static int SplitAmountInput = 1;

    private static uint FastSplitItemID;

    public override void Init()
    {
        ModuleConfig = LoadConfig<Config>() ?? new();

        ItemNames ??= LuminaCache.Get<Item>()
                                 .Where(x => x.FilterGroup != 11 && x.FilterGroup != 16 &&
                                             x.StackSize > 1 &&
                                             !string.IsNullOrEmpty(x.Name.RawString))
                                 .GroupBy(x => x.Name.RawString)
                                 .ToDictionary(x => x.Key, x => x.First());

        _ItemNames = ItemNames.Take(10).ToDictionary(x => x.Key, x => x.Value);

        TaskHelper ??= new() { AbortOnTimeout = true };
        Overlay ??= new(this);
        FastSplitItem.OnClicked = OnFastSplit;

        if (ModuleConfig.AddCommand)
        {
            Service.CommandManager.AddCommand(Command, new(OnCommand)
            {
                HelpMessage = Service.Lang.GetText("AutoSplitStacks-CommandHelp"),
            });
        }

        Service.ContextMenu.OnMenuOpened += OnMenuOpened;
    }

    public override void ConfigUI()
    {
        if (ImGui.Checkbox(Service.Lang.GetText("AutoSplitStacks-AddCommand", Command), ref ModuleConfig.AddCommand))
        {
            SaveConfig(ModuleConfig);
            if (ModuleConfig.AddCommand)
            {
                Service.CommandManager.AddCommand(Command, new(OnCommand)
                {
                    HelpMessage = Service.Lang.GetText("AutoSplitStacks-CommandHelp"),
                });
            }
            else
                Service.CommandManager.RemoveCommand(Command);
        }

        ImGuiOm.HelpMarker(Service.Lang.GetText("AutoSplitStacks-CommandHelp"));

        if (ImGui.BeginTable("SplitItem", 4))
        {
            ImGui.TableSetupColumn("勾选框", ImGuiTableColumnFlags.WidthFixed, CheckboxSize.X);
            ImGui.TableSetupColumn("名称", ImGuiTableColumnFlags.None, 30);
            ImGui.TableSetupColumn("数量", ImGuiTableColumnFlags.WidthFixed, ImGui.CalcTextSize("四个汉字").X);
            ImGui.TableSetupColumn("操作", ImGuiTableColumnFlags.None, 10);

            ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
            ImGui.TableNextColumn();
            if (ImGuiOm.SelectableIconCentered("AddNewGroup", FontAwesomeIcon.Plus))
                ImGui.OpenPopup("AddNewGroupPopup");

            if (ImGui.BeginPopup("AddNewGroupPopup"))
            {
                ImGui.BeginGroup();
                ImGui.AlignTextToFramePadding();
                ImGui.TextColored(ImGuiColors.DalamudOrange, $"{Service.Lang.GetText("Item")}:");

                ImGui.SameLine();
                ImGui.SetNextItemWidth(200f * GlobalFontScale);
                if (ImGui.BeginCombo("###ItemSelectCombo", SelectedItem == null ? "" : SelectedItem.Name.RawString))
                {
                    ImGui.SetNextItemWidth(-1f);
                    ImGui.InputTextWithHint("###ItemSearchInput", Service.Lang.GetText("PleaseSearch"),
                                            ref ItemSearchInput, 100);

                    if (ImGui.IsItemDeactivatedAfterEdit())
                    {
                        if (!string.IsNullOrWhiteSpace(ItemSearchInput) && ItemSearchInput.Length > 1)
                        {
                            _ItemNames = ItemNames
                                         .Where(x => x.Key.Contains(ItemSearchInput,
                                                                    StringComparison.OrdinalIgnoreCase))
                                         .ToDictionary(x => x.Key, x => x.Value);
                        }
                    }

                    foreach (var (itemName, item) in _ItemNames)
                    {
                        var icon = ImageHelper.GetIcon(item.Icon).ImGuiHandle;
                        if (ImGuiOm.SelectableImageWithText(icon, ScaledVector2(24),
                                                            itemName, item == SelectedItem))
                            SelectedItem = item;
                    }

                    ImGui.EndCombo();
                }

                ImGui.AlignTextToFramePadding();
                ImGui.TextColored(ImGuiColors.DalamudOrange, $"{Service.Lang.GetText("Amount")}:");

                ImGui.SameLine();
                ImGui.SetNextItemWidth(200f * GlobalFontScale);
                if (ImGui.InputInt("###SplitAmountInput", ref SplitAmountInput, 0, 0))
                    SplitAmountInput = Math.Clamp(SplitAmountInput, 1, 998);

                ImGui.EndGroup();

                var itemSize = ImGui.GetItemRectSize();

                ImGui.SameLine();
                ImGui.BeginDisabled(SelectedItem == null);
                if (ImGuiOm.ButtonIconWithTextVertical(FontAwesomeIcon.Plus, Service.Lang.GetText("Add"),
                                                       buttonSize:new(ImGui.CalcTextSize("三个字").X, itemSize.Y)))
                {
                    var newGroup = new SplitGroup(SelectedItem.RowId, SplitAmountInput);
                    if (!ModuleConfig.SplitGroups.Contains(newGroup))
                    {
                        ModuleConfig.SplitGroups.Add(newGroup);
                        SaveConfig(ModuleConfig);
                    }
                }

                ImGui.EndDisabled();

                ImGui.EndPopup();
            }

            ImGui.TableNextColumn();
            ImGui.Text(Service.Lang.GetText("Item"));

            ImGui.TableNextColumn();
            ImGui.Text(Service.Lang.GetText("AutoSplitStacks-SplitAmount"));

            foreach (var group in ModuleConfig.SplitGroups.ToList())
            {
                ImGui.TableNextRow();

                ImGui.TableNextColumn();
                var isEnabled = group.IsEnabled;
                if (ImGui.Checkbox($"###IsEnabled_{group.ItemID}", ref isEnabled))
                {
                    var index = ModuleConfig.SplitGroups.IndexOf(group);
                    ModuleConfig.SplitGroups[index].IsEnabled = isEnabled;
                    SaveConfig(ModuleConfig);
                }

                ImGui.TableNextColumn();
                var icon = ImageHelper.GetIcon(LuminaCache.GetRow<Item>(group.ItemID).Icon);
                var name = LuminaCache.GetRow<Item>(group.ItemID).Name.RawString;
                ImGuiOm.TextImage(name, icon.ImGuiHandle, ScaledVector2(24f));

                ImGui.TableNextColumn();
                ImGuiOm.Selectable(group.Amount.ToString());

                if (ImGui.BeginPopupContextItem($"{group.ItemID}_AmountEdit"))
                {
                    if (ImGui.IsWindowAppearing())
                        SplitAmountInput = group.Amount;

                    ImGui.TextColored(ImGuiColors.DalamudOrange, $"{Service.Lang.GetText("Amount")}:");

                    ImGui.SameLine();
                    ImGui.SetNextItemWidth(150f * GlobalFontScale);
                    ImGui.InputInt($"###{group.ItemID}AmountEdit", ref SplitAmountInput, 0, 0);
                    if (ImGui.IsItemDeactivatedAfterEdit())
                    {
                        var index = ModuleConfig.SplitGroups.IndexOf(group);
                        ModuleConfig.SplitGroups[index].Amount = SplitAmountInput;
                        SaveConfig(ModuleConfig);
                    }

                    ImGui.EndPopup();
                }

                ImGui.TableNextColumn();
                if (ImGuiOm.ButtonIcon($"{group.ItemID}_Enqueue", FontAwesomeIcon.Play, Service.Lang.GetText("Execute")))
                    EnqueueSplit(group);

                ImGui.SameLine();
                if (ImGuiOm.ButtonIcon($"{group.ItemID}_Delete", FontAwesomeIcon.TrashAlt,
                                       Service.Lang.GetText("AutoSplitStacks-HoldCtrlToDelete")))
                {
                    if (ImGui.IsKeyDown(ImGuiKey.LeftCtrl))
                    {
                        ModuleConfig.SplitGroups.Remove(group);
                        SaveConfig(ModuleConfig);
                    }
                }
            }

            ImGui.EndTable();
        }
    }

    public override void OverlayUI()
    {
        if (ImGui.IsWindowAppearing())
            ImGui.OpenPopup($"{Service.Lang.GetText("AutoSplitStacks-FastSplit")}###FastSplitPopup");

        var isOpen = true;
        if (ImGui.BeginPopupModal($"{Service.Lang.GetText("AutoSplitStacks-FastSplit")}###FastSplitPopup", 
                                  ref isOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.Text($"{Service.Lang.GetText("AutoSplitStacks-PleaseInputSplitAmount")}:");

            ImGui.SetNextItemWidth(150f * GlobalFontScale);
            if (ImGui.InputInt("###FastSplitAmountInput", ref SplitAmountInput, 0, 0))
                SplitAmountInput = Math.Clamp(SplitAmountInput, 1, 998);

            ImGui.SameLine();
            if (ImGui.Button(Service.Lang.GetText("Confirm")))
            {
                EnqueueSplit(FastSplitItemID, SplitAmountInput);

                ImGui.CloseCurrentPopup();
                Overlay.IsOpen = false;
            }

            ImGui.SameLine();
            if (ImGui.Button(Service.Lang.GetText("Cancel")))
            {
                ImGui.CloseCurrentPopup();
                Overlay.IsOpen = false;
            }

            ImGui.EndPopup();
        }
    }

    public override void OverlayOnClose() => FastSplitItemID = 0;

    private void OnCommand(string command, string args)
    {
        args = args.Trim();
        if (string.IsNullOrWhiteSpace(args)) return;

        if (int.TryParse(args, out var itemID))
        {
            var group = ModuleConfig.SplitGroups.FirstOrDefault(x => x.ItemID == itemID);
            if (group == null) return;

            EnqueueSplit(group);
            return;
        }

        if (ItemNames.TryGetValue(args, out var item))
        {
            var group = ModuleConfig.SplitGroups.FirstOrDefault(x => x.ItemID == item.RowId);
            if (group == null) return;

            EnqueueSplit(group);
        }
    }

    private static void OnMenuOpened(IMenuOpenedArgs args)
    {
        if (args.Target is not MenuTargetInventory { TargetItem: not null } iTarget) return;
        if (iTarget.TargetItem.Value.Quantity <= 1) return;

        args.AddMenuItem(FastSplitItem);
    }

    private void OnFastSplit(IMenuItemClickedArgs args)
    {
        if (args.Target is not MenuTargetInventory { TargetItem: not null } iTarget) return;
        if (iTarget.TargetItem.Value.Quantity <= 1) return;

        FastSplitItemID = iTarget.TargetItem.Value.ItemId;
        Overlay.IsOpen = true;
    }

    private void EnqueueSplit(SplitGroup group)
        => EnqueueSplit(group.ItemID, group.Amount);

    private void EnqueueSplit(uint itemID, int amount)
    {
        TaskHelper.Enqueue(() => ClickItemToSplit(itemID, amount));
        TaskHelper.DelayNext($"SplitRound_{itemID}_{amount}", 100);
        TaskHelper.Enqueue(() => EnqueueSplit(itemID, amount));
    }

    private bool? ClickItemToSplit(uint itemID, int amount)
    {
        if (AddonState.InputNumeric != null || itemID == 0 || amount == 0)
        {
            TaskHelper.Abort();
            return true;
        }

        var agent = AgentInventoryContext.Instance();
        var manager = InventoryManager.Instance();
        var agentInventory = AgentModule.Instance()->GetAgentByInternalId(AgentId.Inventory);
        var addon = RaptureAtkUnitManager.Instance()->GetAddonById((ushort)agentInventory->AddonId);

        if (agent == null || manager == null || agentInventory == null || addon == null || !addon->IsVisible)
        {
            addon->Open(1);
            return false;
        }

        if (IsInventoryFull())
        {
            TaskHelper.Abort();
            NotifyHelper.NotificationWarning(Service.Lang.GetText("AutoSplitStacks-Notification-FullInventory"));
            return true;
        }

        var foundTypes
            = InventoryTypes.Where(type => manager->GetInventoryContainer(type) != null && 
                                                    manager->GetInventoryContainer(type)->Loaded != 0 &&
                                                    manager->GetItemCountInContainer(itemID, type) + 
                                                    manager->GetItemCountInContainer(itemID, type, true) > amount)
                            .ToList();
        if (foundTypes.Count <= 0)
        {
            TaskHelper.Abort();
            NotifyHelper.NotificationWarning(Service.Lang.GetText("AutoSplitStacks-Notification-ItemNoFound"));
            return true;
        }

        foreach (var type in foundTypes)
        {
            var container = manager->GetInventoryContainer(type);
            int? foundSlot = null;
            for (var i = 0; i < container->Size; i++)
            {
                var slot = container->GetInventorySlot(i);
                if (slot->ItemId == itemID)
                {
                    if (slot->GetQuantity() > amount)
                    {
                        foundSlot = i;
                        break;
                    }
                }
            }

            if (foundSlot == null) continue;

            agent->OpenForItemSlot(type, (int)foundSlot, agentInventory->AddonId);
            EnqueueOperations(itemID, type, (int)foundSlot, amount);
            return true;
        }

        TaskHelper.Abort();
        NotifyHelper.NotificationWarning(Service.Lang.GetText("AutoSplitStacks-Notification-ItemNoFound"));
        return true;
    }

    private void EnqueueOperations(uint itemID, InventoryType foundType, int foundSlot, int amount)
    {
        TaskHelper.DelayNext($"ContextMenu_{itemID}_{foundType}_{foundSlot}", 20, false, 2);
        TaskHelper.Enqueue(() =>
        {
            ClickHelper.ContextMenu(LuminaCache.GetRow<Addon>(92).Text.RawString);
            return true;
        }, null, 2);

        TaskHelper.DelayNext($"InputNumeric_{itemID}_{foundType}_{foundSlot}", 20, false, 2);
        TaskHelper.Enqueue(() =>
        {
            if (AddonState.InputNumeric == null || !IsAddonAndNodesReady(AddonState.InputNumeric)) return false;

            Callback(AddonState.InputNumeric, true, amount);
            return true;
        }, null, 2);
    }

    private static bool IsInventoryFull()
    {
        var manager = InventoryManager.Instance();
        if (manager == null) return true;

        foreach (var inventoryType in InventoryTypes)
        {
            var container = manager->GetInventoryContainer(inventoryType);
            if (container == null || container->Loaded == 0) continue;

            for (var index = 0; index < container->Size; index++)
            {
                var slot = container->GetInventorySlot(index);
                if (slot->ItemId == 0) return false;
            }
        }

        return true;
    }

    public override void Uninit()
    {
        Service.CommandManager.RemoveCommand(Command);
        Service.ContextMenu.OnMenuOpened -= OnMenuOpened;

        base.Uninit();
    }

    private class Config : ModuleConfiguration
    {
        public List<SplitGroup> SplitGroups = [];
        public bool AddCommand = true;
    }

    private class SplitGroup : IEquatable<SplitGroup>
    {
        public bool IsEnabled { get; set; } = true;
        public uint ItemID    { get; set; }
        public int  Amount    { get; set; }

        public SplitGroup() { }

        public SplitGroup(uint itemID, int amount)
        {
            ItemID = itemID;
            Amount = amount;
        }

        public bool Equals(SplitGroup? other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;

            return ItemID == other.ItemID;
        }

        public override bool Equals(object? obj)
        {
            if (obj is null) return false;
            if (ReferenceEquals(this, obj)) return true;

            return obj.GetType() == GetType() && Equals((SplitGroup)obj);
        }

        public override int GetHashCode() => (int)ItemID;
    }
}
