using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using DailyRoutines.Managers;
using Dalamud.Interface.Textures.TextureWraps;

namespace DailyRoutines.Helpers;

public static class ImageHelper
{
    private class ImageState
    {
        public IDalamudTextureWrap? Texture    { get; set; }
        public bool                 IsComplete { get; set; }
    }

    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static readonly ConcurrentDictionary<string, ImageState> FileImages = [];
    private static readonly ConcurrentDictionary<(uint ID, bool isHQ), ImageState> IconImages = [];
    private static readonly SemaphoreSlim LoadingSemaphore = new(1, 1);

    private static void ExecuteOnMainThread(Action action)
    {
        Service.Framework.RunOnFrameworkThread(action).Wait();
    }

    public static IDalamudTextureWrap? GetIcon(uint iconID, bool isHQ = false)
        => GetOrLoadImage(IconImages, (iconID, isHQ), () => LoadGameIcon(iconID, isHQ));

    public static IDalamudTextureWrap? GetImage(string urlOrPath)
        => GetOrLoadImage(FileImages, urlOrPath, () => LoadImage(urlOrPath));

    public static bool TryGetIcon(uint iconID, out IDalamudTextureWrap? image, bool isHQ = false)
        => TryGetOrLoadImage(IconImages, (iconID, isHQ), () => LoadGameIcon(iconID, isHQ), out image);

    public static bool TryGetImage(string url, out IDalamudTextureWrap? image)
        => TryGetOrLoadImage(FileImages, url, () => LoadImage(url), out image);

    public static async Task<IDalamudTextureWrap?> GetImageAsync(string urlOrPath)
    {
        if (FileImages.TryGetValue(urlOrPath, out var state) && state.IsComplete)
            return state.Texture;

        await LoadingSemaphore.WaitAsync();
        try
        {
            if (FileImages.TryGetValue(urlOrPath, out state) && state.IsComplete)
                return state.Texture;

            if (!FileImages.TryGetValue(urlOrPath, out state))
            {
                state = new ImageState();
                FileImages[urlOrPath] = state;
            }

            if (!state.IsComplete)
            {
                var texture = await LoadImage(urlOrPath);
                ExecuteOnMainThread(() =>
                {
                    state.Texture = texture;
                    state.IsComplete = true;
                });
            }

            return state.Texture;
        } finally
        {
            LoadingSemaphore.Release();
        }
    }

    private static IDalamudTextureWrap? GetOrLoadImage<TKey>(
        ConcurrentDictionary<TKey, ImageState> cache, TKey key, Func<Task<IDalamudTextureWrap?>> loader)
        where TKey : notnull
    {
        if (cache.TryGetValue(key, out var state) && state.IsComplete)
            return state.Texture;

        _ = LoadImageAsync(cache, key, loader);
        return null;
    }

    private static bool TryGetOrLoadImage<TKey>(
        ConcurrentDictionary<TKey, ImageState> cache, TKey key, Func<Task<IDalamudTextureWrap?>> loader,
        out IDalamudTextureWrap? image) where TKey : notnull
    {
        if (cache.TryGetValue(key, out var state) && state.IsComplete)
        {
            image = state.Texture;
            return image != null;
        }

        _ = LoadImageAsync(cache, key, loader);
        image = null;
        return false;
    }

    private static async Task LoadImageAsync<TKey>(
        ConcurrentDictionary<TKey, ImageState> cache, TKey key, Func<Task<IDalamudTextureWrap?>> loader)
        where TKey : notnull
    {
        await LoadingSemaphore.WaitAsync();
        try
        {
            if (!cache.TryGetValue(key, out var state))
            {
                state = new ImageState();
                cache[key] = state;
            }

            if (!state.IsComplete)
            {
                var texture = await loader();
                ExecuteOnMainThread(() =>
                {
                    state.Texture = texture;
                    state.IsComplete = true;
                });
            }
        } finally
        {
            LoadingSemaphore.Release();
        }
    }

    private static async Task<IDalamudTextureWrap?> LoadGameIcon(uint iconID, bool isHQ)
    {
        var iconData = await Task.Run(() => Service.Texture.GetFromGameIcon(new(iconID, isHQ)));
        IDalamudTextureWrap? result = null;
        ExecuteOnMainThread(() => result = iconData.GetWrapOrDefault());
        return result;
    }

    private static async Task<IDalamudTextureWrap?> LoadImage(string urlOrPath)
    {
        byte[] content;
        if (urlOrPath.StartsWith("http:", StringComparison.OrdinalIgnoreCase) ||
            urlOrPath.StartsWith("https:", StringComparison.OrdinalIgnoreCase))
        {
            using var response = await HttpClient.GetAsync(urlOrPath);
            response.EnsureSuccessStatusCode();
            content = await response.Content.ReadAsByteArrayAsync();
        }
        else
        {
            IDalamudTextureWrap? result = null;
            ExecuteOnMainThread(() =>
            {
                result = File.Exists(urlOrPath)
                             ? Service.Texture.GetFromFile(urlOrPath).GetWrapOrDefault()
                             : Service.Texture.GetFromGame(urlOrPath).GetWrapOrDefault();
            });

            return result;
        }

        try
        {
            IDalamudTextureWrap? result = null;
            ExecuteOnMainThread(async () => result = await Service.Texture.CreateFromImageAsync(content));
            return result;
        }
        catch (Exception ex)
        {
            NotifyHelper.Error("", ex);
            return null;
        }
    }
}
