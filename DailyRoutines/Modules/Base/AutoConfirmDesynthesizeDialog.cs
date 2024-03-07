using ClickLib.Clicks;
using DailyRoutines.Infos;
using DailyRoutines.Managers;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Memory;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace DailyRoutines.Modules;

[ModuleDescription("AutoConfirmDesynthesizeDialogTitle", "AutoConfirmDesynthesizeDialogDescription", ModuleCategories.Base)]
public unsafe class AutoConfirmDesynthesizeDialog : IDailyModule
{
    public bool Initialized { get; set; }
    public bool WithConfigUI => false;

    public void Init()
    {
        Service.AddonLifecycle.RegisterListener(AddonEvent.PreDraw, "SalvageDialog", OnAddon);
    }

    public void ConfigUI() { }

    public void OverlayUI() { }

    private static void OnAddon(AddonEvent type, AddonArgs args)
    {
        var addon = (AddonSalvageDialog*)args.Addon;
        if (addon == null) return;

        var handler = new ClickSalvageDialog();
        handler.CheckBox();
        handler.Desynthesize();
    }

    public void Uninit()
    {
        Service.AddonLifecycle.UnregisterListener(OnAddon);
    }
}