Artwork: original Compositor Mac icon, commit af30c45c10b1cddc2b9fe8401c137f250d50abb6.
Source SHA256: 34C831205B09FA9244812C71CF4007558F3595930CD3EEC916AE8AC7B1148DAD
Windows packaging: the visible rounded-square body is centered and occupies approximately 92 percent of each canvas (87.5 percent at 16 pixels to protect title-bar edges); the original soft shadow is retained around it.
Frames: 16, 20, 24, 28, 32, 36, 40, 48, 56, 64, 72, 96, 128, 192 and 256 pixels.
The window loads this multi-size ICO rather than passing a large PNG to the Windows title bar.
The gradient and rounded-square artwork are retained. Windows icon canvases are adapted, not byte-identical Mac PNGs.
Rebuild: windows/tools/PrepareAppIcon.ps1; requires System.Drawing on Windows and a scratch frame directory.
The repository MIT license accompanies this distribution.
