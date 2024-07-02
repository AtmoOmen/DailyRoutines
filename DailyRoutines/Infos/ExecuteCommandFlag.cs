namespace DailyRoutines.Infos;

public enum ExecuteCommandFlag
{
    /// <summary>
    /// 拔出/收回武器
    /// <para><c>param1</c>: 1 - 拔出, 0 - 收回</para>
    /// <para><c>param2</c>: 未知, 固定为 1</para>
    /// </summary>
    DrawOrSheatheWeapon = 1,

    /// <summary>
    /// 自动攻击
    /// <para><c>param1</c>: 是否开启自动攻击 (0 - 否, 1 - 是)</para>
    /// <para><c>param2</c>: 目标对象ID</para>
    /// <para><c>param3</c>: 是否为手动操作 (0 - 否, 1 - 是)</para>
    /// </summary>
    AutoAttack = 2,

    /// <summary>
    /// 选中目标
    /// <para><c>param1</c>: 目标 Object ID (无目标为: -536870912, 即 int.MinValue / 4)</para>
    /// </summary>
    Target = 3,

    /// <summary>
    /// 下坐骑
    /// <para><c>param1</c>: 未知, 固定为 1</para>
    /// </summary>
    Dismount = 101,

    /// <summary>
    /// 召唤宠物
    /// <para><c>param1</c>: 宠物 ID</para>
    /// </summary>
    SummonPet = 102,

    /// <summary>
    /// 收回宠物
    /// </summary>
    WithdrawPet = 103,

    /// <summary>
    /// 中断咏唱
    /// </summary>
    CancelCast = 105,

    /// <summary>
    /// 共同骑乘
    /// <para><c>param1</c>: 目标 ID</para>
    /// <para><c>param2</c>: 位置索引</para>
    /// </summary>
    RidePillion = 106,

    /// <summary>
    /// 收起时尚配饰
    /// </summary>
    WithdrawParasol = 109,

    /// <summary>
    /// 传送至指定的以太之光
    /// <para><c>param1</c>: 以太之光 ID</para>
    /// <para><c>param2</c>: 是否使用传送券 (0 - 否, 1 - 是)</para>
    /// <para><c>param3</c>: 以太之光 Sub Index</para>
    /// </summary>
    Teleport = 202,

    /// <summary>
    /// 若当前种族不是拉拉菲尔族, 则返回至当前地图的最近安全点
    /// </summary>
    ReturnIfNotLalafell = 213,

    /// <summary>
    /// 立即返回至返回点, 若在副本内则返回至副本内重生点
    /// </summary>
    InstantReturn = 214,

    /// <summary>
    /// 检查指定玩家
    /// <para><c>param1</c>: 待对象 Object ID</para>
    /// </summary>
    Inspect = 300,

    /// <summary>
    /// 更改佩戴的称号
    /// <para><c>param1</c>: 称号 ID</para>
    /// </summary>
    ChangeTitle = 302,

    /// <summary>
    /// 请求过场剧情数据
    /// <para><c>param1</c>: 过场剧情在 Cutscene.csv 中的对应索引</para>
    /// </summary>
    RequestCutscene307 = 307,

    /// <summary>
    /// 请求挑战笔记具体类别下数据
    /// <para><c>param1</c>: 类别索引 (从 1 开始)</para>
    /// </summary>
    RequestContentsNoteCategory = 310,

    /// <summary>
    /// 清除场地标点
    /// </summary>
    ClearFieldMarkers = 313,

    /// <summary>
    /// 请求跨界传送数据
    /// </summary>
    RequestWorldTravel = 316,

    /// <summary>
    /// 放置场地标点
    /// <para><c>param1</c>: 标点索引</para>
    /// <para><c>param2</c>: 坐标 X * 1000</para>
    /// <para><c>param3</c>: 坐标 Y * 1000</para>
    /// <para><c>param4</c>: 坐标 Z * 1000</para>
    /// </summary>
    PlaceFieldMarker = 317,

    /// <summary>
    /// 移除场地标点
    /// <para><c>param1</c>: 标点索引</para>
    /// </summary>
    RemoveFieldMarker = 318,

