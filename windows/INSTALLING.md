# Compositor Windows 0.6.3

从 GitHub Releases 下载 `Compositor-Setup.exe`。默认安装到当前用户的 C 盘程序目录
`%LOCALAPPDATA%\Programs\Compositor`，同时建立桌面和开始菜单快捷方式。
安装向导中的“选择安装位置”页面可直接编辑路径，也可点“浏览”选择其他目录（包括其他盘符）。
不需要单独安装 .NET。升级时运行新版安装包，会沿用上次选择的安装目录。
需要自己选择目录时，请正常双击安装包；静默安装不会显示向导。

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

0.6.1 修复一帧内移开又移回时可能露出旧目标的动画闪回，覆盖工具、标签、菜单、分段选择、
按钮反馈与折叠。窗口改用十五尺寸 ICO，正确取得标题栏小图标；桌面图标主体放大且居中。
升级后会通知 Windows 刷新图标与快捷方式。不同物理 DPI 和刷新率仍需实机验证。

0.6.2 移除左侧工具、项目标签和分段选择上的第二层悬停／按下底板，只让选中背景连续移动。
鼠标悬停和按下改为图标、文字明暗的平滑变化，避免两层底色叠加时突然变亮。普通操作按钮、
关闭按钮的反馈及 Windows 操作逻辑保留。新增实际背景像素检查，覆盖选中与未选中状态。

0.6.3 修复笔刷／橡皮擦标题及同组下拉选项推移控件、菜单经过空隙时背景反复淡入淡出。
普通笔刷和橡皮擦在输入过程中累计笔触，松手时合并为一次可撤销的编辑；长笔画预览只绘制新增部分。
形状预览复用已转换的图像，长套索轮廓使用连续路径。保留原有像素精度、笔刷间距、软边和选区约束。
超大笔刷、复杂图层合成、滤镜最终应用及大图导入／保存仍可能耗时，不保证所有工程的固定帧率。

这是独立社区 Windows 移植版。并非 Mac 原生版本，也尚未完成所有 Mac 功能、像素和时序的实机对照。
大图滤镜性能、多屏 DPI、触控板惯性、Apple Vision 主体选择等仍有差异。
构建未签名。原作和图标的 MIT 许可见 LICENSE；依赖许可见 ThirdPartyLicenses。

Install or upgrade with `Compositor-Setup.exe`. The default is the current user's
`%LOCALAPPDATA%\Programs\Compositor` directory. Use Browse on the destination page to choose
another folder or drive. Upgrades remember the previously selected location. Double-click the
installer normally to see this page; silent installation does not display the wizard.
Settings and editable projects survive uninstall.
Use Help > Language to switch English and Simplified Chinese. This community port retains Windows
window controls and shortcuts, with macOS-inspired in-app controls and transitions.
