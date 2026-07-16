# TLD Workshop — The Long Drive 模组工坊 (WPF-UI 版)

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/download/dotnet/10.0)
[![WPF-UI](https://img.shields.io/badge/WPF--UI-3.0.5-512BD4)](https://github.com/lepoco/wpfui)
[![License](https://img.shields.io/badge/License-AGPL--3.0-blue)](LICENSE)

把原 `长途中文车间最新修复版.exe`（.NET Framework WinForms）重写为 **WPF-UI 3.0.5 + .NET 10** 的现代化模组管理工具。

> ✅ **编译验证**：0 警告 0 错误，4/4 单元测试通过
> ✅ **运行环境**：Windows 10 1809+（WPF 是 Windows-only 框架）

## ✨ 功能特性

### 9 个功能页面
- **🏠 首页** — Hero 区 + 6 个快捷按钮 + 游戏路径卡片 + TLDLoader 状态卡片
- **🔧 TLDLoader 管理** — 安装/卸载/更新模组加载器，实时操作日志，状态色圆点指示
- **📚 浏览模组** — 双源合并（官方 + 中文镜像）+ 5 列卡片网格 + 搜索防抖 + 分类过滤 + 分页
- **📁 我的模组** — 扫描已安装 mod + 卸载/重装 + 全部启用/禁用 + 搜索防抖
- **📦 模组包** — 一键安装模组合集 + 导入/导出 .txt 列表 + 双源徽章
- **📤 提交模组** — FTP 提交到 Workshop（凭据可配置）
- **🛠️ 工具** — 资源下载 / 1000km 注册表标记 / 本地启动 / Steam 启动
- **ℹ️ 关于** — 版本/作者/QQ群/GitHub/开源协议
- **⚙️ 设置** — 三态主题 / 语言 / 数据源 / 每页数量等

### 核心能力
- **🎨 现代化 UI** — WPF-UI 3.0.5 + Mica 背景 + 圆角卡片 + hover 高亮 + 系统主题色
- **🌗 三态主题** — 跟随系统 / 暗色 / 浅色，运行时切换
- **🌍 中英双语** — 完整 i18n，首次启动按系统语言自动检测
- **🚀 首次引导** — 主窗口内覆盖层（不弹独立窗口），3 步引导
- **⚡ 性能优化** — AsyncImage LRU 缓存 + 6 并发下载限制 + 搜索 300ms 防抖 + ScrollViewer 递归深度限制
- **📱 触屏支持** — PreviewTouchMove 手动滚动 + 滚轮支持
- **🔗 双源合并** — 官方源 + 中文源按 FileName + 归一化匹配合并
- **🌐 GitHub 加速** — 中文 TLDLoader 下载套 gh-proxy.com

## 📦 下载使用

### 方式一：直接下载 exe（推荐）
1. 前往 [Releases](../../releases) 页面
2. 下载 `TLDWorkshop-publish-*.zip`
3. 解压双击 `TLDWorkshop.exe` 运行

> 单文件 exe，~33MB，framework-dependent（需安装 [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)）

### 方式二：自己编译
```powershell
git clone -b wpfui https://github.com/Gsjsjzhznsz/htmlTheLongDrivemodsChinesepatch.git
cd htmlTheLongDrivemodsChinesepatch/TLDWorkshop.WPF
dotnet restore TLDWorkshop.sln
dotnet build TLDWorkshop.sln -c Release
# 发布单文件 exe
dotnet publish src/TLDWorkshop.App/TLDWorkshop.App.csproj -c Release -r win-x64
```

## 🏗️ 项目结构

```
TLDWorkshop.WPF/
├── TLDWorkshop.sln
├── src/
│   ├── TLDWorkshop.Core/              # 业务逻辑（无 UI 依赖，可单测）
│   │   ├── Models/                     #   Mod / AppSettings / PatchState / MergedMod
│   │   ├── Services/                   #   ModRepository / PathDetector / TldPatcher /
│   │   │                               #   ModInstaller / UpdateChecker / ModpackService
│   │   └── Contracts/                  #   接口定义
│   └── TLDWorkshop.App/                # WPF-UI 前端
│       ├── App.xaml(.cs)               #   DI 容器 + 主题 + 系统语言检测
│       ├── MainWindow.xaml(.cs)        #   FluentWindow + NavigationView + OnboardingOverlay
│       ├── Styles/AppStyles.xaml       #   共享样式（字体/卡片/按钮）
│       ├── Controls/                   #   AsyncImage(LRU) / ScrollViewerHelper / OnboardingOverlay
│       ├── ViewModels/                 #   9 个 ViewModel（MVVM）
│       ├── Views/                      #   9 个 Page
│       ├── Resources/                  #   Strings.zh.xaml / Strings.en.xaml (i18n)
│       └── Services/                   #   I18nService / NavigationService / DialogService
└── tests/
    └── TLDWorkshop.Core.Tests/         # xUnit 单测
```

## 🔧 技术栈

| 组件 | 版本 | 用途 |
|---|---|---|
| .NET | 10.0 | 运行时 |
| WPF-UI | 3.0.5 | Fluent 风格控件库 |
| CommunityToolkit.Mvvm | 8.2.2 | MVVM 源生成器 |
| Mono.Cecil | 0.11.5 | 游戏 dll 注入 |
| Newtonsoft.Json | 13.0.3 | JSON 解析 |
| Microsoft.Extensions.DI | 8.0.0 | 依赖注入 |

## 🎯 关键实现

### 双源合并
官方源（GitLab 英文）和中文源（极狐镜像）并发拉取，按 FileName 精确匹配 + 归一化匹配（去 M_ / RUNDEN_ 前缀）合并。详情页同时展示两个源，下载时可选源。

### 1000km 注册表标记
直接写 `HKCU\SOFTWARE\Genesz\TheLongDrive\DistanceDriven_h3536230372` = IEEE 754 double 1000.0（8 字节小端序二进制），不依赖游戏目录，任何 TLD 安装都生效。

### 自动检测游戏路径
参考原版 exe 反编译的 `FindTldPath` 逻辑：
1. 优先读 `TLDFolder.txt` 持久化路径
2. 注册表找 Steam 安装目录（WOW6432Node + 32位 双重检查）
3. 解析 `libraryfolders.vdf`，**还原转义路径**（`\\` → `\`）
4. 检查 `appmanifest_1017180.acf` 文件存在（避免同名文件夹误判）

### 中文 TLDLoader 下载加速
GitHub 在国内访问慢，下载 URL 套 `gh-proxy.com` 反向代理：
```
https://gh-proxy.com/https://github.com/Gsjsjzhznsz/.../TLDLoader.dll
```

## 📋 构建要求

- **Windows 10 1809+**（WPF-UI Mica 需要）
- **.NET 10 SDK**（<https://dotnet.microsoft.com/download/dotnet/10.0>）
- **Visual Studio 2022 17.12+** 或 `dotnet build` 命令行

## 📝 许可证

本项目基于 AGPL-3.0 协议开源。原项目基于 KolbenLP / XLDev 的工作，转载/二次开发请注明出处并保持协议一致。

## 🙏 致谢

- [KolbenLP / XLDev](https://gitlab.com/KolbenLP) — 原版 TLD Workshop
- [WPF-UI](https://github.com/lepoco/wpfui) — Fluent 风格 WPF 控件库
- [The Long Drive](https://store.steampowered.com/app/1017180/The_Long_Drive/) — Genesz

## 📞 联系

- **QQ 群**：[点击加入](https://qm.qq.com/q/RTwveRTbMs)
- **GitHub**：[Issues 反馈](../../issues)
