SCP:SL 角色与阵营插件
一个功能丰富的SCP: Secret Laboratory服务器插件，添加了多种特殊角色、阵营系统和游戏机制扩展。

项目特点
1.多样化的特殊SCP角色：SCP-008、SCP-181、SCP-999、SCP-682（三阶段形态）、SCP-3114、SCP-079机器人等

2.丰富的阵营系统：GOC收容部队、蛇之手、德尔塔军团、Alpha-9等7个独特阵营

3.深度游戏机制：自定义友伤规则、阵营关系、角色特殊能力、物品系统

4.完善的玩家体验：持续角色提示、无敌保护、自动重生机制

5.强大的管理工具：远程管理命令、自动平衡、调试系统

📋 已实现功能
SCP特殊角色
角色	 特殊能力	 自动生成条件
SCP-181	10%概率免疫伤害、5%概率直接开门
SCP-999	治疗攻击、手电筒群体治疗、2000生命值	玩家数>5时生成
SCP-682	三阶段形态、复活机制、3000生命值	玩家数>13时自动生成
SCP-3114	无 玩家数>9时自动生成D级人员
SCP-079机器人	战斗形态、SCP语音频道、特殊装备	回合18分钟后自动生成或手动生成
特殊阵营部队
阵营	生成时机	特殊功能
混沌快速支援部队	第2.5/10/17.5/25分钟	重武装、任务：消灭所有敌人
基金会快速支援部队	第2.5/7.5/12.5/17.5分钟	重武装、任务：保护设施
GOC收容部队	回合第10分钟或手动	特殊任务：收容SCP、消灭蛇之手
蛇之手	第12分钟或手动	特殊任务：帮助SCP、消灭人类
德尔塔军团	第15分钟或手动	精英混沌部队、电磁炮指挥官
Alpha-9	核弹爆炸后2分钟或18分钟后	基金会最后的希望、电磁炮指挥官
游戏机制
自定义友伤系统：基于阵营的伤害控制

持续角色提示：实时显示角色信息和阵营状态

特殊物品系统：SCP-2818

无敌保护机制：生成后3秒无敌

自动平衡系统：基于玩家数量的智能生成

版本检查系统：自动检查插件更新

🔧 安装指南
前置要求
SCP: Secret Laboratory 服务器

EXILED 框架 8.2.0 或更高版本

.NET Framework 4.7+

安装步骤
下载最新的 cjjs.dll 文件

将文件放入 SCP Secret Laboratory/EXILED/Plugins 文件夹

重启服务器

配置文件将自动生成在 EXILED\Configs\Plugins\cjjs 文件夹

⚙️ 配置文件
配置文件位于 EXILED\Configs\Plugins\cjjs

yaml
factionplugin:
  # 基础设置
  is_enabled: true
  enable_friendly_fire: true
  debug: false
  
  # SCP角色设置
  enable_scp008: true
  enable_scp181: true
  enable_scp999: true
  scp999_health: 2000
  enable_scp682: true
  enable_scp3114: true
  enable_scp079_bot: true
  enable_scp079_auto_conversion: true
  
  # 阵营设置
  enable_chaos_fast_response: true
  enable_foundation_fast_response: true
  enable_goc_capture: true
  enable_serpents_hand: true
  enable_delta_legion: true
  enable_alpha_nine: true
  
  # 提示系统
  hint_vertical_offset: 15
  hint_font_size: 50
  enable_role_hints: true
🎮 管理命令
SCP角色生成命令
命令	参数	描述
spawn008	[玩家ID]	生成SCP-008
spawn181	[玩家ID]	生成SCP-181
spawn999	[玩家ID]	生成SCP-999
spawn3114	[玩家ID]	生成SCP-3114
spawn682	[玩家ID]	生成SCP-682
spawn079bot	[玩家ID]	生成SCP-079机器人
阵营生成命令
命令	描述
spawnchaosfr	生成混沌快速支援部队
spawnfoundationfr	生成基金会快速支援部队
spawngoc	生成GOC收容部队
spawnserpents	生成蛇之手
spawndelta	生成德尔塔军团
spawnalpha9	生成Alpha-9
force079conversion	强制转换所有SCP-079为机器人
🤝 阵营关系
阵营	盟友	敌人	中立
基金会	基金会快速支援部队、Alpha-9	混沌、GOC、蛇之手、SCP	-
混沌	混沌快速支援部队、德尔塔军团	基金会、GOC、蛇之手、SCP	-
SCP	蛇之手	所有人类阵营	-
GOC	-	SCP、蛇之手	基金会、混沌
蛇之手	SCP	所有人类阵营	-
🛠️ 开发说明
项目结构
text
FactionPlugin/
├── FactionPlugin.cs      # 主插件类
├── Config.cs            # 配置类
├── Commands/            # 命令处理器
└── README.md            # 项目说明
技术特性
基于EXILED 8.2.0框架开发

使用MEC协程处理定时任务

完整的事件订阅系统

异步版本检查机制

防重复生成保护

编译要求
Visual Studio 2019/2022

.NET Framework 4.8

EXILED API 8.2.0

📊 性能优化
协程管理：所有定时任务使用MEC协程

内存优化：定期清理缓存和列表

事件优化：按需订阅事件，避免性能浪费

错误处理：完善的异常捕获和恢复机制

⚠️ 注意事项
兼容性：需要EXILED 8.2.0+，与其他插件可能存在兼容性问题

性能：建议服务器内存4GB+，大量玩家时可能需要调整生成频率

平衡性：部分角色较为强大，建议根据服务器情况调整配置

调试：开启Debug模式会生成详细日志，可能影响性能

📞 支持与反馈
QQ交流群: 733153027

邮箱: 2628823280@qq.com

⭐ 支持项目
如果你喜欢这个插件，请：

给项目点个Star ⭐

提交反馈和建议
