using System;
using System.Collections.Generic;
using System.Linq;
using DailyRoutines.Helpers;
using DailyRoutines.Managers;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;

namespace DailyRoutines.Modules;

[ModuleDescription("AutoCancelCastTitle", "AutoCancelCastDescription", ModuleCategories.技能)]
public unsafe class AutoCancelCast : DailyModuleBase
{
    [Signature("48 83 EC 38 33 D2 C7 44 24 20 00 00 00 00 45 33 C9")]
    private static Action? CancelCast;

    private static HashSet<uint>? TargetAreaActions;

    public override void Init()
    {
        Service.Hook.InitializeFromAttributes(this);
        TaskHelper ??= new TaskHelper { AbortOnTimeout = true, TimeLimitMS = 10000, ShowDebug = false };

        TargetAreaActions ??= LuminaCache.Get<Lumina.Excel.GeneratedSheets.Action>()
                                         .Where(x => x.TargetArea)
                                         .Select(x => x.RowId).ToHashSet();

        Service.Condition.ConditionChange += OnConditionChanged;
    }

    private void OnConditionChanged(ConditionFlag flag, bool value)
    {
        if (flag is ConditionFlag.Casting or ConditionFlag.Casting87)
        {
            if (value)
                TaskHelper.Enqueue(IsNeedToCancel);
            else
                TaskHelper.Abort();
        }
    }

    private static bool? IsNeedToCancel()
    {
        var player = Service.ClientState.LocalPlayer;
        if (player.CastActionType != 1 || TargetAreaActions.Contains(player.CastActionId)) return true;

        var obj = (GameObject*)CharacterManager.Instance()->LookupBattleCharaByEntityId((uint)player.CastTargetObjectId);
        if (obj == null || ActionManager.CanUseActionOnTarget(player.CastActionId, obj)) return false;

        CancelCast();
        return true;
    }

    public override void Uninit()
    {
        Service.Condition.ConditionChange -= OnConditionChanged;

        base.Uninit();
    }
}
