# Daily Routines

<img src="https://raw.githubusercontent.com/AtmoOmen/DailyRoutines/main/Assets/Images/icon.png" width="128" height="128" alt="Icon by DALL·E">

轻量化、低性能消耗、简单的自动化功能/易用性改动合集, 帮你从游戏内一些无意义的重复性工作中解放出来。

提出 ISSUE 或 PR 请随意。

Support CN only, most of the modules are based on CN client, and will not fuction properly in Global or KR client!

## 仓库项目链接

**访问下面的链接, 然后在页面最下方获取在线库链**

```
https://github.com/AtmoOmen/DalamudPlugins
```

## 界面预览

![UI1](https://raw.githubusercontent.com/AtmoOmen/DailyRoutines/main/Assets/Images/UI-1.png)

## 一般
| 名称                 | 描述                                                         |
| -------------------- | ------------------------------------------------------------ |
| 自动丢弃物品         | 启用并配置完毕后,允许快速丢弃/出售预设好中的一组物品         |
| 自动园圃作业         | 启用并配置完成后, 帮你自动或半自动完成园圃相关作业           |
| 自动批量完成生产理符 | 配置完成并点击开始后, 自动重复完成指定的生产理符任务, 直至理符额度耗尽 |
| 自动登录             | 启用并配置完成后, 下次开启游戏时, 自动登录到你所选择的角色   |
| 自动无人岛采集       | 配置完成并点击开始后, 自动循环采集无人岛素材                 |
| 自动加入新人频道     | 点击开始后, 自动重复尝试加入新人频道                         |

## 系统
| 名称                 | 描述                                                         |
| -------------------- | ------------------------------------------------------------ |
| 自动防离开状态       | 每隔固定时间, 重置游戏内部的相关计时器, 以防止进入离开状态   |
| 自动反屏蔽词         | 发送消息/编辑招募描述时, 自动在屏蔽词内部加点, 或是将其转成拼音以防止屏蔽<br>接收消息时, 自动阻止屏蔽词系统工作, 显示消息原文<br>(模块与 StarlightBreaker, Chat 2 不相兼容) (请勿用于破坏游戏环境) |
| 自动跳过过场动画     | (本模块无法跳过原本就无法跳过的动画)                         |
| 自动标记风脉泉       | 在地图上自动标记所有风脉泉的位置, 打开风脉泉界面后, 允许自定义待显示的风脉泉 |
| 强制粘贴多行转单行   | 在游戏内粘贴文本时, 强制将多行文本转成单行, 以方便游戏内编辑发送 |
| 自定义游戏对象缩放   | 启用并配置完成后, 允许自定义游戏内大部分物体对象的缩放       |
| 自定义视距           | 启用并配置完毕后, 可自定义当前游戏摄像头的最大视距           |
| 投影模板切换指令     | 添加 /pdr gpapply <编号> (从 1 开始) 指令, 允许使用指令快捷切换游戏原生保存的各投影模板 |
| 即刻返回             | 启用后, 让游戏原生的返回无需咏唱等待, 并且不再进入冷却 (拉拉菲尔族不可用) |
| 自动限制系统音效频率 | 启用并配置完成后, 自动限制系统音效 (<se.>) 的播放频率        |

## 技能
| 名称                     | 描述                                                         |
| ------------------------ | ------------------------------------------------------------ |
| 自动中断咏唱             | 当目标死亡或不可选中时, 自动中断咏唱                         |
| 自动跳舞                 | 使用舞者技能标准/技巧舞步时, 自动使用对应的舞步技能          |
| 自动抽卡                 | 进入副本或副本内触发重新挑战后, 若当前职业为占星术士, 且等级大于 30, 则自动抽卡一次 |
| 自动开启自动攻击         | 进入战斗时, 自动为所有职业开启自动攻击                       |
| 自动死斗自身             | 使用战士技能死斗时, 强制令自身为技能目标                     |
| 自动出卡                 | 使用占星术士技能出卡时, 自动将当前抽到的卡发给一位适合的队友 |
| 自动防技能重复           | 启用并配置完成后, 使用相应技能时, 会自动检测相关状态是否已存在, 以防止团辅/团减等重叠 |
| 自动重定向地面放置类技能 | 启用并进入副本后, 在使用地面放置类技能时, 自动将其放置位置重定向至场地中心 |
| 自动召唤召唤兽           | 进入副本或副本内触发重新挑战后, 若当前职业需要但未召唤召唤兽, 则自动读条召唤召唤兽 |
| 自动开启盾姿             | 进入副本后, 若当前为防护职业且未开启盾姿, 则自动开启盾姿     |
| 宏进入技能队列           | 启用后, 令所有技能与物品使用都可以进入技能队列               |

## 战斗
| 名称                     | 描述                                                         |
| ------------------------ | ------------------------------------------------------------ |
| 自动检查装等             | 进入副本后, 自动检查小队所有成员的装等, 并提醒装等异常的成员 |
| 自动下坐骑               | 当骑乘坐骑时, 若尝试使用可用技能, 则自动下坐骑并使用技能     |
| 自动确认副本内物体对话框 | 进入副本后, 自动确认副本内交互物体时出现的对话框             |
| 自动进退副本一次         | 添加 /pdr joinexitduty 指令, 使用后可快速进退副本一次        |
| 自动上坐骑               | 启用并配置完成后, 在满足指定条件时, 自动上坐骑               |
| 自动最优队员推荐         | 副本完成时, 自动给予对位队友最优队员推荐                     |
| 自动完成动态演练         | 在副本中, 自动完成全种类的动态演练 (QTE)                     |
| 自动重新焦点目标         | 在副本内, 若丢失的焦点目标再次出现, 则自动重新焦点           |
| 自动显示副本攻略         | 进入副本后, 自动以悬浮窗形式显示来自 新大陆见闻录 的副本攻略 |
| 自动跳过主随过场动画     | 自动跳过神兵要塞帝国南方堡、最终决战天幕魔导城和究极神兵破坏作战的过场动画 |
| 更好的自动跟随           | 启用并配置完成后, 解除了游戏原生自动跟随的目标限制, 并且能够锁定目标自动重新跟随 |
| 快速重置所有木人仇恨     | 启用后, 对单一木人清除仇恨即可清除所有木人的仇恨, 并添加指令用以手动清除 |
| 强制按顺序击杀蔓德拉战队 | 在宝物库内, 若场上存在蔓德拉战队, 则禁止选中不符合当前击杀顺序的蔓德拉 |

## 界面优化
| 名称                     | 描述                                                         |
| ------------------------ | ------------------------------------------------------------ |
| 自动数字输入框最大值     | 启用后, 令所有数字输入框始终为最大值                         |
| 自动刷新市场搜索结果     | 当市场道具搜索结果为 "请稍后" 时, 自动重新请求获取           |
| 更好的青魔法技能组读取   | 启用后, 在青魔法书界面新增按钮, 允许你在不解除以太复制等状态的情况下切换技能组 |
| 更大的高清输入法界面     | 启用并配置完毕后, 允许自定义输入法窗口缩放倍数, 并使部分窗口的文本变得更加清晰 |
| 自定义界面文本替换       | 启用后, 为游戏内所有可显示文本提供自定义的替换选项           |
| 物品右键菜单搜索扩展     | 启用后, 为物品右键菜单中新增一些额外的搜索功能               |
| 玩家右键菜单搜索扩展     | 启用后, 为玩家右键菜单中新增一些额外的搜索功能               |
| 快速目标交互             | 添加一个自定义悬浮窗, 列出周围可交互物体, 可直接点击其中的按钮以实现更快速的交互 |
| 快捷投影台搜索           | 启用并打开编辑投影模板界面后, 自动切换默认栏位并打开收藏柜, 并新增一个快捷搜索悬浮窗 |
| 自定义招募板单页显示数量 | 允许自定义队员招募中单页可显示的招募数量, 上限为单页 100 个招募 |
| 玩家目标情报扩展         | 启用后, 允许你自定义目标情报中所能显示的信息                 |
| 快捷聊天面板             | 在聊天栏输入框旁新增一个面板, 用于快速发送/复制指定的预设文本/宏/游戏内特殊图标<br>(模块与 Chat 2 不相兼容) |

## 界面操作
| 名称                             | 描述                                                         |
| -------------------------------- | ------------------------------------------------------------ |
| 自动任务出发确认                 | 任务出发确认窗口出现后, 自动点击出发按钮                     |
| 自动确认道具分解                 | 道具分解界面出现后, 自动确认                                 |
| 自动连续鉴定                     | 道具鉴定结果界面出现后, 自动点击继续鉴定按钮                 |
| 自动批量删除邮件                 | 打开莫古邮件界面, 切换到指定类别并点击开始后, 即会批量删除该类别下所有的邮件 |
| 自动道具分解                     | 打开道具分解列表, 并点击开始后, 自动分解所有道具             |
| 自动筹备稀有品                   | 打开筹备任务界面, 并点击开始后, 自动上交装备兑换军票         |
| 自动部队合建交纳素材             | 打开提供素材界面, 并点击开始后, 自动交纳当前所有可用素材     |
| 自动精制魔晶石                   | 打开精制魔晶石界面, 并点击开始后, 自动精制当前类别下所有装备的魔晶石 |
| 自动确认任务接取                 | 任务接取界面出现后, 自动点击确认按钮                         |
| 自动确认任务结果                 | 任务结果界面出现后, 自动点击完成按钮                         |
| 自动刷新失效肖像                 | 打开肖像列表, 并点击开始后, 自动刷新所有肖像, 以避免出现失效状态 |
| 自动刷新招募板                   | 打开队员招募后, 每经过固定间隔时间时, 自动刷新一次招募板     |
| 自动取出投影台中可放入收藏柜装备 | 打开投影台界面, 并点击开始后, 自动取出投影台中所有可放入收藏柜的装备 |
| 自动选择并递交物品               | 在递交物品时, 自动选择物品并完成递交                         |
| 自动雇员作业                     | 启用后, 自动或半自动地完成雇员相关作业                       |
| 自动存入收藏柜                   | 打开收藏柜的放入道具界面, 并点击开始后, 自动将所有可存储装备存入收藏柜中 |
| 自动收取潜水艇                   | 打开潜水艇列表, 并点击开始后, 自动确认潜水艇探索结果并重新委派 |
| 自动跳过对话                     | 出现对话框时, 自动快进跳过                                   |

## 金碟
| 名称               | 描述                                                         |
| ------------------ | ------------------------------------------------------------ |
| 自动游玩怪物投篮   | 交互游戏机后, 自动开始重复游玩怪物投篮                       |
| 自动游玩强袭水晶塔 | 交互游戏机后, 自动开始重复游玩强袭水晶塔                     |
| 自动完成仙人仙彩   | 进入仙人仙彩界面后, 自动购买本周所有的仙人彩                 |
| 自动完成仙人微彩   | 进入仙人微彩界面后, 自动完成当日所有的仙人微彩               |
| 自动游玩重击伽美什 | 交互游戏机后, 自动开始重复游玩重击伽美什                     |
| 自动幻卡回收       | 幻卡回收界面出现后, 自动点击确认按钮<br>点击开始后, 自动回收当前所有已收录幻卡 |
| 自动游玩莫古抓球机 | 交互游戏机后, 自动开始重复游玩莫古抓球机                     |

## 通知
| 名称                 | 描述                                                         |
| -------------------- | ------------------------------------------------------------ |
| 自动通知倒计时开始   | 在开始倒计时时, 自动发送一条 Windows 系统通知                |
| 自动通知过场剧情结束 | 在副本内, 如若队友观看过场剧情超过五秒, 则在全部队友的过场剧情都已结束时, 自动发送一条 Windows 系统通知 |
| 自动通知任务出发确认 | 当排到副本时, 自动发送一条 Windows 系统通知                  |
| 自动通知副本名称     | 在进入副本时, 自动在聊天栏通知当前副本名称                   |
| 自动通知副本开始     | 在副本内, 当副本任务开始时, 自动发送一条 Windows 系统通知    |
| 自动通知理符限额刷新 | 在理符刷新时检查当前限额, 自动发送一条 Windows 系统通知, 提醒预计达限的日期 |
| 自动通知新消息       | 启用并配置完毕后, 当你选择的消息类型有新消息传入时, 自动发送一条 Windows 系统通知 |
| 自动通知准备确认     | 收到准备确认时, 自动发送一条 Windows 系统通知                |
| 自动通知招募结束     | 在发布的队员招募因各种原因结束时, 自动发送一条 Windows 系统通知 |
| 自动通知特殊玩家出现 | 启用并配置完成后, 当指定的特殊玩家出现后, 发送提醒信息并执行自定义的文本指令 |