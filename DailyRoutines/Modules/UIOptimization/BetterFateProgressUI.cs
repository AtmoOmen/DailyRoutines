using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using DailyRoutines.Helpers;
using DailyRoutines.Infos;
using DailyRoutines.Managers;
using DailyRoutines.Windows;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Hooking;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ImGuiNET;
using Lumina.Excel.GeneratedSheets;
using Achievement = FFXIVClientStructs.FFXIV.Client.Game.UI.Achievement;

namespace DailyRoutines.Modules;

[ModuleDescription("BetterFateProgressUITitle", "BetterFateProgressUIDescription", ModuleCategories.界面优化)]
public class BetterFateProgressUI : DailyModuleBase
{
    private unsafe delegate void ReceiveAchievementProgressDelegate(Achievement* achievement, uint id, uint current, uint max);
    [Signature("C7 81 ?? ?? ?? ?? ?? ?? ?? ?? 89 91 ?? ?? ?? ?? 44 89 81", DetourName = nameof(ReceiveAchievementProgressDetour))]
    private static Hook<ReceiveAchievementProgressDelegate>? ReceiveAchievementProgressHook;

    private static readonly Throttler<string> Throttler = new();

    private static readonly Dictionary<uint, uint> AchievementToZone = new()
    {
        { 2343, 813 }, // 雷克兰德
        { 2345, 815 }, // 安穆·艾兰
        { 2346, 816 }, // 伊尔美格
        { 2344, 814 }, // 珂露西亚岛
        { 2347, 817 }, // 拉凯提卡大森林
        { 2348, 818 }, // 黑风海
        { 3022, 956 }, // 迷津
        { 3023, 957 }, // 萨维奈岛
        { 3024, 958 }, // 加雷马
        { 3025, 959 }, // 叹息海
        { 3026, 961 }, // 厄尔庇斯
        { 3027, 960 }, // 天外天垓
    };
    private static readonly Dictionary<uint, uint> FateProgress = [];
    private static readonly Dictionary<uint, uint> AchievementToAetheryte = new()
    {
        { 2343, 132 }, // 雷克兰德
        { 2344, 139 }, // 珂露西亚岛
        { 2345, 140 }, // 安穆·艾兰
        { 2346, 144 }, // 伊尔美格
        { 2347, 142 }, // 拉凯提卡大森林
        { 2348, 147 }, // 黑风海
        { 3022, 166 }, // 迷津
        { 3023, 169 }, // 萨维奈岛
        { 3024, 172 }, // 加雷马
        { 3025, 175 }, // 叹息海
        { 3026, 176 }, // 厄尔庇斯
        { 3027, 181 }, // 天外天垓
    };

    private static readonly Dictionary<uint, IDalamudTextureWrap> ZoneTextures = [];
    private static readonly Vector2 FateProgressUISize = ScaledVector2(450f, 150);

    private static IDalamudTextureWrap? BicolorGemIcon;
    private static int BicolorGemAmount;
    private static uint BicolorGemCap;
    private static Vector2 BicolorGemComponentSize;

