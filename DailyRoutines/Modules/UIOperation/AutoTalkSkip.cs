using DailyRoutines.Infos;
using DailyRoutines.Managers;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace DailyRoutines.Modules;

[ModuleDescription("AutoTalkSkipTitle", "AutoTalkSkipDescription", ModuleCategories.界面操作)]
public class AutoTalkSkip : DailyModuleBase
{
    public override void Init() { Service.AddonLifecycle.RegisterListener(AddonEvent.PreDraw, "Talk", OnAddonDraw); }

    public override void ConfigUI() { ConflictKeyText(); }

    private static unsafe void OnAddonDraw(AddonEvent type, AddonArgs args)
    {
        if (Service.KeyState[Service.Config.ConflictKey]) return;
        var addon = args.Addon.ToAtkUnitBase();
        var evt = stackalloc AtkEvent[1]
        {
            new()
            {
                Listener = (AtkEventListener*)addon,
                Flags = 132,
                Target = &AtkStage.Instance()->AtkEventTarget,
            },
        };
        var data = stackalloc AtkEventData[1];
        addon->ReceiveEvent(AtkEventType.MouseClick, 0, evt, data);
    }

    public override void Uninit()
    {
        Service.AddonLifecycle.UnregisterListener(OnAddonDraw);

        base.Uninit();
    }
}
