# AuxDash

Windows x64 原生 WinForms 副屏仪表盘，推荐 1920×480。托盘运行，不依赖 Codex 启动。

## 下载完整运行版

前往 [Releases](https://github.com/Wastrix-ba2ia/AuxDash/releases/latest)，下载 **AuxDash-windows-x64-full.zip**，完整解压后运行“副屏监控.exe”。

完整运行包内置 4 种待机、32 种常规动作以及屏幕唤醒 APT 改编动画，无需火山 API 或重新生成。Codex / PUBG 由每位用户在托盘绑定设置中自行配置；未绑定时也可播放本机动画。完整包默认关闭 Codex 联动和自动内存清理。

GitHub 的“Source code”ZIP 仅有源码，不含人物动画，请普通用户下载上面的完整运行包。

## 构建

Windows 上安装/启用 .NET Framework 4.8，PowerShell 执行：

```powershell
.\build.ps1 -SkipSensorBridge
.\副屏监控.exe
```

源码版不含机器人素材和第三方传感器二进制。构建自动生成纯色背景与系统图标，仪表盘可独立使用；F2 可选自己的背景图。完整媒体包另行提供。

## Codex / PUBG 绑定

右键托盘 → **绑定设置 · Codex / PUBG…**。

- Codex：先在官方客户端登录。数据目录留空时按 CODEX_HOME 或用户目录的 .codex 查找；需要时手动指定 codex.exe。检测连接后保存。不要求 OpenAI 密码或令牌。
- 额度通过本机 app-server 的 account/rateLimits/read 读取；动作联动解析 sessions 中的本地事件，属于版本相关适配，可能因 Codex 更新失效。并发任务按优先级选取，并未绑定某一个聊天。离线和未检测到任务时使用本机模式。
- PUBG：填 Steam PUBG 昵称；验证玩家需个人 PUBG API 密钥，存储使用当前 Windows 用户 DPAPI 加密。只保存昵称也能进行本地游戏进程检测。未实现实时击杀、吃鸡或战绩推送。
- 每个用户使用自己的账户，不要分享作者的认证文件和密钥。

官方接口说明：https://developers.openai.com/codex/app-server
PUBG 开发者入口：https://developer.pubg.com/

## 功能和限制

CPU/GPU/内存/显存/网络、日期农历、长春天气、屏幕唤醒动画（需素材）。天气城市目前固定长春。CPU 温度与功耗需额外兼容的 LibreHardwareMonitor/PawnIO 组件；POWER 是 CPU+GPU 合计，不是插座整机功耗。

自动内存清理可在托盘关闭：超过 80% 持续 30 秒，尝试回收符合条件的最小化应用工作集，每 10 分钟最多一次。工作集减少不等于系统实际释放量，应用恢复时可能重新加载页面。

## 隐私和分发

本机读取任务事件，不上传聊天内容。联网用于天气、连通性探测、PUBG 验证和 Codex 额度查询。配置、缓存、诊断日志都不应上传。

MIT 许可证草案见 LICENSE，第三方和媒体说明见 THIRD_PARTY.md。发布步骤见 GitHub发布步骤.md。请在公开前确认许可证和版权署名。

本项目与 OpenAI、PUBG 无官方隶属关系。
