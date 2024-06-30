using System;
using System.Linq;
using System.Numerics;
using System.Reflection;
using DailyRoutines.Helpers;
using DailyRoutines.Infos;
using DailyRoutines.Managers;
using DailyRoutines.Modules;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Windowing;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using ImGuiNET;
using Lumina.Excel.GeneratedSheets;
using Map = Lumina.Excel.GeneratedSheets.Map;

namespace DailyRoutines.Windows;

public unsafe class Debug() : Window("Daily Routines - 调试窗口###DailyRoutines-Debug", ImGuiWindowFlags.AlwaysAutoResize), IDisposable
{
    internal class Config
    {
        public bool ShowExecuteCommandLog;
        public bool ShowLogMessageLog;
        public bool ShowUseActionLog;
        public bool ShowUseActionLocationLog;
        public bool ShowUseActionPetMoveLog;
    }

    internal static Config DebugConfig = new();
    private static (int commmand, int p1, int p2, int p3, int p4) ExecuteCommandManual;

    public override void Draw()
    {
        if (ImGui.BeginTabBar("DebugTab"))
        {
            DrawInfo();
            DrawManagers();
            DrawOthers();
            ImGui.EndTabBar();
        }
    }

    private static void DrawInfo()
    {
        if (ImGui.BeginTabItem("信息"))
        {
            var infoInstance = TerritoryInfo.Instance();
            var mapAgent = AgentMap.Instance();
            var houseManager = HousingManager.Instance();
            if (ImGui.BeginTable("TerritoryInfoTable", 2, ImGuiTableFlags.Borders))
            {
                ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
                ImGui.TableNextColumn();
                ImGui.Text("区域信息");

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text("Territory Type");
                ImGui.TableNextColumn();
                ImGui.Text($"{Service.ClientState.TerritoryType} ({LuminaCache.GetRow<TerritoryType>(Service.ClientState.TerritoryType)?.ExtractPlaceName() ?? ""})");

                ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
                ImGui.TableNextColumn();
                ImGui.Text("地图信息");

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text("Map ID");
                ImGui.TableNextColumn();
                ImGui.Text($"{Service.ClientState.MapId} ({LuminaCache.GetRow<Map>(Service.ClientState.MapId)?.PlaceName.Value.Name ?? ""})");

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text("Map Size Factor");
                ImGui.TableNextColumn();
                ImGui.Text($"{mapAgent->CurrentMapSizeFactorFloat}");

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text("Map Offset");
                ImGui.TableNextColumn();
                ImGui.Text($"{new Vector2(mapAgent->CurrentOffsetX, mapAgent->CurrentOffsetY)}");

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text("Selected Map ID");
                ImGui.TableNextColumn();
                ImGui.Text($"{mapAgent->SelectedMapId}");

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text("Selected Map Size Factor");
                ImGui.TableNextColumn();
                ImGui.Text($"{mapAgent->SelectedMapSizeFactorFloat}");

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text("Selected Map Offset");
                ImGui.TableNextColumn();
                ImGui.Text($"{new Vector2(mapAgent->SelectedOffsetX, mapAgent->SelectedOffsetY)}");

                if (mapAgent->IsFlagMarkerSet == 1)
                {
                    ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
                    ImGui.TableNextColumn();
                    ImGui.Text("标点信息");

                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.Text("Position");
                    ImGui.TableNextColumn();
                    ImGui.Text($"{new Vector2(mapAgent->FlagMapMarker.XFloat, mapAgent->FlagMapMarker.YFloat)}");

                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.Text("Territory Type");
                    ImGui.TableNextColumn();
                    ImGui.Text($"{mapAgent->FlagMapMarker.TerritoryId} ({LuminaCache.GetRow<TerritoryType>(mapAgent->FlagMapMarker.TerritoryId)?.ExtractPlaceName() ?? ""})");

                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.Text("Map ID");
                    ImGui.TableNextColumn();
                    ImGui.Text($"{mapAgent->FlagMapMarker.MapId} ({LuminaCache.GetRow<Map>(Service.ClientState.MapId)?.PlaceName.Value.Name ?? ""})");
                }

                ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
                ImGui.TableNextColumn();
                ImGui.Text("地区信息");

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text("Area PlaceName ID");
                ImGui.TableNextColumn();
                ImGui.Text($"{infoInstance->AreaPlaceNameId}");

                ImGui.TableNextColumn();
                ImGui.Text("Area PlaceName Name");
                ImGui.TableNextColumn();
                ImGui.Text($"{LuminaCache.GetRow<PlaceName>(infoInstance->AreaPlaceNameId).Name.RawString}");

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text("Sub-Area PlaceName ID");
                ImGui.TableNextColumn();
                ImGui.Text($"{infoInstance->SubAreaPlaceNameId}");

                ImGui.TableNextColumn();
                ImGui.Text("Sub-Area PlaceName Name");
                ImGui.TableNextColumn();
                ImGui.Text($"{LuminaCache.GetRow<PlaceName>(infoInstance->SubAreaPlaceNameId).Name.RawString}");

                ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
                ImGui.TableNextColumn();
                ImGui.Text("位置信息");

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text("Position");
                ImGui.TableNextColumn();
                var roundedX = (float)Math.Round(Service.ClientState.LocalPlayer?.Position.X ?? 0f, 2);
                var roundedY = (float)Math.Round(Service.ClientState.LocalPlayer?.Position.Y ?? 0f, 2);
                var roundedZ = (float)Math.Round(Service.ClientState.LocalPlayer?.Position.Z ?? 0f, 2);
                ImGui.Text($"<{roundedX:F2}, {roundedY:F2}, {roundedZ:F2}>");

                ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
                ImGui.TableNextColumn();
                ImGui.Text("房屋信息");

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text("Ward ID (房区)");
                ImGui.TableNextColumn();
                ImGui.Text($"{houseManager->GetCurrentWard()}");

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text("Plot ID (地皮)");
                ImGui.TableNextColumn();
                ImGui.Text($"{houseManager->GetCurrentPlot()}");

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text("House ID");
                ImGui.TableNextColumn();
                ImGui.Text($"{houseManager->GetCurrentHouseId()}");

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text("Room ID");
                ImGui.TableNextColumn();
                ImGui.Text($"{houseManager->GetCurrentRoom()}");

                ImGui.EndTable();
            }

            ImGui.EndTabItem();
        }
    }

