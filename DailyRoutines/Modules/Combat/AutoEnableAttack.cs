using DailyRoutines.Infos;

namespace DailyRoutines.Modules;

[ModuleDescription("AutoEnableAttackTitle", "AutoEnableAttackDescription", ModuleCategories.战斗)]
public class AutoEnableAttack : DailyModuleBase
{
    private static readonly MemoryPatch AutoAttackPatch = 
        new("41 B0 01 41 0F B6 D0 E9 ?? ?? ?? ?? 41 B0 01", [0x41, 0xF6, 0x47, 0x39, 0x04, 0x0F, 0x85, 0xA7, 0x00, 0x00, 0x00, 0x90]);

    public override void Init()
    {
        AutoAttackPatch.Set(true);
    }

    public override void Uninit()
    {
        AutoAttackPatch.Set(false);
    }
}
