using System.Collections.Generic;
using System.Reflection;
using DailyRoutines.Helpers;
using DailyRoutines.Windows;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Hooking;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using Lumina.Excel.GeneratedSheets;

namespace DailyRoutines.Managers;

public unsafe class LogMessageManager : IDailyManager
{
    public delegate void LogMessageDelegate(uint logMessageID, ushort logKind);

    private static Dictionary<string, LogMessageDelegate>? MethodsInfo;
    private static LogMessageDelegate[]? _methods;
    private static int _length;

    private delegate void ShowLogMessageDelegate(RaptureLogModule* module, uint logMessageID);
    private delegate void ShowLogMessageUIntDelegate(RaptureLogModule* module, uint logMessageID, uint value);
    private delegate void ShowLogMessageUInt2Delegate(RaptureLogModule* module, uint logMessageID, uint value1, uint value2);
    private delegate void ShowLogMessageUInt3Delegate(RaptureLogModule* module, uint logMessageID, uint value1, uint value2, uint value3);
    private delegate void ShowLogMessageStringDelegate(RaptureLogModule* module, uint logMessageID, Utf8String* value);

    [Signature("48 89 5C 24 ?? 48 89 74 24 ?? 48 89 7C 24 ?? 55 48 8D AC 24 ?? ?? ?? ?? 48 81 EC ?? ?? ?? ?? 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 85 ?? ?? ?? ?? 33 F6 8B DA", DetourName = nameof(ShowLogMessageDetour))]
    private Hook<ShowLogMessageDelegate>? ShowLogMessageHook;
    [Signature("48 89 5C 24 ?? 48 89 74 24 ?? 55 57 41 56 48 8D AC 24 ?? ?? ?? ?? 48 81 EC ?? ?? ?? ?? 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 85 ?? ?? ?? ?? 45 33 F6 8B FA 33 D2", DetourName = nameof(ShowLogMessageUIntDetour))]
    private Hook<ShowLogMessageUIntDelegate>? ShowLogMessageUIntHook;
    [Signature("E8 ?? ?? ?? ?? 48 8B 4F ?? 48 8B 01 FF 50 ?? BA ?? ?? ?? ?? 48 8B C8 4C 8B 00 41 FF 50 ?? 45 33 FF", DetourName = nameof(ShowLogMessageUInt2Detour))]
    private Hook<ShowLogMessageUInt2Delegate>? ShowLogMessageUInt2Hook;
    [Signature("E8 ?? ?? ?? ?? 48 8B CB 48 8B 5C 24 ?? 48 8B 74 24 ?? 48 83 C4 ?? 5F E9 ?? ?? ?? ?? CC CC CC CC CC CC CC CC CC CC 48 89 5C 24", DetourName = nameof(ShowLogMessageUInt3Detour))]
    private Hook<ShowLogMessageUInt3Delegate>? ShowLogMessageUInt3Hook;
    [Signature("48 89 5C 24 ?? 55 56 57 41 56 41 57 48 8D AC 24 ?? ?? ?? ?? 48 81 EC ?? ?? ?? ?? 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 85 ?? ?? ?? ?? 45 33 FF 8B F2", DetourName = nameof(ShowLogMessageStringDetour))]
    private Hook<ShowLogMessageStringDelegate>? ShowLogMessageStringHook;

    private RaptureLogModule* Module = null;

    private void Init()
    {
        MethodsInfo ??= [];
        _methods ??= [];

        Module = UIModule.Instance()->GetRaptureLogModule();

        Service.Hook.InitializeFromAttributes(this);
        
        ShowLogMessageHook?.Enable();
        ShowLogMessageUIntHook?.Enable();
        ShowLogMessageUInt2Hook?.Enable();
        ShowLogMessageUInt3Hook?.Enable();
        ShowLogMessageStringHook?.Enable();
    }

    #region Hook
    private void ShowLogMessageDetour(RaptureLogModule* module, uint logMessageID)
    {
        ShowLogMessageHook.Original(module, logMessageID);
        OnReceiveLogMessages(logMessageID);
    }

    private void ShowLogMessageUIntDetour(RaptureLogModule* module, uint logMessageID, uint value)
    {
        ShowLogMessageUIntHook.Original(module, logMessageID, value);
        OnReceiveLogMessages(logMessageID);
    }

