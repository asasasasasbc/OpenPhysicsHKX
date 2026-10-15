# OpenPhysicsHKX

Experimental managed CPU preview solver for HKX Cloth data.
Targets .NET Framework 4.8, Windows x64. No HCT runtime required.

## Build

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Test
```

Output: `bin/Release/OpenPhysicsHKX.Core.dll` and the compatibility CLI.
No NuGet packages or Python are needed to build.

## Use

Reference `OpenPhysicsHKX.Core.dll`, supply explicit particle/constraint data,
then advance the session:

```csharp
var session = new OpenPhysicsHKX.ClothSession(cloths);
session.Step();
var positions = session.GetPositions();
session.Reset();
```

This is not a general HKX reader or a complete physics runtime.
See [API notes](docs/API.md) and [known limits](docs/LIMITATIONS.md).

MIT; see [LICENSE](LICENSE) and [source notes](docs/PROVENANCE.md).
