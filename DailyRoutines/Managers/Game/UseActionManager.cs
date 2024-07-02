using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.Game;
using System.Collections.Generic;
using System.Numerics;
using DailyRoutines.Windows;
using Dalamud.Utility.Signatures;

namespace DailyRoutines.Managers;

public unsafe class UseActionManager : IDailyManager
{
    public delegate void PreUseActionDelegate(ref bool isPrevented,
        ref ActionType actionType, ref uint actionID, ref ulong targetID, ref uint a4, ref uint queueState, ref uint a6);
    public delegate void PostUseActionDelegate(bool result, ActionType actionType, uint actionID, ulong targetID, uint a4, uint queueState, uint a6);

    public delegate void PreUseActionLocationDelegate(ref bool isPrevented,
        ref ActionType type, ref uint actionID, ref ulong targetID, ref Vector3 location, ref uint a4);
    public delegate void PostUseActionLocationDelegate(bool result, ActionType actionType, uint actionID, ulong targetID, Vector3 location, uint a4);

    public delegate void PreUseActionPetMoveDelegate(ref bool isPrevented, ref int a1, ref Vector3 location, ref int perActionID, ref int a4, ref int a5, ref int a6);
    public delegate void PostUseActionPetMoveDelegate(bool result, int a1, Vector3 location, int perActionID, int a4, int a5, int a6);

    internal delegate bool useActionDelegate(
        ActionManager* actionManager, ActionType actionType, uint actionID, ulong targetID = 0xE000_0000, uint a4 = 0,
        uint queueState = 0, uint a6 = 0, void* a7 = null);
    internal static Hook<useActionDelegate>? UseActionHook;

    internal delegate bool useActionLocationDelegate(ActionManager* manager, ActionType type, uint actionID, ulong targetID, Vector3* location, uint a4);
    internal static Hook<useActionLocationDelegate>? UseActionLocationHook;

    internal delegate bool useActionPetMoveDelegate(int a1, Vector3* position, int petActionID, int a4, int a5, int a6);
    [Signature("E8 ?? ?? ?? ?? EB ?? 48 8B 53 ?? 33 C0", DetourName = nameof(UseActionPetMoveDetour))]
    internal static Hook<useActionPetMoveDelegate>? UseActionPetMoveHook;

    // PreUseAction
    private static readonly Dictionary<string, PreUseActionDelegate>? MethodsInfoPreUseAction = [];
    private static PreUseActionDelegate[]? _methodsPreUseAction = [];
    // PostUseAction
    private static readonly Dictionary<string, PostUseActionDelegate>? MethodsInfoPostUseAction = [];
    private static PostUseActionDelegate[]? _methodsPostUseAction = [];

    // PreUseActionLocation
    private static readonly Dictionary<string, PreUseActionLocationDelegate>? MethodsInfoPreUseActionLocation = [];
    private static PreUseActionLocationDelegate[]? _methodsPreUseActionLocation = [];
    // PostUseActionLocation
    private static readonly Dictionary<string, PostUseActionLocationDelegate>? MethodsInfoPostUseActionLocation = [];
    private static PostUseActionLocationDelegate[]? _methodsPostUseActionLocation = [];

    // PreUseActionPetMove
    private static readonly Dictionary<string, PreUseActionPetMoveDelegate>? MethodsInfoPreUseActionPetMove = [];
    private static PreUseActionPetMoveDelegate[]? _methodsPreUseActionPetMove = [];
    // PostUseActionPetMove
    private static readonly Dictionary<string, PostUseActionPetMoveDelegate>? MethodsInfoPostUseActionPetMove = [];
    private static PostUseActionPetMoveDelegate[]? _methodsPostUseActionPetMove = [];

    private void Init()
    {
        UseActionHook = Service.Hook.HookFromAddress<useActionDelegate>(
            (nint)ActionManager.MemberFunctionPointers.UseAction, UseActionDetour);
        UseActionHook.Enable();

        UseActionLocationHook = Service.Hook.HookFromAddress<useActionLocationDelegate>((nint)ActionManager.MemberFunctionPointers.UseActionLocation, UseActionLocationDetour);
        UseActionLocationHook.Enable();

        Service.Hook.InitializeFromAttributes(this);
        UseActionPetMoveHook.Enable();
    }

    #region PreUseAction
    public bool Register(params PreUseActionDelegate[] methods)
    {
        var state = true;
        foreach (var method in methods)
        {
            var uniqueName = GetUniqueName(method);
            if (!MethodsInfoPreUseAction.TryAdd(uniqueName, method)) state = false;
        }

        UpdateMethodsArrayPreUseAction();
        return state;
    }

    public bool Unregister(params PreUseActionDelegate[] methods)
    {
        var state = true;
        foreach (var method in methods)
        {
            var uniqueName = GetUniqueName(method);
            if (!MethodsInfoPreUseAction.Remove(uniqueName)) state = false;
        }

        UpdateMethodsArrayPreUseAction();
        return state;
    }

    private static void UpdateMethodsArrayPreUseAction()
    {
        _methodsPreUseAction = [.. MethodsInfoPreUseAction.Values];
    }

