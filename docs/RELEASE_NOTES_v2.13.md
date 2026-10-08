# OpenOMSI SimHub Bridge v2.13

This release fixes the RGB matrix layout for the cloned OpenOMSI SimHub profile.

- The D glyph is centered.
- The full top row remains transparent for the green right-turn indicator bar.
- Existing N, R, brake and left/right indicator behavior is preserved.
- The repair script updates only the `OpenOMSI` matrix profile and writes a backup before changing settings.

Tested with openOMSI 0.2.x and SimHub 9.12.9 on Windows.
