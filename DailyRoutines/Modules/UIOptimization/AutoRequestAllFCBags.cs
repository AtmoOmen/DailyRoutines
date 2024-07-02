using DailyRoutines.Infos;
using DailyRoutines.Managers;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Hooking;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace DailyRoutines.Modules;

[ModuleDescription("AutoRequestAllFCBagsTitle", "AutoRequestAllFCBagsDescription", ModuleCategories.界面优化)]
public unsafe class AutoRequestAllFCBags : DailyModuleBase
{
    private delegate bool SendInventoryRefreshDelegate(InventoryManager* instance, int inventoryType);

    [Signature("48 89 5C 24 ?? 48 89 74 24 ?? 57 48 83 EC ?? 8B DA 48 8B F1 33 D2 0F B7 FA",
               DetourName = nameof(SendInventoryRefreshDetour))]
    private static Hook<SendInventoryRefreshDelegate>? SendInventoryRefreshHook;

    private static readonly InventoryType[] FreeCompanyInventories =
    [
        InventoryType.FreeCompanyPage1, InventoryType.FreeCompanyPage2, InventoryType.FreeCompanyPage3,
        InventoryType.FreeCompanyPage4,
        InventoryType.FreeCompanyGil, InventoryType.FreeCompanyCrystals,
    ];

    public override void Init()
    {
        Service.Hook.InitializeFromAttributes(this);
        SendInventoryRefreshHook.Enable();

        Service.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "FreeCompanyChest", OnAddon);
        TaskHelper = new();
    }

    private void OnAddon(AddonEvent type, AddonArgs args)
    {
        // 预请求数据
        TaskHelper.Enqueue(() =>
        {
            foreach (var inventory in FreeCompanyInventories)
                Service.ExecuteCommandManager.ExecuteCommand(ExecuteCommandFlag.RequestInventory, (int)inventory);
        });
    }

    private static bool SendInventoryRefreshDetour(InventoryManager* instance, int inventoryType)
    {
        // 直接返回 true 防锁
        Service.ExecuteCommandManager.ExecuteCommand(ExecuteCommandFlag.RequestInventory, inventoryType);
        return true;
    }

    public override void Uninit()
    {
        Service.AddonLifecycle.UnregisterListener(OnAddon);
        base.Uninit();
    }
}
