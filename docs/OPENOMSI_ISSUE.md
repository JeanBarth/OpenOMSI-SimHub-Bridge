# Proposal: community SimHub telemetry bridge for openOMSI

Hello,

I built and tested an unofficial companion bridge for openOMSI and SimHub on Windows. It uses the openOMSI Lua plugin API and sends local telemetry to a SimHub plugin over UDP on localhost.

The tested setup provides speed, throttle, brake, engine data, doors, lights, turn indicators, automatic/electric D-N-R handling, manual gears, Shake-It feedback and RGB matrix output. It does not include any OMSI maps, buses, textures, sounds or other original game content.

The companion project is here: **PASTE-GITHUB-REPOSITORY-URL-HERE**

Would the maintainers prefer this to remain a separate companion project, or would documentation/examples for external telemetry consumers be useful in the openOMSI repository? I am happy to adapt the integration to the project's preferred API and contribution style.

The bridge source and documentation are MIT licensed. It is not affiliated with openOMSI, OMSI, Aerosoft or SimHub.
