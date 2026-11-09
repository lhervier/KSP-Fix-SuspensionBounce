"""Plays the scenarios of docs/performance.md one after the other, in one KSP, and reads what they cost.

It drives KSP through KSP-Util-MCPServer, a mod that answers HTTP requests on 127.0.0.1, and needs nothing but
Python 3: no AI, no package to install. Copy the saves of the scenarios (diag/ of the repository) into a sandbox game,
start KSP with KSP-Util-MCPServer installed, and either this mod with measureCost = true in its settings.cfg, or the
fork of KSPProfiler that KSP-Util-MCPServer can drive (or both), wait for the main menu, then run:

    python run-cost.py --folder <your sandbox game> --ksp <your KSP folder> --out out

For each scenario it does what docs/performance.md asks a player to do: load the save, let the parking brake go
(the saves were made with Parking Brake on: installed, it holds the vessel still as it loads), set the camera, hold
the wheel throttle for a scenario that drives, then let the game run --settle seconds. It then measures for --seconds
seconds without asking anything of the game: the script sleeps. With the profiler, the measure is a capture of its
window (Start capture, Stop capture, Export to CSV); with this mod's measure, the lines it writes to KSP.log every
10 s, of which those whose 10 s lie entirely inside the measure are kept.

On Windows it also brings KSP's window in front of every other window before each measure, and moves the mouse
pointer off it; it checks again at the start and at the stop. Play the profiler runs full screen (FULLSCREEN = True
in settings.cfg), and do not use the computer during a run.

It writes lines.json (one entry per scenario: the mod's lines, the profiler's frames and CSV), prints one line per
scenario, and quits KSP (unless --keep-running is given).
"""
import argparse
import ctypes
import json
import os
import re
import sys
import time
import urllib.request

URL = None

# The capture of KSPProfiler is asked for as many frames as its window accepts: it ends on Stop, not before.
PROFILER_FRAMES = 10000

# The wheel throttle of the scenarios that drive.
THROTTLE = 0.3

# The parts whose springs a scenario sets by hand: the crane's TR-2L wheels.
WHEELS = ("wheelMed",)

# Each scenario: the save, and what is done after the load. throttle holds the wheel throttle, brakes off, SAS off;
# springs sets the springs of every wheel of the vessel by hand to that strength, and settle overrides --settle.
SCENARIOS = {
    "crane-rest": dict(save="SuspensionBounceCraneMunAuto"),
    # A save of the crane on its end-stops breaks it as it loads: lowered onto them after the load, it holds.
    "crane-end-stops": dict(save="SuspensionBounceCraneMunAuto", springs=0.05, settle=30),
    "rover-driving": dict(save="SuspensionBounceRoverFlat", throttle=THROTTLE),
    "rover-end-stops-driving": dict(save="SuspensionBounceEndStopsFlat", throttle=THROTTLE),
    "light-rover-eve": dict(save="SuspensionBounceLightRoverEve"),
}

COST_LINE = re.compile(
    r"\[KSPFixSuspensionBounce\] Cost over (\d+) physics steps \(\d+ s\), ([\d.]+) suspensions per step, "
    r"active vessel .*: wheels and mod ([\d.-]+) µs/step, of which the game's wheels ([\d.-]+) and the mod "
    r"([\d.-]+) \(dampers and springs ([\d.-]+); end-stops ([\d.-]+), of which force records ([\d.-]+) and the "
    r"solver ([\d.-]+), run (\d+) times\)")
COST_FIELDS = ("steps", "suspensions", "total", "wheels", "mod", "dampers", "endStops", "records", "solver",
               "solverRuns")