    private void ShowLogMessageUInt2Detour(RaptureLogModule* module, uint logMessageID, uint value1, uint value2)
    {
        ShowLogMessageUInt2Hook.Original(module, logMessageID, value1, value2);
        OnReceiveLogMessages(logMessageID);
    }

    private void ShowLogMessageUInt3Detour(RaptureLogModule* module, uint logMessageID, uint value1, uint value2, uint value3)
    {
        ShowLogMessageUInt3Hook.Original(module, logMessageID, value1, value2, value3);
        OnReceiveLogMessages(logMessageID);
    }

    private void ShowLogMessageStringDetour(RaptureLogModule* module, uint logMessageID, Utf8String* value)
    {
        ShowLogMessageStringHook.Original(module, logMessageID, value);
        OnReceiveLogMessages(logMessageID);
    }
    #endregion

    #region Event
    public bool Register(params LogMessageDelegate[] methods)
    {
        var state = true;
        foreach (var method in methods)
        {
            var uniqueName = GetUniqueName(method);
            if (!MethodsInfo.TryAdd(uniqueName, method)) state = false;
        }

        UpdateMethodsArray();
        return state;
    }

    public bool Unregister(params LogMessageDelegate[] methods)
    {
        var state = true;
        foreach (var method in methods)
        {
            var uniqueName = GetUniqueName(method);
            if (!MethodsInfo.Remove(uniqueName)) state = false;
        }

        UpdateMethodsArray();
        return state;
    }

    private static void OnReceiveLogMessages(uint logMessageID)
    {
        var logKind = LuminaCache.GetRow<LogMessage>(logMessageID).LogKind;
        if (logKind == 0) return;

        if (Debug.DebugConfig.ShowLogMessageLog)
            Service.Log.Debug($"[Log Message Manager]\nID:{logMessageID} | 类型:{logKind}");

        for (var i = 0; i < _length; i++)
        {
            var method = _methods[i];
            method.Invoke(logMessageID, logKind);
        }
    }

    private static string GetUniqueName(LogMessageDelegate method)
    {
        var methodInfo = method.Method;
        return $"{methodInfo.DeclaringType.FullName}_{methodInfo.Name}";
    }

    private static void UpdateMethodsArray()
    {
        _methods = [..MethodsInfo.Values];
        _length = _methods.Length;
    }
    #endregion

    #region Send
    public void Send(uint logMessageID)
    {
        ShowLogMessageHook.Original(Module, logMessageID);
    }

    public void SendUInt(uint logMessageID, uint value)
    {
        ShowLogMessageUIntHook.Original(Module, logMessageID, value);
    }

    public void SendUInt2(uint logMessageID, uint value1, uint value2)
    {
        ShowLogMessageUInt2Hook.Original(Module, logMessageID, value1, value2);
    }

    public void SendUInt3(uint logMessageID, uint value1, uint value2, uint value3)
    {
        ShowLogMessageUInt3Hook.Original(Module, logMessageID, value1, value2, value3);
    }

    public void SendString(uint logMessageID, string value)
    {
        ShowLogMessageStringHook.Original(Module, logMessageID, Utf8String.FromString(value));
    }

    public void SendString(uint logMessageID, SeString value)
    {
        var utf8String = Utf8String.FromString(".");
        utf8String->SetString(value.Encode());
        ShowLogMessageStringHook.Original(Module, logMessageID, utf8String);
    }

    public void SendString(uint logMessageID, Utf8String* value)
    {
        ShowLogMessageStringHook.Original(Module, logMessageID, value);
    }
    #endregion

    private void Uninit()
    {
        MethodsInfo = null;
        _methods = null;
        _length = 0;

        ShowLogMessageHook?.Dispose();
        ShowLogMessageUIntHook?.Dispose();
        ShowLogMessageUInt2Hook?.Dispose();
        ShowLogMessageUInt3Hook?.Dispose();
        ShowLogMessageStringHook?.Dispose();

        ShowLogMessageHook = null;
        ShowLogMessageUIntHook = null;
        ShowLogMessageUInt2Hook = null;
        ShowLogMessageUInt3Hook = null;
        ShowLogMessageStringHook = null;

        Module = null;
    }
}
