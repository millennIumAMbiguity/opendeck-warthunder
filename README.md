# War Thunder Telemetry for OpenDeck

[OpenDeck](https://github.com/nekename/OpenDeck) plugin that shows live War Thunder flight data on Stream Deck keys and dials, and sends keys to the game.
Works great together with the [Saitek Pro Flight Multi Panel plugin](https://github.com/millennIumAMbiguity/opendeck-saitek-multipanel): values go on the panel's LCD and indicators drive its button LEDs.

Written in C# / .NET 10, compiled to a native binary. Windows and Linux (key sending is Windows only for now).

## How it works

The game serves telemetry on `http://127.0.0.1:8111` (`/state`, `/indicators`). The plugin only polls while one of its actions is on screen:
every 250 ms while flying (`WT_INTERVAL_MS` to change), every 1 s when not flying, every 3 s when the game isn't running, and not at all when nothing is shown.
A dial on a Multi Panel knob only counts as shown while the panel's selector is on it; the device plugin pushes selector changes, nothing is polled for that.

## Actions

- **Flight Data** (key or dial): IAS, TAS, Mach, altitude, vertical speed, heading, AoA, G, throttle, RPM, flaps, gear, airbrake, fuel %/mass, oil temperature.
  Units: km/h / kt / mph, m / ft, m/s / ft/min, kg / lb. On a Multi Panel knob dial the value also goes to the LCD (digits only: Mach ×100, G and AoA ×10).
- **Text** (key or dial): several values on one key from a template, e.g. `{aircraft}\nIAS {ias}\nALT {alt:ft}`.
- **Indicator** (key): lit when a value is above/below a threshold. Presets: gear down, flaps out, airbrake, low fuel, high G, high AoA, WEP, low altitude.
  On the Multi Panel's AP–REV keys it drives the button LED (pick a bright lit color).
- **Send Key** (key or dial): a key on press (held, or tapped once for switches) and per knob step.

Indicator and Flight Data can send keys too, so one panel button can toggle the gear *and* show its state on the LED.
Keys go out as hardware scancodes (SendInput), which the game reads. F13–F24 don't exist on normal keyboards, so they never clash with existing binds:
bind them in War Thunder's controls menu by clicking a control and pressing the panel button or Stream Deck key.

## Ready-made layout

`scripts/wt-layout.py` writes a "War Thunder" profile for the Multi Panel and a 15-key Stream Deck and selects it; other profiles are left alone.
Close OpenDeck first, then run it with your Stream Deck's id from `%APPDATA%\opendeck\profiles`:

```sh
python scripts/wt-layout.py --streamdeck sd-XXXXXXXX
```

| Panel control | Action | Key |
|---|---|---|
| AP / HDG / NAV / IAS | Indicator GEAR / FLAPS / BRAKE / ENG (running) | F13 / F14 / F15 / F16 |
| ALT / VS / APR / REV | Indicator LOW (<300 m) / FUEL (<20%) / G (>6) / AoA (>15°) | F17 / F18 / F19 / F20 |
| A/T ARM / A/T OFF | Send Key, tapped once per flip | F21 / F22 |
| Flaps lever up / down | Send Key, tapped | F23 / F24 |
| Knob on ALT / VS | altitude m / vertical speed m/s | Ctrl+F13/F14, Ctrl+F15/F16 |
| Knob on IAS / HDG / CRS | IAS km/h / heading / throttle % | Ctrl+F17/F18, Ctrl+F19/F20, Ctrl+F21/F22 |
| Trim wheel | Send Key | Ctrl+F23 (up) / Ctrl+F24 (down) |

## Install

From OpenDeck's plugin store, or download `io.github.millenniumambiguity.warthunder.sdPlugin.zip` from the [releases](../../releases) and install it from file in OpenDeck.

## Build

```powershell
.\scripts\install.ps1          # Windows: native build + install into %APPDATA%\opendeck\plugins (needs VS "Desktop development with C++"; -NoAot to skip)
```

```sh
./scripts/build-linux.sh       # Linux: native build (needs clang, zlib1g-dev; --no-aot to skip) + install into ~/.config/opendeck/plugins
```

Restart OpenDeck after installing. Releases are built by GitHub Actions: push a tag matching the manifest version (`v0.1.0`).
`warthunder-telemetry --dump` prints what the game currently reports for every field.

War Thunder is a trademark of Gaijin Entertainment. This is an unofficial fan project that only reads the game's public local telemetry.

## License

MIT, see [LICENSE](LICENSE).