def call(tool, **args):
    """Calls one tool of KSP-Util-MCPServer and returns its answer, decoded from JSON when it is JSON. Right after a
    save is loaded, the game can still say it is not in flight for a moment: the call is then tried again, for up to
    ten seconds."""
    body = json.dumps({"jsonrpc": "2.0", "id": 1, "method": "tools/call",
                       "params": {"name": tool, "arguments": args}}).encode()
    for attempt in range(20):
        request = urllib.request.Request(URL, body, {"Content-Type": "application/json"})
        with urllib.request.urlopen(request, timeout=900) as response:
            result = json.loads(response.read())["result"]
        text = result["content"][0].get("text", "") if result["content"] else ""
        if not (result.get("isError") and text.startswith("Not in flight")):
            break
        time.sleep(0.5)
    if result.get("isError"):
        raise RuntimeError(tool + ": " + text)
    try:
        answer = json.loads(text)
    except ValueError:
        return text
    # The tools another mod adds answer inside "returned".
    if isinstance(answer, dict) and list(answer) == ["returned"]:
        return answer["returned"]
    return answer


def has_profiler():
    """Whether the fork of KSPProfiler that KSP-Util-MCPServer drives is installed."""
    body = json.dumps({"jsonrpc": "2.0", "id": 1, "method": "tools/list"}).encode()
    request = urllib.request.Request(URL, body, {"Content-Type": "application/json"})
    with urllib.request.urlopen(request, timeout=60) as response:
        names = {t["name"] for t in json.loads(response.read())["result"]["tools"]}
    return "profiler_start_capture" in names


def in_front(title):
    """Whether the window of that title is the foreground window; None but on Windows."""
    if sys.platform != "win32":
        return None
    user32 = ctypes.windll.user32
    window = user32.FindWindowW(None, title)
    return bool(window) and user32.GetForegroundWindow() == window


