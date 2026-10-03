"""
Writes a ready-made "War Thunder" OpenDeck profile for the Saitek Multi Panel and a Stream Deck.
Existing profiles are not touched; the new profile is selected (switch back in OpenDeck's profile list).
OpenDeck must be closed while this runs, or it will overwrite the files on its next save.

    python scripts/wt-layout.py [--streamdeck sd-XXXX] [--panel pz-multipanel] [--force]

Every panel control gets its own spare key (F13-F24, Ctrl+F13-F24). Bind them in War Thunder's
controls menu by clicking a control there and then pressing the panel button / turning the knob.
"""
import argparse
import json
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
WT = "io.github.millenniumambiguity.warthunder.sdPlugin"
PROFILE = "War Thunder"

# ---- Multi Panel: 2x6 keys, 6 dials -------------------------------------------------------------
# Buttons AP..REV: Indicator (LED shows state) + key. Switches: Send Key, tapped once per flip.
PANEL_KEYS = [
    ("indicator", {"preset": "Gear down", "field": "gear", "op": "above", "threshold": 50, "label": "GEAR", "key": "F13"}),
    ("indicator", {"field": "flaps", "op": "above", "threshold": 5, "label": "FLAPS", "key": "F14"}),
    ("indicator", {"field": "airbrake", "op": "above", "threshold": 5, "label": "BRAKE", "key": "F15"}),
    ("indicator", {"field": "rpm", "op": "above", "threshold": 100, "label": "ENG", "key": "F16"}),
    ("indicator", {"field": "alt", "unit": "m", "op": "below", "threshold": 300, "label": "LOW", "key": "F17"}),
    ("indicator", {"field": "fuel", "op": "below", "threshold": 20, "label": "FUEL", "key": "F18"}),
    ("indicator", {"field": "g", "op": "above", "threshold": 6, "label": "G", "key": "F19"}),
    ("indicator", {"field": "aoa", "op": "above", "threshold": 15, "label": "AoA", "key": "F20"}),
    ("sendkey", {"key": "F21", "tap": True}),  # A/T switch -> ARM
    ("sendkey", {"key": "F22", "tap": True}),  # A/T switch -> OFF
    ("sendkey", {"key": "F23", "tap": True}),  # flaps lever up
    ("sendkey", {"key": "F24", "tap": True}),  # flaps lever down
]
PANEL_DIALS = [
    ("flightdata", {"field": "alt", "unit": "m", "keyCw": "Ctrl+F13", "keyCcw": "Ctrl+F14"}),
    ("flightdata", {"field": "vs", "unit": "ms", "keyCw": "Ctrl+F15", "keyCcw": "Ctrl+F16"}),
    ("flightdata", {"field": "ias", "unit": "kmh", "keyCw": "Ctrl+F17", "keyCcw": "Ctrl+F18"}),
    ("flightdata", {"field": "hdg", "keyCw": "Ctrl+F19", "keyCcw": "Ctrl+F20"}),
    ("flightdata", {"field": "throttle", "keyCw": "Ctrl+F21", "keyCcw": "Ctrl+F22"}),
    ("sendkey", {"keyCw": "Ctrl+F23", "keyCcw": "Ctrl+F24"}),  # pitch trim wheel
]

# ---- Stream Deck MK.2: 3x5 keys ---------------------------------------------------------------
# Same F13/F14/F15 as the panel's GEAR/FLAPS/BRAKE, so either one works once bound.
STREAMDECK_KEYS = [
    ("text", {"template": "{aircraft}"}),
    ("flightdata", {"field": "ias", "unit": "kmh"}),
    ("flightdata", {"field": "alt", "unit": "m"}),
    ("flightdata", {"field": "vs", "unit": "ms"}),
    ("flightdata", {"field": "hdg"}),
    ("flightdata", {"field": "mach"}),
    ("flightdata", {"field": "g"}),
    ("flightdata", {"field": "aoa"}),
    ("flightdata", {"field": "throttle"}),
    ("flightdata", {"field": "rpm"}),
    ("indicator", {"field": "gear", "op": "above", "threshold": 50, "label": "GEAR", "key": "F13"}),
    ("indicator", {"field": "flaps", "op": "above", "threshold": 5, "label": "FLAPS", "key": "F14"}),
    ("indicator", {"field": "airbrake", "op": "above", "threshold": 5, "label": "BRAKE", "key": "F15"}),
    ("text", {"template": "FUEL\\n{fuel}%\\n{fuelmass} kg"}),
    ("flightdata", {"field": "oil"}),
]


