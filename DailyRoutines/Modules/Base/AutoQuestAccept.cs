using DailyRoutines.Infos;
using DailyRoutines.Managers;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Interface.Internal.Notifications;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace DailyRoutines.Modules;

[ModuleDescription("AutoQuestAcceptTitle", "AutoQuestAcceptDescription", ModuleCategories.Base)]
public class AutoQuestAccept : DailyModuleBase
{
    public override void Init()
    {
        Service.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "JournalAccept", OnAddonSetup);
    }

    public override void ConfigUI() => ConflictKeyText();

    private static unsafe void OnAddonSetup(AddonEvent type, AddonArgs args)
    {
        if (Service.KeyState[Service.Config.ConflictKey])
        {
            P.PluginInterface.UiBuilder.AddNotification(Service.Lang.GetText("ConflictKey-InterruptMessage"),
                                                        "Daily Routines", NotificationType.Success);
            return;
        }

        var addon = (AtkUnitBase*)args.Addon;
        if (addon == null) return;

        var questID = addon->AtkValues[226].UInt;
        if (questID == 0) return;

        AddonManager.Callback(addon, true, 3, questID);
    }

    public override void Uninit()
    {
        Service.AddonLifecycle.UnregisterListener(OnAddonSetup);

        base.Uninit();
    }
}
