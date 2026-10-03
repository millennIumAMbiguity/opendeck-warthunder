// Shared by the War Thunder property inspectors. Mirrors Fields.cs / Units in the plugin.
const FIELDS = [
	{ id: "ias", label: "IAS (indicated airspeed)", kind: "speed" },
	{ id: "tas", label: "TAS (true airspeed)", kind: "speed" },
	{ id: "mach", label: "Mach", kind: "mach" },
	{ id: "alt", label: "Altitude", kind: "altitude" },
	{ id: "vs", label: "Vertical speed", kind: "vspeed" },
	{ id: "hdg", label: "Heading", kind: "heading" },
	{ id: "aoa", label: "Angle of attack", kind: "angle" },
	{ id: "g", label: "G load", kind: "g" },
	{ id: "throttle", label: "Throttle %", kind: "percent" },
	{ id: "rpm", label: "Engine RPM", kind: "rpm" },
	{ id: "flaps", label: "Flaps %", kind: "percent" },
	{ id: "gear", label: "Gear % (100 = down)", kind: "percent" },
	{ id: "airbrake", label: "Airbrake %", kind: "percent" },
	{ id: "fuel", label: "Fuel %", kind: "percent" },
	{ id: "fuelmass", label: "Fuel mass", kind: "mass" },
	{ id: "oil", label: "Oil temperature °C", kind: "temp" },
];
const SHORT = { ias: "IAS", tas: "TAS", mach: "MACH", alt: "ALT", vs: "V/S", hdg: "HDG", aoa: "AoA", g: "G", throttle: "THR", rpm: "RPM", flaps: "FLAPS", gear: "GEAR", airbrake: "BRAKE", fuel: "FUEL", fuelmass: "FUEL", oil: "OIL" };
const UNITS = {
	speed: [["kmh", "km/h"], ["kt", "knots"], ["mph", "mph"]],
	altitude: [["m", "meters"], ["ft", "feet"]],
	vspeed: [["ms", "m/s"], ["fpm", "ft/min"]],
	mass: [["kg", "kg"], ["lb", "lb"]],
};

function fillSelect(select, options, value) {
	select.innerHTML = "";
	for (const [v, text] of options) {
		const o = document.createElement("option");
		o.value = v;
		o.textContent = text;
		select.appendChild(o);
	}
	if (value !== undefined && options.some(([v]) => v === value)) select.value = value;
}

function fillUnits(fieldId, unitSelect, unitRow, value) {
	const kind = FIELDS.find((f) => f.id === fieldId)?.kind;
	const units = UNITS[kind];
	unitRow.style.display = units ? "flex" : "none";
	if (units) fillSelect(unitSelect, units, value);
}

function connect(port, uuid, registerEvent, actionInfo) {
	const ws = new WebSocket("ws://localhost:" + port);
	ws.onopen = () => ws.send(JSON.stringify({ event: registerEvent, uuid }));
	return (settings) => ws.send(JSON.stringify({ event: "setSettings", context: actionInfo.context, payload: settings }));
}

// Key combo inputs: type e.g. "F13" or "Ctrl+G", or focus the box and press keys to record.
// Names match KeyCombo in KeySender.cs.
function keyName(e) {
	const c = e.code;
	if (c.startsWith("Key")) return c.slice(3);
	if (c.startsWith("Digit")) return c.slice(5);
	if (/^F\d+$/.test(c) || /^Numpad\d$/.test(c)) return c;
	const map = {
		NumpadAdd: "NumpadAdd", NumpadSubtract: "NumpadSubtract", NumpadMultiply: "NumpadMultiply", NumpadDivide: "NumpadDivide",
		NumpadDecimal: "NumpadDecimal", NumpadEnter: "NumpadEnter", Space: "Space", Enter: "Enter", Tab: "Tab", Backspace: "Backspace",
		Insert: "Insert", Delete: "Delete", Home: "Home", End: "End", PageUp: "PageUp", PageDown: "PageDown",
		ArrowUp: "Up", ArrowDown: "Down", ArrowLeft: "Left", ArrowRight: "Right", Minus: "Minus", Equal: "Equals",
		BracketLeft: "LBracket", BracketRight: "RBracket", Semicolon: "Semicolon", Quote: "Quote", Backquote: "Grave",
		Backslash: "Backslash", Comma: "Comma", Period: "Period", Slash: "Slash",
	};
	return map[c] ?? null;
}

function bindKeyInput(input, value, onChange) {
	input.value = value ?? "";
	input.placeholder = "e.g. F13 or Ctrl+G (click, then press keys)";
	input.addEventListener("keydown", (e) => {
		if (["Control", "Shift", "Alt", "Meta"].includes(e.key)) return;
		if (e.key === "Escape" || (e.key === "Backspace" && !e.ctrlKey && !e.shiftKey && !e.altKey)) {
			e.preventDefault();
			input.value = "";
			onChange();
			return;
		}
		const name = keyName(e);
		if (!name) return;
		e.preventDefault();
		input.value = [e.ctrlKey && "Ctrl", e.shiftKey && "Shift", e.altKey && "Alt", name].filter(Boolean).join("+");
		onChange();
	});
	input.addEventListener("input", onChange);
}
