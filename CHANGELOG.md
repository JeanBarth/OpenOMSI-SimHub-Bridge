## 2.14.21

- Add vehicle detection and tablet key mappings for AFR4 and IBox terminals, including the IBox-specific zero, delete and enter keys.
- Keep the F11-delimited tablet transport to avoid triggering openOMSI screenshots.

## 2.14.20

- Replace the F12 tablet frame marker with F11 to avoid triggering openOMSI screenshots.

## 2.14.19

- Replace unreliable reserved F13-F24 transport with F12-delimited numpad events and log received frames for diagnosis.

## 2.14.18

- Improve 32/64-bit tablet key injection and support route entry from ATRON's sales screen; retain IBIS and Almex input mappings.

## 2.14.17

- Use SendInput for tablet key frames, add tap feedback and log received commands for diagnosis.

## 2.14.16

- Enable route and duty entry for ATRON cabin mode and fix ATRON keypad dispatch.

## 2.14.15

- Place line and duty-route selection beneath the tablet numeric keypad.

## 2.14.14

- Keep the numeric keypad visible below the terminal display for ATRON and route-entry profiles.

## 2.14.13

- Brighten the compact speed readout in the top status strip.

## 2.14.12

- Move fuel into the compact top status strip and arrange destination, doors and ticket sales opposite the keypad.

## 2.14.11

- Place the vehicle terminal beside the dashboard data and compact the layout for landscape tablet screens.

## 2.14.10

- Keep the speed and indicators in a compact top strip and show the vehicle controls on the same page.

## 2.14.9

- Add support for ATRON numeric keypad, confirm/cancel and ticket key controls in the tablet interface.

## 2.14.8

- Add an initial tablet-to-game command channel using reserved function-key events and vehicle-aware Lua trigger profiles for Almex, IBIS and standard ticket printers.

## 2.14.7

- Color closed IBIS doors in the destination yellow and open ATRON doors red.

## 2.14.6

- Read OMSI door positions from both underscored and legacy variable names; include the extra rear door pair on buses that have four door pairs.

## 2.14.5

- Keep dashboard cards at content height while the page background fills the full-screen viewport.

## 2.14.4

- Stretch the dashboard page to the full viewport height so its theme fills the full-screen canvas.

## 2.14.3

- Match the full-screen page background to the selected IBIS or ATRON theme.

## 2.14.2

- Add an in-page full-screen toggle with standard and legacy Android browser APIs.

## 2.14.1

- Fix tablet dashboard telemetry field names so speed, fuel, gear and route data appear.

# 2.14.0

- Add a local-network, read-only web dashboard for Android tablets.
- Show speed, fuel, turn indicators, doors, line, destination and next stop.
- Add switchable IBIS-inspired and ATRON-inspired visual themes.
- Use standard browser APIs to target older Android browsers such as Android 6.

# Changelog

## 2.13.0

- Centered the D glyph on the right RGB matrix.
- Kept the complete top row transparent so the right-turn indicator bar can blink across the row.
- Applied the correction specifically to the cloned `OpenOMSI` SimHub profile.

## 2.12.0

- Added profile-aware repair for a cloned The Bus matrix profile.
- Preserved the existing indicator, brake, N and R containers.

## 2.11.0

- Moved the compact D one row down and one column right.

## 2.10.0

- Added a compact D overlay and preserved the upper-left right-indicator pixel.

## 2.9.0

- Added D/N/R handling for automatic and electric buses.
- Preserved numbered gears for manual buses.
# 2.14.0

- Add a local-network, read-only web dashboard for Android tablets.
- Show speed, fuel, turn indicators, doors, line, destination and next stop.
- Keep the dashboard compatible with older Android browsers using XMLHttpRequest and responsive layouts.












- Replace the F12 tablet frame marker with F11 to avoid triggering openOMSI screenshots.
-## 2.14.19
## 2.14.20

- Replace the F12 tablet frame marker with F11 to avoid triggering openOMSI screenshots.

## 2.14.19
## 2.14.22

- Use the Almex enter key for tablet OK and replace the F11 transport marker with the unused numpad plus key to prevent view changes.

## 2.14.22

- Replace the F11 frame marker with numpad plus to avoid triggering openOMSI's view change while sending tablet input.
- Keep Almex OK mapped to the terminal's NB/enter button; preserve the profile-specific confirmation keys for other terminals.

