using System.Linq;
using System.Reflection;
using ClickLib;
using DailyRoutines.Helpers;
using Dalamud.Game;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Interface;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace DailyRoutines.Managers;

public class Service
{
    internal static void Init(IDalamudPluginInterface pluginInterface)
    {
        // PluginInterface 初始化
        InitPluginInterface(pluginInterface);
        pluginInterface.Create<Service>();

        // 配置 初始化
        Config = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Config.Initialize(pluginInterface);

        // Helpers/Infos 初始化
        Click.Initialize();
        AddonHelper.Init();
        FieldMarkerHelper.Init();

        foreach (var property in typeof(Service).GetProperties(BindingFlags.Static | BindingFlags.Public)
                                                .Where(p => typeof(IDailyManager).IsAssignableFrom(p.PropertyType)))
        {
            var manager = (IDailyManager?)property.GetValue(null);
            var managerType = manager?.GetType();

            var initMethod = managerType?.GetMethod("Init", BindingFlags.NonPublic | BindingFlags.Instance);
            initMethod?.Invoke(manager, null);
        }
    }

    private static void InitPluginInterface(IDalamudPluginInterface pluginInterface)
    {
        PluginInterface = pluginInterface;
        PluginInterface.UiBuilder.DisableCutsceneUiHide = true;
        UiBuilder = pluginInterface.UiBuilder;
    }

    internal static void Uninit()
    {
        PluginInterface.RemoveChatLinkHandler();

        foreach (var property in typeof(Service).GetProperties(BindingFlags.Static | BindingFlags.Public)
                                                .Where(p => typeof(IDailyManager).IsAssignableFrom(p.PropertyType))
                                                .Reverse())
        {
            var manager = (IDailyManager?)property.GetValue(null);
            var managerType = manager?.GetType();

            var initMethod = managerType?.GetMethod("Uninit", BindingFlags.NonPublic | BindingFlags.Instance);
            initMethod?.Invoke(manager, null);
        }

        LuminaCache.ClearCache();
        Config.Uninit();
        TaskHelper.DisposeAll();
    }

    #region DalamudServices

    [PluginService] public static IAddonLifecycle AddonLifecycle { get; private set; } = null!;
    [PluginService] public static IAddonEventManager AddonEvent { get; private set; } = null!;
    [PluginService] public static IAetheryteList AetheryteList { get; private set; } = null!;
    [PluginService] public static IBuddyList BuddyList { get; private set; } = null!;
    [PluginService] public static IChatGui Chat { get; private set; } = null!;
    [PluginService] public static IClientState ClientState { get; private set; } = null!;
    /// <summary>
    ///     如非必要请使用 CommandManager, 而不是 ICommandManager 来添加命令
    /// </summary>
    [PluginService] public static ICommandManager Command { get;  set; } = null!;
    [PluginService] public static ICondition   Condition   { get; private set; } = null!;
    [PluginService] public static IContextMenu ContextMenu { get; private set; } = null!;
    /// <summary>
    ///     如非必要请使用 LuminaCache, 而不是 IDataManager 来获取游戏表格数据
    /// </summary>
    [PluginService] public static IDataManager Data { get; private set; } = null!;
    [PluginService] public static IDtrBar DtrBar { get; private set; } = null!;
    [PluginService] public static IDutyState DutyState { get; private set; } = null!;
    [PluginService] public static IFateTable Fate { get; private set; } = null!;
    [PluginService] public static IFlyTextGui FlyText { get; private set; } = null!;
    /// <summary>
    ///     如果需要挂钩 Update 事件, 请使用 FrameworkManager
    /// </summary>
    [PluginService] public static IFramework           Framework       { get; private set; } = null!;
    [PluginService] public static IGameConfig          GameConfig      { get; private set; } = null!;
    [PluginService] public static IGameGui             Gui             { get; private set; } = null!;
    [PluginService] public static IGameInteropProvider Hook            { get; private set; } = null!;
    [PluginService] public static IGameInventory       Inventory       { get; private set; } = null!;
    [PluginService] public static IGameLifecycle       Lifecycle       { get; private set; } = null!;
    [PluginService] public static IGameNetwork         Network         { get; private set; } = null!;
    [PluginService] public static IGamepadState        Gamepad         { get; private set; } = null!;
    [PluginService] public static IJobGauges           JobGauges       { get; private set; } = null!;
    [PluginService] public static IKeyState            KeyState        { get; private set; } = null!;
    [PluginService] public static IMarketBoard         MarketBoard     { get; private set; } = null!;
    [PluginService] public static INotificationManager DalamudNotice   { get; private set; } = null!;
    [PluginService] public static IObjectTable         ObjectTable     { get; private set; } = null!;
    [PluginService] public static IPartyFinderGui      PartyFinder     { get; private set; } = null!;
    [PluginService] public static IPartyList           PartyList       { get; private set; } = null!;
    [PluginService] public static IPluginLog           Log             { get; private set; } = null!;
    [PluginService] public static ITargetManager       Target          { get; private set; } = null!;
    [PluginService] public static ITextureProvider     Texture         { get; private set; } = null!;
    [PluginService] public static ITitleScreenMenu     TitleScreenMenu { get; private set; } = null!;
    [PluginService] public static IToastGui            Toast           { get; private set; } = null!;

    #endregion

    public static IUiBuilder              UiBuilder             { get; private set; } = null!;
    public static IDalamudPluginInterface PluginInterface       { get; private set; } = null!;
    public static Configuration           Config                { get; private set; } = null!;
    public static FontManager             FontManager           { get; private set; } = new();
    public static LanguageManager         Lang                  { get; private set; } = new();
    public static WindowManager           WindowManager         { get; private set; } = new();
    public static UseActionManager        UseActionManager      { get; private set; } = new();
    public static CommandManager          CommandManager        { get; private set; } = new();
    public static ExecuteCommandManager   ExecuteCommandManager { get; private set; } = new();
    public static LogMessageManager       LogMessageManager     { get; private set; } = new();
    public static FrameworkManager        FrameworkManager      { get; private set; } = new();
    public static NotifyManager           Notify                { get; private set; } = new();
    public static SigScanner              SigScanner            { get; private set; } = new();
    public static IPCManager              IPCManager            { get; private set; } = new();
    public static LinkPayloadManager      LinkPayloadManager    { get; private set; } = new();
    public static ModuleManager           ModuleManager         { get; private set; } = new();
    public static OnlineStatsManager      DataUploadManager     { get; private set; } = new();
}
