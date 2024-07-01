using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using DailyRoutines.Infos;
using DailyRoutines.Managers;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using ImGuiNET;

namespace DailyRoutines.Modules;

[ModuleDescription("AutoAdjustScreenBrightnessTitle", "AutoAdjustScreenBrightnessDescription", ModuleCategories.一般)]
public class AutoAdjustScreenBrightness : DailyModuleBase, IDisposable
{
    private static readonly ScreenHelper screenHelper = new();
    private static readonly CancellationTokenSource cancelTokenSource = new();
    private static Config moduleConfig = null!;
    private static int originalBrightness = 100;
    private static int currentScreenBrightness;
    private static double currentSceneBrightness;

    public override void Init()
    {
        moduleConfig = LoadConfig<Config>() ?? new Config();
        originalBrightness = screenHelper.GetCurrentBrightness() + 10;
        _ = MonitorBrightnessAsync(cancelTokenSource.Token);
    }

    public override void ConfigUI()
    {
        DisplayCurrentBrightness();
        ConfigureSettings();
    }

    private static void DisplayCurrentBrightness()
    {
        ImGui.BeginGroup();
        ImGui.Text(
            $"{Service.Lang.GetText("AutoAdjustScreenBrightness-CurrentScreenBrightness")}: {currentScreenBrightness}%");

        ImGui.SameLine();
        ImGui.Text(
            $"{Service.Lang.GetText("AutoAdjustScreenBrightness-CurrentSceneBrightness")}: {Math.Round(currentSceneBrightness, 2)}%");

        ImGui.EndGroup();

        ImGui.GetWindowDrawList().AddRect(
            ImGui.GetItemRectMin() - ImGuiHelpers.ScaledVector2(2f),
            ImGui.GetItemRectMax() + ImGuiHelpers.ScaledVector2(2f),
            ImGui.ColorConvertFloat4ToU32(ImGuiColors.DalamudWhite),
            2f, ImDrawFlags.RoundCornersAll, 3f);

        ImGuiHelpers.ScaledDummy(5f);
    }

    private void ConfigureSettings()
    {
        if (ImGui.Checkbox(Service.Lang.GetText("AutoAdjustScreenBrightness-DisableInCutscene"),
                           ref moduleConfig.DisableInCutscene))
            SaveConfig(moduleConfig);

        ConfigureSlider("AutoAdjustScreenBrightness-Threshold", ref moduleConfig.BrightnessThreshold, -10, 100);
        ConfigureSlider("AutoAdjustScreenBrightness-ThresholdContent", ref moduleConfig.BrightnessThresholdContent, -10,
                        100);

        ConfigureSlider("AutoAdjustScreenBrightness-AdjustSpeed", ref moduleConfig.AdjustSpeed, 1, 200);
        ConfigureSlider("AutoAdjustScreenBrightness-BrightnessMin", ref moduleConfig.BrightnessMin, -10, 110);
        ConfigureSlider("AutoAdjustScreenBrightness-BrightnessOriginal", ref originalBrightness, -10, 110);
    }

    private void ConfigureSlider(string label, ref int value, int min, int max)
    {
        ImGui.SetNextItemWidth(150f * ImGuiHelpers.GlobalScale);
        if (ImGui.SliderInt($"{Service.Lang.GetText(label)} (%)", ref value, min, max) &&
            ImGui.IsItemDeactivatedAfterEdit())
            SaveConfig(moduleConfig);

        ImGuiOm.TooltipHover(Service.Lang.GetText($"{label}Help"));
    }

    private static async Task MonitorBrightnessAsync(CancellationToken token)
    {
        const int hysteresis = 5;
        var stepSize = Math.Max(1, (moduleConfig.AdjustSpeed / 10) + 1);
        var currentBrightness = originalBrightness;
        var targetBrightness = originalBrightness;
        var lastSignificantBrightness = originalBrightness;
        var haveRestored = false;

        while (!token.IsCancellationRequested)
        {
            if (ShouldSkipAdjustment())
            {
                await Task.Delay(100, token);
                continue;
            }

            using var bitmap = CaptureScreen();
            currentScreenBrightness = screenHelper.GetCurrentBrightness();
            currentSceneBrightness = CalculateBrightPixelsPercentage(bitmap);

            if (Math.Abs(currentSceneBrightness - lastSignificantBrightness) > hysteresis)
            {
                targetBrightness = currentSceneBrightness > GetBrightnessThreshold()
                                       ? moduleConfig.BrightnessMin
                                       : originalBrightness;

                lastSignificantBrightness = (int)currentSceneBrightness;
            }

            if (ShouldRestoreBrightness(ref haveRestored))
            {
                targetBrightness = originalBrightness;
                screenHelper.SetBrightness(targetBrightness);
                haveRestored = true;
            }
            else if (haveRestored)
            {
                screenHelper.SetBrightness(targetBrightness);
                haveRestored = false;
            }

            if (currentBrightness != targetBrightness)
            {
                currentBrightness = AdjustBrightness(currentBrightness, targetBrightness, stepSize);
                screenHelper.SetBrightness(currentBrightness);
            }

            await Task.Delay(100, token);
        }
    }

