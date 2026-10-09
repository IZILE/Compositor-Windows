# Compositor Windows

轻量的 Windows 图像编辑器，支持图层合成、绘画和照片编辑。保留 Windows 窗口与快捷键操作，应用内界面和动效持续参考 Mac 原版，中英文可切换。

**[下载最新版安装包](https://github.com/IZILE/Compositor-Windows/releases/latest/download/Compositor-Setup.exe)** · [更新记录](https://github.com/IZILE/Compositor-Windows/releases) · [问题反馈](https://github.com/IZILE/Compositor-Windows/issues)

安装目录可选，默认位于 C 盘当前用户的程序目录；升级覆盖原位置，保留设置和工程，无需另装 .NET。

## 界面预览

以下为 **0.6.5 当前应用界面**，随版本更新。

![Compositor Windows 中文编辑界面](docs/previews/editor.png)

点击样式即可查看效果，选中背景连续移动：

![形状选择与实时预览动图](docs/previews/materials.gif)

笔刷直接显示样式，支持预览后选择和导入：

![笔刷样式与实际笔画预览](docs/previews/brushes.png)

## 能做什么

- 图层、蒙版、选区、文字、变换和常用滤镜，可编辑的 `.comp` 工程。
- 绘画、擦除、图章、修复、模糊、涂抹和液化；大小滑条与精确数值输入并存。
- 14 种笔刷、18 种形状、12 组渐变、10 种图案、36 个色板颜色；支持 ABR/PNG、SVG、GGR、图片图案和 GPL 导入。
- 素材切换即时预览，取消还原；图案可在画布预览后填充，并支持撤销。

## 使用与开发

双击桌面快捷方式启动。**帮助 → 语言**切换中文或 English；使用 Ctrl 快捷键，笔刷大小也可用 `[` / `]` 调节。

[安装与升级](windows/INSTALLING.md) · [构建说明](docs/BUILDING.md) · [已知差异](docs/mac-parity-checklist.md) · [本次验证](docs/11a-materials-preview.md)

独立社区移植，基于 [Compositor 原作](https://github.com/robbietilton/Compositor) 和 [chenguisen 的 C# 移植](https://github.com/chenguisen/Compositor/tree/compositor_win)，遵循 [MIT 许可](LICENSE)。当前仍有未移植的 Mac 功能；ABR 导入静态笔尖，SVG 导入填充轮廓，GGR 支持固定线性 RGB 分段。扩展可编辑形状使用 Windows 格式 12，旧 Mac 版本请使用 PNG/PSD 导出。
