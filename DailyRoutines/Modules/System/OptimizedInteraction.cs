using System.Threading.Tasks;
using DailyRoutines.Helpers;
using DailyRoutines.Managers;
using Dalamud.Hooking;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using Lumina.Excel.GeneratedSheets;

namespace DailyRoutines.Modules;

[ModuleDescription("OptimizedInteractionTitle", "OptimizedInteractionDescription", ModuleCategories.系统)]
public class OptimizedInteraction : DailyModuleBase
{
    // 当前位置无法进行该操作
    private delegate bool CameraObjectBlockedDelegate(nint a1, nint a2, nint a3);
    [Signature("E8 ?? ?? ?? ?? 84 C0 75 ?? B9 ?? ?? ?? ?? E8 ?? ?? ?? ?? EB ?? 40 B7",
               DetourName = nameof(CameraObjectBlockedDetour))]
    private static Hook<CameraObjectBlockedDelegate>? CameraObjectBlockedHook;

    // 目标处于视野之外
    private unsafe delegate bool IsObjectInViewRangeDelegate(TargetSystem* system, GameObject* gameObject);
    [Signature(
        "E8 ?? ?? ?? ?? 84 C0 75 ?? 48 8B 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 48 8B C8 48 8B 10 FF 52 ?? 48 8B C8 BA ?? ?? ?? ?? E8 ?? ?? ?? ?? E9",
        DetourName = nameof(IsObjectInViewRangeHookDetour))]
    private static Hook<IsObjectInViewRangeDelegate>? IsObjectInViewRangeHook;

    // 跳跃中无法进行该操作 / 飞行中无法进行该操作
    private delegate bool InteractCheck0Delegate(nint a1, nint a2, nint a3, nint a4, bool a5);
    [Signature("48 89 5C 24 ?? 48 89 74 24 ?? 57 48 83 EC ?? 49 8B 00 49 8B C8", DetourName = nameof(InteractCheck0Detour))]
    private static Hook<InteractCheck0Delegate>? InteractCheck0Hook;

    // 跳跃中无法进行该操作
    private delegate bool IsPlayerOnJumpingDelegate(nint a1);

    [Signature("E8 ?? ?? ?? ?? 84 C0 0F 85 ?? ?? ?? ?? 48 8D 8D ?? ?? ?? ?? E8 ?? ?? ?? ?? 84 C0 0F 85",
               DetourName = nameof(IsPlayerOnJumpingDetour))]
    private static Hook<IsPlayerOnJumpingDelegate>? IsPlayerOnJumping0Hook;

    [Signature("E8 ?? ?? ?? ?? 84 C0 0F 85 ?? ?? ?? ?? 48 8D 8D ?? ?? ?? ?? 48 89 9C 24",
               DetourName = nameof(IsPlayerOnJumpingDetour))]
    private static Hook<IsPlayerOnJumpingDelegate>? IsPlayerOnJumping1Hook;

    [Signature("E8 ?? ?? ?? ?? 84 C0 74 ?? 48 85 DB 74 ?? 48 8B 03 48 8B CB FF 50",
               DetourName = nameof(IsPlayerOnJumpingDetour))]
    private static Hook<IsPlayerOnJumpingDelegate>? IsPlayerOnJumping2Hook;

    // 检查目标距离 / 高低
    private delegate bool CheckTargetPositionDelegate(nint a1, nint a2, nint a3, byte a4, byte a5);
    [Signature("40 53 57 41 56 48 83 EC ?? 48 8B 02", DetourName = nameof(CheckTargetPositionDetour))]
    private static Hook<CheckTargetPositionDelegate>? CheckTargetPositionHook;

    // 剧情被中断
    private unsafe delegate bool EventCanceledDelegate(EventFramework* framework);
    [Signature("E8 ?? ?? ?? ?? 84 C0 74 ?? 48 8B CB E8 ?? ?? ?? ?? 48 3B C7", DetourName = nameof(EventCanceledDetour))]
    private static Hook<EventCanceledDelegate>? EventCanceledHook;

    // 检查目标距离
    private unsafe delegate float CheckTargetDistanceDelegate(GameObject* localPlayer, GameObject* target);
    [Signature("E8 ?? ?? ?? ?? 0F 2F 05 ?? ?? ?? ?? 76 ?? 48 8B 03 48 8B CB FF 50 ?? 48 8B C8 BA ?? ?? ?? ?? E8 ?? ?? ?? ?? EB", 
               DetourName = nameof(CheckTargetDistanceDetour))]
    private static Hook<CheckTargetDistanceDelegate>? CheckTargetDistanceHook;

    public override void Init()
    {
        Service.Hook.InitializeFromAttributes(this);
        SwitchHooks(true);

        Service.ClientState.TerritoryChanged += OnZoneChanged;
        Task.Run(async () =>
        {
            await Service.Framework.RunOnFrameworkThread(() => Service.ClientState.TerritoryType != 0);
            OnZoneChanged(Service.ClientState.TerritoryType);
        });
    }

    private static void OnZoneChanged(ushort zone)
    {
        var zoneData = LuminaCache.GetRow<TerritoryType>(zone);
        SwitchHooks(!zoneData.IsPvpZone);
    }

    private static void SwitchHooks(bool isEnable)
    {
        if (isEnable)
        {
            CameraObjectBlockedHook.Enable();
            IsObjectInViewRangeHook.Enable();
            InteractCheck0Hook.Enable();
            IsPlayerOnJumping0Hook.Enable();
            IsPlayerOnJumping1Hook.Enable();
            IsPlayerOnJumping2Hook.Enable();
            CheckTargetPositionHook.Enable();
            EventCanceledHook.Enable();
            CheckTargetDistanceHook.Enable();
        }
        else
        {
            CameraObjectBlockedHook.Disable();
            IsObjectInViewRangeHook.Disable();
            InteractCheck0Hook.Disable();
            IsPlayerOnJumping0Hook.Disable();
            IsPlayerOnJumping1Hook.Disable();
            IsPlayerOnJumping2Hook.Disable();
            CheckTargetPositionHook.Disable();
            EventCanceledHook.Disable();
            CheckTargetDistanceHook.Disable();
        }
    }

    private static bool CameraObjectBlockedDetour(nint a1, nint a2, nint a3) => true;

    private static unsafe bool IsObjectInViewRangeHookDetour(TargetSystem* system, GameObject* gameObject) => true;

    private static bool InteractCheck0Detour(nint a1, nint a2, nint a3, nint a4, bool a5) => true;

    private static bool IsPlayerOnJumpingDetour(nint a1) => false;

    private static bool CheckTargetPositionDetour(nint a1, nint a2, nint a3, byte a4, byte a5) => true;

    private static unsafe bool EventCanceledDetour(EventFramework* framework) => false;

    private static unsafe float CheckTargetDistanceDetour(GameObject* localPlayer, GameObject* target) => 0f;
}
