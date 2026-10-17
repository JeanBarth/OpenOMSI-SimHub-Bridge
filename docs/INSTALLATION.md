# Installation

## Release package

1. Close openOMSI and SimHub.
2. Copy `OpenOMSI_SimHub_Bridge.lua` into the openOMSI `plugins` folder.
3. Copy `OpenOMSI.SimHub.Plugin.dll` into the SimHub installation folder.
4. If RGB matrix output is needed, make sure the SimHub matrix profile is named `OpenOMSI`, then run `RGB-Matrix-D-reparieren.cmd` as administrator.
5. Start SimHub and then openOMSI.

## Tablet dashboard

1. Connect the Android tablet and the SimHub PC to the same Wi-Fi network.
2. Start SimHub and openOMSI. The plugin serves the dashboard on TCP port `44445`.
3. On the PC, find its local IPv4 address in Windows Settings > Network & internet > Wi-Fi > Hardware properties.
4. In the tablet browser, open `http://<PC-IP>:44445/`, replacing `<PC-IP>` with that address (for example `192.168.1.25`).
5. If Windows Firewall asks, allow SimHub on private networks.

The cockpit view refreshes telemetry several times per second and does not require an Android app. The Bedienung view can send fixed, allow-listed commands for detected Almex, ATRON, IBIS, IBox and AFR4 profiles. openOMSI must remain the foreground application on the PC for commands to be relayed; commands are rejected while another application is focused. Buses without a recognized profile keep the controls disabled until a matching profile is added. The transport uses the numpad plus key as a frame marker; tablet OK maps to each supported terminal's confirmation key, including Almex NB. On openOMSI 0.2.22 and newer, door positions, headlight level, handbrake, stop brake and indicators use openOMSI's standard bus API. The light indicators also combine common bus-script variables when available. The tablet's headlight and high-beam indicators use the documented lights-variable mapping, with a bus-variable fallback where needed. The road speed-limit sign below the keypad reads the active lane's limit through `omsi.map.speed_limit()`; older builds show a dash.

Use the IBIS and ATRON buttons to switch between the monochrome display and touchscreen-inspired themes. Tap Vollbild to hide browser controls where supported; tap Vollbild beenden to restore them. The page uses standard HTML, CSS and XMLHttpRequest to target older browsers, including Android 6; confirm the layout on the actual tablet. The dashboard is available only while SimHub and the plugin are running.

The matrix helper creates a backup before changing `ArduinoRGBMatrixSettings.json`. It updates the `OpenOMSI` profile only and is safe to run again.

## SimHub properties

The plugin publishes properties such as `OpenOMSI.SpeedKmh`, `OpenOMSI.Brake`, `OpenOMSI.Gear`, `OpenOMSI.TurnLeft`, `OpenOMSI.TurnRight`, `OpenOMSI.Vehicle`, `OpenOMSI.Map` and `OpenOMSI.Connected`.

The normal SimHub game data is also normalized so existing dashboards, Shake-It effects and Arduino outputs can use the telemetry without per-game changes. The bridge reads the current lane speed limit through `omsi.map.speed_limit()` on openOMSI 0.2.22 and newer and exposes it as `OpenOMSI.SpeedLimitKmh`.

## Removing the bridge

Close SimHub and openOMSI, then remove the bridge DLL and Lua file. Restore the timestamped RGB matrix backup if you no longer want the OpenOMSI-specific matrix overlay.
