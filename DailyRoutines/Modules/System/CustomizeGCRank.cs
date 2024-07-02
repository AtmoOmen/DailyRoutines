using DailyRoutines.Managers;
using Dalamud.Hooking;
using Dalamud.Interface.Utility;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using ImGuiNET;

namespace DailyRoutines.Modules;

[ModuleDescription("CustomizeGCRankTitle", "CustomizeGCRankDescription", ModuleCategories.系统)]
public unsafe class CustomizeGCRank : DailyModuleBase
{
    private delegate byte GetGrandCompanyRankDeleagte(PlayerState* instance);

    [Signature("0F B6 81 ?? ?? ?? ?? FF C8 83 F8 ?? 73", DetourName = nameof(GetGrandCompanyRankDetour))]
    private static Hook<GetGrandCompanyRankDeleagte>? GetGrandCompanyRankHook;

    private static byte? OriginalRank;

    private static int CustomRank = 11;

    public override void Init()
    {
        AddConfig(nameof(CustomRank), CustomRank);
        CustomRank = GetConfig<int>(nameof(CustomRank));

        Service.Hook.InitializeFromAttributes(this);
        GetGrandCompanyRankHook?.Enable();
    }

    public override void ConfigUI()
    {
        ImGui.SetNextItemWidth(200f * GlobalFontScale);
        ImGui.SliderInt(Service.Lang.GetText("CustomizeGCRank-GCRank"), ref CustomRank, 1, 19);

        if (ImGui.IsItemDeactivatedAfterEdit())
            UpdateConfig(nameof(CustomRank), CustomRank);
    }

    private static byte GetGrandCompanyRankDetour(PlayerState* instance)
    {
        var original = GetGrandCompanyRankHook.Original(instance);
        if (original == 0) return original;

        OriginalRank ??= original;
        return (byte)CustomRank;
    }

    public override void Uninit()
    {
        var instance = PlayerState.Instance();
        switch (instance->GrandCompany)
        {
            case 1:
                instance->GCRankMaelstrom = OriginalRank ?? (byte)CustomRank;
                break;
            case 2:
                instance->GCRankTwinAdders = OriginalRank ?? (byte)CustomRank;
                break;
            case 3:
                instance->GCRankImmortalFlames = OriginalRank ?? (byte)CustomRank;
                break;
        }

        OriginalRank = null;

        base.Uninit();
    }
}
