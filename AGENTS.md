# Compositor Windows

- Preserve English/Chinese switching, editable `.comp` projects and Windows operating conventions.
- Match the Mac app's in-app layout, controls and transitions; retain Windows native window buttons.
- Port upstream features manually. Do not merge a Swift source tree into the Windows build.
- Use American spelling. A saved-format change requires a documented manifest version bump.
- Build and check with windows/tools/BuildInstaller.ps1 and VerifyPublished.ps1. Use isolated QA data.
- Follow windows/RELEASING.md: each delivered version goes to the authorized public repository.
  Exclude user data, private attachments, credentials and machine-specific logs.
- Report meaningful phases in plain Chinese with an editable PlantUML diagram and honest remaining work.
  Do not claim source comparisons establish pixel-perfect Mac parity.