    private static bool ShouldSkipAdjustment() =>
        (moduleConfig.DisableInCutscene && Flags.WatchingCutscene) || !ApplicationIsActivated();

    private static int GetBrightnessThreshold() =>
        Flags.BoundByDuty ? moduleConfig.BrightnessThresholdContent : moduleConfig.BrightnessThreshold;

    private static bool ShouldRestoreBrightness(ref bool haveRestored) =>
        (moduleConfig.DisableInCutscene && Flags.WatchingCutscene) || (!ApplicationIsActivated() && !haveRestored);

    private static int AdjustBrightness(int current, int target, int step) =>
        Math.Clamp(current + (target > current ? step : -step), 0, 255);

    private static Bitmap CaptureScreen()
    {
        var bounds = Screen.GetBounds(Point.Empty);
        var bitmap = new Bitmap(bounds.Width, bounds.Height);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(Point.Empty, Point.Empty, bounds.Size);
        return bitmap;
    }

    private static double CalculateBrightPixelsPercentage(Bitmap bitmap)
    {
        var marginWidth = bitmap.Width / 4;
        var marginHeight = bitmap.Height / 4;
        var centerWidth = bitmap.Width / 2;
        var centerHeight = bitmap.Height / 2;

        var centerRect = new Rectangle(marginWidth, marginHeight, centerWidth, centerHeight);
        var bitmapData = bitmap.LockBits(centerRect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        var bytesPerPixel = Image.GetPixelFormatSize(bitmapData.PixelFormat) / 8;
        var byteCount = bitmapData.Stride * bitmapData.Height;
        var pixels = new byte[byteCount];
        Marshal.Copy(bitmapData.Scan0, pixels, 0, byteCount);
        bitmap.UnlockBits(bitmapData);

        double brightPixels = 0;
        var totalPixels = centerWidth * centerHeight;

        for (var y = 0; y < centerHeight; y++)
        {
            var currentLine = y * bitmapData.Stride;
            for (var x = 0; x < centerWidth; x++)
            {
                var xIndex = currentLine + (x * bytesPerPixel);
                var blue = pixels[xIndex];
                var green = pixels[xIndex + 1];
                var red = pixels[xIndex + 2];

                var perceivedBrightness = ((0.299 * red) + (0.587 * green) + (0.114 * blue)) / 255.0;

                if (perceivedBrightness > 0.8)
                    brightPixels += 1.5;
                else if (perceivedBrightness > 0.75)
                    brightPixels++;
            }
        }

        return Math.Min(brightPixels, totalPixels) / totalPixels * 100;
    }

    public override void Uninit()
    {
        base.Uninit();
        cancelTokenSource.Cancel();
        screenHelper.SetBrightness(originalBrightness);
    }

    public void Dispose()
    {
        cancelTokenSource.Dispose();
        screenHelper.Dispose();
    }

    private class Config : ModuleConfiguration
    {
        public int AdjustSpeed = 50;
        public int BrightnessMin = 30;
        public int BrightnessThreshold = 55;
        public int BrightnessThresholdContent = 65;
        public bool DisableInCutscene = true;
    }

    private class ScreenHelper : IDisposable
    {
        private readonly ManagementScope _scope = new("root\\WMI");
        private readonly ManagementObjectSearcher _searcherBrightness;
        private readonly ManagementObjectSearcher _searcherMethods;
        private readonly byte[] _validBrightnessLevels;

        public ScreenHelper()
        {
            _searcherBrightness = new ManagementObjectSearcher(_scope, new SelectQuery("WmiMonitorBrightness"));
            _searcherMethods = new ManagementObjectSearcher(_scope, new SelectQuery("WmiMonitorBrightnessMethods"));
            _validBrightnessLevels = GetBrightnessLevels();
            IsSupported = _validBrightnessLevels.Length > 0;
        }

        public bool IsSupported { get; }

        public int GetCurrentBrightness()
        {
            foreach (var o in _searcherBrightness.Get())
            {
                var obj = (ManagementObject)o;
                return (byte)obj.GetPropertyValue("CurrentBrightness");
            }

            return 0;
        }

        public void SetBrightness(int brightness)
        {
            if (!IsSupported) return;

            var targetBrightness = (byte)Math.Clamp(brightness, 0, 100);
            var nearestValidBrightness = _validBrightnessLevels.FirstOrDefault(level => level >= targetBrightness);

            if (nearestValidBrightness > 0)
            {
                foreach (var o in _searcherMethods.Get())
                {
                    var obj = (ManagementObject?)o;
                    obj?.InvokeMethod("WmiSetBrightness", new object[] { uint.MaxValue, nearestValidBrightness });
                    break;
                }
            }
        }

        private byte[] GetBrightnessLevels()
        {
            try
            {
                foreach (var o in _searcherBrightness.Get())
                {
                    var obj = (ManagementObject)o;
                    return (byte[])obj.GetPropertyValue("Level");
                }
            }
            catch (Exception ex)
            {
                Service.Log.Error(ex.Message);
            }

            return [];
        }

        public void Dispose()
        {
            _searcherBrightness.Dispose();
            _searcherMethods.Dispose();
        }
    }
}
