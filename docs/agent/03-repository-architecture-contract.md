# Repository Architecture Contract

Planned greenfield structure:

```text
DocumentIDE.sln
src/
  DocumentIDE.Core/
  DocumentIDE.Infrastructure/
  DocumentIDE.Api/
  DocumentIDE.Desktop/
  DocumentIDE.ShareHost/
  DocumentIDE.Web/
tests/
  DocumentIDE.Core.Tests/
  DocumentIDE.IntegrationTests/
  DocumentIDE.ArchitectureTests/
  DocumentIDE.SecurityTests/
  DocumentIDE.SafetyTests/
  DocumentIDE.PerformanceTests/
  DocumentIDE.GoldenTests/
docs/
  specs/
  agent/
  sprints/
  decisions/
  quality/
  templates/
scripts/
installer/
```

Rules:

- Core contains domain contracts/use cases, not Win32/COM/SQLite implementation.
- Infrastructure contains filesystem/OpenXML/SQLite/Word/ngrok adapters.
- Api exposes privileged loopback application endpoints.
- Desktop owns native lifecycle/WebView2 bridge.
- ShareHost is a separate restricted public-viewer process.
- Web owns React UI.
- Public ShareHost must not reference privileged mutation/Word service modules.
