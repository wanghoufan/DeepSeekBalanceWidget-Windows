# DeepSeek Balance Widget

> **Windows 版（本仓库）** · DeepSeek 余额 & ChatGPT Plus 用量监控桌面悬浮窗 · 基于 .NET 8 / WPF
>
> macOS 版已拆分到独立仓库：[DeepSeekBalanceWidget-Mac](https://github.com/wanghoufan/DeepSeekBalanceWidget-Mac)

> DeepSeek 余额 & ChatGPT Plus 用量监控桌面悬浮窗 · 支持 Windows 11 · 基于 .NET 8
>
> _DeepSeek balance & ChatGPT Plus usage monitor — a desktop widget for Windows 11._

一个面向 Windows 11 的 DeepSeek API 余额与 ChatGPT Plus 用量监控小工具。它支持余额轮询、Plus 剩余额度、开机自启和异常状态提示；Windows 版本另提供贴边自动隐藏、迷你胶囊和系统托盘。

这是一个面向中文用户的 Windows 桌面工具，界面基于 WPF。下面的标签是 GitHub 使用的技术分类，中文含义见[标签说明](#标签说明)。

[![CI](https://github.com/wanghoufan/DeepSeekBalanceWidget-Windows/actions/workflows/ci.yml/badge.svg)](https://github.com/wanghoufan/DeepSeekBalanceWidget-Windows/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/wanghoufan/DeepSeekBalanceWidget-Windows?display_name=tag)](https://github.com/wanghoufan/DeepSeekBalanceWidget-Windows/releases/latest)
[![Platform](https://img.shields.io/badge/platform-Windows-0078D4)](https://github.com/wanghoufan/DeepSeekBalanceWidget-Windows)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4)](https://dotnet.microsoft.com/)

![DeepSeek 余额监控 v0.6.0](artifacts/ui-audit/02-after.png)

## 下载

前往 [Releases](https://github.com/wanghoufan/DeepSeekBalanceWidget-Windows/releases/latest) 下载对应平台的安装包：

| 文件 | 适用平台 |
| --- | --- |
| `DeepSeekBalanceWidget-v0.6.1-win-x64.zip` | Windows 11 x64 |

**Windows**：解压后直接运行 `DeepSeekBalanceWidget.exe`。发布包为 Windows x64 自包含单文件版本，目标电脑无需预先安装 .NET Runtime。

> 第一次启动后，请在设置中填写自己的 DeepSeek API Key。Windows 版 API Key 使用 DPAPI 加密保存在本地，不会上传到 GitHub。

## macOS 版本

macOS 版已拆分到独立仓库：[DeepSeekBalanceWidget-Mac](https://github.com/wanghoufan/DeepSeekBalanceWidget-Mac)。本仓库仅维护 Windows 版本，macOS 的构建、发布与使用请前往该仓库。

## 主要功能

- 实时显示 DeepSeek API 总余额、充值余额和有效赠送余额，并显示与上一次成功刷新的金额和百分比变化
- 通过本机 Codex 登录状态持续显示 **ChatGPT Plus 用量**：以对齐表格呈现两个账号的 5 小时滚动窗口与周窗口剩余额度、重置倒计时（v0.4.0 起逐列对齐）
- 通过官方用量接口持续显示 **OpenCode Go 额度**：5 小时 / 周 / 月三窗口剩余百分比、距恢复倒计时与进度条；未配置 Key / Key 无效 / 网络失败等状态直接显示在区块上（替代原 WorkBuddy 占位）
- 余额与额度预警：DeepSeek 低余额、ChatGPT / OpenCode 额度降到设定档位（默认 20% / 10%）时弹出常驻警报并循环警报声，额度恢复后弹出恢复通知；同一档位每个周期仅提醒一次
- **迷你胶囊单行宽布局**：DeepSeek 余额（含变动小字）｜GPT 双账号四列对齐｜OpenCode Go 三窗口进度条｜贴边 / 最小化 / 关闭按钮贴最右，刷新时间显示于右上角
- **胶囊区块顺序自定义**（v0.4.0）：设置内上移/下移调整 DeepSeek / ChatGPT / OpenCode / WorkBuddy 的渲染顺序，保存即生效
- 低余额及异常下降提醒，带冷却机制避免重复打扰
- Windows 版提供完整卡片与迷你胶囊模式，可自由拖动、记忆位置，并可选贴边自动隐藏
- 系统托盘/菜单栏状态、置顶、隐藏、开机自启；按北京时间显示官方峰值时段参考
- Windows 版 API Key 使用 DPAPI CurrentUser 加密保存

## v0.6.0 更新亮点

- **额度恢复提醒改版**：ChatGPT 额度只要进入新周期（5h / 周窗口重置回满）就一律弹恢复提醒，不再要求本周期预警过；恢复时胶囊边框闪绿色呼吸灯，一眼区分"恢复了好消息"与"额度告急"的橙/红预警
- **恢复弹窗与预警同级**：绿色描边 + 「知道了」按钮 + 循环提示音；OpenCode 不做恢复提醒（消耗极少）
- **11 种柔和恢复提示音**：门铃叮咚 / 八音盒 / 清脆风铃 / 水滴 / 钢琴琶音 / 竖琴滑音 / 木琴 / 吉他拨弦 / 悠扬钟声 / 鸟鸣 / 晨光舒缓，全部程序化合成，设置页可逐个试听，与警报声风格独立配置
- **限时时长可配置**：限时提醒从固定 10 秒改为 10 秒 / 30 秒 / 1 分钟三档
- 右键菜单新增「测试恢复提醒」「测试额度预警」，随时手动验证提醒效果

<details>
<summary>查看当前界面（v0.6.0）</summary>

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

macOS 构建与发布请在独立仓库 DeepSeekBalanceWidget-Mac 进行。

打上 `v*` 标签推送后，GitHub Actions 会自动构建 Windows 与 macOS（arm64/x64）安装包并发布到 Releases 页面。

## 开发

环境要求：

- Windows 11（Windows 版）
- .NET 8 SDK
- Visual Studio 2022、Rider 或 VS Code（可选）

构建与测试：

```powershell
dotnet build DeepSeekBalanceWidget.sln
dotnet test DeepSeekBalanceWidget.sln
```

macOS 项目构建见独立仓库 DeepSeekBalanceWidget-Mac。

运行 Mock：

```powershell
dotnet run --project .\src\DeepSeekBalanceWidget -- --mock-scenario sequence
```

## 项目结构

```text
.
├─ src/
│  └─ DeepSeekBalanceWidget/           Windows WPF 应用
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

本项目不会把 API Key 写入源代码、日志或 GitHub Actions。请不要把 `%APPDATA%\DeepSeekBalanceWidget\config.json` 提交或发送给其他人。

## 开机自启

开机自启记录当前正在运行的 EXE 路径。建议先运行 `release\DeepSeekBalanceWidget.exe`，再在设置中启用开机自启，避免注册表继续指向 Debug 构建目录。

## 文档

版本变化记录见 [CHANGELOG.md](CHANGELOG.md)。
