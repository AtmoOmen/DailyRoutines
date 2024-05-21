using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using DailyRoutines.Helpers;
using DailyRoutines.Managers;
using ECommons.Automation;
using Microsoft.Toolkit.Uwp.Notifications;

namespace DailyRoutines.Notifications;

public class WinToast : DailyNotificationBase
{

    private static TaskManager? TaskManager;

    public override void Init()
    {
        TaskManager ??= new TaskManager { AbortOnTimeout = true, TimeLimitMS = 10000, ShowDebug = false };
    }

    public static void Notify(string title, string content, ToolTipIcon toolTipIcon = ToolTipIcon.Info)
    {
        new ToastContentBuilder()
            .AddText(title)
            .AddText(content)
            .Show();
    }

    public override void Uninit()
    {
        TaskManager?.Abort();
    }
}