    private static string GetUniqueName(PreUseActionDelegate method)
    {
        var methodInfo = method.Method;
        return $"{methodInfo.DeclaringType.FullName}_{methodInfo.Name}";
    }
    #endregion

    #region PostUseAction
    public bool Register(params PostUseActionDelegate[] methods)
    {
        var state = true;
        foreach (var method in methods)
        {
            var uniqueName = GetUniqueName(method);
            if (!MethodsInfoPostUseAction.TryAdd(uniqueName, method)) state = false;
        }

        UpdateMethodsArrayPostUseAction();
        return state;
    }

    public bool Unregister(params PostUseActionDelegate[] methods)
    {
        var state = true;
        foreach (var method in methods)
        {
            var uniqueName = GetUniqueName(method);
            if (!MethodsInfoPostUseAction.Remove(uniqueName)) state = false;
        }

        UpdateMethodsArrayPostUseAction();
        return state;
    }

    private static void UpdateMethodsArrayPostUseAction()
    {
        _methodsPostUseAction = [.. MethodsInfoPostUseAction.Values];
    }

    private static string GetUniqueName(PostUseActionDelegate method)
    {
        var methodInfo = method.Method;
        return $"{methodInfo.DeclaringType.FullName}_{methodInfo.Name}";
    }
    #endregion

    #region PreUseActionLocation
    public bool Register(params PreUseActionLocationDelegate[] methods)
    {
        var state = true;
        foreach (var method in methods)
        {
            var uniqueName = GetUniqueName(method);
            if (!MethodsInfoPreUseActionLocation.TryAdd(uniqueName, method)) state = false;
        }

        UpdateMethodsArrayPreUseActionLocation();
        return state;
    }

    public bool Unregister(params PreUseActionLocationDelegate[] methods)
    {
        var state = true;
        foreach (var method in methods)
        {
            var uniqueName = GetUniqueName(method);
            if (!MethodsInfoPreUseActionLocation.Remove(uniqueName)) state = false;
        }

        UpdateMethodsArrayPreUseActionLocation();
        return state;
    }

    private static void UpdateMethodsArrayPreUseActionLocation()
    {
        _methodsPreUseActionLocation = [.. MethodsInfoPreUseActionLocation.Values];
    }

    private static string GetUniqueName(PreUseActionLocationDelegate method)
    {
        var methodInfo = method.Method;
        return $"{methodInfo.DeclaringType.FullName}_{methodInfo.Name}";
    }
    #endregion

    #region PostUseActionLocation
    public bool Register(params PostUseActionLocationDelegate[] methods)
    {
        var state = true;
        foreach (var method in methods)
        {
            var uniqueName = GetUniqueName(method);
            if (!MethodsInfoPostUseActionLocation.TryAdd(uniqueName, method)) state = false;
        }

        UpdateMethodsArrayPostUseActionLocation();
        return state;
    }

    public bool Unregister(params PostUseActionLocationDelegate[] methods)
    {
        var state = true;
        foreach (var method in methods)
        {
            var uniqueName = GetUniqueName(method);
            if (!MethodsInfoPostUseActionLocation.Remove(uniqueName)) state = false;
        }

        UpdateMethodsArrayPostUseActionLocation();
        return state;
    }

    private static void UpdateMethodsArrayPostUseActionLocation()
    {
        _methodsPostUseActionLocation = [.. MethodsInfoPostUseActionLocation.Values];
    }

    private static string GetUniqueName(PostUseActionLocationDelegate method)
    {
        var methodInfo = method.Method;
        return $"{methodInfo.DeclaringType.FullName}_{methodInfo.Name}";
    }
    #endregion

    #region PreUseActionPetMove
    public bool Register(params PreUseActionPetMoveDelegate[] methods)
    {
        var state = true;
        foreach (var method in methods)
        {
            var uniqueName = GetUniqueName(method);
            if (!MethodsInfoPreUseActionPetMove.TryAdd(uniqueName, method)) state = false;
        }

        UpdateMethodsArrayPreUseActionPetMove();
        return state;
    }

    public bool Unregister(params PreUseActionPetMoveDelegate[] methods)
    {
        var state = true;
        foreach (var method in methods)
        {
            var uniqueName = GetUniqueName(method);
            if (!MethodsInfoPreUseActionPetMove.Remove(uniqueName)) state = false;
        }

        UpdateMethodsArrayPreUseActionPetMove();
        return state;
    }

    private static void UpdateMethodsArrayPreUseActionPetMove()
    {
        _methodsPreUseActionPetMove = [.. MethodsInfoPreUseActionPetMove.Values];
    }

    private static string GetUniqueName(PreUseActionPetMoveDelegate method)
    {
        var methodInfo = method.Method;
        return $"{methodInfo.DeclaringType.FullName}_{methodInfo.Name}";
    }
    #endregion

    #region PostUseActionPetMove
    public bool Register(params PostUseActionPetMoveDelegate[] methods)
    {
        var state = true;
        foreach (var method in methods)
        {
            var uniqueName = GetUniqueName(method);
            if (!MethodsInfoPostUseActionPetMove.TryAdd(uniqueName, method)) state = false;
        }

        UpdateMethodsArrayPostUseActionPetMove();
        return state;
    }