    public override void Init()
    {
        Service.Hook.InitializeFromAttributes(this);
        ReceiveAchievementProgressHook.Enable();

        ObtainAllFateProgress();
        RefreshBackground();

        BicolorGemIcon ??= ImageHelper.GetIcon(LuminaCache.GetRow<Item>(26807).Icon);
        BicolorGemCap = LuminaCache.GetRow<Item>(26807).StackSize;

        Overlay ??= new Overlay(this);
        Overlay.Flags &= ~ImGuiWindowFlags.NoTitleBar;
        Overlay.Flags &= ~ImGuiWindowFlags.AlwaysAutoResize;
        Overlay.Flags |= ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;

        Overlay.SizeConstraints = new()
        {
            MinimumSize = FateProgressUISize,
        };

        Overlay.WindowName = $"{LuminaCache.GetRow<Addon>(3924).Text.RawString}###BetterFateProgressUI";
        Service.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "FateProgress", OnAddon);
    }

    public override unsafe void OverlayPreDraw()
    {
        if (!Throttler.Throttle("Refresh", 10000)) return;

        ObtainAllFateProgress();
        BicolorGemAmount = InventoryManager.Instance()->GetInventoryItemCount(26807);
    }

    public override void OverlayOnOpen() => ObtainAllFateProgress();

    public override void OverlayUI()
    {
        using (FontManager.UIFont120.Push())
        {
            DrawBicolorGemComponent();
            DrawFateProgressTabs();

            ImGui.Dummy(new(1));
        }
    }

    private static void DrawBicolorGemComponent()
    {
        if (BicolorGemIcon == null) return;

        var originalPos = ImGui.GetCursorPos();
        ImGui.SetCursorPos(BicolorGemComponentSize with 
                               { X = ImGui.GetWindowSize().X - BicolorGemComponentSize.X - (ImGui.GetStyle().ItemSpacing.X * 2) });
        ImGui.BeginGroup();

        ImGui.Image(BicolorGemIcon.ImGuiHandle, ScaledVector2(24f));

        ImGui.SameLine();
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 2f);
        ImGui.Text($"{BicolorGemAmount}/{BicolorGemCap}");

        ImGui.EndGroup();
        ImGui.SetCursorPos(originalPos);
        BicolorGemComponentSize = ImGui.GetItemRectSize();
    }

    private static void DrawFateProgressTabs()
    {
        if (FateProgress.Count != AchievementToZone.Count) return;

        ImGui.BeginGroup();
        if (ImGui.BeginTabBar("FateProgressTab"))
        {
            DrawFateProgressTabItem("5.0", 0, 6);
            DrawFateProgressTabItem("6.0", 6, 6);
            ImGui.EndTabBar();
        }
        ImGui.EndGroup();

        var itemSpacing = ImGui.GetStyle().ItemSpacing;
        ImGui.SetWindowSize(new((FateProgressUISize.X * 2) + (itemSpacing.X * 3), ImGui.GetCursorPosY() + itemSpacing.Y));
    }

    private static void DrawFateProgressTabItem(string tabTitle, int start, int count)
    {
        if (ImGui.BeginTabItem(tabTitle))
        {
            var bgCounter = (uint)start;
            var counter = 0;

            foreach (var (achievementID, fateProgress) in FateProgress.Skip(start).Take(count))
            {
                DrawMapFateProgressChild(achievementID, fateProgress, bgCounter);
                if (counter == 0) ImGui.SameLine();

                counter++;
                bgCounter++;
                if (counter == 2) counter = 0;
            }

            ImGui.EndTabItem();
        }
    }

    private static void DrawMapFateProgressChild(uint achievementID, uint fateProgress, uint bgCounter)
    {
        var zoneSheetRow = LuminaCache.GetRow<TerritoryType>(AchievementToZone[achievementID]);

        if (ImGui.BeginChild($"{achievementID}", FateProgressUISize, true, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            DrawBackgroundImage(bgCounter);
            DisplayZoneName(zoneSheetRow);
            DisplayFateProgress(fateProgress);
            ImGui.EndChild();
        }

        HandleInteraction(achievementID, zoneSheetRow);
    }

    private static void DrawBackgroundImage(uint bgCounter)
    {
        var originalCursorPos = ImGui.GetCursorPos();
        if (ZoneTextures.TryGetValue(bgCounter, out var textureWarp))
        {
            ImGui.SetCursorPos(originalCursorPos - ScaledVector2(10f, 4));
            ImGui.Image(textureWarp.ImGuiHandle, ImGui.GetWindowSize() + ScaledVector2(10f, 4f));
        }
        ImGui.SetCursorPos(originalCursorPos);
    }

    private static void DisplayZoneName(TerritoryType zoneSheetRow)
    {
        ImGui.SetWindowFontScale(1.05f);
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (8f * GlobalFontScale));
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (4f * GlobalFontScale));
        ImGui.Text(zoneSheetRow.ExtractPlaceName());
    }

    private static void DisplayFateProgress(uint fateProgress)
    {
        ImGui.SetWindowFontScale(0.8f);
        var text = fateProgress > 6 ? $"{fateProgress - 6}/60" : $"{fateProgress}/6";
        ImGui.SetCursorPosY(ImGui.GetContentRegionMax().Y - ImGui.CalcTextSize(text).Y);
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (4f * GlobalFontScale));
        ImGui.Text(text);

        DisplayFinalProgress(fateProgress);
    }

    private static void DisplayFinalProgress(uint fateProgress)
    {
        var remainingProgress = 66 - fateProgress;
        var text = fateProgress == 66
                       ? LuminaCache.GetRow<Addon>(3930).Text.RawString
                       : Service.Lang.GetText("BetterFateProgressUI-LeftFateAmount", remainingProgress);

        ImGui.SetWindowFontScale(0.95f);
        ImGui.SameLine();
        ImGui.SetCursorPosX(ImGui.GetContentRegionMax().X - ImGui.CalcTextSize(text).X);
        ImGui.TextColored(ImGuiColors.ParsedGold, text);
    }

    private static unsafe void HandleInteraction(uint achievementID, TerritoryType zoneSheetRow)
    {
        if (ImGui.IsItemHovered())
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
        {
            var agent = AgentMap.Instance();
            if (agent->AgentInterface.IsAgentActive() && agent->SelectedMapId == zoneSheetRow.Map.Row)
                agent->AgentInterface.Hide();
            else
            {
                agent->MapTitleString = *Utf8String.FromString(LuminaCache.GetRow<Addon>(3924).Text);
                agent->OpenMapByMapId(zoneSheetRow.Map.Row);
            }
        }

        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            if (AchievementToAetheryte.TryGetValue(achievementID, out var aetheryteID))
                Telepo.Instance()->Teleport(aetheryteID, 0);
        }
    }

    private static void ObtainAllFateProgress()
    {
        foreach (var achivement in AchievementToZone.Keys)
            Service.ExecuteCommandManager.ExecuteCommand(ExecuteCommandFlag.RequestAchievement, (int)achivement);
    }

    private static void RefreshBackground()
    {
        Task.Run(async () =>
        {
            ZoneTextures.Clear();

            const string uldPath = "ui/uld/FateProgress.uld";
            const string fate3Path = "ui/uld/FlyingPermission3_hr1.tex";
            const string fate4Path = "ui/uld/FlyingPermission4_hr1.tex";

            var sbBackground = await ImageHelper.GetImageAsync(fate3Path);
            if (sbBackground != null)
            {
                for (var i = 0; i < 6; i++)
                    ZoneTextures[(uint)i] = Service.PluginInterface.UiBuilder.LoadUld(uldPath).LoadTexturePart(fate3Path, i);
            }

            var ewBackground = await ImageHelper.GetImageAsync(fate4Path);
            if (ewBackground != null)
            {
                for (var i = 0; i < 6; i++)
                    ZoneTextures[(uint)i + 6] =
                        Service.PluginInterface.UiBuilder.LoadUld(uldPath).LoadTexturePart(fate4Path, i);
            }
        });
    }

    private static unsafe void ReceiveAchievementProgressDetour(Achievement* achievement, uint id, uint current, uint max)
    {
        ReceiveAchievementProgressHook.Original(achievement, id, current, max);

        if (!AchievementToZone.ContainsKey(id)) return;
        FateProgress[id] = current;
    }

    private unsafe void OnAddon(AddonEvent type, AddonArgs args)
    {
        var addon = (AtkUnitBase*)args.Addon;
        if (addon == null) return;

        addon->Close(true);
        Overlay.IsOpen ^= true;
    }

    public override void Uninit()
    {
        FateProgress.Clear();
        Service.AddonLifecycle.UnregisterListener(OnAddon);

        base.Uninit();
    }
}
