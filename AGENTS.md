# AGENTS.md — DeepSeek Balance Widget

桌面悬浮小工具：监控 DeepSeek API 余额与 ChatGPT Plus 用量。Windows 用 WPF。**macOS 版已拆分到独立仓库 [DeepSeekBalanceWidget-Mac](https://github.com/wanghoufan/DeepSeekBalanceWidget-Mac)，本仓库现为纯 Windows/WPF 项目。**

## 怎么跑起来

- 开发构建/测试：`dotnet build DeepSeekBalanceWidget.sln` 然后 `dotnet test DeepSeekBalanceWidget.sln`
- 本地发布 Windows 单文件：`powershell -File scripts/publish.ps1 -Runtime win-x64` → `release/DeepSeekBalanceWidget.exe`（本机未安装 `pwsh`/PowerShell 7，勿用 `pwsh`）
- macOS 构建与发布见独立仓库 [DeepSeekBalanceWidget-Mac](https://github.com/wanghoufan/DeepSeekBalanceWidget-Mac)
- 日常使用运行 `release/DeepSeekBalanceWidget.exe`，不要从 `src/.../bin/Debug/` 启动

## 技术栈

- .NET 8（Windows `net8.0-windows` + WPF）
- 余额源：DeepSeek 开放平台 API；Plus 用量源：本机 `~/.cc-switch/codex_oauth_auth.json`
- 私钥：Windows DPAPI（CurrentUser），不上传仓库

## 目录与约定

- `src/DeepSeekBalanceWidget`（WPF）为本仓库唯一应用；共享的 `Models/`、`Services/` 业务逻辑现仅供 Windows 使用（macOS 版在独立仓库 DeepSeekBalanceWidget-Mac 自带副本）
- `docs/plans/` 为历史方案记录；`CHANGELOG.md` 是当前功能真相来源
- `release/`、`*.exe`、`*.zip`、`*.app`、`*.dmg`、`artifacts/runtime/`、`config.json` 已 gitignore，不入库

## ⚠️ 发布前版本一致性（易漏）

打 tag 前必须把 Windows 端版本升到与 `CHANGELOG.md` 顶部一致，否则 exe 内部版本与 GitHub tag 不符：
1. `src/DeepSeekBalanceWidget/DeepSeekBalanceWidget.csproj` 的 `<Version>`/`<AssemblyVersion>`/`<FileVersion>`
（macOS 端版本在独立仓库 DeepSeekBalanceWidget-Mac 自行管理）

Release 包的 zip 文件名由 git tag（`v*`）决定，README 下载表的三处版本号也要同步改。

## 当前状态与下一步

- 当前已发布版本：0.5.0（OpenCode Go 额度监测、预警系统重构、设置页改版、胶囊整改）
- 下一步：① 补齐 WorkBuddy 实际额度接入，替换胶囊右侧灰显 “WB --” 占位；② 或继续迭代其他监测项
