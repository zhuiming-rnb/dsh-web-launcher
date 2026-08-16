# DSH-Web Launcher — 会话上下文摘要（2026-08-16）

## 项目
原生桌面启动器，包裹 DeepSeek Harness 本地 Web（http://127.0.0.1:3080/）。
- 本地：`E:\workplace\DSH-Web`　GitHub：https://github.com/zhuiming-rnb/dsh-web-launcher（MIT，git 身份 zhuiming-rnb / 2715216697@qq.com）

## 当前状态（HEAD = 1d6aeea）
- **原生 Windows 标题栏**（豆包式自定义顶栏 4d5a460 / 9b41303 已 git revert 回退）
- 原生 WinForms + WebView2 窗口：单实例、托盘（最小化/关闭缩托盘、无气泡）、**点桌面图标可靠唤出**（1d6aeea 修复前台锁）、窗口标题跟随 DSH 会话名、健康监测（5s 探活 3080 + 断线重连面板）、路径自适应（node/dsh 自动解析，DSH_BIN/DSH_WORKSPACE 可覆盖）、日志 dsh-web.log（UTF-8）、PerMonitorV2 DPI、黑色鲸鱼图标（exe 内嵌）、服务自愈（自动拉起 node dsh）、计划任务 DSH-Web 登录自启、窗口位置记忆
- 桌面快捷方式 `DeepSeek Harness Web.lnk` 直接指向 `app\DSH-Web.exe`（无 powershell/无 cmd 闪窗）

## 文件结构
- `app\DSH-Web.cs`（C#5 兼容，系统 csc 只支持到 C#5：无 `?.`/`out var`/字符串插值）、`app.manifest`（PerMonitorV2）
- 脚本：`build.ps1`（编译+重启，csc 加 `/codepage:65001`）、`install.ps1`/`uninstall.ps1`（含中文，必须 UTF-8 BOM）、`make-dsh-icon.ps1`（-Fill 换色）、`patch-dsh-frontend.ps1`（前端 PNG 图标补丁，幂等）、`start-dsh.ps1`
- 图标：`dsh.ico`/`dsh-black.ico`（黑鲸鱼）、`favicon-192/512.png`；`webview2-sdk\` 为构建缓存（gitignore）
- dsh 服务：`node %LOCALAPPDATA%\npm-cache\_npx\<hash>\node_modules\@deepseek-ai\dsh\lib\bin.js web`

## 关键环境事实
- 网络：registry.npmjs.org 不稳（ECONNRESET）；nuget.org / github.com / npmmirror 可用；api.github.com 被墙（node fetch 失败，curl 000）
- Windows 11 24H2 (26200)：**DWM 深色标题栏属性无效**（实测 HRESULT=0 但视觉不变）；WebView2 Runtime 151 已装
- PowerShell 5.1 读无 BOM UTF-8 脚本乱码 → 中文 .ps1 需加 BOM；pwsh 对 P/Invoke 结构体成员算术有怪癖
- 截图/PrintWindow 验证不可靠（窗口被 Chrome 遮挡 + PowerShell 坑）→ 用 WindowFromPoint / GetForegroundWindow / GWL_STYLE 等 API 验证
- 杀 DSH-Web.exe 安全（build.ps1 会先停再重启）；**不要杀 node dsh 服务器**（本会话跑在它上面）

## 最近修复
- 点桌面图标唤不出：Windows 前台锁拦截 SetForegroundWindow → 修复 = FindFirstTopWindow 优先可见窗口 + SetWindowPos(HWND_TOP, NOACTIVATE) + AttachThreadInput + SetForegroundWindow

## 用户已决策不做
- 集成成 dsh 插件（设置插件列表）——不做
- 给仓库打 dsh-plugin 标签——不打（不是真插件，会被插件市场误收录）

## 可选待办（用户未选）
- CI 自动构建 Release、README 截图/英文版、exe bridge + 真 dsh 插件（路线 A）
