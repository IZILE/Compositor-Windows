# Building Compositor Windows

Install the .NET 10 SDK (CI pins 10.0.401). From the repository root:

```powershell
dotnet test windows/tests/Compositor.Core.Tests/Compositor.Core.Tests.csproj -c Release
dotnet run --project windows/src/Compositor.Desktop/Compositor.Desktop.csproj
```

To make the installer, install the official Inno Setup 7 compiler and run:

```powershell
./windows/tools/BuildInstaller.ps1 -Compiler "C:/Program Files (x86)/Inno Setup 7/ISCC.exe"
./windows/tools/VerifyPublished.ps1 -Executable ./dist/published/Compositor.exe -OutputDirectory ./dist/checks
```

The installer is self-contained and supports choosing its directory. The release workflow builds,
tests and packages on Windows. It preserves existing versioned assets rather than replacing them.
Generated previews are captured from the actual application's `--materials` and `--dialog-check`
diagnostics. Refresh the three files in `docs/previews` and the README version with every release;
do not use mockups as evidence of application behavior.

See [project format](project-format.md), [customizations](../windows/CUSTOMIZATION.md) and
[known gaps](mac-parity-checklist.md). Windows format 12 retains additional editable vector shapes;
ordinary documents remain format 11 for compatibility with the Mac original.