def load_manifest():
    with open(os.path.join(ROOT, "plugin", WT, "manifest.json"), encoding="utf-8") as f:
        return {a["UUID"].rsplit(".", 1)[1]: a for a in json.load(f)["Actions"]}


def image_path(rel):
    hi = os.path.join(ROOT, "plugin", WT, rel + "@2x.png")
    return f"plugins/{WT}/{rel}@2x.png" if os.path.exists(hi) else f"plugins/{WT}/{rel}.png"


def state(manifest_state, icon, text=""):
    return {
        "alignment": manifest_state.get("TitleAlignment", "middle"),
        "background_colour": "#000000",
        "colour": "#FFFFFF",
        "family": "Liberation Sans",
        "image": icon,
        "image_scale": 100,
        "name": "",
        "show": True,
        "size": manifest_state.get("FontSize", 16),
        "stroke_colour": "#000000",
        "stroke_size": 3,
        "style": "Regular",
        "text": text,
        "underline": False,
    }


def instance(actions, kind, settings, controller, position):
    a = actions[kind]
    icon = image_path(a["Icon"])
    settings = {k: v for k, v in settings.items() if k != "preset"}
    action = {
        "controllers": a["Controllers"],
        "disable_automatic_states": False,
        "encoder": None,
        "icon": icon,
        "name": a["Name"],
        "plugin": WT,
        "property_inspector": f"plugins/{WT}/{a['PropertyInspectorPath']}",
        "states": [state(s, image_path(s["Image"])) for s in a["States"]],
        "supported_in_multi_actions": a.get("SupportedInMultiActions", True),
        "tooltip": a.get("Tooltip", ""),
        "uuid": a["UUID"],
        "visible_in_action_list": True,
    }
    return {
        "action": action,
        "children": None,
        "context": f"{controller}.{position}.0",
        "current_state": 0,
        "settings": settings,
        "states": [state(s, image_path(s["Image"]), settings.get("label", "")) for s in a["States"]],
    }


def write_profile(profiles_dir, device, keys, dials, force):
    actions = load_manifest()
    folder = os.path.join(profiles_dir, device)
    if not os.path.isdir(folder):
        sys.exit(f"No OpenDeck profiles for {device} in {profiles_dir}")
    path = os.path.join(folder, PROFILE + ".json")
    if os.path.exists(path) and not force:
        sys.exit(f"{path} already exists (use --force to replace it)")

    profile = {
        "infobars": [],
        "keys": [instance(actions, k, s, "Keypad", i) for i, (k, s) in enumerate(keys)],
        "sliders": [instance(actions, k, s, "Encoder", i) for i, (k, s) in enumerate(dials)],
    }
    with open(path, "w", encoding="utf-8") as f:
        json.dump(profile, f, indent=2, ensure_ascii=False)

    device_file = os.path.join(profiles_dir, device + ".json")
    with open(device_file, encoding="utf-8") as f:
        device_settings = json.load(f)
    device_settings["selected_profile"] = PROFILE
    with open(device_file, "w", encoding="utf-8") as f:
        json.dump(device_settings, f, indent=2)
    print(f"Wrote {path} and selected it for {device}")


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--panel", default="pz-multipanel")
    parser.add_argument("--streamdeck", help="Stream Deck device id, e.g. sd-DL12L1A83554 (see the profiles folder)")
    parser.add_argument("--force", action="store_true", help="replace an existing War Thunder profile")
    default_dir = os.path.join(os.environ.get("APPDATA", os.path.expanduser("~/.config")), "opendeck", "profiles")
    parser.add_argument("--profiles", default=default_dir)
    args = parser.parse_args()

    write_profile(args.profiles, args.panel, PANEL_KEYS, PANEL_DIALS, args.force)
    if args.streamdeck:
        write_profile(args.profiles, args.streamdeck, STREAMDECK_KEYS, [], args.force)


if __name__ == "__main__":
    main()
