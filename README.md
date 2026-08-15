# DSH Web 桌面启动器（DSH-Web Launcher）

把 DeepSeek Harness 的本地 Web（`http://127.0.0.1:3080/`）封装成**原生桌面应用**：
独立窗口、鲸鱼图标、无 cmd 闪窗、服务与窗口解耦、开机自启、双击自愈。

> 仓库：https://github.com/zhuiming-rnb/dsh-web-launcher

---

## 功能特性

| 特性 | 说明 |
| --- | --- |
| 🪟 原生应用窗口 | WinForms + WebView2 内嵌浏览器内核，无地址栏/标签页，标题 "DeepSeek Harness" |
| 🐋 鲸鱼图标 | 黑色（默认）/ 可换色，内嵌进 exe；窗口、任务栏、Alt+Tab、桌面快捷方式全部一致 |
| 🚫 无 cmd 闪窗 | 快捷方式直接指向 exe（winexe 无控制台），服务由 exe 内部隐藏拉起 |
| 🖥️ 高分屏清晰 | Per-Monitor V2 DPI 感知（manifest + P/Invoke），和浏览器同样锐利 |
| 🔄 服务自愈 | 服务没起 → exe 自动后台拉起 node dsh（无窗口）→ 显示"正在启动…"加载画面 → 进入界面 |
| 🔒 关窗口不关服务 | dsh 为独立分离进程，关掉应用窗口服务照跑，随时秒开 |
| 🚀 开机自启 | 计划任务 `DSH-Web`（登录时静默拉起服务，隐藏窗口） |
| 🎯 独立任务栏身份 | AUMID `DeepSeekHarness.Desktop`，不与 Edge 等混在一起 |

## 架构

```
桌面快捷方式 (DeepSeek Harness Web.lnk)
   │  直接指向 DSH-Web.exe（无 powershell，无控制台）
   ▼
DSH-Web.exe（原生应用）
   ├─ 加载画面「正在启动 DeepSeek Harness…」
   ├─ 检测 127.0.0.1:3080
   │    └─ 未启动 → 内部隐藏启动: node <dsh bin> web（CreateNoWindow，无窗口）
   ├─ 等待就绪（最长 90s）
   └─ WebView2 加载 http://127.0.0.1:3080/

登录自启: 计划任务 DSH-Web → start-dsh.ps1（WMI 分离启动，父进程 WmiPrvSE，无控制台）
```

## 文件清单

```
E:\workplace\DSH-Web\
├── README.md                  本文档
├── install.ps1                一键安装/修复（幂等，可反复运行）
├── uninstall.ps1              一键卸载（快捷方式/计划任务/可选删目录）
├── build.ps1                  从源码重建 exe（换图标颜色等）
├── make-dsh-icon.ps1          图标生成器（SVG 矢量路径 → 多尺寸 .ico + PWA PNG）
├── patch-dsh-frontend.ps1     前端图标补丁（dsh 更新后重跑；幂等）
├── start-dsh.ps1              服务启动脚本（幂等：端口占用则跳过）
├── open-dsh.ps1               备用入口（快捷方式已直接指向 exe，此脚本保留作手动使用）
├── dsh.ico / dsh-black.ico    鲸鱼图标（多尺寸 16–256）
├── favicon-192.png / favicon-512.png   PWA 图标
├── wv2-version.txt            WebView2 SDK 版本号
├── dsh-web.log                服务启动/健康日志
├── app\
│   ├── DSH-Web.exe            原生应用（图标/DPI 清单已内嵌）
│   ├── DSH-Web.cs             源码（WinForms + WebView2）
│   ├── app.manifest           DPI(PerMonitorV2) + 兼容性清单
│   └── Microsoft.Web.WebView2.Core.dll / WinForms.dll / WebView2Loader.dll
└── webview2-sdk\              构建依赖（首次构建自动从 nuget 下载解压）
```

## 安装 / 修复

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File E:\workplace\DSH-Web\install.ps1
```

幂等：已装好的部分自动跳过；任何一步坏了重跑一次即可修复。安装内容：
1. 生成图标（若缺）→ 构建 exe（若缺或源码更新）
2. 打前端图标补丁（index.html + manifest + favicon.ico/PNG）
3. 注册计划任务 `DSH-Web`（登录自启服务）
4. 创建桌面快捷方式（指向 exe，黑色鲸鱼图标）

## 卸载

```powershell
# 仅移除快捷方式和计划任务
powershell -NoProfile -ExecutionPolicy Bypass -File E:\workplace\DSH-Web\uninstall.ps1

