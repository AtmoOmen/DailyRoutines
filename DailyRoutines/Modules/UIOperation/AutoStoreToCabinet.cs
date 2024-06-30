using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using DailyRoutines.Helpers;
using DailyRoutines.Managers;
using DailyRoutines.Windows;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Interface.Colors;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ImGuiNET;
using Lumina.Excel.GeneratedSheets;

namespace DailyRoutines.Modules;

[ModuleDescription("AutoStoreToCabinetTitle", "AutoStoreToCabinetDescription", ModuleCategories.界面操作)]
public class AutoStoreToCabinet : DailyModuleBase
{
    private static readonly List<InventoryType> ValidInventoryTypes =
    [
        InventoryType.Inventory1, InventoryType.Inventory2, InventoryType.Inventory3,
        InventoryType.Inventory4, InventoryType.ArmoryBody, InventoryType.ArmoryEar, InventoryType.ArmoryFeets,
        InventoryType.ArmoryHands, InventoryType.ArmoryHead, InventoryType.ArmoryLegs, InventoryType.ArmoryRings,
        InventoryType.ArmoryNeck, InventoryType.ArmoryWrist, InventoryType.ArmoryRings, InventoryType.ArmoryMainHand,
        InventoryType.ArmoryOffHand,
    ];

    private static Dictionary<uint, uint>? CabinetItems; // Item ID - Cabinet Index

    private static CancellationTokenSource? CancelSource;
    private static bool IsOnTask;

    private static unsafe AtkUnitBase* Cabinet => (AtkUnitBase*)Service.Gui.GetAddonByName("Cabinet");

    public override void Init()
    {
        CabinetItems ??= LuminaCache.Get<Cabinet>()
                                    .Where(x => x.Item.Row > 0)
                                    .ToDictionary(x => x.Item.Row, x => x.RowId);

        CancelSource ??= new();
        Overlay ??= new Overlay(this);

        Service.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "Cabinet", OnAddon);
        Service.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, "Cabinet", OnAddon);
    }

    public override unsafe void OverlayPreDraw()
    {
        if (Cabinet == null)
            Overlay.IsOpen = false;
    }

    public override void OverlayUI()
    {
        unsafe
        {
            var addon = Cabinet;
            var pos = new Vector2(addon->GetX() + 6, addon->GetY() - ImGui.GetWindowHeight() + 6);
            ImGui.SetWindowPos(pos);
        }

        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(ImGuiColors.DalamudYellow, Service.Lang.GetText("AutoStoreToCabinetTitle"));

        ImGui.SameLine();
        ImGui.Spacing();

        ImGui.SameLine();
        ImGui.BeginDisabled(IsOnTask);
        if (ImGui.Button(Service.Lang.GetText("Start")))
        {
            IsOnTask = true;
            Task.Run(async () =>
            {
                try
                {
                    var list = ScanValidCabinetItems();
                    if (list.Count > 0)
                    {
                        foreach (var item in list)
                        {
                            Service.ExecuteCommandManager.ExecuteCommand(425, (int)item);
                            await Task.Delay(100);
                        }
                    }
                } finally
                {
                    IsOnTask = false;
                }
            }, CancelSource.Token);
        }

        ImGui.EndDisabled();

        ImGui.SameLine();
        if (ImGui.Button(Service.Lang.GetText("Stop")))
        {
            CancelSource.Cancel();
            IsOnTask = false;
        }

        ImGuiOm.HelpMarker(Service.Lang.GetText("AutoStoreToCabinet-StoreHelp"));
    }

    private static List<uint> ScanValidCabinetItems()
    {
        var list = new List<uint>();
        unsafe
        {
            var inventoryManager = InventoryManager.Instance();
            foreach (var inventory in ValidInventoryTypes)
            {
                var container = inventoryManager->GetInventoryContainer(inventory);
                if (container == null) continue;

                for (var i = 0; i < container->Size; i++)
                {
                    var slot = container->GetInventorySlot(i);
                    if (slot == null) continue;

                    var item = slot->ItemId;
                    if (item == 0) continue;

                    if (!CabinetItems.TryGetValue(item, out var index)) continue;

                    list.Add(index);
                }
            }
        }

        return list;
    }

    private void OnAddon(AddonEvent type, AddonArgs args)
    {
        Overlay.IsOpen = type switch
        {
            AddonEvent.PostSetup => true,
            AddonEvent.PreFinalize => false,
            _ => Overlay.IsOpen,
        };
    }

    public override void Uninit()
    {
        CancelSource?.Cancel();
        CancelSource?.Dispose();
        CancelSource = null;

        base.Uninit();
    }
}
