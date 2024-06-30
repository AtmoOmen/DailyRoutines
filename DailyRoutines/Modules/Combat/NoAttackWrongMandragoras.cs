using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DailyRoutines.Helpers;
using DailyRoutines.Managers;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Hooking;
using Dalamud.Utility.Signatures;

using Lumina.Excel.GeneratedSheets;
using GameObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;

namespace DailyRoutines.Modules;

[ModuleDescription("NoAttackWrongMandragorasTitle", "NoAttackWrongMandragorasDescription",
                   ModuleCategories.战斗)]
public unsafe class NoAttackWrongMandragoras : DailyModuleBase
{
    [Signature("40 53 48 83 EC 20 F3 0F 10 89 ?? ?? ?? ?? 0F 57 C0 0F 2E C8 48 8B D9 7A 0A",
               DetourName = nameof(IsTargetableDetour))]
    private static Hook<IsTargetableDelegate>? IsTargetableHook;

    private static List<uint[]>? Mandragoras;

    private static readonly List<IBattleNpc> ValidBattleNPCs = [];

    // 水城, 运河, 运河深层, 运河神殿, 梦羽宝境, 梦羽宝殿, 惊奇, 育体
    private static readonly HashSet<uint> ValidZones = [558, 712, 725, 794, 879, 924, 1000, 1123];

    public override void Init()
    {
        Mandragoras ??= LuminaCache.Get<BNpcName>()
                                   .Where(x => x.Singular.RawString.Contains("王后"))
                                   .Select(queen => new[]
                                   {
                                       queen.RowId - 4, queen.RowId - 3, queen.RowId - 2, queen.RowId - 1, queen.RowId,
                                   })
                                   .ToList();

        Service.Hook.InitializeFromAttributes(this);
        IsTargetableHook?.Enable();

        Service.ClientState.TerritoryChanged += OnZoneChanged;
        if (ValidZones.Contains(Service.ClientState.TerritoryType))
            OnZoneChanged(Service.ClientState.TerritoryType);
    }

    private static void OnZoneChanged(ushort zone)
    {
        if (ValidZones.Contains(zone))
            IsTargetableHook?.Enable();
        else
            IsTargetableHook?.Disable();
    }

    private static byte IsTargetableDetour(GameObject* potentialTarget)
    {
        var isTargetable = IsTargetableHook.Original(potentialTarget);
        if (!ValidZones.Contains(Service.ClientState.TerritoryType) || Mandragoras == null) return isTargetable;

        if (Throttler.Throttle("NoAttackWrongMandragoras-Update", 100))
        {
            ValidBattleNPCs.Clear();
            foreach (var obj in Service.ObjectTable)
            {
                var distance = Vector3.Distance(Service.ClientState.LocalPlayer.Position, obj.Position);
                if (distance > 45) continue;
                if (obj.IsValid() && obj is IBattleNpc { IsDead: false } battleObj)
                    ValidBattleNPCs.Add(battleObj);
            }
        }

        var objID = potentialTarget->GetNameId();
        foreach (var mandragoraSeries in Mandragoras)
        {
            var index = Array.IndexOf(mandragoraSeries, objID);
            if (index != -1)
            {
                for (var i = index - 1; i > -1; i--)
                {
                    var mandragora =
                        ValidBattleNPCs.FirstOrDefault(
                            x => ((GameObject*)x.Address)->BaseId == mandragoraSeries[i]);

                    if (mandragora is { IsDead: false } && mandragora.IsValid())
                        return 0;
                }

                return 1;
            }
        }

        return isTargetable;
    }

    public override void Uninit()
    {
        Service.ClientState.TerritoryChanged -= OnZoneChanged;

        base.Uninit();
    }

    private delegate byte IsTargetableDelegate(GameObject* gameObj);
}