    /// <summary>
    /// 将青魔法师技能交换或应用于有效技能
    /// <para><c>param1</c>: 类型 (0 为应用有效技能, 1 为交换有效技能)</para>
    /// <para><c>param2</c>: 格子序号 (从 0 开始, 小于 24)</para>
    /// <para><c>param3</c>: 技能 ID / 格子序号 (从 0 开始, 小于 24)</para>
    /// </summary>
    AssignBLUActionToSlot = 315,

    /// <summary>
    /// 清除来自木人的仇恨
    /// <para><c>param1</c>: 木人的 Object ID</para>
    /// </summary>
    ResetStrkingDummy = 319,

    /// <summary>
    /// 请求指定物品栏数据
    /// <para><c>param1</c>: (int)InventoryType</para>
    /// </summary>
    RequestInventory = 405,

    /// <summary>
    /// 进入镶嵌魔晶石状态
    /// <para><c>param1</c>: 物品 ID</para>
    /// </summary>
    EnterMateriaAttachState = 408,

    /// <summary>
    /// 退出镶嵌魔晶石状态
    /// </summary>
    LeaveMateriaAttachState = 410,

    /// <summary>
    /// 取消魔晶石镶嵌委托
    /// </summary>
    CancelMateriaMeldRequest = 419,

    /// <summary>
    /// 请求收藏柜的数据
    /// </summary>
    RequestCabinet = 424,

    /// <summary>
    /// 存入物品至收藏柜
    /// <para><c>param1</c>: 物品在 Cabinet.csv 中的对应索引</para>
    /// </summary>
    StoreToCabinet = 425,

    /// <summary>
    /// 从收藏柜中取回物品
    /// <para><c>param1</c>: 物品在 Cabinet.csv 中的对应索引</para>
    /// </summary>
    RestoreFromCabinet = 426,

    /// <summary>
    /// 请求陆行鸟鞍囊的数据
    /// </summary>
    RequestSaddleBag = 444,

    /// <summary>
    /// 更改闲置状态姿势
    /// <para><c>param2</c>: 姿势索引</para>
    /// </summary>
    IdlePostureChange = 505,

    /// <summary>
    /// 进入闲置状态姿势
    /// <para><c>param2</c>: 姿势索引</para>
    /// </summary>
    IdlePostureEnter = 506,

    /// <summary>
    /// 退出闲置状态姿势
    /// </summary>
    IdlePostureExit = 507,

    /// <summary>
    /// 钓鱼
    /// <para><c>param1</c>: 动作 (0 - 抛竿, 1 - 收杆, 2 - 提钩, 4 - 换饵)</para>
    /// </summary>
    Fish = 701,

    /// <summary>
    /// 放弃任务投票
    /// </summary>
    VoteAbandon = 808,

    /// <summary>
    /// 进入 临危受命 范围
    /// <para><c>param1</c>: FATE ID</para>
    /// </summary>
    FateEnter = 812,

    /// <summary>
    /// 为 临危受命 等级同步
    /// <para><c>param1</c>: FATE ID</para>
    /// <para><c>param2</c>: 是否等级同步 (0 - 否, 1 - 是)</para>
    /// </summary>
    FateLevelSync = 813,

    /// <summary>
    /// 临危受命 野怪生成
    /// <para><c>param1</c>: Object ID</para>
    /// </summary>
    FateMobSpawn = 814,

    /// <summary>
    /// 离开副本
    /// <para><c>param1</c>: 类型 (0 - 正常退本, 1 - 一段时间未操作)</para>
    /// </summary>
    LeaveDuty = 819,

    /// <summary>
    /// 昔日重现模式
    /// <para><c>param1</c>: QuestRedo.csv 中对应的昔日重现章节序号 (0 - 退出昔日重现)</para>
    /// </summary>
    QuestRedo = 824,

    /// <summary>
    /// 刷新物品栏
    /// </summary>
    InventoryRefresh = 830,

    /// <summary>
    /// 请求过场剧情数据
    /// <para><c>param1</c>: 过场剧情在 Cutscene.csv 中的对应索引</para>
    /// </summary>
    RequestCutscene831 = 831,

