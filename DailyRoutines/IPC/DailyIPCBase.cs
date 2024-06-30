using DailyRoutines.Helpers;
using DailyRoutines.Managers;
using Dalamud.Plugin;
using System;

namespace DailyRoutines.IPC;

public abstract class DailyIPCBase
{
    public bool Initialized { get; internal set; }
    public virtual string? InternalName { get; set; }
    protected static IDalamudPluginInterface PI => Service.PluginInterface;

    public virtual void Init() { }

    protected T? Execute<T>(Func<T>? func)
    {
        if (!IPCManager.IsPluginEnabled(InternalName)) return default;

        try
        {
            if (func != null) return func();
        }
        catch (Exception ex)
        {
            NotifyHelper.Error("", ex);
        }

        return default;
    }

    protected void Execute(Action action)
    {
        if (IPCManager.IsPluginEnabled(InternalName))
        {
            try
            {
                action?.Invoke();
            }
            catch (Exception ex)
            {
                NotifyHelper.Error("", ex);
            }
        }
    }

    protected void Execute<T>(Action<T>? action, T param)
    {
        if (!IPCManager.IsPluginEnabled(InternalName)) return;

        try
        {
            action?.Invoke(param);
        }
        catch (Exception ex)
        {
            NotifyHelper.Error("", ex);
        }
    }

    protected void Execute<T1, T2>(Action<T1, T2>? action, T1 p1, T2 p2)
    {
        if (!IPCManager.IsPluginEnabled(InternalName)) return;

        try
        {
            action?.Invoke(p1, p2);
        }
        catch (Exception ex)
        {
            NotifyHelper.Error("", ex);
        }
    }

    public virtual void Uninit() { }
}
