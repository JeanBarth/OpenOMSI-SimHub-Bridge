# OpenOMSI SimHub Bridge

Unofficial community telemetry bridge between [openOMSI](https://github.com/openOMSI-Project/openOMSI) and [SimHub](https://www.simhubdash.com/).

The bridge sends local openOMSI telemetry to SimHub over UDP and exposes it as normal SimHub game data. It can drive dashboards, Arduino devices, RGB matrix displays, Shake-It effects, bass shakers and pedal actuators.

This project is not affiliated with openOMSI, SimHub, OMSI or Aerosoft.

## Features

- Local UDP telemetry at 20 Hz on `127.0.0.1:44444`
- Local tablet cockpit with telemetry and allow-listed vehicle-control actions at TCP port `44445`
- Responsive dashboard using simple browser features for older Android tablets and current devices
- Speed, throttle, brake, steering, engine data, fuel, doors and lights
- Automatic, electric and manual transmission handling
- D/N/R for automatic and electric buses; numbered gears for manual buses
- Turn indicators and hazard lights
- SimHub properties under the `OpenOMSI.*` namespace
- RGB matrix helper for the cloned `OpenOMSI` SimHub profile
- Speed-dependent low-level feedback for electric buses

## Requirements

- Windows
- openOMSI 0.2.x with Lua plugin support
- SimHub 9.12.9 or a compatible later release
- .NET Framework 4.8 for building the SimHub plugin
- An original OMSI 2 installation is required by openOMSI itself

## Installation

See [`docs/INSTALLATION.md`](docs/INSTALLATION.md). A ready-to-use package is available in the GitHub Releases section. The release ZIP contains the compiled plugin, Lua sender and RGB matrix repair helper.

## Tablet dashboard

Install the plugin and Lua sender as usual, then connect the tablet to the same Wi-Fi network as the PC. Open `http://<PC-IP>:44445/` in the tablet browser. Find the PC's local IPv4 address in Windows network settings. The cockpit shows speed, fuel, turn indicators, doors and route information. Its Bedienung view sends fixed actions for supported Almex, ATRON, IBIS, IBox and AFR4 profiles; openOMSI must remain the foreground application on the PC for commands to work. Unsupported vehicle profiles keep controls disabled. It has selectable IBIS-inspired monochrome and ATRON-inspired touchscreen themes and targets older browsers such as Android 6; check it on the actual tablet to confirm rendering. Windows Firewall may ask to allow SimHub to accept connections on a private network; allow private network access for the tablet to connect.

## Building the SimHub plugin

The project references assemblies from the local SimHub installation. Set `SIMHUB_INSTALL_PATH` to the SimHub folder and build `src/OpenOMSI.SimHub.Plugin/OpenOMSI.SimHub.Plugin.csproj` with Visual Studio or MSBuild. SimHub assemblies are not included in this repository and must not be redistributed with it.

## Compatibility and limitations

- The bridge is designed for the openOMSI Lua API available in the tested 0.2.x builds.
- SimHub plugin internals can change between SimHub releases; test after SimHub updates.
- The RGB matrix helper changes only the profile named `OpenOMSI` and creates a timestamped backup before writing settings.
- No OMSI maps, buses, textures, sounds, scripts or other original game content are included.

## License

The bridge source and documentation are released under the MIT License. See [`LICENSE`](LICENSE). Third-party software keeps its own licence terms.

## Credits and support

Please open an issue with the openOMSI and SimHub versions, a short reproduction, and relevant log excerpts. Do not upload original OMSI content or personal telemetry logs containing private information.
