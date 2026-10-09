# Compositor Windows

独立社区 Windows 图像编辑器，基于 [Compositor 原作](https://github.com/robbietilton/Compositor)
和 [chenguisen 的 C# / Avalonia 移植](https://github.com/chenguisen/Compositor/tree/compositor_win)。
保留可编辑的 `.comp` 工程、中英切换和 Windows 操作方式，持续对齐 Mac 原版的应用内界面与动效。

## 下载与安装

从 [Releases](https://github.com/IZILE/Compositor-Windows/releases) 下载 `Compositor-Setup.exe`。
默认安装到 `%LOCALAPPDATA%\Programs\Compositor`，建立桌面和开始菜单快捷方式。
升级时运行新版安装包覆盖同一位置；设置和用户工程在卸载后保留。无需单独安装 .NET。
帮助 → 语言可随时切换简体中文和英文。窗口按钮、Ctrl 快捷键、文件选择器保持 Windows 方式。

## 0.6.0

- 工具栏、项目标签、四组工具模式、Camera Raw 两组选择及顶层／下拉／右键菜单使用连续移动的圆角背景。
- 快速反向选择从当前画面继续，布局更新不重启动画；菜单支持鼠标与键盘，保留禁用项、分隔线和子菜单逻辑。
- 有界画布／图层缓存与后台滤镜预览，过期任务不会回填画面；Camera Raw 重置同步参数和选项。
- 固定六图层工程的平移计算约 195→2.3 ms；复杂图层修改仍约 44 ms，不能视为所有操作的实际帧率。
- 质量优先：保留格式解码、中英字体和工程保存；实验裁剪尚有警告，正式版维持完整依赖。

同时包含之前版本的修复：

- 修复不透明度文字截断、数字居中、负号与小数的宽度预留。
- 统一圆角按钮、菜单、标签、工具选中、列表、滚动条和颜色数字步进器的状态过渡。
- 修复快速开关弹窗和反向折叠时可能出现的过期动画；拖动数值与滚动位置即时响应。
- 支持从选区添加蒙版、Alt 单独查看蒙版，修复灰度蒙版缩略图。
- 增加画笔硬度内圈与图章来源标记，重打包居中的九尺寸图标，提供可覆盖升级的安装版。

887 项核心测试、2161 个界面断言、92 个中英文弹窗布局检查及 Windows 工作流检查通过。
这些是自动检查；仍需不同 DPI、大图性能和真实 Mac 动效的实机对照。
Apple Vision 主体选择、完整图层拖放、蒙版变换、软笔实时草稿和 Camera Raw 的一些细节尚未完成。
构建未签名；当前不能声称所有 Mac 功能或像素、时序完全一致。
详见 [本阶段报告](docs/06a-responsiveness.md)、[对齐记录](docs/mac-parity-checklist.md)、[阶段汇报](docs/05a-interface.md)
与 [验证摘要](docs/verification-0.6.0.json)。

## 构建

Windows x64，PowerShell 7，.NET SDK 10.0.401，Inno Setup 7。

```powershell
dotnet test windows/tests/Compositor.Core.Tests/Compositor.Core.Tests.csproj -c Release
dotnet build windows/Compositor.slnx -c Release -warnaserror
./windows/tools/BuildInstaller.ps1 -Compiler 'C:/Program Files (x86)/Inno Setup 7/ISCC.exe' -OutputDirectory ./dist
./windows/tools/VerifyPublished.ps1 -Executable ./dist/published/Compositor.exe -OutputDirectory ./qa
```

[构建说明](windows/CUSTOMIZATION.md) · [安装说明](windows/INSTALLING.md)
· [发布流程](windows/RELEASING.md) · [工程格式](docs/project-format.md)

每个交付版本通过独立 GitHub Release 提供安装包、源码和 SHA-256 校验文件。
后续更新递增版本号并保留已有 Release；本地安装覆盖同一个目录。
源码包含 GitHub Actions 发布流程，云端执行结果以 Actions 页面为准。

## Attribution / English

Independent Windows community port of Compositor. English and Simplified Chinese UI,
editable `.comp` projects, Windows window controls and shortcuts, and macOS-inspired in-app controls.
Download the installer from Releases. Build with .NET 10 and PowerShell 7 on Windows x64.
Full current macOS feature, rendering and animation parity is not established.

Original: robbietilton/Compositor, Mac reference `af30c45c10b1cddc2b9fe8401c137f250d50abb6` (1.4.7).
C# community baseline: chenguisen/Compositor `c51be1e57d699edce857115f43bbca579f18dcd4`.
Original MIT copyright is preserved in [LICENSE](LICENSE). See [NOTICE](NOTICE) for dependencies and icon provenance.
