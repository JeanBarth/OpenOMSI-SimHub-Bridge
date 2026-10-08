# Third-party components

The bridge uses public APIs exposed by openOMSI and SimHub. It does not redistribute the openOMSI executable, OMSI 2 content, SimHub application files or SimHub installation assemblies.

The C# project references `GameReaderCommon.dll`, `Newtonsoft.Json.dll`, `log4net.dll`, `SimHub.Logging.dll` and `SimHub.Plugins.dll` from the user's local SimHub installation at build time. Those files are intentionally not included in this repository.

The repository's own source and documentation are MIT licensed. Refer to the respective upstream projects for the terms that apply to their software and APIs.
