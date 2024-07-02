/*
using DailyRoutines.Helpers;
using DailyRoutines.Managers;
using Dalamud;
using Dalamud.Hooking;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Info;

namespace DailyRoutines.Modules;

// 大部分来自 STP 的 AutoRefreshMarketPrices, 作者为: Chalkos
[ModuleDescription("AutoRefreshMarketSearchResultTitle", "AutoRefreshMarketSearchResultDescription",
                   ModuleCategories.界面优化)]
public unsafe class AutoRefreshMarketSearchResult : DailyModuleBase
{
    [Signature("E8 ?? ?? ?? ?? 8B 5B 04 85 DB", DetourName = nameof(HandlePricesDetour))]
    private readonly Hook<HandlePricesDelegate>? HandlePricesHook;

    private nint waitMessageCodeChangeAddress = nint.Zero;
    private bool waitMessageCodeError;
    private byte[] waitMessageCodeOriginalBytes = new byte[5];


    public override void Init()
    {
        Service.Hook.InitializeFromAttributes(this);
        HandlePricesHook?.Enable();

        waitMessageCodeError = false;
        waitMessageCodeChangeAddress = Service.SigScanner.ScanText(
            "BA ?? ?? ?? ?? E8 ?? ?? ?? ?? 4C 8B C0 BA ?? ?? ?? ?? 48 8B CE E8 ?? ?? ?? ?? 45 33 C9");

        if (SafeMemory.ReadBytes(waitMessageCodeChangeAddress, 5, out waitMessageCodeOriginalBytes))
        {
            if (!SafeMemory.WriteBytes(waitMessageCodeChangeAddress, [0xBA, 0xB9, 0x1A, 0x00, 0x00]))
            {
                waitMessageCodeError = true;
                Service.Log.Error("Failed to write new instruction");
            }
        }
        else
        {
            waitMessageCodeError = true;
            Service.Log.Error("Failed to read original instruction");
        }

        TaskHelper ??= new TaskHelper { AbortOnTimeout = true, TimeLimitMS = 5000, ShowDebug = false };
    }

    private long HandlePricesDetour(void* unk1, void* unk2, void* unk3, void* unk4, void* unk5, void* unk6, void* unk7)
    {
        var result = HandlePricesHook.Original.Invoke(unk1, unk2, unk3, unk4, unk5, unk6, unk7);
        if (result != 1)
            TaskHelper.Enqueue(RefreshPrices);

        return result;
    }

    private static void RefreshPrices()
    {
        if (!TryGetAddonByName<AddonItemSearchResult>("ItemSearchResult", out var addonItemSearchResult)) return;
        if (!AddonItemSearchResultThrottled(addonItemSearchResult)) return;
        ((InfoProxyItemSearch*)InfoModule.Instance()->GetInfoProxyById(InfoProxyId.ItemSearch))->RequestData();
    }

    private static bool AddonItemSearchResultThrottled(AddonItemSearchResult* addon)
    {
        return addon != null
               && addon->ErrorMessage != null
               && addon->ErrorMessage->AtkResNode.IsVisible()
               && addon->HitsMessage != null
               && !addon->HitsMessage->AtkResNode.IsVisible();
    }

    public override void Uninit()
    {
        if (!waitMessageCodeError &&
            !SafeMemory.WriteBytes(waitMessageCodeChangeAddress, waitMessageCodeOriginalBytes))
            Service.Log.Error("Failed to write original instruction");

        base.Uninit();
    }

    private delegate long HandlePricesDelegate(
        void* unk1, void* unk2, void* unk3, void* unk4, void* unk5, void* unk6,
        void* unk7);
}
*/
