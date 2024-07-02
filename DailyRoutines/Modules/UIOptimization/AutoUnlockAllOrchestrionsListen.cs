using DailyRoutines.Managers;
using Dalamud;
using Dalamud.Hooking;
using Dalamud.Utility.Signatures;

namespace DailyRoutines.Modules;

[ModuleDescription("AutoUnlockAllOrchestrionsListenTitle", "AutoUnlockAllOrchestrionsListenDescription",
                   ModuleCategories.界面优化)]
public unsafe class AutoUnlockAllOrchestrionsListen : DailyModuleBase
{
    [Signature("E8 ?? ?? ?? ?? 32 C0 48 83 C4 ?? 5B C3 48 8B CB E8 ?? ?? ?? ?? 32 C0 48 83 C4 ?? 5B C3 CC CC CC CC CC CC CC CC CC CC CC",
               DetourName = nameof(PlayMusicHotelPreCheckDetour))]
    private static Hook<PlayMusicPreCheckDelegate>? PlayMusicHotelPreCheckHook;

    // [Signature(
    //     "E8 ?? ?? ?? ?? 32 C0 48 83 C4 ?? 5B C3 CC CC CC CC CC CC CC CC CC CC CC CC CC CC CC CC CC CC CC CC 48 89 5C 24",
    //     DetourName = nameof(PlayMusicHomePreCheckDetour))]
    // private static Hook<PlayMusicPreCheckDelegate>? PlayMusicHomePreCheckHook;
    //
    // [Signature("E8 ?? ?? ?? ?? 32 C0 48 83 C4 ?? 5B C3 48 8B CB E8 ?? ?? ?? ?? 32 C0 48 83 C4 ?? 5B C3 48 8B CB",
    //            DetourName = nameof(AddToPlaylistDetour))]
    // private static Hook<PlayMusicPreCheckDelegate>? PlayMusicAddToPlaylistHook;

    [Signature("40 57 48 83 EC ?? 0F B7 F9 B9")]
    private static PlayMusicDelegate? PlayMusic;


    public override void Init()
    {
        Service.Hook.InitializeFromAttributes(this);
        PlayMusicHotelPreCheckHook?.Enable();
        // PlayMusicHomePreCheckHook?.Enable();
        // PlayMusicAddToPlaylistHook.Enable();
    }

    private static nint PlayMusicHotelPreCheckDetour(nint a1, uint pageIndex)
    {
        UnlockAndParseOrchestions(a1, pageIndex, out var musicIndex);

        PlayMusic(musicIndex);
        // PlayMusicHotelPreCheckHook.Original(a1, pageIndex);
        return 0;
    }

    private static nint PlayMusicHomePreCheckDetour(nint a1, uint pageIndex)
    {
        UnlockAndParseOrchestions(a1, pageIndex, out var musicIndex);

        PlayMusic(musicIndex);
        // var original = PlayMusicHomePreCheckHook.Original(a1, pageIndex);
        return 0;
    }

    // private static nint AddToPlaylistDetour(nint a1, uint pageIndex)
    // {
    //     UnlockAndParseOrchestions(a1, pageIndex, out _);
    //
    //     var original = PlayMusicAddToPlaylistHook.Original(a1, pageIndex);
    //     return original;
    // }

    private static void UnlockAndParseOrchestions(nint a1, uint pageIndex, out uint musicIndex)
    {
        var musicAddress = (uint*)(*(long*)(a1 + 1744) + (120L * pageIndex));
        musicIndex = *musicAddress;

        var musicUnlock = *((byte*)musicAddress + 8);
        if (musicUnlock != 1)
            SafeMemory.Write((nint)((byte*)musicAddress + 8), (byte)1);
    }

    private delegate nint PlayMusicPreCheckDelegate(nint a1, uint index);

    private delegate nint PlayMusicDelegate(uint a1);
}
