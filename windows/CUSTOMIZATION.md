# Compositor Windows community build 0.6.2

This independent Windows build starts from the C# / Avalonia community port by chenguisen,
branch `compositor_win`, commit `c51be1e57d699edce857115f43bbca579f18dcd4`.
The original application is [robbietilton/Compositor](https://github.com/robbietilton/Compositor).
The original MIT license and copyright notice remain in the source and portable package.

## Changes

- Replace button, popup, numeric-field and slider appearance with compact Mac-style templates.
  Buttons fade their press feedback over 80 ms; segmented choices move their blue highlight over
  160 ms. These native-style timings are tuned locally, not measured on a live Mac.
- Draw the brush's scaled double-rim cursor in a separate visual so pointer motion does not
  recompose document pixels. Tool variants now retain their selection and update their rail glyph.
- Reuse layer cells and canvas-relative thumbnails. Selection uses a gray row and a blue border
  around the active image or mask. Folder disclosure, mask target, mask enable/link and clipping
  captions are interactive. Deep nesting compacts indentation to keep controls in narrow panels.
- Black and white mask choices change paint coverage without changing the selected target.
  Shift-click enables/disables a mask; Ctrl-click loads image pixels or mask dark areas as a selection.
  Returning to a tab also restores its selected mask after its layer list is loaded.
- Add Ungroup Layers (Ctrl+Shift+G), preserving children, order, nesting and one-step undo.
- A continuous opacity slider drag previews the change and records one undo step.
- Use the original Mac app icon for the Windows executable and its windows.
- Match the Mac source's tool rail, 42-point header, compact controls, segmented brush modes,
  project tabs, layer rows, resizable layers panel and ten Camera Raw sections.
- Brush size, hardness, opacity, blur radius and smoothing are edited directly in the header.
  Number fields accept arrow keys and labels can be scrubbed horizontally.
- Edit transform position, size, aspect ratio, scale, rotation and sampling in the header.
  Ctrl+T keeps changes in a preview document until Apply or Cancel; applying is one undo step.
  Auto Select picks layers on the canvas, and hidden handles still allow moving the active layer.
- Tab neighbors use a 0.15-second ease-out transition. Persistent transform actions use a
  0.12-second ease-out opacity transition. Remaining native Mac animation parity is unverified.
- Add the Zoom tool: click to double, Alt-click to halve, or drag horizontally for anchored zoom.
- Camera Raw parameters and dialogs use flexible tracks and editable numeric values, with
  preview controls and a reset that restores canonical nonzero defaults.
- Add Chinese font fallback to the UI and per-character fallback to rasterized text layers,
  keeping the stored font name. Common dialog choices and labels have additional translations.
- Drag image files and `.comp` project folders from Explorer onto the editor. Multiple inputs are
  accepted; a damaged input reports its failure without preventing subsequent files from opening.
- Copy, Copy Merged and Cut publish Windows bitmap formats and a PNG representation. Paste accepts
  external images and Explorer file lists, centers external images, and creates a document in a blank tab.
  Same-document pastes retain their original selection position and remain one undo step. A newer
  text-only clipboard does not paste an outdated internal image.
- The desktop now connects the port's layered PSD/PSB importer. An imported Photoshop file opens as
  a separate unsaved document and can be saved as `.comp`. Conversion notes appear in the status line;
  hovering over it displays the complete message. The importer supports 8-bit RGB PSD/PSB.
- Reopening a normalized project path activates its existing tab and preserves unsaved edits.
  Save As refuses to overwrite a project currently open in another tab.
- Switching tabs or opening a new project commits an active text edit instead of canceling it.
  The text remains one undo step in its original tab.
- Image imports and pastes check the remaining raster pixel budget, including layer masks, and
  check the layer limit before allocating the incoming surface.
- The import picker accepts multiple files. Open, import, export, clipboard and budget messages
  have additional Chinese translations.
- Switch between Simplified Chinese and English in Help > Language, without reopening the editor.
  Documents, layer names, pixels, selections and undo history are retained. The language is remembered.
- Closing the window or a tab asks to Save, Don't Save or Cancel. New and imported documents are
  considered unsaved even before their first edit. Text still being typed is committed before saving.
- Canceling or failing Save As keeps the previous path and dirty state. Saving a background tab uses
  that tab's document and history.
- A save refuses to replace a file, an unrelated folder, a linked package, or linked files in a package.
  Extra unrelated files in a valid package are preserved while referenced editor assets are replaced.
- Pulling a guide from a ruler shows hidden guides, so it can subsequently be dragged with Move.
- The toolbar's zoom readout follows the canvas when a project opens or its viewport changes.
- An installed or desktop executable uses LocalAppData/Compositor-Windows/data for persistent settings;
  a portable copy uses data/ beside its executable. Native single-file extraction does not change
  this location. COMPOSITOR_DATA_DIR overrides it for isolated QA.
- Command-line project and image paths open after the window has been laid out.
- Help shows this Windows build's provenance rather than checking the macOS release feed.
- `--lifecycle` verifies save/close decisions, language switching after garbage collection,
  text commits, image imports and the actual window Close event. `--window` captures a full headless frame.
- `--create-sample` writes an editable sample project and its flattened PNG.
- `--windows-integration` verifies clipboard, file drops, Photoshop conversion, text tab switching,
  duplicate-project handling and budget checks. It also calls Avalonia's actual Win32 PNG serializer
  on isolated memory without reading or changing the user's system clipboard.

## Build

Use a .NET 10 SDK on Windows x64. NuGet restores dependencies; no Xcode or Qt toolchain is needed.

```powershell
dotnet test windows/tests/Compositor.Core.Tests/Compositor.Core.Tests.csproj -c Release
dotnet build windows/Compositor.slnx -c Release -warnaserror
dotnet publish windows/src/Compositor.Desktop/Compositor.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o dist/Compositor-Windows
```

The single-file executable contains the managed app, runtime and native libraries. At launch .NET extracts
native libraries, including LibRaw, into its per-user cache. Source and dependency notices are supplied
separately. Set PublishSingleFile=false to build a directory with separate replaceable native libraries.

## UI checks

The app has diagnostic commands that exercise its real controls on Avalonia's headless platform.
Set a separate `COMPOSITOR_DATA_DIR` for every run, so a check's settings do not affect another check.

```powershell
Compositor.exe --lang en --tabs tabs.png
Compositor.exe --lang en --tools tools.png
Compositor.exe --lang en --shortcuts shortcuts.png
Compositor.exe --lang en --camera-raw raw.png
Compositor.exe --lang en --dialogs dialogs.png
Compositor.exe --lang en --clicks clicks-en.png
Compositor.exe --lang zh-CN --clicks clicks-zh.png
Compositor.exe --lang en --lifecycle unsaved.png
Compositor.exe --lang en --windows-integration windows-en.png
Compositor.exe --lang zh-CN --windows-integration windows-zh.png
Compositor.exe --lang en --ui-check ui-frames
Compositor.exe --lang en --dialog-check dialog-frames
```

These diagnostics do not show a desktop window. A diagnostic exit code of zero indicates its checks passed.
On PowerShell, wait for the GUI executable with `Start-Process -Wait -PassThru` when recording the exit code.
The Windows integration diagnostic requires Windows. The Win32 serializer probe uses reflection against
the pinned Avalonia version; it is only called by that diagnostic and is not part of normal editing.
The animation frame diagnostic uses a controlled clock from the pinned Avalonia implementation so CPU
load cannot skip its sampled intermediate frames. This clock is confined to the diagnostic.

## Installer and 0.5.0 changes

The per-user installer defaults to `%LOCALAPPDATA%/Programs/Compositor`, creates desktop and Start menu
shortcuts, upgrades the same directory, and preserves settings and user projects on uninstall.
The original icon artwork is centered on nine Windows icon canvases. Windows window buttons,
Ctrl shortcuts, file pickers and context menus remain unchanged.

Opacity captions use their natural text width. Numeric fields are centered and reserve space for
signs and fractional digits. Rounded menus, tool selection, lists, scrollbars, mask target borders,
folder disclosure arrows and Camera Raw folds share eased state transitions. Popup closing cancels
queued opening callbacks; folds cache their destination height to avoid restarting an active transition.
Pointer-driven values and scroll positions update immediately.

Masks can be created from the current selection and viewed alone with Alt-click. The grayscale
mask thumbnail now preserves grayscale coverage. Brush cursors include a hardness ring, and the
clone tool marks the chosen source position; live source-pixel preview remains incomplete.

See INSTALLING.md for user instructions and RELEASING.md for the authorized public GitHub release
process. The workflow builds and checks the published executable before creating a versioned release.

## 0.6.0 responsiveness

0.6.0 adds a bounded canvas raster cache and a 64 MiB layer raster cache. Overlay changes and viewport
movement within cached bounds reuse pixels. Render inputs include mutable mask flags and placement,
so edits invalidate the corresponding cache rather than leaving stale pixels. Native pixel conversion
replaces per-pixel channel swapping; opacity lookup tables retain the original rounding rule.
The tool rail, project tabs, four tool-mode groups and Camera Raw upright/curve-channel choices share a
160 ms selection surface that moves and resizes continuously from its displayed state when interrupted.
The first layout places it directly; repeated layout does not restart the transition. Camera Raw Reset
restores both the geometry/curve settings and the segmented choices. Filter previews use a single worker,
independent source pixels, latest-request checks and retained frame lifetimes. UI input does not wait for
a filter preview. Final filter application, import, saving and very large compositions still contain
synchronous work. Headless CPU timings are not physical desktop frame-rate measurements.

## 0.6.1 interrupted motion and icons

Rapid away-and-back input can set a transition target equal to its currently displayed value before the
next frame. With Avalonia 12.1.3 this could briefly expose the abandoned target. Motion.Set holds the
displayed value while cancelling only that property's transition, preserving other animated properties
and controls sharing a style. Selection surfaces, menu movement, tab movement, folds, and shared button,
toggle, field and scrollbar feedback use this path. Dragged values still update immediately.
Regression checks include no-clock-tick input bursts and transient property callbacks, as well as
intermediate and final frames. A controlled-clock result is not a physical desktop frame-rate guarantee.

WindowIcon now loads the embedded multi-frame ICO. The previous PNG path produced a 256-pixel cursor
through the pinned Win32 backend even when requesting a small title-bar icon. The --native-icons
diagnostic checks the actual Win32 loader at 16 through 64 pixels. The EXE and shortcut ICO contain
15 sizes through 256 pixels; the artwork body fills more of the larger canvases while retaining the
original gradient and soft shadow. Installation sends targeted Shell item-change notifications for
the updated icon and shortcuts. Different physical DPI settings still need desktop comparison.

## 0.6.2 selection and hover handoff

The original Mac tool rail uses plain buttons with a selected background. The Windows shared Button
theme also painted a stationary hover/pressed overlay above the moving selection. These independent
surfaces made the selected background brighten during handoff even with continuous coordinates.
Tool buttons, tab selection buttons and segmented choices now use the selection-item style: only
the shared selection paints a background, while content opacity eases for hover and press. Tab close
buttons and other independent action controls retain their ordinary feedback. Pixel regressions check
both selected and unselected backgrounds, in addition to geometry, press feedback and operating logic.
Tab updates also reuse the existing text control: replacing it on selection briefly invalidated pointer
hover, interrupting the release feedback even though the selection surface itself remained continuous.

## Limits

This customized community build follows the existing C# port, not every feature of the
latest macOS application. Apple Vision subject selection/background removal is not implemented.
Rendering uses Skia and CPU kernels, and some filters, font rendering and color handling differ from
the macOS Core Image/Metal pipeline. Large-canvas performance and pixel-level macOS parity have not
been systematically benchmarked. Some detailed diagnostics and uncommon option names remain English.
The build is unsigned and has no automatic update channel.
Native file-picker behavior, clipboard ownership/locking across other running applications, and different
display scaling settings still require manual desktop testing. Automated clipboard checks use the isolated
headless clipboard plus the native Win32 PNG serializer; they do not modify the system clipboard.
