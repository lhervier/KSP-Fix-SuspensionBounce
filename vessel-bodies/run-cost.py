"""Plays the scenarios of performance.md, next to this script, one after the other, in one KSP, and reads what they
cost.

It drives KSP through KSP-Util-MCPServer, a mod that answers HTTP requests on 127.0.0.1, and needs nothing but
Python 3: no AI, no package to install. Copy the saves of the scenarios into a sandbox game (vessel-bodies/saves,
end-stop-slide/saves and fixed-tuning/saves of the repository: performance.md says which is where), start KSP with
KSP-Util-MCPServer installed, and either this mod with measureCost = true in its settings.cfg, or the fork of
KSPProfiler that KSP-Util-MCPServer can drive (or both), wait for the main menu, then run:

    python run-cost.py --folder <your sandbox game> --ksp <your KSP folder> --out out

The saves were made with Parking Brake on, for the runs without this mod: installed, it holds the vessel still as it
loads. The crane's has a second save, made without it, for the runs with this mod (SuspensionBounceCraneMunAuto;
SuspensionBounceCraneMunAutoStock, with Parking Brake, is loaded when Parking Brake is installed): saved under Parking
Brake, the game puts it back on the ground as it loads, and it sinks past its end-stops. The rover's, made without
Parking Brake, is played without it, with this mod or without it, Physics Easing Fix holding it still as it loads. All
were made with the terrain detail High (settings.cfg, TERRAIN preset). Install Parking Brake only to play without this
mod, or with fixedTuning = false: it sets the vessel's speed back to zero at every physics step, and under Fixed
Tuning's springs that makes the vessel rock a little harder at each step, as it loads, until it leaves the ground. The
script refuses to start when Parking Brake is installed with Fixed Tuning on.

For each scenario it does what performance.md asks a player to do: load the save, let the parking brake go if it
holds the vessel, set the camera, hold the wheel throttle for a scenario that drives, then let the game run --settle
seconds. It then measures for --seconds
seconds without asking anything of the game: the script sleeps. With the profiler, the measure is a capture of its
window (Start capture, Stop capture, Export to CSV); with this mod's measure, the lines it writes to KSP.log every
10 s, of which those whose 10 s lie entirely inside the measure are kept. The game writes its log to the disk in
batches: the lines it has not written 20 s after the measure are missing from what the script prints, and are to be
read again from KSP.log once KSP has quit, by the times of the measure that lines.json keeps. The crane on its
end-stops needs Advanced Tweakables (ADVANCED_TWEAKABLES = True in settings.cfg) with fixedTuning = false, whose
springs it sets with the game's sliders; with Fixed Tuning on, it sets them with this mod's suspension window.

On Windows it also brings KSP's window in front of every other window before each measure, and moves the mouse
pointer off it; it checks again at the start and at the stop. Play the profiler runs full screen (FULLSCREEN = True
in settings.cfg), and do not use the computer during a run.

It writes lines.json (one entry per scenario: the times of the measure, the mod's lines, the profiler's frames and
CSV), prints one line per scenario, and quits KSP (unless --keep-running is given).
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
# springs sets the springs of every wheel of the vessel by hand to that strength, with the game's slider, or, with
# Fixed Tuning on, sets every suspension for that mass in t (tunedMass) with this mod's suspension window; settle
# overrides --settle.
SCENARIOS = {
    # The crane loads with its wheels a little past their end-stops, and takes about a minute and a half to settle.
    "crane-rest": dict(save="SuspensionBounceCraneMunAuto", stock_save="SuspensionBounceCraneMunAutoStock", settle=90),
    # A save of the crane on its end-stops breaks it as it loads: lowered onto them after the load, it holds. Its
    # wheels rest on their end-stops, and their springs still carry over half its weight, which the tyres read: it
    # holds still with End-Stop Slide off too.
    "crane-end-stops": dict(save="SuspensionBounceCraneMunAuto", stock_save="SuspensionBounceCraneMunAutoStock",
                            springs=1.0, tunedMass=8, settle=60),
    "rover-driving": dict(save="SuspensionBounceRoverFlat", throttle=THROTTLE),
    "rover-end-stops-driving": dict(save="SuspensionBounceEndStopsFlat", throttle=THROTTLE),
    "light-rover-eve": dict(save="SuspensionBounceLightRoverEve"),
}

COST_LINE = re.compile(
    r"\[KSPFixSuspensionBounce\] Cost over (\d+) physics steps \(\d+ s\), ([\d.]+) suspensions per step, "
    r"active vessel .*: wheels and mod ([\d.-]+) µs/step, of which the game's wheels ([\d.-]+) and the mod "
    r"([\d.-]+) \(dampers(?: and springs)? ([\d.-]+); end-stops ([\d.-]+), of which force records ([\d.-]+) and the "
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


# The time a line of KSP.log was written at, local time of day, and the length of the window of a cost line.
LOG_TIME = re.compile(r"^\[\w+ (\d\d):(\d\d):(\d\d\.\d+)\]")
COST_WINDOW = re.compile(r"physics steps \((\d+) s\)")


def day_seconds(moment):
    """A time.time() value as seconds since the local midnight, the clock KSP.log's lines are stamped with."""
    local = time.localtime(moment)
    return local.tm_hour * 3600 + local.tm_min * 60 + local.tm_sec + moment % 1


