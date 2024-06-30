using DailyRoutines.Managers;
using DailyRoutines.Notifications;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;

namespace DailyRoutines.Modules;

[ModuleDescription("AutoNotifyReadyCheckTitle", "AutoNotifyReadyCheckDescription", ModuleCategories.通知)]
public class AutoNotifyReadyCheck : DailyModuleBase
{
    public override void Init() { Service.Chat.ChatMessage += OnChatMessage; }

    private static void OnChatMessage(XivChatType type, int timestamp, ref SeString sender, ref SeString message, ref bool ishandled)
    {
        var uintType = (uint)type;
        if (uintType != 57 && uintType != 313 && uintType != 569) return;

        var content = message.ExtractText();
        if (!content.Contains("发起了准备确认") && !content.Contains(" a ready check") &&
            !content.Contains("レディチェックを開始しました")) return;

        content = content.Trim('。');
        content = content.Trim('.');
        WinToast.Notify(content, content);
    }

    public override void Uninit()
    {
        Service.Chat.ChatMessage -= OnChatMessage;

        base.Uninit();
    }
}