# 连目录一起删（-RemoveApp）
powershell -NoProfile -ExecutionPolicy Bypass -File E:\workplace\DSH-Web\uninstall.ps1 -RemoveApp
```

## 日常使用

- 双击桌面 **DeepSeek Harness Web** → 加载画面 → DeepSeek 界面
- 关窗口不关服务；再次双击秒开
- 服务异常时双击图标会自动拉起

## 维护

### dsh 更新 / 重装后
npm 缓存里的前端 dist 会被覆盖，重跑补丁即可：
```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File E:\workplace\DSH-Web\patch-dsh-frontend.ps1
```

### 换图标颜色（黑/蓝/白等）
```powershell
# 黑色(默认)
powershell -NoProfile -ExecutionPolicy Bypass -File E:\workplace\DSH-Web\build.ps1 -Fill "#000000"
# DeepSeek 品牌蓝
powershell -NoProfile -ExecutionPolicy Bypass -File E:\workplace\DSH-Web\build.ps1 -Fill "#4D6BFE"
# 白色
powershell -NoProfile -ExecutionPolicy Bypass -File E:\workplace\DSH-Web\build.ps1 -Fill "#FFFFFF"
```
重编译后自动重启应用；桌面/任务栏图标随之更新（若桌面图标未变，右键桌面 → 刷新）。

### 改启动加载画面文案
编辑 `app\DSH-Web.cs` 中 `_status.Text`，然后 `build.ps1` 重建。

## 移植到其他机器

脚本全部基于 `$PSScriptRoot`/环境变量，仓库放哪都行。需要按机器调整的只有 `app\DSH-Web.cs` 里的两处硬编码：

| 位置 | 说明 |
| --- | --- |
| `StartServerIfNeeded()` 中的 `node` 路径 | 默认 `D:\Program Files\nodejs\node.exe`，改成你机器的 Node 路径（或把 `FileName` 改成 `"node"` 走 PATH） |
| `StartServerIfNeeded()` 中的 `WorkingDirectory` | 默认 `E:\workplace`，改成你的工作区路径 |
| `dshBin`（脚本和 exe 内） | 默认指向 `%LOCALAPPDATA%\npm-cache\_npx\<hash>\...`，`<hash>` 随 npx 安装位置变化；装了 dsh 的机器路径不同时改这里 |

改完跑 `build.ps1` 重建即可。

## 常见问题

| 问题 | 解决 |
| --- | --- |
| 桌面快捷方式图标不变 | Explorer 按路径缓存图标：改图标后若没刷新，右键桌面→刷新，或重启资源管理器 |
| 之前有 cmd 闪窗 | 已修复：快捷方式直接指向 exe（无 powershell）；若仍见旧行为，确认快捷方式 Target 是 `DSH-Web.exe` |
| 画面模糊 | PerMonitorV2 已内嵌；若仍模糊，确认没有旧版 exe 在运行 |
| 服务端口被其他程序占用 | start-dsh.ps1 会检测占用并记录日志；查看 `dsh-web.log` |
| 计划任务没生效 | `Get-ScheduledTask -TaskName DSH-Web` 查看状态；或重跑 install.ps1 |

## 技术要点

- **WebView2**：系统已装 Runtime（151.x）；SDK 1.0.4129.50 由构建脚本自动从 nuget.org 获取
- **DPI**：`app.manifest` 声明 `PerMonitorV2`（系统启动时读取）+ 代码 P/Invoke `SetProcessDpiAwarenessContext` 兜底
- **分离进程**：node dsh 用 `CreateNoWindow`/WMI `Win32_Process.Create` 启动，父进程与终端无关，关任何窗口不影响
- **图标**：`make-dsh-icon.ps1` 直接解析官方 favicon.svg 的矢量路径（M/C/Z 贝塞尔），System.Drawing 渲染 16–256px 多尺寸 .ico
- **前端补丁**：向 dist 注入 PNG favicon + manifest 图标，让 PWA 可安装、站点图标可用（dsh 更新后需重跑）