def bring_to_front(title):
    """Makes the window of that title the foreground window, in front of every other one and with the keyboard,
    and moves the mouse pointer off it. Returns whether it is in front; None but on Windows."""
    if sys.platform != "win32":
        return None
    user32 = ctypes.windll.user32
    window = user32.FindWindowW(None, title)
    if not window:
        return False
    # Windows lets a program take the foreground only in some cases, one of them right after a key press: Alt
    # pressed and released, then the window asked for.
    user32.keybd_event(0x12, 0, 0, 0)
    user32.keybd_event(0x12, 0, 2, 0)
    user32.SetForegroundWindow(window)
    time.sleep(0.5)

    class Rect(ctypes.Structure):
        _fields_ = [("left", ctypes.c_long), ("top", ctypes.c_long), ("right", ctypes.c_long),
                    ("bottom", ctypes.c_long)]
    rect = Rect()
    user32.GetWindowRect(window, ctypes.byref(rect))
    width, height = user32.GetSystemMetrics(0), user32.GetSystemMetrics(1)
    # The middle of the first edge of the screen outside the window, never a corner: full screen, the middle of its
    # left edge.
    for x, y in ((2, height // 2), (width - 3, height // 2), (width // 2, 2)):
        if not (rect.left <= x < rect.right and rect.top <= y < rect.bottom):
            user32.SetCursorPos(x, y)
            break
    else:
        user32.SetCursorPos(2, height // 2)
    return in_front(title)


def log_size(ksp):
    path = os.path.join(ksp, "KSP.log")
    return os.path.getsize(path) if os.path.exists(path) else 0


def cost_lines(ksp, start, end):
    """The cost lines KSP.log gained between the two sizes, but the first: its 10 s began before the measure."""
    with open(os.path.join(ksp, "KSP.log"), "rb") as f:
        f.seek(start)
        text = f.read(end - start).decode("utf-8", "replace")
    found = [dict(zip(COST_FIELDS, [float(v) for v in m.groups()])) for m in COST_LINE.finditer(text)]
    return found[1:]


def log(*parts):
    print(time.strftime("%H:%M:%S"), *parts, flush=True)


def open_menu(part):
    """Opens the window of a part, as a right click does, and returns what it shows, once the game has filled it: just
    after a save is loaded, a window can open empty for a moment."""
    for attempt in range(20):
        menus = call("part_menu", action="open", part=part) if attempt == 0 else call("read_part_menus")
        items = next((m["items"] for m in menus if m["part"] == part), [])
        if any(item["kind"] == "button" for item in items):
            return items
        call("wait", seconds=0.25)
    return items


def item_named(items, name):
    """The item of a window whose field or event has this name, None when the window does not show it."""
    return next((i for i in items if name in (i.get("name"), i.get("event"))), None)


# Parking Brake's button in the window of a command part: the name of its event, and its label while the brake holds,
# in each language the mod is translated to (in English for the others).
PARKING_BRAKE = "ToggleParkingBrake"
PARKING_BRAKE_HELD = ("Disengage parking brake", "Desacionando o freio de mão", "Выключить стояночный тормоз")


def parking_brake():
    """The part of the active vessel whose window shows Parking Brake's button, and whether the brake holds, as the
    button's label tells; (None, False) when no window of the vessel shows it."""
    for part in call("list_parts"):
        button = item_named(open_menu(part["id"]), PARKING_BRAKE)
        if button:
            return part["id"], button["label"] in PARKING_BRAKE_HELD
    call("part_menu", action="close")
    return None, False


def release_parking_brake(part):
    """Waits until Parking Brake has nothing left to cancel, the vessel still, then lets it go by its button in the
    window of the command part, as the player does; the brakes stay as they are. A vessel that never looks still is
    let go after 15 s. Returns whether it was still."""
    still = 0
    last = None
    for attempt in range(30):
        vessel = call("get_state")["vessel"]
        # How far it turned since the last look, half a second ago, in degrees: heading and pitch.
        turned = 360.0 if last is None else max(abs((vessel["heading"] - last["heading"] + 180) % 360 - 180),
                                                abs(vessel["pitch"] - last["pitch"]))
        last = vessel
        still = still + 1 if vessel["surfaceSpeed"] < 0.01 and turned < 0.25 else 0
        if still >= 4 and attempt >= 6:
            break
        call("wait", seconds=0.5)
    # A vessel whose suspensions push it within each physics step never looks still, whatever Parking Brake cancels:
    # it is let go after 15 s all the same.
    open_menu(part)
    menus = call("press_part_button", part=part, button=PARKING_BRAKE)
    # The mod renames its button at once, but the window shows the new label only from its next update.
    for attempt in range(8):
        items = next((m["items"] for m in menus if m["part"] == part), [])
        button = item_named(items, PARKING_BRAKE)
        if button and button["label"] not in PARKING_BRAKE_HELD:
            break
        call("wait", seconds=0.25)
        menus = call("read_part_menus")
    else:
        raise RuntimeError("Parking Brake still holds the vessel after its button was pressed")
    call("part_menu", action="close")
    return still >= 4


def set_springs(strength):
    """Sets the springs of the active vessel's wheels by hand, in each wheel's window: Spring/Damper on Override,
    then Spring Strength."""
    for part in call("list_parts"):
        if part["name"] not in WHEELS:
            continue
        call("part_menu", action="open", part=part["id"])
        # The window may take a moment to show all its items.
        for attempt in range(20):
            items = [i for w in call("read_part_menus") if w["part"] == part["id"] for i in w["items"]]
            names = {i.get("name") or i.get("event") for i in items}
            if "springTweakable" in names or "EvtAutoSpringDamperToggle" in names:
                break
            call("wait", seconds=0.25)
        if "springTweakable" not in names:
            call("press_part_button", part=part["id"], button="EvtAutoSpringDamperToggle")
        call("set_part_menu_value", part=part["id"], field="springTweakable", value=strength)
    call("part_menu", action="close")


def scenario(name, options, profiler, out):
    """Plays one scenario and returns what it measured."""
    case = SCENARIOS[name]
    call("load_save", folder=options.folder, save=case["save"])
    brake_part, held = parking_brake()
    parked_still = release_parking_brake(brake_part) if held else None
    call("set_camera", distance=options.camera[0], heading=options.camera[1], pitch=options.camera[2])
    if "springs" in case:
        # Lowered once the vessel has settled on its wheels, and in two steps: dropped at once from a fresh load, the
        # crane can lose a wheel through the terrain.
        time.sleep(10)
        set_springs(0.5)
        time.sleep(5)
        set_springs(case["springs"])
    if "throttle" in case:
        # The rovers' saves have SAS on: steering let go, it is the SAS that would hold or lose the heading.
        call("set_flight", sas=False)
        call("set_controls", wheel_throttle=case["throttle"], brakes=False)
    if profiler:
        call("profiler_open")
    front = bring_to_front(options.window_title)
    time.sleep(case.get("settle", options.settle))
    before = call("get_state").get("vessel") or {}

    # The measure: nothing is asked of the game between its start and its stop.
    front_at_start = in_front(options.window_title)
    start = log_size(options.ksp)
    if profiler:
        call("profiler_start_capture", frames=PROFILER_FRAMES)
    time.sleep(options.seconds)
    if profiler:
        call("profiler_stop_capture")
    front_at_stop = in_front(options.window_title)
    # The game writes its log a moment late.
    time.sleep(1)
    row = dict(scenario=name, save=case["save"], windowInFront=[front, front_at_start, front_at_stop],
               parkedStill=parked_still)
    row["cost"] = cost_lines(options.ksp, start, log_size(options.ksp))
    if profiler:
        row["profiler"] = call("profiler_state")
        call("profiler_export", directory=out, fileName=name + ".csv")
        row["csv"] = name + ".csv"
    state = call("get_state").get("vessel") or {}
    row["speed"] = state.get("surfaceSpeed")
    # Where the rover's front points at both ends of the measure: one that drives with its steering let go should
    # keep its heading.
    row["heading"] = [before.get("roverHeading"), state.get("roverHeading")]
    if "throttle" in case:
        call("set_controls")
    return row


def main():
    global URL
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--folder", required=True, help="the sandbox game under saves/ the saves were copied into")
    parser.add_argument("--ksp", required=True, help="the KSP folder, whose KSP.log the mod's measure writes to")
    parser.add_argument("--scenarios", nargs="+", choices=sorted(SCENARIOS), default=list(SCENARIOS),
                        help="which scenarios, in this order (default: all)")
    parser.add_argument("--settle", type=float, default=10, help="seconds between the load and the measure")
    parser.add_argument("--seconds", type=float, default=35, help="how long each measure lasts")
    parser.add_argument("--camera", type=float, nargs=3, default=[25, 0, 20],
                        metavar=("DISTANCE", "HEADING", "PITCH"),
                        help="where to put the camera, in metres from the vessel and degrees")
    parser.add_argument("--window-title", default="Kerbal Space Program", help="the title of KSP's window")
    parser.add_argument("--out", default="out", help="where lines.json and the profiler's CSV files go")
    parser.add_argument("--port", type=int, default=8770, help="the port of KSP-Util-MCPServer")
    parser.add_argument("--keep-running", action="store_true", help="leave KSP running at the end")
    options = parser.parse_args()
    sys.stdout.reconfigure(encoding="utf-8")
    URL = "http://127.0.0.1:%d/mcp/" % options.port
    out = os.path.abspath(options.out)
    os.makedirs(out, exist_ok=True)

    profiler = has_profiler()
    # KSP-Util-MCPServer loads a save from the space centre or in flight only, as a player does: from the main
    # menu, the game is opened at the space centre first.
    if call("get_state")["scene"] == "MAINMENU":
        call("open_game", folder=options.folder)
    rows = []
    for name in options.scenarios:
        row = scenario(name, options, profiler, out)
        rows.append(row)
        text = "%-24s" % name
        if row["cost"]:
            mean = {k: sum(c[k] for c in row["cost"]) / len(row["cost"]) for k in COST_FIELDS}
            text += "  wheels and mod %6.1f µs/step (game's wheels %6.1f, mod %6.1f; solver run %d of %d steps)" % (
                mean["total"], mean["wheels"], mean["mod"], mean["solverRuns"], mean["steps"])
        if profiler:
            text += "  profiler: %s frames" % (row["profiler"] or {}).get("capturedFrames")
        text += "  speed %.1f m/s, heading %s, window in front %s" % (row["speed"] or 0, row["heading"], row["windowInFront"])
        log(text)
        with open(os.path.join(out, "lines.json"), "w", encoding="utf-8", newline="\n") as f:
            json.dump(rows, f, indent=1)
    log("done")
    if not options.keep_running:
        call("quit_game")


if __name__ == "__main__":
    main()