def cost_lines(ksp, offset, start, end):
    """The cost lines of KSP.log, from the byte offset on, whose whole window lies between the two time.time() values.
    The game writes its log to the disk in batches, a quiet minute sometimes not at all: waits, for up to 20 s, until
    the log shows a cost line written after the end, and returns what it found then. The measure's times are kept in
    lines.json, for the lines to be read again from KSP.log once the game has quit."""
    first, last = day_seconds(start), day_seconds(end)
    found = []
    deadline = time.time() + 20
    while True:
        with open(os.path.join(ksp, "KSP.log"), "rb") as f:
            f.seek(offset)
            text = f.read().decode("utf-8", "replace")
        found, after = [], False
        for line in text.splitlines():
            match, stamp = COST_LINE.search(line), LOG_TIME.match(line)
            if not match or not stamp:
                continue
            written = int(stamp.group(1)) * 3600 + int(stamp.group(2)) * 60 + float(stamp.group(3))
            window = float(COST_WINDOW.search(line).group(1))
            if written > last:
                after = True
            elif written - window >= first - 0.5:
                found.append(dict(zip(COST_FIELDS, [float(v) for v in match.groups()])))
        if after or time.time() > deadline:
            return found
        time.sleep(2)


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


FIXED_TUNING = "com.github.lhervier.ksp.fix.suspensions.fixedtuning.FixedTuning"


def fixed_tuning_active():
    """Whether this mod is installed with Fixed Tuning on, as the game reads it."""
    try:
        return call("get_member", type=FIXED_TUNING, member="IsActive")["value"] is True
    except RuntimeError as error:
        if "Unknown type" in str(error):
            # This mod is not installed.
            return False
        raise


def parking_brake_installed():
    """Whether Parking Brake is installed, from any scene: KSP-Util-MCPServer knows its vessel module's type, loaded
    on a vessel or not."""
    try:
        call("get_member", type="ParkingBrake.ParkingBrake", member="enabled")
        return True
    except RuntimeError as error:
        return "Unknown type" not in str(error)


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


TUNING_API = "com.github.lhervier.ksp.fix.suspensions.fixedtuning.SuspensionTuningApi"


def tune(mass, compression):
    """Sets every suspension of the active vessel as the Auto button of this mod's suspension window does: predicted
    for the body the vessel is on and mass t (0: the vessel's), shared by the centre of mass, at that compression of
    the travel, with a damping ratio of 0.7."""
    log("tune:", call("call_method", type=TUNING_API, method="Auto", arguments=["", mass, True, compression, 0.7]))


def scenario(name, options, profiler, out):
    """Plays one scenario and returns what it measured."""
    case = SCENARIOS[name]
    # Where KSP.log ends on the disk before the load: every cost line of the measure comes after it.
    offset = log_size(options.ksp)
    # The Stock save, made under Parking Brake, when Parking Brake is installed (see the first lines).
    stock = "stock_save" in case and parking_brake_installed()
    call("load_save", folder=options.folder, save=case["stock_save"] if stock else case["save"])
    brake_part, held = parking_brake() if parking_brake_installed() else (None, False)
    parked_still = release_parking_brake(brake_part) if held else None
    call("set_camera", distance=options.camera[0], heading=options.camera[1], pitch=options.camera[2])
    if "springs" in case:
        # Lowered once the vessel has settled on its wheels, and in two steps: dropped at once from a fresh load, the
        # crane can lose a wheel through the terrain.
        time.sleep(10)
        if fixed_tuning_active():
            # The game's sliders are hidden: the suspension window sets every suspension, the tanker's legs too, first
            # at two thirds of their travel, then for the scenario's mass.
            tune(0, 0.66)
            time.sleep(5)
            tune(case["tunedMass"], 0.33)
        else:
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
    start = time.time()
    if profiler:
        call("profiler_start_capture", frames=PROFILER_FRAMES)
    time.sleep(options.seconds)
    if profiler:
        call("profiler_stop_capture")
    end = time.time()
    front_at_stop = in_front(options.window_title)
    row = dict(scenario=name, save=case["save"], windowInFront=[front, front_at_start, front_at_stop],
               parkedStill=parked_still, measure=[start, end])
    row["cost"] = cost_lines(options.ksp, offset, start, end)
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
    if parking_brake_installed() and fixed_tuning_active():
        sys.exit("Parking Brake is installed with Fixed Tuning on: remove it from GameData (see the first lines of "
                 "this script)")

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
