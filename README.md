# DeepSeek Balance Widget

> DeepSeek 余额 & ChatGPT Plus 用量监控桌面悬浮窗 · 支持 Windows 11 与 macOS · 基于 .NET 8
>
> _DeepSeek balance & ChatGPT Plus usage monitor — a desktop widget for Windows 11 and macOS._

一个面向 Windows 11 与 macOS 的 DeepSeek API 余额与 ChatGPT Plus 用量监控小工具。它支持余额轮询、Plus 剩余额度、开机自启和异常状态提示；Windows 版本另提供贴边自动隐藏、迷你胶囊和系统托盘。

这是一个面向中文用户的桌面工具。Windows 界面基于 WPF，macOS 界面基于 Avalonia，二者共用余额与用量读取逻辑。下面的标签是 GitHub 使用的技术分类，中文含义见[标签说明](#标签说明)。

[![CI](https://github.com/wanghoufan/DeepSeekBalanceWidget/actions/workflows/ci.yml/badge.svg)](https://github.com/wanghoufan/DeepSeekBalanceWidget/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/wanghoufan/DeepSeekBalanceWidget?display_name=tag)](https://github.com/wanghoufan/DeepSeekBalanceWidget/releases/latest)
[![Platform](https://img.shields.io/badge/platform-macOS%20%7C%20Windows-0078D4)](https://github.com/wanghoufan/DeepSeekBalanceWidget)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4)](https://dotnet.microsoft.com/)

![DeepSeek 余额监控 v0.4.0](artifacts/ui-audit/02-after.png)

## 下载

前往 [Releases](https://github.com/wanghoufan/DeepSeekBalanceWidget/releases/latest) 下载对应平台的安装包：

| 文件 | 适用平台 |
| --- | --- |
| `DeepSeekBalanceWidget-v0.4.0-win-x64.zip` | Windows 11 x64 |
| `DeepSeekBalanceWidget-v0.4.0-macos-arm64.zip` | macOS Apple Silicon（M 系列） |
| `DeepSeekBalanceWidget-v0.4.0-macos-x64.zip` | macOS Intel |

**Windows**：解压后直接运行 `DeepSeekBalanceWidget.exe`。发布包为 Windows x64 自包含单文件版本，目标电脑无需预先安装 .NET Runtime。

**macOS**：解压后把 `DeepSeekBalanceWidget.app` 拖入“应用程序”文件夹即可，无需安装 .NET。

> 第一次启动后，请在设置中填写自己的 DeepSeek API Key。Windows 版 API Key 使用 DPAPI 加密保存在本地，macOS 版存入登录钥匙串，都不会上传到 GitHub。

## macOS 使用

macOS 版是独立的原生 `.app` 菜单栏应用，不会影响现有 Windows 版。它支持 Apple Silicon 和 Intel Mac，API Key 保存到当前用户的 macOS 登录钥匙串。

首次运行未签名的应用时，若 Gatekeeper 阻止打开，请在 Finder 中按住 Control 点击应用并选择“打开”。

macOS 版在设置中可启用“登录时自动启动”；这会写入当前用户的 `~/Library/LaunchAgents/com.deepseekbalancewidget.plist`。ChatGPT Plus 用量继续读取本机 `~/.cc-switch/codex_oauth_auth.json`，与 Windows 版使用相同的数据来源。

### 在 Mac 上自行构建

在安装了 .NET 8 SDK 的 Mac 上执行：

```bash
chmod +x scripts/publish-macos.sh
./scripts/publish-macos.sh arm64   # Apple Silicon（M 系列）
# 或 ./scripts/publish-macos.sh x64  # Intel Mac
open release/macos-arm64/DeepSeekBalanceWidget.app
```

生成的应用是自包含的，最终使用者不需要安装 .NET（发布包会包含运行时，因此体积会较大）。

要像其他 App 一样从 Launchpad 启动，可在打包后安装到当前用户的“应用程序”目录：

```bash
bash scripts/install-macos.sh arm64
```

脚本会自动打开应用，使其注册到 Launchpad；之后可直接从 Launchpad 或 Finder 的“应用程序”中启动。同名旧版本会被移动为带时间戳的备份。

## 主要功能

- 实时显示 DeepSeek API 总余额、充值余额和有效赠送余额，并显示与上一次成功刷新的金额和百分比变化
- 通过本机 Codex 登录状态持续显示 **ChatGPT Plus 用量**：以对齐表格呈现两个账号的 5 小时滚动窗口与周窗口剩余额度、重置倒计时（v0.4.0 起逐列对齐）
- 通过官方用量接口持续显示 **OpenCode Go 额度**：5 小时 / 周 / 月三窗口剩余百分比、距恢复倒计时与进度条；未配置 Key / Key 无效 / 网络失败等状态直接显示在区块上（替代原 WorkBuddy 占位）
- 余额与额度预警：DeepSeek 低余额、ChatGPT / OpenCode 额度降到设定档位（默认 20% / 10%）时弹出常驻警报并循环警报声，额度恢复后弹出恢复通知；同一档位每个周期仅提醒一次
- **迷你胶囊单行宽布局**：DeepSeek 余额（含变动小字）｜GPT 双账号四列对齐｜OpenCode Go 三窗口进度条｜贴边 / 最小化 / 关闭按钮贴最右，刷新时间显示于右上角
- **胶囊区块顺序自定义**（v0.4.0）：设置内上移/下移调整 DeepSeek / ChatGPT / OpenCode / WorkBuddy 的渲染顺序，保存即生效
- 低余额及异常下降提醒，带冷却机制避免重复打扰
- Windows 版提供完整卡片与迷你胶囊模式，可自由拖动、记忆位置，并可选贴边自动隐藏
- macOS 版在菜单栏实时显示余额、Plus 用量百分比和高峰时段指示
- 系统托盘/菜单栏状态、置顶、隐藏、开机自启；按北京时间显示官方峰值时段参考
- Windows 版 API Key 使用 DPAPI CurrentUser 加密保存；macOS 版存入登录钥匙串

## v0.4.0 更新亮点

- **ChatGPT 用量对齐表格**：双账号上下两行、五列逐列对齐，每行含剩余百分比与重置倒计时
- **迷你胶囊单行宽布局**：DeepSeek 余额｜GPT 双账号四列对齐｜WorkBuddy 占位，贴边按钮固定最右
- **胶囊区块顺序自定义**：设置内上移/下移调整渲染顺序，保存即生效

> **开发中（未发布）**：工作区已提交但尚未打 tag 发布，包含 OpenCode Go 额度监测（替代原 WB 占位）、预警系统重构（常驻弹窗 + 循环警报声、位置可配）、设置页改版（左侧导航 + 监测项 2×2 卡片）、胶囊整改（单行宽、按钮贴最右、刷新时间右上角、OC 区块、GPT 列距收紧）与若干修复。发布时将随版本号一并更新上方下载表与徽标。详见 [CHANGELOG.md](CHANGELOG.md)「未发布」。

<details>
<summary>查看当前界面（v0.4.0）</summary>

| 迷你胶囊（单行宽布局） | 展开卡片 |
| --- | --- |
| <img src="artifacts/ui-audit/01-before.png" width="480"> | <img src="artifacts/ui-audit/02-after.png" width="280"> |

</details>

完整变更见 [CHANGELOG.md](CHANGELOG.md)。

## 标签说明

| GitHub 标签 | 中文解释 |
| --- | --- |
| `deepseek` | 对接 DeepSeek 开放平台 API |
| `api-monitoring` | 监控 API 余额、变化和可用状态 |
| `desktop-widget` | 桌面悬浮小工具 |
| `windows` | 面向 Windows 11 使用 |
| `wpf` | Windows 版使用 WPF 构建桌面界面 |
| `macos` | 面向 macOS 12+ 使用 |
| `avalonia` | macOS 版使用 Avalonia 构建界面 |
| `dotnet` | 基于 .NET 8 开发 |

仓库右侧的 About 区域提供项目简介和这些技术标签；README 负责提供完整的中文使用说明。

## 日常启动

Windows 日常使用请运行发布包中的 `DeepSeekBalanceWidget.exe`，macOS 从 Launchpad 或“应用程序”中启动 `DeepSeekBalanceWidget.app`。不要从
`src\...\bin\Debug\...` 启动；该目录属于开发构建缓存，路径和文件会随编译变化。

## 发布

在 Windows 的 PowerShell 中运行：

```powershell
.\scripts\publish.ps1
```

脚本会生成 Windows x64、自包含、单文件发布版本：

```text
release\DeepSeekBalanceWidget.exe
```

自包含版本无需目标电脑预先安装 .NET Runtime。发布文件体积会明显大于 Debug 目录里的 apphost，这是正常现象。

发布脚本支持 Windows x64 和 Windows ARM64：

```powershell
.\scripts\publish.ps1 -Runtime win-arm64
```

在 macOS 上运行 `scripts/publish-macos.sh` 生成 `.app` 包，见上文“在 Mac 上自行构建”。

打上 `v*` 标签推送后，GitHub Actions 会自动构建 Windows 与 macOS（arm64/x64）安装包并发布到 Releases 页面。

## 开发

环境要求：

- Windows 11（Windows 版）或 macOS 12+（macOS 版）
- .NET 8 SDK
- Visual Studio 2022、Rider 或 VS Code（可选）

构建与测试：

```powershell
dotnet build DeepSeekBalanceWidget.sln
dotnet test DeepSeekBalanceWidget.sln
```

macOS 项目单独构建：

```bash
dotnet build src/DeepSeekBalanceWidget.Mac/DeepSeekBalanceWidget.Mac.csproj
dotnet run --project src/DeepSeekBalanceWidget.Mac/DeepSeekBalanceWidget.Mac.csproj
```

运行 Mock：

```powershell
dotnet run --project .\src\DeepSeekBalanceWidget -- --mock-scenario sequence
```

## 项目结构

```text
.
├─ src/
│  ├─ DeepSeekBalanceWidget/           Windows WPF 应用
│  └─ DeepSeekBalanceWidget.Mac/       macOS Avalonia 应用
├─ tests/                  自动化测试
├─ artifacts/ui-audit/     UI 前后对照截图
├─ scripts/                构建与发布脚本
├─ release/                本地发布产物，不提交 Git
├─ DeepSeekBalanceWidget.sln
└─ README.md
```

## 配置与安全

Windows 用户配置保存在：

```text
%APPDATA%\DeepSeekBalanceWidget\config.json
```

API Key 使用 Windows DPAPI 的 CurrentUser 范围加密。配置文件、API Key 和本地发布产物不会提交到 GitHub。

macOS 的普通配置位于 `~/Library/Application Support/DeepSeekBalanceWidget/config.json`，API Key 不写入该文件，而是存入 macOS 登录钥匙串。

本项目不会把 API Key 写入源代码、日志或 GitHub Actions。请不要把 `%APPDATA%\DeepSeekBalanceWidget\config.json` 提交或发送给其他人。

## 开机自启

开机自启记录当前正在运行的 EXE 路径。建议先运行 `release\DeepSeekBalanceWidget.exe`，再在设置中启用开机自启，避免注册表继续指向 Debug 构建目录。

## 文档

版本变化记录见 [CHANGELOG.md](CHANGELOG.md)。
