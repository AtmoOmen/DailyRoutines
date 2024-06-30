global using static DailyRoutines.Infos.Widgets;
global using static OmenTools.Helpers.HelpersOm;
global using static DailyRoutines.Helpers.AddonHelper;
global using static DailyRoutines.Infos.Extensions;
global using static DailyRoutines.Helpers.ThrottlerHelper;
global using OmenTools.ImGuiOm;
global using OmenTools.Helpers;
using System;
using DailyRoutines.Managers;
using Dalamud.Plugin;
using System.Reflection;

namespace DailyRoutines;

public sealed class Plugin : IDalamudPlugin
{
    public static string Name => "Daily Routines";
    public static Version? Version { get; private set; }

    private static bool IsDev;

    public Plugin(IDalamudPluginInterface pluginInterface)
    {
#if RELEASE
        if (pluginInterface.IsDev)
        {
            IsDev = true;
            return;
        }
#endif

        Version ??= Assembly.GetExecutingAssembly().GetName().Version;

        Service.Init(pluginInterface);
    }

    public void Dispose()
    {
        if (IsDev) return;

        Service.Uninit();
    }
}
