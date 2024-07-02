using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DailyRoutines.Helpers;
using DailyRoutines.Infos;
using DailyRoutines.Managers;
using DailyRoutines.Windows;
using Dalamud.Game.Addon.Events;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Client.UI.Shell;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FFXIVClientStructs.Interop;
using ImGuiNET;
using Lumina.Excel.GeneratedSheets;

namespace DailyRoutines.Modules;

[ModuleDescription("QuickChatPanelTitle", "QuickChatPanelDescription", ModuleCategories.界面优化)]
public unsafe class QuickChatPanel : DailyModuleBase
{
    private const float DefaultOverlayWidth = 300f;

    private static readonly Dictionary<MacroDisplayMode, string> MacroDisplayModeLoc = new()
    {
        { MacroDisplayMode.List, Service.Lang.GetText("QuickChatPanel-List") },
        { MacroDisplayMode.Buttons, Service.Lang.GetText("QuickChatPanel-Buttons") },
    };

    private static Config ModuleConfig = null!;

    private static char[] SeIconChars = [];
    private static Vector2 ButtonPos = new(0);
    private static IAddonEventHandle? MouseClickHandle;
    private static string MessageInput = string.Empty;
    private static int _dropMacroIndex = -1;
    private static string ItemSearchInput = string.Empty;
    private static Dictionary<string, Item>? ItemNames;
    private static Dictionary<string, Item> _ItemNames = [];

    private static Vector2 TwentyCharsSize;

