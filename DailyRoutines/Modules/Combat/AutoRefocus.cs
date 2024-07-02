using DailyRoutines.Infos;
using DailyRoutines.Managers;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Client.Game.Control;

namespace DailyRoutines.Modules;

[ModuleDescription("AutoRefocusTitle", "AutoRefocusDescription", ModuleCategories.战斗)]
public unsafe class AutoRefocus : DailyModuleBase
{
    private delegate void SetFocusTargetByObjectIDDelegate(TargetSystem* targetSystem, ulong objectID);
    [Signature("E8 ?? ?? ?? ?? BA 0C 00 00 00 48 8D 0D", DetourName = nameof(SetFocusTargetByObjectID))]
    private static Hook<SetFocusTargetByObjectIDDelegate>? SetFocusTargetByObjectIDHook;

    private static ulong FocusTarget;
    private static bool IsNeedToRefocus;

    public override void Init()
    {
        Service.Hook.InitializeFromAttributes(this);
        SetFocusTargetByObjectIDHook?.Enable();

        if (Flags.BoundByDuty) OnZoneChange(Service.ClientState.TerritoryType);
        Service.ClientState.TerritoryChanged += OnZoneChange;
        Service.FrameworkManager.Register(OnUpdate);
    }

    private static void OnZoneChange(ushort territory)
    {
        FocusTarget = 0;
        IsNeedToRefocus = PresetData.Contents.ContainsKey(territory);
    }

    private static void OnUpdate(IFramework framework)
    {
        if (!Throttler.Throttle("AutoRefocus", 1000)) return;
        if (!IsNeedToRefocus || FocusTarget == 0 || FocusTarget == 0xE000_0000) return;

        if (Service.Target.FocusTarget == null)
            SetFocusTargetByObjectIDHook.Original(TargetSystem.Instance(), FocusTarget);
    }

    private static void SetFocusTargetByObjectID(TargetSystem* targetSystem, ulong objectID)
    {
        FocusTarget = objectID;
        SetFocusTargetByObjectIDHook.Original(targetSystem, objectID);
    }

    public override void Uninit()
    {
        Service.ClientState.TerritoryChanged -= OnZoneChange;

        base.Uninit();
    }
}
