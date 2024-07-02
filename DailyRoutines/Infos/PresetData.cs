using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DailyRoutines.Helpers;
using DailyRoutines.Managers;
using Dalamud.Interface.Internal;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Lumina.Excel.GeneratedSheets;
using Action = Lumina.Excel.GeneratedSheets.Action;
using Status = Lumina.Excel.GeneratedSheets.Status;

namespace DailyRoutines.Infos;

public class PresetData
{
    public static Dictionary<uint, Action>                 PlayerActions   => playerActions.Value;
    public static Dictionary<uint, Status>                 Statuses        => statuses.Value;
    public static Dictionary<uint, ContentFinderCondition> Contents        => contents.Value;
    public static Dictionary<uint, Item>                   Gears           => gears.Value;
    public static Dictionary<uint, Item>                   Dyes            => dyes.Value;
    public static Dictionary<uint, World>                  CNWorlds        => cnWorlds.Value;
    public static Dictionary<uint, TerritoryType>          Zones           => zones.Value;
    public static Dictionary<uint, Mount>                  Mounts          => mounts.Value;
    public static Dictionary<uint, Item>                   Food            => food.Value;
    public static Dictionary<uint, Item>                   Seeds           => seeds.Value;
    public static Dictionary<uint, Item>                   Soils           => soils.Value;
    public static Dictionary<uint, Item>                   Fertilizers     => fertilizers.Value;
    public static Dictionary<uint, ContentFinderCondition> HighEndContents => highEndContents.Value;
    public static ISharedImmediateTexture Icon => icon.Value;

    public static bool TryGetContent(uint rowID, out ContentFinderCondition content)
        => Contents.TryGetValue(rowID, out content);

    public static bool TryGetGear(uint rowID, out Item item)
        => Gears.TryGetValue(rowID, out item);

    public static bool TryGetCNWorld(uint rowID, out World world)
        => CNWorlds.TryGetValue(rowID, out world);

    #region Lazy

    private static readonly Lazy<ISharedImmediateTexture> icon =
        new(() => Service.Texture.GetFromFile(Path.Join(Service.PluginInterface.AssemblyLocation.DirectoryName, "Assets", "icon.png")));

    private static readonly Lazy<Dictionary<uint, ContentFinderCondition>> highEndContents =
        new(() => LuminaCache.Get<ContentFinderCondition>()
                             .Where(x => !string.IsNullOrWhiteSpace(x.Name.RawString) && x.HighEndDuty)
                             .DistinctBy(x => x.TerritoryType.Row)
                             .ToDictionary(x => x.TerritoryType.Row, x => x));

    private static readonly Lazy<Dictionary<uint, Item>> seeds =
        new(() => LuminaCache.Get<Item>()
                             .Where(x => x.FilterGroup == 20)
                             .ToDictionary(x => x.RowId, x => x));

    private static readonly Lazy<Dictionary<uint, Item>> soils =
        new(() => LuminaCache.Get<Item>()
                             .Where(x => x.FilterGroup == 21)
                             .ToDictionary(x => x.RowId, x => x));

    private static readonly Lazy<Dictionary<uint, Item>> fertilizers =
        new(() => LuminaCache.Get<Item>()
                             .Where(x => x.FilterGroup == 22)
                             .ToDictionary(x => x.RowId, x => x));

    private static readonly Lazy<Dictionary<uint, Action>> playerActions =
        new(() => LuminaCache.Get<Action>()
                             .Where(x => x.ClassJob.Value != null && x.Range != -1 && x.Icon != 0 &&
                                         !string.IsNullOrWhiteSpace(x.Name.RawString))
                             .Where(x => x is
                             {
                                 IsPlayerAction: false,
                                 ClassJobLevel: > 0,
                             }
                                             or { IsPlayerAction: true })
                             .OrderBy(x => x.ClassJob.Row)
                             .ThenBy(x => x.ClassJobLevel)
                             .ToDictionary(x => x.RowId, x => x));

    private static readonly Lazy<Dictionary<uint, Status>> statuses =
        new(() => LuminaCache.Get<Status>()
                             .Where(x => !string.IsNullOrWhiteSpace(x.Name.RawString))
                             .ToDictionary(x => x.RowId, x => x));

    private static readonly Lazy<Dictionary<uint, ContentFinderCondition>> contents =
        new(() => LuminaCache.Get<ContentFinderCondition>()
                             .Where(x => !string.IsNullOrWhiteSpace(x.Name.RawString))
                             .DistinctBy(x => x.TerritoryType.Row)
                             .OrderBy(x => x.ContentType.Row)
                             .ThenBy(x => x.ClassJobLevelRequired)
                             .ToDictionary(x => x.TerritoryType.Row, x => x));

    private static readonly Lazy<Dictionary<uint, Item>> gears =
        new(() => LuminaCache.Get<Item>()
                             .Where(x => x.EquipSlotCategory.Value.RowId != 0)
                             .DistinctBy(x => x.RowId)
                             .ToDictionary(x => x.RowId, x => x));

    private static readonly Lazy<Dictionary<uint, Item>> dyes =
        new(() => LuminaCache.Get<StainTransient>()
                             .Where(x => x.Item1.Value != null)
                             .ToDictionary(x => x.RowId, x => x.Item1.Value)!);

    private static readonly Lazy<Dictionary<uint, World>> cnWorlds =
        new(() => LuminaCache.Get<World>()
                             .Where(x => x.DataCenter.Value.Region == 5 &&
                                         !string.IsNullOrWhiteSpace(x.Name.RawString) &&
                                         !string.IsNullOrWhiteSpace(x.InternalName.RawString) &&
                                         IsChineseString(x.Name.RawString))
                             .ToDictionary(x => x.RowId, x => x));

    private static readonly Lazy<Dictionary<uint, TerritoryType>> zones =
        new(() => LuminaCache.Get<TerritoryType>()
                             .Where(x => !(string.IsNullOrWhiteSpace(x.Name.RawString) ||
                                           x.PlaceNameIcon <= 0 || x.PlaceNameRegionIcon <= 0))
                             .ToDictionary(x => x.RowId, x => x));

    private static readonly Lazy<Dictionary<uint, Mount>> mounts =
        new(() => LuminaCache.Get<Mount>()
                             .Where(x => !string.IsNullOrWhiteSpace(x.Singular.RawString) && x.Icon > 0)
                             .ToDictionary(x => x.RowId, x => x));

    private static readonly Lazy<Dictionary<uint, Item>> food =
        new(() => LuminaCache.Get<Item>()
                             .Where(x => !string.IsNullOrWhiteSpace(x.Name.RawString) && x.FilterGroup == 5)
                             .ToDictionary(x => x.RowId, x => x));

    #endregion
}