    /// <summary>
    /// 请求成就进度数据
    /// <para><c>param1</c>: 成就在 Achievement.csv 中的对应索引</para>
    /// </summary>
    RequestAchievement = 1000,

    /// <summary>
    /// 请求所有成就概览 (不含具体成就内容)
    /// </summary>
    RequestAllAchievement = 1001,

    /// <summary>
    /// 请求接近达成成就概览 (不含具体成就内容)
    /// <para><c>param1</c>: 未知, 固定为 1</para>
    /// </summary>
    RequestNearCompletionAchievement = 1002,

    /// <summary>
    /// 请求抽选数据
    /// <para><c>param1</c>: Territory Type</para>
    /// <para><c>param2</c>: 地皮对应索引</para>
    /// <code>
    /// <![CDATA[
    /// const int HousesPerArea = 60;
    /// const int AreaOffset = 256;
    /// 
    /// // 第 1 区 第 41 号
    /// var wardID = 0;
    /// var districtOffset = wardID * AreaOffset;
    /// var houseID = 40;
    /// var position = districtOffset + houseID]]>
    /// </code>
    /// </summary>
    RequestLotteryData = 1105,

    /// <summary>
    /// 请求门牌数据
    /// <para><c>param1</c>: Territory Type</para>
    /// <para><c>param2</c>: 地皮对应索引</para>
    /// <code>
    /// <![CDATA[
    /// const int HousesPerArea = 60;
    /// const int AreaOffset = 256;
    /// 
    /// // 第 1 区 第 41 号
    /// var wardID = 0;
    /// var districtOffset = wardID * AreaOffset;
    /// var houseID = 40;
    /// var position = districtOffset + houseID]]>
    /// </code>
    /// </summary>
    RequestPlacardData = 1106,

    /// <summary>
    /// 请求住宅区数据
    /// <para><c>param1</c>: Territory Type</para>
    /// <para><c>param2</c>: 分区索引</para>
    /// </summary>
    RequestHousingAreaData = 1107,

    /// <summary>
    /// 移动到庭院门前
    /// <para><c>param1</c>: 地块索引</para>
    /// </summary>
    MoveToFrontGate = 1122,

    /// <summary>
    /// 庭院相关操作
    /// <para><c>param2</c>: 45 - 布置庭具状态</para>
    /// </summary>
    Garden = 1123,

    /// <summary>
    /// 设置房屋背景音乐
    /// <para><c>param1</c>: 管弦乐曲在 Orchestrion.csv 中的对应索引</para>
    /// </summary>
    SetHouseBackgroundMusic = 1145,

    /// <summary>
    /// 修理潜水艇部件
    /// <para><c>param1</c>: 潜水艇索引</para>
    /// <para><c>param2</c>: 潜水艇部件索引</para>
    /// </summary>
    RepairSubmarinePart = 1153,

    /// <summary>
    /// 领取战利水晶
    /// </summary>
    CollectTrophyCrystal = 1200,

    /// <summary>
    /// 请求挑战笔记数据
    /// </summary>
    RequestContentsNote = 1301,

    /// <summary>
    /// 陆行鸟装甲
    /// <para><c>param1</c>: 部位 (0 - 头部, 1 - 身体, 2 - 腿部)</para>
    /// <para><c>param2</c>: 在 BuddyEquip.csv 中对应的装备索引 (0 - 卸下装备)</para>
    /// </summary>
    BuddyEquip = 1701,

    /// <summary>
    /// 请求金碟游乐场面板 整体 信息
    /// </summary>
    RequestGSGeneral = 1850,

    /// <summary>
    /// 请求金碟游乐场面板 萌宠之王 信息
    /// </summary>
    RequestGSLordofVerminion = 2010,

    /// <summary>
    /// 启用/解除自动加入新人频道设置
    /// </summary>
    EnableAutoJoinNoviceNetwork = 2102,

    /// <summary>
    /// 请求投影台数据
    /// </summary>
    RequestPrismBox = 2350,

    /// <summary>
    /// 取出投影台物品
    /// <para><c>param1</c>: 投影台内部物品 ID (MirageManager.Instance().PrismBoxItemIds)</para>
    /// </summary>
    RestorePrsimBoxItem = 2352,