    private static void DrawManagers()
    {
        if (ImGui.BeginTabItem("管理器"))
        {
            ImGui.PushID("Execute Command Manager");
            ImGui.TextColored(ImGuiColors.DalamudYellow, "Execute Command Manager");
            ImGui.Separator();

            ImGui.Checkbox("显示日志###Execute Command Manager", ref DebugConfig.ShowExecuteCommandLog);

            ImGui.PushItemWidth(70f * GlobalFontScale);
            ImGui.InputInt("Command", ref ExecuteCommandManual.commmand, 0, 0);

            ImGui.InputInt("P1", ref ExecuteCommandManual.p1, 0, 0);
            ImGui.SameLine();
            ImGui.InputInt("P2", ref ExecuteCommandManual.p2, 0, 0);

            ImGui.InputInt("P3", ref ExecuteCommandManual.p3, 0, 0);
            ImGui.SameLine();
            ImGui.InputInt("P4", ref ExecuteCommandManual.p4, 0, 0);
            ImGui.PopItemWidth();

            ImGui.SameLine();
            if (ImGui.Button("执行"))
                Service.ExecuteCommandManager.ExecuteCommand(ExecuteCommandManual.commmand, ExecuteCommandManual.p1,
                                                             ExecuteCommandManual.p2, ExecuteCommandManual.p3,
                                                             ExecuteCommandManual.p4);
            ImGui.PopID();

            ImGui.TextColored(ImGuiColors.DalamudYellow, "Log Message Manager");
            ImGui.Separator();

            ImGui.Checkbox("显示日志###Log Message Manager", ref DebugConfig.ShowLogMessageLog);

            ImGui.TextColored(ImGuiColors.DalamudYellow, "Use Action Manager");
            ImGui.Separator();

            ImGui.Checkbox("显示 Action 日志##Use Action Manager", ref DebugConfig.ShowUseActionLog);
            ImGui.Checkbox("显示 Location Action 日志##Use Action Manager", ref DebugConfig.ShowUseActionLocationLog);
            ImGui.Checkbox("显示 Pet Move 日志##Use Action Manager", ref DebugConfig.ShowUseActionPetMoveLog);
            ImGui.EndTabItem();
        }
    }

    private static void DrawOthers()
    {
        if (ImGui.BeginTabItem("其他"))
        {
            ImGui.TextColored(ImGuiColors.DalamudYellow, "快捷操作:");
            ImGui.Separator();

            if (ImGui.Button("输出模块一览"))
            {
                var markdown = OutputAllModulesMarkdown();
                ImGui.SetClipboardText(markdown);
            }
            ImGui.EndTabItem();
        }
    }

    private static string OutputAllModulesMarkdown()
    {
        var allModules = Assembly.GetExecutingAssembly().GetTypes()
                                 .Where(t => typeof(DailyModuleBase).IsAssignableFrom(t) &&
                                             t is { IsClass: true, IsAbstract: false } &&
                                             t.GetCustomAttribute<ModuleDescriptionAttribute>() != null)
                                 .Select(type => new
                                 {
                                     Title = Service.Lang.GetText(
                                         type.GetCustomAttribute<ModuleDescriptionAttribute>()?.TitleKey ??
                                         "DevModuleTitle"),
                                     Description = Service.Lang.GetText(
                                         type.GetCustomAttribute<ModuleDescriptionAttribute>()?.DescriptionKey ??
                                         "DevModuleDescription"),
                                     Category = type.GetCustomAttribute<ModuleDescriptionAttribute>()?.Category ??
                                                ModuleCategories.一般,
                                 })
                                 .OrderBy(m => m.Category)
                                 .ToList();

        var groupedModules = allModules.GroupBy(m => m.Category);

        var markdown = "";
        foreach (var group in groupedModules)
        {
            markdown += $"## {group.Key}\n";
            markdown += "| 名称 | 描述 |\n";
            markdown += "|------|------|\n";

            foreach (var module in group)
            {
                var formattedDescription = module.Description.Replace("\n", "<br>");
                markdown += $"| {module.Title} | {formattedDescription} |\n";
            }

            markdown += "\n";
        }

        return markdown;
    }

    public void Dispose()
    {
        
    }
}