    public override void Init()
    {
        ModuleConfig = LoadConfig<Config>() ?? new();

        if (ModuleConfig.SoundEffectNotes.Count <= 0)
        {
            for (var i = 1U; i < 17; i++)
                ModuleConfig.SoundEffectNotes[i] = $"<se.{i}>";

            SaveConfig(ModuleConfig);
        }

        var tempSeIconList = new List<char>();
        foreach (SeIconChar seIconChar in Enum.GetValues(typeof(SeIconChar)))
            tempSeIconList.Add((char)seIconChar);

        SeIconChars = [.. tempSeIconList];
        ItemNames ??= LuminaCache.Get<Item>()
                                 .Where(x => !string.IsNullOrEmpty(x.Name.RawString))
                                 .GroupBy(x => x.Name.RawString)
                                 .ToDictionary(x => x.Key, x => x.First());

        _ItemNames = ItemNames.Take(10).ToDictionary(x => x.Key, x => x.Value);

        Service.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "ChatLog", OnAddon);
        Service.AddonLifecycle.RegisterListener(AddonEvent.PostRefresh, "ChatLog", OnAddon);
        Service.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, "ChatLog", OnAddon);
        if (AddonState.ChatLog != null) OnAddon(AddonEvent.PostSetup, null);

        Overlay ??= new Overlay(this);
        Overlay.Flags |= ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollWithMouse;
        Overlay.Flags &= ~ImGuiWindowFlags.AlwaysAutoResize;
        Overlay.SizeConstraints = new()
        {
            MinimumSize = new(1, ModuleConfig.OverlayHeight),
        };

        TaskHelper ??= new TaskHelper { AbortOnTimeout = true, TimeLimitMS = 5000, ShowDebug = false };
    }

    public override void ConfigUI()
    {
        // 左半边
        ImGui.BeginGroup();
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(ImGuiColors.DalamudOrange, $"{Service.Lang.GetText("QuickChatPanel-Messages")}:");

        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(ImGuiColors.DalamudOrange, $"{Service.Lang.GetText("QuickChatPanel-Macro")}:");

        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(ImGuiColors.DalamudOrange, $"{Service.Lang.GetText("QuickChatPanel-SystemSound")}:");

        ImGui.Spacing();

        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(ImGuiColors.DalamudOrange, $"{Service.Lang.GetText("QuickChatPanel-ButtonOffset")}:");

        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(ImGuiColors.DalamudOrange, $"{Service.Lang.GetText("QuickChatPanel-ButtonSize")}:");

        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(ImGuiColors.DalamudOrange, $"{Service.Lang.GetText("QuickChatPanel-ButtonIcon")}:");

        ImGui.Spacing();

        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(ImGuiColors.DalamudOrange, $"{Service.Lang.GetText("QuickChatPanel-FontScale")}:");

        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(ImGuiColors.DalamudOrange, $"{Service.Lang.GetText("QuickChatPanel-OverlayHeight")}:");

        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(ImGuiColors.DalamudOrange, $"{Service.Lang.GetText("QuickChatPanel-OverlayPosOffset")}:");

        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(ImGuiColors.DalamudOrange, $"{Service.Lang.GetText("QuickChatPanel-OverlayMacroDisplayMode")}:");

        ImGui.EndGroup();

        ImGui.SameLine();

        // 右半边
        ImGui.BeginGroup();
        ImGui.SetNextItemWidth(240f * GlobalFontScale);
        if (ImGui.BeginCombo("###MessagesCombo",
                             Service.Lang.GetText("QuickChatPanel-SavedMessagesAmountText",
                                                  ModuleConfig.SavedMessages.Count)))
        {
            ImGui.InputText("###MessageToSaveInput", ref MessageInput, 1000);

            ImGui.SameLine();
            if (ImGuiOm.ButtonIcon("###MessagesInputAdd", FontAwesomeIcon.Plus))
            {
                if (!ModuleConfig.SavedMessages.Contains(MessageInput))
                {
                    ModuleConfig.SavedMessages.Add(MessageInput);
                    SaveConfig(ModuleConfig);
                }
            }

            if (ModuleConfig.SavedMessages.Count > 0) ImGui.Separator();

            foreach (var message in ModuleConfig.SavedMessages.ToList())
            {
                ImGuiOm.ButtonSelectable(message);

                if (ImGui.BeginPopupContextItem())
                {
                    if (ImGuiOm.ButtonSelectable(Service.Lang.GetText("Delete")))
                        ModuleConfig.SavedMessages.Remove(message);

                    ImGui.EndPopup();
                }
            }

            ImGui.EndCombo();
        }

        ImGui.SetNextItemWidth(240f * GlobalFontScale);
        if (ImGui.BeginCombo("###MacroCombo",
                             Service.Lang.GetText("QuickChatPanel-SavedMacrosAmountText", ModuleConfig.SavedMacros.Count),
                             ImGuiComboFlags.HeightLargest))
        {
            DrawMacroChild(true);

            ImGui.SameLine();
            DrawMacroChild(false);

            ImGui.EndCombo();
        }

        ImGui.SetNextItemWidth(240f * GlobalFontScale);
        if (ImGui.BeginCombo("###SoundEffectNoteEditCombo", "", ImGuiComboFlags.HeightLarge))
        {
            foreach (var seNote in ModuleConfig.SoundEffectNotes)
            {
                ImGui.PushID($"{seNote.Key}");
                ImGui.AlignTextToFramePadding();
                ImGui.Text($"<se.{seNote.Key}>{(seNote.Key < 10 ? "  " : "")}");

                ImGui.SameLine();
                ImGui.Text("——>");

                var note = seNote.Value;
                ImGui.SameLine();
                ImGui.SetNextItemWidth(200f * GlobalFontScale);
                if (ImGui.InputText("", ref note, 32))
                    ModuleConfig.SoundEffectNotes[seNote.Key] = note;

                if (ImGui.IsItemDeactivatedAfterEdit())
                    SaveConfig(ModuleConfig);

                ImGui.PopID();
            }

            ImGui.EndCombo();
        }

        ImGui.Spacing();

        ImGui.SetNextItemWidth(150f * GlobalFontScale);
        ImGui.InputFloat2("###ButtonOffsetInput", ref ModuleConfig.ButtonOffset, "%.1f");
        if (ImGui.IsItemDeactivatedAfterEdit())
            SaveConfig(ModuleConfig);

        ImGui.SetNextItemWidth(100f * GlobalFontScale);
        var intConfigButtonSize = (int)ModuleConfig.ButtonSize;
        ImGui.InputInt("###ButtonSizeInput", ref intConfigButtonSize, 0, 0);
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            ModuleConfig.ButtonSize = (ushort)Math.Clamp(intConfigButtonSize, 1, 65536);
            SaveConfig(ModuleConfig);
        }

        ImGui.SetNextItemWidth(100f * GlobalFontScale);
        ImGui.InputInt("###ButtonIconInput", ref ModuleConfig.ButtonIcon, 0, 0);
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            ModuleConfig.ButtonIcon = Math.Max(ModuleConfig.ButtonIcon, 1);
            SaveConfig(ModuleConfig);
        }

        ImGui.SameLine();
        if (ImGuiOm.ButtonIcon("OpenIconBrowser", FontAwesomeIcon.Search,
                               Service.Lang.GetText("QuickChatPanel-OpenIconBrowser")))
            ChatHelper.Instance.SendMessage("/xldata icon");

        ImGui.Spacing();

        // 字体缩放
        ImGui.SetNextItemWidth(100f * GlobalFontScale);
        ImGui.InputFloat("###FontScaleInput", ref ModuleConfig.FontScale, 0, 0, "%.1f");
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            ModuleConfig.FontScale = (float)Math.Clamp(ModuleConfig.FontScale, 0.1, 10f);
            SaveConfig(ModuleConfig);
        }

        // 窗口高度
        ImGui.SetNextItemWidth(100f * GlobalFontScale);
        ImGui.InputFloat("###OverlayHeightInput", ref ModuleConfig.OverlayHeight, 0, 0, "%.1f");
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            ModuleConfig.OverlayHeight = Math.Clamp(ModuleConfig.OverlayHeight, 100f, 10000f);
            SaveConfig(ModuleConfig);

            Overlay.SizeConstraints = new()
            {
                MinimumSize = new(1, ModuleConfig.OverlayHeight),
            };
        }

        // 窗口位置偏移
        ImGui.SetNextItemWidth(150f * GlobalFontScale);
        ImGui.InputFloat2("###OverlayPosOffsetInput", ref ModuleConfig.OverlayOffset, "%.1f");
        if (ImGui.IsItemDeactivatedAfterEdit())
            SaveConfig(ModuleConfig);

        ImGui.SetNextItemWidth(100f * GlobalFontScale);
        if (ImGui.BeginCombo("###OverlayMacroDisplayModeCombo", MacroDisplayModeLoc[ModuleConfig.OverlayMacroDisplayMode]))
        {
            foreach (MacroDisplayMode mode in Enum.GetValues(typeof(MacroDisplayMode)))
                if (ImGui.Selectable(MacroDisplayModeLoc[mode], mode == ModuleConfig.OverlayMacroDisplayMode))
                {
                    ModuleConfig.OverlayMacroDisplayMode = mode;
                    SaveConfig(ModuleConfig);
                }

            ImGui.EndCombo();
        }

        ImGui.EndGroup();

        return;

        void DrawMacroChild(bool isIndividual)
        {
            var childSize = new Vector2(200 * GlobalFontScale, 300 * GlobalFontScale);
            var module = RaptureMacroModule.Instance();
            if (ImGui.BeginChild($"{(isIndividual ? "Individual" : "Shared")}MacroSelectChild", childSize))
            {
                ImGui.Text(Service.Lang.GetText($"QuickChatPanel-{(isIndividual ? "Individual" : "Shared")}Macros"));
                ImGui.Separator();

                var span = isIndividual ? module->Individual : module->Shared;
                for (var i = 0; i < span.Length; i++)
                {
                    var macro = span.GetPointer(i);
                    if (macro == null) continue;

                    var name = macro->Name.ExtractText();
                    var icon = ImageHelper.GetIcon(macro->IconId);
                    if (string.IsNullOrEmpty(name) || icon == null) continue;

                    var currentSavedMacro = (*macro).ToSavedMacro();
                    currentSavedMacro.Position = i;
                    currentSavedMacro.Category = isIndividual ? 0U : 1U;

                    ImGui.PushID($"{currentSavedMacro.Category}-{currentSavedMacro.Position}");
                    if (ImGuiOm.SelectableImageWithText(icon.ImGuiHandle, new(24), name,
                                                        ModuleConfig.SavedMacros.Contains(currentSavedMacro),
                                                        ImGuiSelectableFlags.DontClosePopups))
                    {
                        if (!ModuleConfig.SavedMacros.Remove(currentSavedMacro))
                        {
                            ModuleConfig.SavedMacros.Add(currentSavedMacro);
                            SaveConfig(ModuleConfig);
                        }
                    }

                    if (ModuleConfig.SavedMacros.Contains(currentSavedMacro) && ImGui.BeginPopupContextItem())
                    {
                        ImGui.TextColored(ImGuiColors.DalamudOrange,
                                          $"{Service.Lang.GetText("QuickChatPanel-LastUpdateTime")}:");

                        ImGui.SameLine();
                        ImGui.Text($"{ModuleConfig.SavedMacros.Find(x => x.Equals(currentSavedMacro))?.LastUpdateTime}");

                        ImGui.Separator();

                        if (ImGuiOm.SelectableTextCentered(Service.Lang.GetText("Refresh")))
                        {
                            var currentIndex = ModuleConfig.SavedMacros.IndexOf(currentSavedMacro);
                            if (currentIndex != -1)
                            {
                                ModuleConfig.SavedMacros[currentIndex] = currentSavedMacro;
                                SaveConfig(ModuleConfig);
                            }
                        }

                        ImGui.EndPopup();
                    }

                    ImGui.PopID();
                }

                ImGui.EndChild();
            }
        }
    }

    public override void OverlayPreDraw()
    {
        if (Service.ClientState.LocalPlayer == null ||
            AddonState.ChatLog == null || !AddonState.ChatLog->IsVisible ||
            AddonState.ChatLog->GetNodeById(5) == null)
            Overlay.IsOpen = false;
    }

    public override void OverlayUI()
    {
        using (FontManager.GetUIFont(ModuleConfig.FontScale).Push())
        {
            var textInputNode = AddonState.ChatLog->GetNodeById(5);
            var buttonPos = new Vector2(textInputNode->X + textInputNode->Width, textInputNode->ScreenY) +
                            ModuleConfig.ButtonOffset;

            ImGui.SetWindowPos(buttonPos with { Y = buttonPos.Y - ImGui.GetWindowSize().Y - 16f } + ModuleConfig.OverlayOffset);

            TwentyCharsSize = ImGui.CalcTextSize("我也不知道要说什么但是真得要凑齐二十几个汉字");

            var isOpen = true;
            ImGui.SetNextWindowPos(new(ImGui.GetWindowPos().X, ImGui.GetWindowPos().Y + ImGui.GetWindowHeight()));
            ImGui.SetNextWindowSize(new(ImGui.GetWindowWidth(), TwentyCharsSize.Y * 1.8f));
            if (ImGui.Begin("###QuickChatPanel-SendMessages", ref isOpen,
                            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar |
                            ImGuiWindowFlags.NoScrollWithMouse))
            {

                if (ImGuiOm.SelectableTextCentered(Service.Lang.GetText("QuickChatPanel-SendChatboxMessage")))
                {
                    var inputNode = (AtkComponentTextInput*)AddonState.ChatLog->GetComponentNodeById(5);
                    var text = inputNode->AtkComponentInputBase.UnkText1;
                    if (!string.IsNullOrWhiteSpace(text.ToString()))
                    {
                        ChatHelper.Instance.SendMessageUnsafe(text.AsSpan().ToArray());
                        inputNode->AtkComponentInputBase.UnkText1.Clear();
                        inputNode->AtkComponentInputBase.UnkText2.Clear();
                        inputNode->AtkComponentInputBase.AtkTextNode->SetText(string.Empty);
                    }
                }
                ImGui.End();
            }

            ImGui.BeginGroup();
            DrawOverlayContent();
            ImGui.EndGroup();

            ImGui.Separator();
        }
    }

    private void DrawOverlayContent()
    {
        if (ImGui.BeginTabBar("###QuickChatPanel", ImGuiTabBarFlags.Reorderable))
        {
            // 消息
            MessageTabItem();

            // 宏
            MacroTabItem();

            // 系统音
            SystemSoundTabItem();

            // 游戏物品
            GameItemTabItem();

            // 特殊物品符号
            SpecialIconCharTabItem();

            ImGui.SameLine();
            if (ImGui.Button($"{FontAwesomeIcon.Cog.ToIconString()}###OpenQuickChatPanelSettings"))
            {
                WindowManager.Main.IsOpen ^= true;
                if (WindowManager.Main.IsOpen)
                {
                    Main.SearchString = Service.Lang.GetText("QuickChatPanelTitle");
                    return;
                }

                Main.SearchString = string.Empty;
            }

            ImGui.SameLine();
            ImGuiOm.HelpMarker(Service.Lang.GetText("QuickChatPanelTitle-DragHelp"));

            ImGui.EndTabBar();
        }
    }

    private void MessageTabItem()
    {
        if (ImGui.BeginTabItem(Service.Lang.GetText("QuickChatPanel-Messages")))
        {
            var maxTextWidth = 300f * GlobalFontScale;
            if (ImGui.BeginChild("MessagesChild", ImGui.GetContentRegionAvail(), false))
            {
                for (var i = 0; i < ModuleConfig.SavedMessages.Count; i++)
                {
                    var message = ModuleConfig.SavedMessages[i];

                    var textWidth = ImGui.CalcTextSize(message).X;
                    maxTextWidth = Math.Max(textWidth + 64, maxTextWidth);
                    maxTextWidth = Math.Max(TwentyCharsSize.X, maxTextWidth);

                    ImGuiOm.SelectableTextCentered(message);

                    if (ImGui.IsKeyDown(ImGuiKey.LeftShift))
                    {
                        if (ImGui.BeginDragDropSource())
                        {
                            if (ImGui.SetDragDropPayload("MessageReorder", nint.Zero, 0)) _dropMacroIndex = i;
                            ImGui.TextColored(ImGuiColors.DalamudYellow, message);
                            ImGui.EndDragDropSource();
                        }

                        if (ImGui.BeginDragDropTarget())
                        {
                            if (_dropMacroIndex >= 0 ||
                                ImGui.AcceptDragDropPayload("MessageReorder").NativePtr != null)
                            {
                                SwapMessages(_dropMacroIndex, i);
                                _dropMacroIndex = -1;
                            }

                            ImGui.EndDragDropTarget();
                        }
                    }

                    if (ImGui.IsItemClicked(ImGuiMouseButton.Left)) ImGui.SetClipboardText(message);

                    if (ImGui.IsItemClicked(ImGuiMouseButton.Right)) ChatHelper.Instance.SendMessage(message);

                    ImGuiOm.TooltipHover(Service.Lang.GetText("QuickChatPanel-SendMessageHelp"));

                    if (i != ModuleConfig.SavedMessages.Count - 1)
                        ImGui.Separator();
                }

                ImGui.EndChild();
            }

            ImGui.SetWindowSize(new(Math.Max(DefaultOverlayWidth, maxTextWidth),
                                    ModuleConfig.OverlayHeight * GlobalFontScale));

            ImGui.EndTabItem();
        }
    }

    private void MacroTabItem()
    {
        if (ImGui.BeginTabItem(Service.Lang.GetText("QuickChatPanel-Macro")))
        {
            var maxTextWidth = 300f * GlobalFontScale;
            if (ImGui.BeginChild("MacroChild", ImGui.GetContentRegionAvail(), false))
            {
                ImGui.BeginGroup();
                for (var i = 0; i < ModuleConfig.SavedMacros.Count; i++)
                {
                    var macro = ModuleConfig.SavedMacros[i];

                    var name = macro.Name;
                    var icon = ImageHelper.GetIcon(macro.IconID);
                    if (string.IsNullOrEmpty(name) || icon == null) continue;

                    switch (ModuleConfig.OverlayMacroDisplayMode)
                    {
                        case MacroDisplayMode.List:
                            if (ImGuiOm.SelectableImageWithText(icon.ImGuiHandle, new(24), name, false))
                            {
                                var gameMacro =
                                    RaptureMacroModule.Instance()->GetMacro(macro.Category, (uint)macro.Position);

                                RaptureShellModule.Instance()->ExecuteMacro(gameMacro);
                            }

                            break;
                        case MacroDisplayMode.Buttons:
                            var textSize = ImGui.CalcTextSize("六个字也行吧");
                            var buttonSize = textSize with { Y = (textSize.Y * 2) + icon.Height };

                            if (ImGuiOm.ButtonImageWithTextVertical(icon, name, buttonSize))
                            {
                                var gameMacro =
                                    RaptureMacroModule.Instance()->GetMacro(macro.Category, (uint)macro.Position);

                                RaptureShellModule.Instance()->ExecuteMacro(gameMacro);
                            }

                            break;
                    }

                    if (ImGui.IsKeyDown(ImGuiKey.LeftShift))
                    {
                        if (ImGui.BeginDragDropSource())
                        {
                            if (ImGui.SetDragDropPayload("MacroReorder", nint.Zero, 0)) _dropMacroIndex = i;
                            ImGui.TextColored(ImGuiColors.DalamudYellow, name);
                            ImGui.EndDragDropSource();
                        }

                        if (ImGui.BeginDragDropTarget())
                        {
                            if (_dropMacroIndex >= 0 ||
                                ImGui.AcceptDragDropPayload("MacroReorder").NativePtr != null)
                            {
                                SwapMacros(_dropMacroIndex, i);
                                _dropMacroIndex = -1;
                            }

                            ImGui.EndDragDropTarget();
                        }
                    }

                    switch (ModuleConfig.OverlayMacroDisplayMode)
                    {
                        case MacroDisplayMode.List:
                            if (i != ModuleConfig.SavedMacros.Count - 1)
                                ImGui.Separator();

                            break;
                        case MacroDisplayMode.Buttons:
                            if ((i + 1) % 5 != 0) ImGui.SameLine();
                            else
                            {
                                ImGui.SameLine();
                                ImGui.Dummy(new(20 * ModuleConfig.FontScale));
                            }

                            break;
                    }
                }

                ImGui.EndGroup();
                maxTextWidth = ImGui.GetItemRectSize().X;

                ImGui.EndChild();
            }

            ImGui.SetWindowSize(new(Math.Max(DefaultOverlayWidth, maxTextWidth),
                                    ModuleConfig.OverlayHeight * GlobalFontScale));

            ImGui.EndTabItem();
        }
    }

    private static void SystemSoundTabItem()
    {
        if (ImGui.BeginTabItem(Service.Lang.GetText("QuickChatPanel-SystemSound")))
        {
            var maxTextWidth = 300f * GlobalFontScale;
            if (ImGui.BeginChild("SystemSoundChild"))
            {
                ImGui.BeginGroup();
                foreach (var seNote in ModuleConfig.SoundEffectNotes)
                {
                    ImGuiOm.ButtonSelectable($"{seNote.Value}###PlaySound{seNote.Key}");

                    if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
                        UIModule.PlayChatSoundEffect(seNote.Key);

                    if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                        ChatHelper.Instance.SendMessage($"<se.{seNote.Key}><se.{seNote.Key}>");

                    ImGuiOm.TooltipHover(Service.Lang.GetText("QuickChatPanel-SystemSoundHelp"));
                }

                ImGui.EndGroup();
                maxTextWidth = ImGui.GetItemRectSize().X;
                maxTextWidth = Math.Max(TwentyCharsSize.X, maxTextWidth);
                ImGui.EndChild();
            }

            ImGui.SetWindowSize(new(Math.Max(DefaultOverlayWidth, maxTextWidth),
                                    ModuleConfig.OverlayHeight * GlobalFontScale));

            ImGui.EndTabItem();
        }
    }

    private static void GameItemTabItem()
    {
        if (ImGui.BeginTabItem(Service.Lang.GetText("QuickChatPanel-GameItems")))
        {
            var maxTextWidth = 300f * GlobalFontScale;
            if (ImGui.BeginChild("GameItemChild", ImGui.GetContentRegionAvail(), false))
            {
                ImGui.SetNextItemWidth(-1f);
                ImGui.InputTextWithHint("###GameItemSearchInput", Service.Lang.GetText("PleaseSearch"),
                                        ref ItemSearchInput, 100);

                if (ImGui.IsItemDeactivatedAfterEdit())
                {
                    if (!string.IsNullOrWhiteSpace(ItemSearchInput) && ItemSearchInput.Length > 1)
                    {
                        _ItemNames = ItemNames
                                     .Where(
                                         x => x.Key.Contains(ItemSearchInput, StringComparison.OrdinalIgnoreCase))
                                     .ToDictionary(x => x.Key, x => x.Value);
                    }
                }

                ImGui.Separator();

                var longestText = string.Empty;
                foreach (var (itemName, item) in _ItemNames)
                {
                    if (itemName.Length > longestText.Length) longestText = itemName;

                    var isConflictKeyHolding = Service.KeyState[Service.Config.ConflictKey];
                    var icon = ImageHelper.GetIcon(item.Icon, isConflictKeyHolding).ImGuiHandle;

                    if (ImGuiOm.SelectableImageWithText(icon, ScaledVector2(24f), itemName, false))
                        Service.Chat.Print(new SeStringBuilder().AddItemLink(item.RowId, isConflictKeyHolding).Build());
                }

                maxTextWidth = ImGui.CalcTextSize(longestText).X + (200f * GlobalFontScale);
                maxTextWidth = Math.Max(TwentyCharsSize.X, maxTextWidth);

                ImGui.EndChild();
            }

            ImGui.SetWindowSize(new(Math.Max(DefaultOverlayWidth, maxTextWidth),
                                    ModuleConfig.OverlayHeight * GlobalFontScale));

            ImGui.EndTabItem();
        }
    }

    private static void SpecialIconCharTabItem()
    {
        if (ImGui.BeginTabItem(Service.Lang.GetText("QuickChatPanel-SpecialIconChar")))
        {
            var maxTextWidth = 300f * GlobalFontScale;
            if (ImGui.BeginChild("SeIconChild", ImGui.GetContentRegionAvail(), false))
            {
                ImGui.BeginGroup();
                for (var i = 0; i < SeIconChars.Length; i++)
                {
                    var icon = SeIconChars[i];

                    if (ImGui.Button($"{icon}", new(96 * ModuleConfig.FontScale)))
                        ImGui.SetClipboardText(icon.ToString());

                    ImGuiOm.TooltipHover($"0x{(int)icon:X4}");

                    if ((i + 1) % 7 != 0) ImGui.SameLine();
                    else
                    {
                        ImGui.SameLine();
                        ImGui.Dummy(new(20 * ModuleConfig.FontScale));
                    }
                }
                ImGui.EndGroup();

                maxTextWidth = ImGui.GetItemRectSize().X;
                maxTextWidth = Math.Max(TwentyCharsSize.X, maxTextWidth);

                ImGui.EndChild();
            }

            ImGui.SetWindowSize(new(Math.Max(DefaultOverlayWidth, maxTextWidth),
                                    ModuleConfig.OverlayHeight * GlobalFontScale));

            ImGui.EndTabItem();
        }
    }

    private void OnAddon(AddonEvent type, AddonArgs? args)
    {
        if (!Throttler.Throttle("QuickChatPanel-UIAdjust")) return;
        switch (type)
        {
            case AddonEvent.PostSetup:
            case AddonEvent.PostRefresh:
                if (Service.ClientState.LocalPlayer == null || AddonState.ChatLog == null) return;
                FreeNode();

                var textInputNode = AddonState.ChatLog->GetNodeById(5);
                var collisionNode = AddonState.ChatLog->GetNodeById(15);
                if (textInputNode == null || collisionNode == null) return;

                ButtonPos = new Vector2(textInputNode->X + textInputNode->Width - ModuleConfig.ButtonSize - 6,
                                        textInputNode->Y - 3) + ModuleConfig.ButtonOffset;

                AtkResNode* iconNode = null;
                for (var i = 0; i < AddonState.ChatLog->UldManager.NodeListCount; i++)
                {
                    var node = AddonState.ChatLog->UldManager.NodeList[i];
                    if (node->NodeId == 10001)
                    {
                        iconNode = node;
                        break;
                    }
                }

                if (iconNode is null)
                    MakeIconNode(10001, ButtonPos, ModuleConfig.ButtonIcon);
                else
                {
                    iconNode->SetPositionFloat(ButtonPos.X, ButtonPos.Y);
                    iconNode->SetHeight(ModuleConfig.ButtonSize);
                    iconNode->SetWidth(ModuleConfig.ButtonSize);
                    ((AtkImageNode*)iconNode)->LoadIconTexture((uint)ModuleConfig.ButtonIcon, 0);
                }

                break;
            case AddonEvent.PreFinalize:
                FreeNode();
                break;
        }
    }

    private void MakeIconNode(uint nodeId, Vector2 position, int icon)
    {
        var imageNode = MakeImageNode(nodeId, new PartInfo(0, 0, 64, 64));
        imageNode->AtkResNode.NodeFlags = NodeFlags.AnchorTop | NodeFlags.AnchorLeft | NodeFlags.Visible |
                                          NodeFlags.Enabled | NodeFlags.EmitsEvents;

        imageNode->WrapMode = 1;
        imageNode->Flags = (byte)ImageNodeFlags.AutoFit;

        imageNode->LoadIconTexture((uint)icon, 0);
        imageNode->AtkResNode.ToggleVisibility(true);

        imageNode->AtkResNode.SetWidth(ModuleConfig.ButtonSize);
        imageNode->AtkResNode.SetHeight(ModuleConfig.ButtonSize);
        imageNode->AtkResNode.SetPositionShort((short)position.X, (short)position.Y);

        LinkNodeAtEnd((AtkResNode*)imageNode, AddonState.ChatLog);

        imageNode->AtkResNode.NodeFlags |= NodeFlags.RespondToMouse | NodeFlags.EmitsEvents | NodeFlags.HasCollision;
        AddonState.ChatLog->UpdateCollisionNodeList(true);
        MouseClickHandle ??=
            Service.AddonEvent.AddEvent((nint)AddonState.ChatLog, (nint)(&imageNode->AtkResNode), AddonEventType.MouseClick,
                                        OnEvent);
    }

    private void OnEvent(AddonEventType atkEventType, nint atkUnitBase, nint atkResNode)
    {
        Overlay.IsOpen ^= true;
    }

    private static void FreeNode()
    {
        if (AddonState.ChatLog == null) return;

        for (var i = 0; i < AddonState.ChatLog->UldManager.NodeListCount; i++)
        {
            var node = AddonState.ChatLog->UldManager.NodeList[i];
            if (node->NodeId == 10001)
            {
                UnlinkAndFreeImageNode((AtkImageNode*)node, AddonState.ChatLog);
                Service.AddonEvent.RemoveEvent(MouseClickHandle);
                MouseClickHandle = null;
            }
        }
    }

    private void SwapMacros(int index1, int index2)
    {
        (ModuleConfig.SavedMacros[index1], ModuleConfig.SavedMacros[index2]) =
            (ModuleConfig.SavedMacros[index2], ModuleConfig.SavedMacros[index1]);

        TaskHelper.Abort();

        TaskHelper.DelayNext(500);
        TaskHelper.Enqueue(() => { SaveConfig(ModuleConfig); });
    }

    private void SwapMessages(int index1, int index2)
    {
        (ModuleConfig.SavedMessages[index1], ModuleConfig.SavedMessages[index2]) =
            (ModuleConfig.SavedMessages[index2], ModuleConfig.SavedMessages[index1]);

        TaskHelper.Abort();

        TaskHelper.DelayNext(500);
        TaskHelper.Enqueue(() => { SaveConfig(ModuleConfig); });
    }

    public override void Uninit()
    {
        FreeNode();

        Service.AddonLifecycle.UnregisterListener(OnAddon);

        base.Uninit();
    }

    public class SavedMacro : IEquatable<SavedMacro>
    {
        public uint     Category       { get; set; } // 0 - Individual; 1 - Shared
        public int      Position       { get; set; }
        public string   Name           { get; set; } = string.Empty;
        public uint     IconID         { get; set; } = 0;
        public DateTime LastUpdateTime { get; set; } = DateTime.MinValue;

        public bool Equals(SavedMacro? other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            return Category == other.Category && Position == other.Position;
        }

        public override bool Equals(object? obj)
        {
            if (obj is null) return false;
            if (ReferenceEquals(this, obj)) return true;
            return obj.GetType() == GetType() && Equals((SavedMacro)obj);
        }

        public override int GetHashCode() { return HashCode.Combine(Category, Position); }
    }

    private enum MacroDisplayMode
    {
        List,
        Buttons,
    }

    private class Config : ModuleConfiguration
    {
        public readonly List<SavedMacro> SavedMacros = [];
        public readonly List<string> SavedMessages = [];
        public readonly Dictionary<uint, string> SoundEffectNotes = [];
        public int ButtonIcon = 46;
        public Vector2 ButtonOffset = new(0);
        public ushort ButtonSize = 48;
        public float FontScale = 1.5f;
        public float OverlayHeight = 250f;
        public Vector2 OverlayOffset = new(0);
        public MacroDisplayMode OverlayMacroDisplayMode = MacroDisplayMode.Buttons;
    }
}