    /// <summary>
    /// 请求投影模板数据
    /// </summary>
    RequestGlamourPlates = 2355,

    /// <summary>
    /// 进入/退出投影模板选择状态
    /// <para><c>param1</c>: 0 - 退出, 1 - 进入</para>
    /// <para><c>param2</c>: 未知, 可能为 0 或 1</para>
    /// </summary>
    EnterGlamourPlateState = 2356,

    /// <summary>
    /// 应用投影模板 (需要先进入投影模板选择状态)
    /// <para><c>param1</c>: 投影模板索引</para>
    /// </summary>
    ApplyGlamourPlate = 2357,

    /// <summary>
    /// 请求金碟游乐场面板 多玛方城战 信息
    /// </summary>
    RequestGSMahjong = 2550,

    /// <summary>
    /// 请求青魔法书数据
    /// </summary>
    RequstAOZNotebook = 2601,

    /// <summary>
    /// 请求亲信战友数据
    /// </summary>
    RequestTrustedFriend = 2651,

    /// <summary>
    /// 请求剧情辅助器数据
    /// </summary>
    RequestDutySupport = 2653,

    /// <summary>
    /// 分解指定的物品 / 回收指定物品的魔晶石
    /// <para><c>param1</c>: 分解: 3735552 / 回收魔晶石: 3735553</para>
    /// <para><c>param2</c>: 未知, 可能为位置信息</para>
    /// <para><c>param3</c>: 未知, 可能为位置信息</para>
    /// <para><c>param4</c>: 物品 ID</para>
    /// </summary>
    Desynthesize = 2800,

    /// <summary>
    /// 请求无人岛工房排班数据
    /// <para><c>param1</c>: 具体天数 (0 为本周期第一天, 7 为下周期第一天)</para>
    /// </summary>
    MJIWorkshopRequest = 3254,
    
    /// <summary>
    /// 请求无人岛工房排班物品数据
    /// </summary>
    MJIWorkshopRequestItem = 3258,

    /// <summary>
    /// 无人岛工房排班
    /// <para><c>param1</c>: 物品和排班时间段</para>
    /// <para><c>param2</c>: 具体天数 (0 为本周期第一天, 7 为下周期第一天)</para>
    /// <para><c>param4</c>: 添加/删除 (0 - 添加, 1 - 删除)</para>
    /// </summary>
    MJIWorkshopAssign = 3259,

    /// <summary>
    /// 收取无人岛屯货仓库探索结果
    /// <para><c>param1</c>: 仓库索引</para>
    /// </summary>
    MJIGranaryCollect = 3262,

    /// <summary>
    /// 无人岛屯货仓库派遣探险
    /// <para><c>param1</c>: 仓库索引</para>
    /// <para><c>param2</c>: 目的地索引</para>
    /// <para><c>param3</c>: 探索天数</para>
    /// </summary>
    MJIGranaryAssign = 3264,

    /// <summary>
    /// 托管单块无人岛耕地
    /// <para><c>param1</c>: 耕地索引</para>
    /// <para><c>param2</c>: 种子物品 ID</para>
    /// </summary>
    MJIFarmEntrustSingle = 3279,

    /// <summary>
    /// 取消托管单块无人岛耕地
    /// <para><c>param1</c>: 耕地索引</para>
    /// </summary>
    MJIFarmDismiss = 3280,

    /// <summary>
    /// 收取单块无人岛耕地
    /// <para><c>param1</c>: 耕地索引</para>
    /// <para><c>param2</c>: 收取后是否取消托管 (0 - 否, 1 - 是)</para>
    /// </summary>
    MJIFarmCollectSingle = 3281,

    /// <summary>
    /// 收取全部无人岛耕地
    /// <para><c>param1</c>: *(int*)MJIManager.Instance()->GranariesState</para>
    /// </summary>
    MJIFarmCollectAll = 3282,

    /// <summary>
    /// 请求无人岛工房需求数据
    /// </summary>
    MJIFavorStateRequest = 3292,

    /// <summary>
    /// 掷骰子
    /// <para><c>param1</c>: 类型 (固定为 0)</para>
    /// <para><c>param2</c>: 最大值</para>
    /// </summary>
    RollDice = 9000,
}
