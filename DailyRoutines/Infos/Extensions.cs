using System;
using System.IO;
using System.Net;
using System.Numerics;
using DailyRoutines.Helpers;
using DailyRoutines.Managers;
using DailyRoutines.Modules;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Network.Structures.InfoProxy;
using Dalamud.Game.Text.SeStringHandling;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using Lumina.Excel.GeneratedSheets;

namespace DailyRoutines.Infos;

public static class Extensions
{
    /// <summary>
    /// You need to specify the position and category by yourself
    /// </summary>
    /// <param name="macro"></param>
    /// <returns></returns>
    public static QuickChatPanel.SavedMacro ToSavedMacro(this RaptureMacroModule.Macro macro)
    {
        var savedMacro = new QuickChatPanel.SavedMacro
        {
            Name = macro.Name.ExtractText(),
            IconID = macro.IconId,
            LastUpdateTime = DateTime.Now
        };

        return savedMacro;
    }

    public static unsafe ExpandPlayerMenuSearch.CharacterSearchInfo ToCharacterSearchInfo(this ICharacter chara)
    {
        var info = new ExpandPlayerMenuSearch.CharacterSearchInfo()
        {
            Name = chara.Name.ExtractText(),
            World = LuminaCache.GetRow<World>(((FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)chara.Address)->HomeWorld).Name.RawString
        };
        return info;
    }

    public static unsafe FFXIVClientStructs.FFXIV.Client.Game.Character.Character* ToCharacterStruct(this ICharacter chara)
        => (FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)chara.Address;

    public static ExpandPlayerMenuSearch.CharacterSearchInfo ToCharacterSearchInfo(this CharacterData chara)
    {
        var info = new ExpandPlayerMenuSearch.CharacterSearchInfo()
        {
            Name = chara.Name,
            World = chara.HomeWorld.GameData.Name.RawString
        };
        return info;
    }

    public static BitmapFontIcon ToBitmapFontIcon(this ClassJob? job)
    {
        if (job == null || job.RowId == 0) return BitmapFontIcon.NewAdventurer;
        return (BitmapFontIcon)job.RowId + 127;
    }

    public static string ExtractPlaceName(this TerritoryType row) 
        => row.PlaceName.Value.Name.RawString;

    public static Vector2 ToVector2(this Vector3 vector3) 
        => new(vector3.X, vector3.Z);

    public static Vector3 ToVector3(this Vector2 vector2) 
        => vector2.ToVector3(Service.ClientState.LocalPlayer?.Position.Y ?? 0);

    public static Vector3 ToVector3(this Vector2 vector2, float Y) 
        => new(vector2.X, Y, vector2.Y);

    public static void SaveToBinaryFile(this WebResponse response, string filePath)
    {
        var buffer = new byte[1024];
        using var rs = response.GetResponseStream();
        using var fileStream = new FileStream(
            filePath,
            FileMode.OpenOrCreate,
            FileAccess.Write,
            FileShare.ReadWrite
        );

        while (true)
        {
            var count = rs.Read(buffer, 0, buffer.Length);
            if (count <= 0)
            {
                break;
            }

            fileStream.Write(buffer, 0, count);
        }
    }
}
