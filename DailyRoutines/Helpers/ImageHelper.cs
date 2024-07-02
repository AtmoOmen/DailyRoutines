using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using DailyRoutines.Managers;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;

namespace DailyRoutines.Helpers;

public static class ImageHelper
{
    private record ImageLoadingResult
    {
        public ISharedImmediateTexture? ImmediateTexture { get; set; }
        public IDalamudTextureWrap?     TextureWrap      { get; set; }
        public bool                     IsCompleted      { get; set; }

        public IDalamudTextureWrap? Texture =>
            Service.Framework.RunOnFrameworkThread(() => ImmediateTexture?.GetWrapOrDefault() ?? TextureWrap).Result;
    }

    private static readonly ConcurrentDictionary<string, ImageLoadingResult> CachedTextures = [];
    private static readonly ConcurrentDictionary<(uint ID, bool HQ), ImageLoadingResult> CachedIcons = [];
    private static readonly List<Func<byte[], byte[]>> ConversionsToBitmap = [b => b];

    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static volatile bool ThreadRunning;

    public static IDalamudTextureWrap? GetIcon(uint iconID, bool isHQ = false)
    {
        TryGetIconTextureWrap(iconID, isHQ, out var texture);
        return texture;
    }

    public static IDalamudTextureWrap? GetImage(string urlOrPath)
    {
        TryGetTextureWrap(urlOrPath, out var texture);
        return texture;
    }

    public static bool TryGetImage(string urlOrPath, out IDalamudTextureWrap? textureWrap)
        => TryGetTextureWrap(urlOrPath, out textureWrap);

    public static bool TryGetIconTextureWrap(uint icon, bool hq, out IDalamudTextureWrap? textureWrap)
    {
        if (!CachedIcons.TryGetValue((icon, hq), out var result))
        {
            result = new();
            CachedIcons[(icon, hq)] = result;
            BeginThreadIfNotRunning();
        }

        textureWrap = result.Texture;
        return result.Texture != null;
    }

    public static bool TryGetTextureWrap(string url, out IDalamudTextureWrap? textureWrap)
    {
        if (!CachedTextures.TryGetValue(url, out var result))
        {
            result = new();
            CachedTextures[url] = result;
            BeginThreadIfNotRunning();
        }

        textureWrap = result.Texture;
        return result.Texture != null;
    }

    public static async Task<IDalamudTextureWrap?> GetImageAsync(string urlOrPath)
    {
        TryGetTextureWrap(urlOrPath, out var texture);
        while (texture == null)
        {
            await Task.Delay(100);
            TryGetTextureWrap(urlOrPath, out texture);
        }

        return texture;
    }

    internal static void BeginThreadIfNotRunning()
    {
        if (ThreadRunning) return;
        NotifyHelper.Verbose("Starting ThreadLoadImageHandler");
        ThreadRunning = true;
        Task.Run(async () =>
        {
            var idleTicks = 0;
            while (idleTicks < 100)
                if (await LoadPendingTexturesAsync() || await LoadPendingIconsAsync())
                    idleTicks = 0;
                else
                {
                    idleTicks++;
                    await Task.Delay(100);
                }

            NotifyHelper.Verbose($"Stopping ThreadLoadImageHandler, ticks={idleTicks}");
            ThreadRunning = false;
        });
    }

    private static async Task<bool> LoadPendingTexturesAsync()
    {
        if (CachedTextures.TryGetFirst(x => x.Value.IsCompleted == false, out var keyValuePair))
        {
            keyValuePair.Value.IsCompleted = true;
            try
            {
                if (Uri.TryCreate(keyValuePair.Key, UriKind.Absolute, out var uri) &&
                    uri.Scheme is "http" or "https")
                {
                    var content = await HttpClient.GetByteArrayAsync(uri);
                    foreach (var conversion in ConversionsToBitmap)
                        try
                        {
                            var texture = await Service.Texture.CreateFromImageAsync(conversion(content));
                            keyValuePair.Value.TextureWrap = texture;
                            return true;
                        }
                        catch (Exception ex)
                        {
                            NotifyHelper.Error($"Error converting image: {uri}", ex);
                        }
                }

                keyValuePair.Value.ImmediateTexture = File.Exists(keyValuePair.Key)
                                                          ? Service.Texture.GetFromFile(keyValuePair.Key)
                                                          : Service.Texture.GetFromGame(keyValuePair.Key);
            }
            catch (Exception ex)
            {
                NotifyHelper.Error($"Error loading image: {keyValuePair.Key}", ex);
            }

            return true;
        }

        return false;
    }

    private static async Task<bool> LoadPendingIconsAsync()
    {
        return await Task.Run(() =>
        {
            if (CachedIcons.TryGetFirst(x => x.Value.IsCompleted == false, out var keyValuePair))
            {
                keyValuePair.Value.IsCompleted = true;
                NotifyHelper.Verbose($"Loading icon {keyValuePair.Key.ID}, hq={keyValuePair.Key.HQ}");
                try
                {
                    keyValuePair.Value.ImmediateTexture =
                        Service.Texture.GetFromGameIcon(new(keyValuePair.Key.ID, hiRes: keyValuePair.Key.HQ));
                }
                catch (Exception ex)
                {
                    NotifyHelper.Error($"Error loading icon: {keyValuePair.Key.ID}, hq={keyValuePair.Key.HQ}", ex);
                }

                return Task.FromResult(true);
            }

            return Task.FromResult(false);
        });
    }

    public static void ClearAll()
    {
        foreach (var (_, value) in CachedTextures)
            try
            {
                value.TextureWrap?.Dispose();
            }
            catch (Exception ex)
            {
                NotifyHelper.Error("Error disposing texture", ex);
            }

        CachedTextures.Clear();

        foreach (var (_, value) in CachedIcons)
            try
            {
                value.TextureWrap?.Dispose();
            }
            catch (Exception ex)
            {
                NotifyHelper.Error("Error disposing icon", ex);
            }

        CachedIcons.Clear();
    }

}
