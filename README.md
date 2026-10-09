# Compositor Windows

独立社区 Windows 图像编辑器，基于 [Compositor 原作](https://github.com/robbietilton/Compositor)
和 [chenguisen 的 C# / Avalonia 移植](https://github.com/chenguisen/Compositor/tree/compositor_win)。
保留可编辑的 `.comp` 工程、中英切换和 Windows 操作方式，持续对齐 Mac 原版的应用内界面与动效。

## 下载与安装

从 [Releases](https://github.com/IZILE/Compositor-Windows/releases) 下载 `Compositor-Setup.exe`。
双击安装包进入向导，可通过“浏览”选择安装目录（包括其他盘）。默认安装到 `%LOCALAPPDATA%\Programs\Compositor`，建立桌面和开始菜单快捷方式。
升级时记住此前选择的目录，运行新版安装包覆盖同一位置；设置和用户工程在卸载后保留。无需单独安装 .NET。
帮助 → 语言可随时切换简体中文和英文。窗口按钮、Ctrl 快捷键、文件选择器保持 Windows 方式。

## 0.6.4

- 所有笔刷类工具加入大小滑条，保留数字输入；空间不足时折叠轨道，默认窗口与中英文布局均检查通过。拖动时调整窗口不会拆掉正在操作的控件。
- 新增笔刷面板和 ABR 静态采样笔尖／PNG 导入、缩略图、保存；支持绘制、擦除和蒙版。现代 ABR 的名称、角度、间距、纹理、压感、散布和双重笔刷等动态描述参数暂不恢复，程序生成笔刷不支持。
- 形状直接显示矩形／椭圆／直线；修复选择未传到画布、相同边界复用旧形状，以及颜色和负斜率直线预览。
- 液化和涂抹按附近轨迹索引计算，同一测试中约 17819→134 ms、6387→49 ms，最终像素与修改前完全一致；这是固定场景 CPU 计算，非物理 FPS。图章／模糊和复杂合成仍待优化。
- 962 项核心测试、3561 个界面断言、94 组双语弹窗及十三组最终 EXE 检查通过；安装、升级、卸载和设置保留已验证。没有启用裁剪，也没有新增 ABR 解析依赖。

详见[笔刷与形状报告](docs/10a-brush-tools.md)、[前后性能记录](docs/stroke-tools-before-0.6.4.json)、[修改后记录](docs/stroke-tools-after-0.6.4.json)
及[独立 ABR 蒙版核对](docs/abr-reference-0.6.4.json)。上一版的[公开发布和独立云端验证](docs/09b-install-release.md)也已补充。

## 0.6.3

- 笔刷与橡皮擦在输入时只计算新增线段，松手合并一次结果，整条笔画仍是一次撤销。八组硬／软笔、绘制／擦除的最终像素与修改前逐字节一致。
- 临时笔画复用有界图像，避免每帧重复提交整条长轨迹；形状预览复用转换结果，长套索通过连续路径绘制。
- 修复菜单经过项目空隙、分隔线或禁用项时的背景闪烁；快速往返、键盘、子菜单与关闭检查保留。
- 工具栏按当前字体和中英文最长选项预留空间，修复笔刷／橡皮擦和同组模式、参数、下拉值变化导致的控件移动。
- 最终 EXE 的十二组检查、3015 个界面断言和 900 项核心测试通过。安装目录可选，覆盖升级保留设置与原位置。
- 扩展平移、缩放、框选、套索、渐变引导线、形状、图层不透明度及变换的性能测量。复杂图层合成仍约 40 ms，需要后续优化；测量不是物理屏幕 FPS。

详见[输入、工具栏与菜单报告](docs/09a-input-layout.md)、[性能记录](docs/performance-0.6.3.json)
及[笔刷测量](docs/brush-performance-0.6.3.json)。上一版的[安装、公开发布与独立云端验证](docs/08b-install-release.md)也已补充。

## 0.6.2

- 左侧工具、分段模式和项目标签只保留一层连续移动的选中背景，避免悬停／按下背景叠色。
- 悬停和按下反馈改为图标与文字的平滑明暗变化，操作逻辑和普通动作按钮反馈保留。
- 切换标签时复用文字控件，避免重建内容使悬停状态短暂丢失。
- 增加 24 项背景像素及状态衔接检查；修改前能够复现失败，修复后通过。
- 本地最终程序检查及安装升级通过；Mac 实机逐帧和不同物理 DPI 对照仍待验证。

详见[单层选择背景报告](docs/08a-selection-surfaces.md)。
上一版的[公开发布及独立云端验证](docs/07b-install-release.md)也已补充记录。

## 0.6.1

- 修复一帧内移开又移回时，工具、标签、菜单和分段选中背景短暂跳向旧目标的问题。
- 按钮透明度、开关颜色、折叠高度和箭头同样处理快速往返，其他控件和属性继续动画。
- 窗口改用十五尺寸 ICO；Windows 实际加载器正确选择标题栏小图标，桌面图标主体放大且居中。
- 覆盖安装后定向通知 Windows 刷新图标及快捷方式，保留同一安装位置和设置。
- 最终程序通过新增同帧输入回归、原生图标检查及既有功能检查。当前固定六图层测试的平移计算约 2.6 ms，图层修改约 50 ms；仍有复杂计算瓶颈。

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
- 增加画笔硬度内圈与图章来源标记，重打包居中的十五尺寸图标，提供可覆盖升级的安装版。

962 项核心测试、3561 个界面断言、中英文 94 组弹窗布局及十三组最终程序检查通过。
这些是自动检查；仍需不同 DPI、大图性能和真实 Mac 动效的实机对照。
Apple Vision 主体选择、完整图层拖放、蒙版变换、软笔实时草稿和 Camera Raw 的一些细节尚未完成。
构建未签名；当前不能声称所有 Mac 功能或像素、时序完全一致。
详见 [本阶段报告](docs/07a-motion-icons.md)、[对齐记录](docs/mac-parity-checklist.md)、[阶段汇报](docs/05a-interface.md)
与 [验证摘要](docs/verification-0.6.4.json)。

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
