# Compositor Windows 0.6.7

从 [GitHub Releases](https://github.com/IZILE/Compositor-Windows/releases) 下载 `Compositor-Setup.exe`。
双击安装包，在“选择安装位置”页面编辑路径或点“浏览”选择文件夹、盘符。
默认位置为 C 盘当前用户的 `%LOCALAPPDATA%\Programs\Compositor`。升级沿用上次选择的位置，
保留设置与导入的素材，并更新桌面和开始菜单快捷方式。静默安装不显示目录选择向导。

界面语言可在“帮助 → 语言”切换。窗口按钮、Ctrl 快捷键、文件选择器及右键菜单保持 Windows 操作方式。
设置与导入的素材保存在 `%LOCALAPPDATA%\Compositor-Windows\data`；工程保存在用户选择的位置。
卸载保留这些设置、素材与工程。`.comp` 是工程文件夹：选择其中的 `manifest.json`，或把整个文件夹拖入窗口。

## 常用操作

- 画笔类工具的大小可拖动滑条、拖动标签或输入数值；笔刷与橡皮擦也支持 `[` / `]`。窄窗口会收起部分轨道，点数值旁按钮展开。
- 点笔刷预览选择样式、导入 ABR/PNG。形状、渐变、图案与色板的预览按钮也可选择及导入素材。
- 选区工具栏提供新建／添加／减去及扩展、收缩、羽化；Shift/Alt 可临时添加／减去一次选区。
- 文字工具可选择字体、字号、颜色、对齐、字距及行距；行距 0 为自动。点“导入字体…”导入 TTF/OTF，无需安装到系统；导入后立即用于当前文字，重启后仍可选择。
- 点“完成”或 Ctrl+Enter 保留文字编辑，点“取消”还原。数字框支持键盘输入、上下键和 Enter。
- 拖动图层名称或行内空白可排序、移入文件夹；Ctrl+拖动复制图层或整个文件夹。Alt+拖动蒙版缩略图可复制蒙版，也可通过右键菜单复制／粘贴。
- “文件 → 导出 PNG”可选择快速、均衡或最小体积三档无损压缩，保留透明度；JPEG 可拖动或输入质量，并比较原图与压缩后的实际画面。预览下方显示实际文件大小，JPEG 透明区域合成白色背景。

## 格式范围

支持静态采样 ABR 1、2、6、7、9、10 的 8/16 位原始及 RLE 笔尖，现代 ABR 动态参数暂不恢复。
PNG 使用透明度作为笔尖；不透明 PNG 使用深色区域。导入笔尖保留自身软边，图章、修复、模糊、液化和涂抹仍使用圆形工作范围。
支持 SVG 填充轮廓、固定线性 RGB 的 GGR、PNG/JPEG/WebP 图案及 GPL 色板。

导入字体仅保存在本机素材库，尚未嵌入工程；换电脑后若要继续编辑同一字体，需要再次导入。
工程中保存的文字图像仍能正常显示和导出。文字样式目前作用于整层，部分文字格式及复杂文字整形待完善。
普通工程保持格式 11；扩展可编辑形状使用 Windows 格式 12，旧 Mac 版本可通过 PNG/PSD 交换。

这是基于 [原作](https://github.com/robbietilton/Compositor) 和社区 C# 移植继续开发的独立版本。
Mac 功能仍在补齐；物理 DPI、显卡、触控板与原生动画时序尚未完成全部实机对照。
原作与图标的 MIT 许可见 LICENSE；依赖许可见 ThirdPartyLicenses。

## Installation

Run `Compositor-Setup.exe` and use Browse on the destination page to choose a folder or drive.
The default is `%LOCALAPPDATA%\Programs\Compositor`. Upgrades reuse the previous location and retain
settings, imported assets and projects. Settings and assets are stored in `%LOCALAPPDATA%\Compositor-Windows\data`.
Use the desktop or Start menu shortcut to launch. Uninstall preserves user content.

The text toolbar imports app-local TTF/OTF fonts. To edit with that font on another computer, import it there as well.
PNG export offers three lossless compression levels; JPEG export offers adjustable quality and a compressed preview.
The displayed byte count belongs to the exported file. JPEG composites transparency onto white.