    public bool Unregister(params PostUseActionPetMoveDelegate[] methods)
    {
        var state = true;
        foreach (var method in methods)
        {
            var uniqueName = GetUniqueName(method);
            if (!MethodsInfoPostUseActionPetMove.Remove(uniqueName)) state = false;
        }

        UpdateMethodsArrayPostUseActionPetMove();
        return state;
    }

    private static void UpdateMethodsArrayPostUseActionPetMove()
    {
        _methodsPostUseActionPetMove = [.. MethodsInfoPostUseActionPetMove.Values];
    }

    private static string GetUniqueName(PostUseActionPetMoveDelegate method)
    {
        var methodInfo = method.Method;
        return $"{methodInfo.DeclaringType.FullName}_{methodInfo.Name}";
    }
    #endregion

    #region Hooks
    private static bool UseActionDetour(
        ActionManager* actionManager, ActionType actionType, uint actionID, ulong targetID = 0xE000_0000, uint a4 = 0,
        uint queueState = 0, uint a6 = 0, void* a7 = null)
    {
        if (Debug.DebugConfig.ShowUseActionLog)
            Service.Log.Debug($"[Use Action Manager] 一般类技能\n类型:{actionType} | ID:{actionID} | 目标ID: {targetID} | a4:{a4} | 队列状态:{queueState} | a6:{a6}");

        var isPrevented = false;
        foreach (var preUseAction in _methodsPreUseAction)
        {
            preUseAction.Invoke
                (ref isPrevented, ref actionType, ref actionID, ref targetID, ref a4, ref queueState, ref a6);
        }

        if (isPrevented) return false;
        
        var original = UseActionHook.Original(actionManager, actionType, actionID, targetID, a4, queueState, a6, a7);
        foreach (var postUseAction in _methodsPostUseAction)
        {
            postUseAction.Invoke(original, actionType, actionID, targetID, a4, queueState, a6);
        }

        return original;
    }

    private static bool UseActionLocationDetour(
        ActionManager* manager, ActionType type, uint actionID, ulong targetID, Vector3* location, uint a4)
    {
        if (Debug.DebugConfig.ShowUseActionLocationLog)
            Service.Log.Debug($"[Use Action Manager] 地面类技能\n类型:{type} | ID:{actionID} | 目标ID: {targetID} | 地点:{*location} | a4:{a4}");

        var isPrevented = false;
        var location0 = *location;
        foreach (var preUseAction in _methodsPreUseActionLocation)
        {
            preUseAction.Invoke(ref isPrevented, ref type, ref actionID, ref targetID, ref location0, ref a4);
        }

        if (isPrevented) return false;

        var original = UseActionLocationHook.Original(manager, type, actionID, targetID, &location0, a4);
        foreach (var postUseAction in _methodsPostUseActionLocation)
        {
            postUseAction.Invoke(original, type, actionID, targetID, location0, a4);
        }

        return original;
    }

    private static bool UseActionPetMoveDetour(int a1, Vector3* location, int petActionID, int a3, int a4, int a5)
    {
        if (Debug.DebugConfig.ShowUseActionPetMoveLog)
            Service.Log.Debug($"[Use Action Manager] 召唤物移动技能\n类型:{a1} | ID:{petActionID} | 地点:{*location} | a3: {a3} | a4:{a4} | a5: {a5}");

        var isPrevented = false;
        var location0 = *location;
        foreach (var preUseAction in _methodsPreUseActionPetMove)
        {
            preUseAction.Invoke(ref isPrevented, ref a1, ref location0, ref petActionID, ref a3, ref a4, ref a5);
        }

        if (isPrevented) return false;

        var original = UseActionPetMoveHook.Original(a1, location, petActionID, a3, a4, a5);

        foreach (var postUseAction in _methodsPostUseActionPetMove)
        {
            postUseAction.Invoke(original, a1, location0, petActionID, a3, a4, a5);
        }

        return original;
    }
    #endregion

    #region Invokes
    
    public bool UseAction(ActionType type, uint actionID, ulong targetID = 0xE000_0000, uint a4 = 0, uint queueState = 0, uint a6 = 0, void* a7 = null) 
        => UseActionHook.Original(ActionManager.Instance(), type, actionID, targetID, a4, queueState, a6, a7);

    public bool UseActionLocation(ActionType type, uint actionID, ulong targetID, Vector3 location, uint a4) 
        => UseActionLocationHook.Original(ActionManager.Instance(), type, actionID, targetID, &location, a4);

    public bool UseActionPetMove(Vector3 location) => UseActionPetMoveHook.Original(1800, &location, 3, 0, 0, 0);

    #endregion

    private void Uninit()
    {
        UseActionHook?.Dispose();
        UseActionHook = null;

        UseActionLocationHook?.Dispose();
        UseActionLocationHook = null;

        UseActionPetMoveHook?.Dispose();
        UseActionPetMoveHook = null;
    }
}
