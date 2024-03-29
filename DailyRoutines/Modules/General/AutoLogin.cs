using ClickLib;
using DailyRoutines.Infos;
using DailyRoutines.Managers;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using ECommons.Automation;
using ECommons.ExcelServices;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ImGuiNET;

namespace DailyRoutines.Modules;

[ModuleDescription("AutoLoginTitle", "AutoLoginDescription", ModuleCategories.General)]
public class AutoLogin : DailyModuleBase
{
    private class SavedWorld(string name, uint worldID)
    {
        public string Name { get; set; } = name;
        public uint WorldID { get; set; } = worldID;
    }

    private static bool HasLoginOnce;

    private static string WorldInput = string.Empty;
    private static SavedWorld? ConfigSelectedWorld;
    private static int ConfigSelectedCharaIndex = -1;

    public override void Init()
    {
        AddConfig(this, "SelectedWorld", null);
        AddConfig(this, "SelectedCharaIndex", 0);

        ConfigSelectedWorld = GetConfig<SavedWorld?>(this, "SelectedWorld");
        WorldInput = ConfigSelectedWorld == null ? string.Empty : ConfigSelectedWorld.Name;
        ConfigSelectedCharaIndex = GetConfig<int>(this, "SelectedCharaIndex");

        TaskManager ??= new TaskManager { AbortOnTimeout = true, TimeLimitMS = 20000, ShowDebug = false };
        Service.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "_TitleMenu", OnTitleMenu);
    }

    public override unsafe void ConfigUI()
    {
        ConflictKeyText();

        ImGui.AlignTextToFramePadding();
        ImGui.Text($"{Service.Lang.GetText("AutoLogin-ServerName")}:");

        ImGui.SameLine();
        ImGui.SetNextItemWidth(100f * ImGuiHelpers.GlobalScale);
        if (ImGui.InputText("##AutoLogin-EnterServerName", ref WorldInput, 16,
                            ImGuiInputTextFlags.EnterReturnsTrue))
        {
            if (ExcelWorldHelper.TryGet(WorldInput, out var world))
            {
                WorldInput = world.Name.RawString;
                ConfigSelectedWorld = new(world.Name.RawString, world.RowId);
                UpdateConfig(this, "SelectedWorld", ConfigSelectedWorld);
            }
            else
            {
                Service.Chat.PrintError(
                    Service.Lang.GetText("AutoLogin-ServerNotFoundErrorMessage", WorldInput));
                WorldInput = string.Empty;
                ConfigSelectedWorld = null;
                UpdateConfig(this, "SelectedWorld", ConfigSelectedWorld);
            }
        }

        ImGui.SameLine();
        if (ImGui.SmallButton(Service.Lang.GetText("AutoLogin-CurrentWorld")))
        {
            if (ExcelWorldHelper.TryGet(AgentLobby.Instance()->LobbyData.CurrentWorldName.ExtractText(), out var world))
            {
                WorldInput = world.Name.RawString;
                ConfigSelectedWorld = new(world.Name.RawString, world.RowId);
                UpdateConfig(this, "SelectedWorld", ConfigSelectedWorld);
            }
        }

        ImGui.SameLine();
        ImGui.Spacing();

        ImGui.SameLine();
        ImGui.Text($"{Service.Lang.GetText("AutoLogin-CharacterIndex")}:");

        ImGui.SameLine();
        ImGui.SetNextItemWidth(100f * ImGuiHelpers.GlobalScale);
        if (ImGui.InputInt("##AutoLogin-EnterCharaIndex", ref ConfigSelectedCharaIndex, 1, 1,
                           ImGuiInputTextFlags.EnterReturnsTrue))
        {
            if (ConfigSelectedCharaIndex is < 0 or > 8) ConfigSelectedCharaIndex = 0;
            else
            {
                UpdateConfig(this, "SelectedCharaIndex", ConfigSelectedCharaIndex);
            }
        }

        ImGuiOm.TooltipHover(Service.Lang.GetText("AutoLogin-CharaIndexInputTooltip"));

        ImGui.AlignTextToFramePadding();
        ImGui.Text($"{Service.Lang.GetText("AutoLogin-LoginState")}:");
        
        ImGui.SameLine();
        ImGui.TextColored(HasLoginOnce ? ImGuiColors.HealerGreen : ImGuiColors.DPSRed, HasLoginOnce ? Service.Lang.GetText("AutoLogin-LoginOnce") : Service.Lang.GetText("AutoLogin-HaveNotLogin"));

        ImGui.SameLine();
        if (ImGui.SmallButton(Service.Lang.GetText("Refresh")))
        {
            HasLoginOnce = false;
        }
    }

    private void OnTitleMenu(AddonEvent eventType, AddonArgs? addonInfo)
    {
        if (ConfigSelectedWorld == null || ConfigSelectedCharaIndex == -1) return;
        if (HasLoginOnce) return;

        if (InterruptByConflictKey()) return;

        TaskManager.Enqueue(SelectStartGame);
    }

    private unsafe bool? SelectStartGame()
    {
        if (InterruptByConflictKey()) return true;

        AgentManager.SendEvent(AgentId.Lobby, 0, 1);
        TaskManager.Enqueue(SelectCharacter);
        return true;
    }

    private unsafe bool? SelectCharacter()
    {
        if (InterruptByConflictKey()) return true;

        if (Service.Gui.GetAddonByName("_TitleMenu") != nint.Zero) return false;
        var agent = AgentLobby.Instance();
        if (agent == null) return false;

        if (TryGetAddonByName<AtkUnitBase>("_CharaSelectListMenu", out var addon) &&
            HelpersOm.IsAddonAndNodesReady(addon))
        {
            if (agent->WorldId == 0) return false;
            if (agent->WorldId == ConfigSelectedWorld.WorldID)
            {
                AgentManager.SendEvent(AgentId.Lobby, 0, 6, ConfigSelectedCharaIndex);
                AgentManager.SendEvent(AgentId.Lobby, 0, 18, 0, ConfigSelectedCharaIndex);
                AgentManager.SendEvent(AgentId.Lobby, 0, 6, ConfigSelectedCharaIndex);

                TaskManager.Enqueue(() => Click.TrySendClick("select_yes"));
                TaskManager.Enqueue(() => HasLoginOnce = true);
                return true;
            }

            TaskManager.Enqueue(SelectWorld);
            return true;
        }

        return false;
    }

    private unsafe bool? SelectWorld()
    {
        if (InterruptByConflictKey()) return true;

        var agent = AgentLobby.Instance();
        if (agent == null) return false;

        if (TryGetAddonByName<AtkUnitBase>("_CharaSelectWorldServer", out var addon))
        {
            for (var i = 0; i < 16; i++)
            {
                AgentManager.SendEvent(AgentId.Lobby, 0, 9, 0, i);

                if (agent->WorldId == ConfigSelectedWorld.WorldID)
                {
                    AgentManager.SendEvent(AgentId.Lobby, 0, 10, 0, i);

                    TaskManager.DelayNext(200);
                    TaskManager.Enqueue(SelectCharacter);

                    return true;
                }
            }
        }

        return false;
    }

    public override void Uninit()
    {
        Service.AddonLifecycle.UnregisterListener(OnTitleMenu);
        HasLoginOnce = false;

        base.Uninit();
    }
}
