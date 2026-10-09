# Compositor Windows 0.6.0

从 GitHub Releases 下载 `Compositor-Setup.exe`。默认安装到当前用户的 C 盘程序目录
`%LOCALAPPDATA%\Programs\Compositor`，同时建立桌面和开始菜单快捷方式。
不需要单独安装 .NET。升级时运行新版安装包，覆盖同一个安装目录。

界面在“帮助 → 语言”中切换简体中文和英文。保留 Windows 窗口按钮、Ctrl 快捷键、
右键菜单和文件选择方式。应用内控件采用统一的深色、圆角和缓出过渡。

设置继续保存在 `%LOCALAPPDATA%\Compositor-Windows\data`；工程保存在用户选择的位置。
卸载程序不删除这些设置和工程。`.comp` 在 Windows 上是工程文件夹；打开时选择其中的
`manifest.json`，或把整个工程文件夹拖入窗口。

0.5.0 修复不透明度标签截断、数值居中及有符号小数的显示宽度，补充菜单、工具选中、
滚动条、列表、蒙版目标、文件夹箭头和 Camera Raw 折叠过渡。快速切换时过渡从当前状态继续，
弹窗关闭会取消尚未执行的打开反馈。滑块、滚动位置及画布输入即时跟随指针。

蒙版新增从当前选区建立、Alt 点击单独查看、返回画面提示；修复灰度缩略图。
画笔新增硬度内圈，图章显示已选择的来源标记。

0.6.0 增加画布与图层缓存，减少平移、缩放、选区和光标反馈时的重复合成。
左侧工具栏、项目标签、四组工具模式与 Camera Raw 引导校正／曲线通道共用连续移动的圆角选中背景，
快速或反向点选时从当前位置继续过渡；中英切换后也会跟随文字宽度调整。
滤镜预览在后台串行计算，只显示最新参数对应的结果；关闭面板后不会回填过期画面。
测试工程的平移重绘从约 195 毫秒降到约 2.3 毫秒；这是固定工程的自动测量，不能代表所有设备的帧率。
复杂图层修改、最终滤镜应用、大图导入和保存仍可能耗时，尚不能承诺所有操作达到 60 FPS。

这是独立社区 Windows 移植版。并非 Mac 原生版本，也尚未完成所有 Mac 功能、像素和时序的实机对照。
大图滤镜性能、多屏 DPI、触控板惯性、Apple Vision 主体选择等仍有差异。
构建未签名。原作和图标的 MIT 许可见 LICENSE；依赖许可见 ThirdPartyLicenses。

Install or upgrade with `Compositor-Setup.exe`. The default is the current user's
`%LOCALAPPDATA%\Programs\Compositor` directory. Settings and editable projects survive uninstall.
Use Help > Language to switch English and Simplified Chinese. This community port retains Windows
window controls and shortcuts, with macOS-inspired in-app controls and transitions.
