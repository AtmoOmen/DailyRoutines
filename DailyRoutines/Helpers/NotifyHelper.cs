using System;
using System.Windows.Forms;
using DailyRoutines.Managers;
using DailyRoutines.Notifications;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface.ImGuiNotification;

namespace DailyRoutines.Helpers;

public static class NotifyHelper
{
    public static void Toast(string title, string message) => WinToast.Notify(title, message, ToolTipIcon.None);

    public static void Toast(string message) => WinToast.Notify(message, message, ToolTipIcon.None);

    public static void ToastError(string title, string message) => WinToast.Notify(title, message, ToolTipIcon.Error);

    public static void ToastError(string message) => WinToast.Notify(message, message, ToolTipIcon.Error);

    public static void ToastWarning(string title, string message) => WinToast.Notify(title, message, ToolTipIcon.Warning);

    public static void ToastWarning(string message) => WinToast.Notify(message, message, ToolTipIcon.Warning);

    public static void ToastInfo(string title, string message) => WinToast.Notify(title, message);

    public static void ToastInfo(string message) => WinToast.Notify(message, message);

    public static void NotificationSuccess(string message) => Service.DalamudNotice.AddNotification(new()
    {
        Title = "Daily Routines",
        Content = message,
        Type = NotificationType.Success,
        InitialDuration = TimeSpan.FromSeconds(3),
        ExtensionDurationSinceLastInterest = TimeSpan.FromSeconds(1),
    });

    public static void NotificationWarning(string message) => Service.DalamudNotice.AddNotification(new()
    {
        Title = "Daily Routines",
        Content = message,
        Type = NotificationType.Warning,
        InitialDuration = TimeSpan.FromSeconds(3),
        ExtensionDurationSinceLastInterest = TimeSpan.FromSeconds(1),
    });

    public static void NotificationError(string message) => Service.DalamudNotice.AddNotification(new()
    {
        Title = "Daily Routines",
        Content = message,
        Type = NotificationType.Error,
        InitialDuration = TimeSpan.FromSeconds(3),
        ExtensionDurationSinceLastInterest = TimeSpan.FromSeconds(1),
    });

    public static void NotificationInfo(string message) => Service.DalamudNotice.AddNotification(new()
    {
        Title = "Daily Routines",
        Content = message,
        Type = NotificationType.Info,
        InitialDuration = TimeSpan.FromSeconds(3),
        ExtensionDurationSinceLastInterest = TimeSpan.FromSeconds(1),
    });

    public static void ChatError(string message) =>
        Service.Chat.PrintError(new SeStringBuilder().Append(DRPrefix).AddUiForeground($" {message}", 518).Build());

    public static void ChatError(SeString message)
    {
        var builder = new SeStringBuilder();
        builder.Append(DRPrefix).Append(" ");
        foreach (var payload in message.Payloads)
            if (payload.Type == PayloadType.RawText)
                builder.AddUiForeground($" {((TextPayload)payload).Text}", 518);
            else builder.Add(payload);
    }

    public static void Chat(string message) =>
        Service.Chat.Print(new SeStringBuilder().Append(DRPrefix).Append($" {message}").Build());

    public static void Chat(SeString message) =>
        Service.Chat.Print(new SeStringBuilder().Append(DRPrefix).Append(" ").Append(message).Build());

    public static void Verbose(string message) => Service.Log.Verbose(message);

    public static void Verbose(string message, Exception ex) => Service.Log.Verbose(ex, message);

    public static void Debug(string message) => Service.Log.Debug(message);

    public static void Debug(string message, Exception ex) => Service.Log.Debug(ex, message);

    public static void Warning(string message) => Service.Log.Warning(message);

    public static void Warning(string message, Exception ex) => Service.Log.Warning(ex, message);

    public static void Error(string message) => Service.Log.Error(message);

    public static void Error(string message, Exception ex) => Service.Log.Error(ex, message);
}
