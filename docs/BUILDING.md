# Building from source

The Lua sender does not require compilation. Copy it to the openOMSI `plugins` folder for testing.

The SimHub plugin targets .NET Framework 4.8 and references assemblies from a local SimHub installation. Set the `SIMHUB_INSTALL_PATH` environment variable to that installation folder, then build:

```powershell
$env:SIMHUB_INSTALL_PATH = 'C:\Program Files (x86)\SimHub'
& 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe' `
  '.\src\OpenOMSI.SimHub.Plugin\OpenOMSI.SimHub.Plugin.csproj' `
  /t:Rebuild /p:Configuration=Release
```

Do not commit SimHub's assemblies, generated `bin`/`obj` files, personal logs or an OMSI installation. The compiled DLL is attached to releases as a convenience for users.

The RGB matrix repair script is PowerShell and should be tested on a copied configuration before changing a user's live SimHub settings.
