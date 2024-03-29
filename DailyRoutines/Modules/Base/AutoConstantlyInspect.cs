using DailyRoutines.Infos;
using DailyRoutines.Managers;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Interface.Internal.Notifications;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ImGuiNET;

namespace DailyRoutines.Modules;

[ModuleDescription("AutoConstantlyInspectTitle", "AutoConstantlyInspectDescription", ModuleCategories.Base)]
public class AutoConstantlyInspect : DailyModuleBase
{
    public override void Init()
    {
        Service.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "ItemInspectionResult", OnAddon);
    }

    public override void ConfigUI() => ConflictKeyText();

    private static unsafe void OnAddon(AddonEvent type, AddonArgs args)
    {
        if (Service.KeyState[Service.Config.ConflictKey])
        {
            P.PluginInterface.UiBuilder.AddNotification(Service.Lang.GetText("ConflictKey-InterruptMessage"),
                                                        "Daily Routines", NotificationType.Success);
            return;
        }

        var addon = (AtkUnitBase*)args.Addon;
        if (addon == null) return;

        var nextButton = addon->GetButtonNodeById(74);
        if (nextButton == null || !nextButton->IsEnabled) return;
        AgentManager.SendEvent(AgentId.ItemInspection, 3, 0);
        addon->Close(true);
    }

    public override void Uninit()
    {
        Service.AddonLifecycle.UnregisterListener(OnAddon);

        base.Uninit();
    }
}
