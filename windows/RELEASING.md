# Public releases

The user has authorized publication of each delivered version in the public repository
https://github.com/IZILE/Compositor-Windows. Continue using this repository and versioned GitHub Releases.
Do not publish local settings, personal projects, attachments, access tokens, or diagnostic logs containing local paths.
Keep original MIT attribution and dependency notices. Source comparisons do not establish pixel-perfect Mac fidelity.

1. Increment the desktop project's Version. Update INSTALLING.md and the release notes.
2. Build and run the core tests, the actual published EXE's UI/dialog/motion checks, and Windows workflows.
3. Build Compositor-Setup.exe with tools/BuildInstaller.ps1 and a verified Inno Setup compiler.
4. Verify isolated install, repeat install, and uninstall before updating the local installed application.
5. Publish the reviewed Windows source, editable progress diagrams, version notes, and public-safe verification summary.
6. Create a vX.Y.Z GitHub Release. Attach Compositor-Setup.exe, Compositor-Windows-source.zip, and SHA256SUMS.txt.
7. Download or verify the uploaded asset digests. Keep older GitHub Releases; replace the local installation and desktop shortcut.

Build on Windows x64 with PowerShell 7, .NET SDK 10 and Inno Setup 7:

```powershell
./windows/tools/BuildInstaller.ps1 -Compiler 'C:/Program Files (x86)/Inno Setup 7/ISCC.exe' -OutputDirectory ./dist
```

The included icon source and script can reproduce the Windows ICO canvases. Diagnostic data must use isolated
COMPOSITOR_DATA_DIR paths. Release assets should contain the product and source, not machine-specific QA folders.
