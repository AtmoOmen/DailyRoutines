using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using DailyRoutines.Managers;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;

namespace DailyRoutines.Helpers;

public static class ImageHelper
{
    internal class ImageLoadingResult
    {
        internal ISharedImmediateTexture? ImmediateTexture;
        internal IDalamudTextureWrap? TextureWrap;

        internal IDalamudTextureWrap? Texture =>
            Service.Framework.RunOnFrameworkThread(() => ImmediateTexture?.GetWrapOrDefault() ?? TextureWrap).Result;
        internal bool IsCompleted;
        public ImageLoadingResult(ISharedImmediateTexture? immediateTexture) { ImmediateTexture = immediateTexture; }
        public ImageLoadingResult(IDalamudTextureWrap? textureWrap) { TextureWrap = textureWrap; }
        public ImageLoadingResult() { }
    }

    internal static ConcurrentDictionary<string, ImageLoadingResult> CachedTextures = new();
    internal static ConcurrentDictionary<(uint ID, bool HQ), ImageLoadingResult> CachedIcons = new();

    private static readonly List<Func<byte[], byte[]>> _conversionsToBitmap = [b => b];

    private static volatile bool ThreadRunning;
    internal static HttpClient? httpClient;

    public static void ClearAll()
    {
        foreach (var x in CachedTextures)
            Safe(() => { x.Value.TextureWrap?.Dispose(); });

        Safe(CachedTextures.Clear);
        foreach (var x in CachedIcons)
            Safe(() => { x.Value.TextureWrap?.Dispose(); });

        Safe(CachedIcons.Clear);
    }

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
        httpClient ??= new()
        {
            Timeout = TimeSpan.FromSeconds(10),
        };

        if (ThreadRunning) return;
        NotifyHelper.Verbose("Starting ThreadLoadImageHandler");
        ThreadRunning = true;
        new Thread(() =>
        {
            var idleTicks = 0;
            Safe(delegate
            {
                while (idleTicks < 100)
                {
                    Safe(delegate
                    {
                        {
                            if (CachedTextures.TryGetFirst(x => x.Value.IsCompleted == false, out var keyValuePair))
                            {
                                idleTicks = 0;
                                keyValuePair.Value.IsCompleted = true;
                                NotifyHelper.Verbose("Loading image " + keyValuePair.Key);
                                if (keyValuePair.Key.StartsWith("http:", StringComparison.OrdinalIgnoreCase) ||
                                    keyValuePair.Key.StartsWith("https:", StringComparison.OrdinalIgnoreCase))
                                {
                                    var result = httpClient.GetAsync(keyValuePair.Key).Result;
                                    result.EnsureSuccessStatusCode();
                                    var content = result.Content.ReadAsByteArrayAsync().Result;

                                    IDalamudTextureWrap texture = null;
                                    foreach (var conversion in _conversionsToBitmap)
                                        try
                                        {
                                            texture = Service.Texture.CreateFromImageAsync(conversion(content)).Result;
                                            break;
                                        }
                                        catch (Exception ex)
                                        {
                                            NotifyHelper.Error("", ex);
                                        }

                                    keyValuePair.Value.TextureWrap = texture;
                                }
                                else
                                {
                                    if (File.Exists(keyValuePair.Key))
                                        keyValuePair.Value.ImmediateTexture = Service.Texture.GetFromFile(keyValuePair.Key);
                                    else
                                        keyValuePair.Value.ImmediateTexture = Service.Texture.GetFromGame(keyValuePair.Key);
                                }
                            }
                        }

                        {
                            if (CachedIcons.TryGetFirst(x => x.Value.IsCompleted == false, out var keyValuePair))
                            {
                                idleTicks = 0;
                                keyValuePair.Value.IsCompleted = true;
                                NotifyHelper.Verbose($"Loading icon {keyValuePair.Key.ID}, hq={keyValuePair.Key.HQ}");
                                keyValuePair.Value.ImmediateTexture =
                                    Service.Texture.GetFromGameIcon(new(keyValuePair.Key.ID, hiRes: keyValuePair.Key.HQ));
                            }
                        }
                    });

                    idleTicks++;
                    if (CachedTextures.All(x => x.Value.IsCompleted) && CachedIcons.All(x => x.Value.IsCompleted))
                        Thread.Sleep(100);
                }
            });

            NotifyHelper.Verbose($"Stopping ThreadLoadImageHandler, ticks={idleTicks}");
            ThreadRunning = false;
        }).Start();
    }

    public static void AddConversionToBitmap(Func<byte[], byte[]> conversion) { _conversionsToBitmap.Add(conversion); }

    public static void RemoveConversionToBitmap(Func<byte[], byte[]> conversion)
    {
        _conversionsToBitmap.Remove(conversion);
    }

    private static void Safe(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            NotifyHelper.Error("Error in Safe action", ex);
        }
    }
}
