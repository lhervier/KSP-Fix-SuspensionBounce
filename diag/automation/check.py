"""Plays one of the checked cases of the README, several times in a row, and sums up what KSP Diag - Suspension read.

It drives KSP through KSP-Util-MCPServer, a mod that answers HTTP requests on 127.0.0.1, and needs nothing but
Python 3: no AI, no package to install. Copy the case's save (or craft) into a sandbox game, start KSP with
KSP-Util-MCPServer and KSP Diag - Suspension installed (and this mod, or not, to compare), wait for the main menu,
then run, for instance:

    python check.py light-rover-eve --folder <your sandbox game> --ksp <your KSP folder> --label stock

Cases, and the save or craft each one loads (in the folder diag of the repository):

    probe            SuspensionBounce.craft, launched on the runway (copy it into Ships/SPH of the game)
    leg-minmus       SuspensionBounceLegMinmus: an engineer places a landing leg alone, foot down, on Minmus
    leg-kerbin       SuspensionBounceLegKerbin: the same on Kerbin
    lander           SuspensionBounceLegKerbin: the lander next to the engineer, made the active vessel
    light-rover-eve  SuspensionBounceLightRoverEve: a rover of a quarter of a ton, loaded on Eve
    heavy-rover-eve  SuspensionBounceHeavyRoverEve: a rover of 10 t on six TR-2L wheels, loaded on Eve
    bulldozer-eve    SuspensionBounceBulldozerEve: the same with a blade of 3.5 t, 14 t in all (the blade needs
                     KSP-Mod-Bulldozer)
    rover-mun        SuspensionBounceRoverMun: the rover of 10 t, brakes on, on the Mun
    rover-kerbin     SuspensionBounceRoverKerbin: the same rover on Kerbin
    rover-minmus     SuspensionBounceRoverMinmus: the same rover on Minmus
    hinge-rover-eve  SuspensionBounceHingeRoverEve: a rover on Eve lowered on its wheels by its hinges
    hinge-rover-eve-manual  SuspensionBounceHingeRoverEveManual: the same, its springs set by hand
    crane-mun        SuspensionBounceCraneMun: a crane of 33 t on the Mun, brakes on, its springs set by hand
    crane-mun-auto   SuspensionBounceCraneMunAuto: the same, its springs left to the game
    end-stops-flat   SuspensionBounceEndStopsFlat: a rover whose springs are at their lowest, driven at 24 m/s
    end-stops-slope  SuspensionBounceEndStopsSlope: a rover with soft springs, braked across a slope of about 20 deg
    straight-line    SuspensionBounceRoverFlat: a rover driven from standstill on flat ground, wheel throttle held,
                     steering, brakes and SAS let go

The saves (but the probe's craft) were made with Parking Brake on: with Parking Brake installed, the vessel is held
still as it loads, wherever the game puts it, and let go once it has nothing left to cancel, brakes kept as saved.
Without Parking Brake, the vessel settles on its own as it loads.

For each trial it loads the save, lets the parking brake go, does what the case asks a player to do, records KSP
Diag - Suspension (its button Record CSV) for --seconds seconds or for the whole drive, and takes a screenshot at the
end. It then prints one line per trial from the recording: the speed over the ground, how many wheels touch the ground
and are bottomed out, the share of the weight the suspensions carry, how far the vessel turned, and, with --ksp, the
lines KSP.log gained ("damaged wheel", "Exploded").
The lone leg is not the active vessel, which KSP Diag - Suspension reads: for it, only the log lines and a
screenshot every 4 s. Everything goes to --out: lines.json, the CSV files, the screenshots. It never saves the game, and quits
KSP at the end (unless --keep-running is given).
"""
import argparse
import csv
import json
import os
import shutil
import sys
import time
import urllib.request

URL = None

CASES = {
    "probe": dict(craft="SPH/SuspensionBounce.craft", seconds=20),
    "leg-minmus": dict(save="SuspensionBounceLegMinmus", seconds=20, heading=242.19, ground=(430, 560)),
    "leg-kerbin": dict(save="SuspensionBounceLegKerbin", seconds=20, heading=17.19, ground=(530, 450)),
    "lander": dict(save="SuspensionBounceLegKerbin", seconds=30, vessel="SuspensionBounce Lander"),
    "light-rover-eve": dict(save="SuspensionBounceLightRoverEve", seconds=30),
    "heavy-rover-eve": dict(save="SuspensionBounceHeavyRoverEve", seconds=30),
    "bulldozer-eve": dict(save="SuspensionBounceBulldozerEve", seconds=30),
    "rover-mun": dict(save="SuspensionBounceRoverMun", seconds=60),
    "rover-kerbin": dict(save="SuspensionBounceRoverKerbin", seconds=60),
    "rover-minmus": dict(save="SuspensionBounceRoverMinmus", seconds=60),
    "hinge-rover-eve": dict(save="SuspensionBounceHingeRoverEve", seconds=45, hinges=True),
    "hinge-rover-eve-manual": dict(save="SuspensionBounceHingeRoverEveManual", seconds=45, hinges=True),
    "crane-mun": dict(save="SuspensionBounceCraneMun", seconds=60),
    "crane-mun-auto": dict(save="SuspensionBounceCraneMunAuto", seconds=60),
    "end-stops-flat": dict(save="SuspensionBounceEndStopsFlat", drive=dict(heading=201, speed=24, distance=800)),
    "end-stops-slope": dict(save="SuspensionBounceEndStopsSlope", seconds=30),
    "straight-line": dict(save="SuspensionBounceRoverFlat", seconds=30, coast=0.3),
}

# The landing leg the engineer picks up on the lander, by its persistent id in the save.
LEG = 1627742187
LOG_LINES = ("damaged wheel", "Exploded")


def call(name, **args):
    """Calls one tool of KSP-Util-MCPServer and returns its answer, decoded from JSON when it is JSON."""
    body = json.dumps({"jsonrpc": "2.0", "id": 1, "method": "tools/call",
                       "params": {"name": name, "arguments": args}}).encode()
    request = urllib.request.Request(URL, body, {"Content-Type": "application/json"})
    with urllib.request.urlopen(request, timeout=900) as response:
        result = json.loads(response.read())["result"]
    text = result["content"][0].get("text", "") if result["content"] else ""
    if result.get("isError"):
        raise RuntimeError(name + ": " + text)
    try:
        return json.loads(text)
    except ValueError:
        return text


def log(*parts):
    print(time.strftime("%H:%M:%S"), *parts, flush=True)


def mouse(tool, **args):
    """Moves or clicks the mouse, again up to three times when the game did not see the pointer there."""
    for attempt in range(3):
        try:
            return call(tool, **args)
        except RuntimeError as e:
            if attempt == 2 or "did not see the pointer" not in str(e):
                raise
            time.sleep(0.5)


def switch(name):
    """Makes the named vessel the active one, as the ] key does, once the game lets it."""
    for attempt in range(20):
        if (call("get_state").get("vessel") or {}).get("name") == name:
            return
        try:
            call("switch_vessel", vessel=name)
            return
        except RuntimeError:
            call("wait", seconds=0.5)
    raise SystemExit("the game never let %s be switched to" % name)


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


def click_until(part, field, value):
    """Clicks a switch or a cycle of the part's open window until its value is the one given."""
    for attempt in range(8):
        items = call("read_part_menus")
        items = next((m["items"] for m in items if m["part"] == part), [])
        if item_named(items, field)["value"] == value:
            return
        call("click_part_menu_field", part=part, field=field)
    raise RuntimeError("%s never got to %s" % (field, value))


def lower_hinges():
    """Plays the rover's KAL-1000 sequence backwards at full speed, from the controller's window: play speed 100 %,
    direction reversed, play. Its hinges lower the rover onto its wheels."""
    kal = next(p["id"] for p in call("list_parts") if p["name"] == "controller1000")
    open_menu(kal)
    call("set_part_menu_value", part=kal, field="sequencePlaySpeed", value=100)
    click_until(kal, "sequenceDirectionIndex", 1)  # the second state: backwards
    click_until(kal, "sequencePlayingIndex", 0)  # the first state: playing


def set_brakes(percent):
    """Sets the brake force of every wheel of the active vessel, as its slider in the wheel's window does."""
    for part in call("list_parts"):
        if "wheel" in part and item_named(open_menu(part["id"]), "brakeTweakable"):
            call("set_part_menu_value", part=part["id"], field="brakeTweakable", value=percent)
    call("part_menu", action="close")


# Parking Brake's button in the window of a command part: the name of its event, and its label while the brake holds,
# in each language the mod is translated to (in English for the others).
PARKING_BRAKE = "ToggleParkingBrake"
PARKING_BRAKE_HELD = ("Disengage parking brake", "Desacionando o freio de mão", "Выключить стояночный тормоз")


def parking_brake():
    """The part of the active vessel whose window shows Parking Brake's button, and whether the brake holds, as the
    button's label tells; (None, False) when no window of the vessel shows it: Parking Brake is not installed, or the
    vessel cannot be controlled (a pod without its crew), and the player could not let it go either."""
    for part in call("list_parts"):
        button = item_named(open_menu(part["id"]), PARKING_BRAKE)
        if button:
            return part["id"], button["label"] in PARKING_BRAKE_HELD
    call("part_menu", action="close")
    return None, False


def release_parking_brake(part):
    """Waits until Parking Brake has nothing left to cancel, then lets the vessel go by its button in the window of
    the command part, as the player does; the brakes stay as they are. The save was made with the parking brake on,
    the vessel at rest: loaded, the vessel is pushed out of the ground and set in motion, which the parking brake stops
    at every physics step. A vessel that never looks still is let go after 15 s. Returns whether it was still."""
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


def place_leg(case):
    """Has the engineer pick up the leg on the lander and set it down, foot down, on the ground in front of him."""
    call("construction_tool", panel="open", tool="place")
    call("set_camera", distance=12, pitch=20, heading=case["heading"])
    point = call("locate_part", part=LEG)["point"]
    mouse("mouse_click", x=point["x"], y=point["y"])
    gx, gy = case["ground"]
    mouse("mouse_move", x=gx, y=gy)
    if call("construction_tool").get("selectedPart") != LEG:
        raise RuntimeError("the leg is not held")
    call("wait", seconds=1)
    mouse("mouse_click", x=gx, y=gy)


def log_size(ksp):
    try:
        return os.path.getsize(os.path.join(ksp, "KSP.log"))
    except OSError:
        return 0


def log_lines(ksp, start):
    """Returns the lines of LOG_LINES that KSP.log gained since it was start bytes long."""
    with open(os.path.join(ksp, "KSP.log"), "rb") as f:
        f.seek(start)
        text = f.read().decode("utf-8", "replace")
    return [l.strip() for l in text.splitlines() if any(k in l for k in LOG_LINES)]


def steps(path):
    """Reads a CSV of KSP Diag - Suspension: one entry per physics step, the vessel columns and the wheels' rows."""
    by_time = {}
    with open(path, newline="", encoding="utf-8") as f:
        for row in csv.DictReader(f):
            by_time.setdefault(row["time_s"], []).append(row)
    return [rows for t, rows in sorted(by_time.items(), key=lambda item: float(item[0]))]


def number(text):
    try:
        return float(text)
    except (TypeError, ValueError):
        return None


def percentile(values, share):
    values = sorted(v for v in values if v is not None)
    return values[min(len(values) - 1, int(share * len(values)))] if values else None


def summary(path, moving_from=None):
    """Sums up a recording: over all of it, its last third, and, from moving_from m/s on, the steps in motion."""
    rows = steps(path)
    if not rows:
        return {}
    vessel = [r[0] for r in rows]
    speed = [number(v["speed_mm_s"]) for v in vessel]
    last = vessel[len(vessel) * 2 // 3:]
    result = dict(
        seconds=round(float(vessel[-1]["time_s"]) - float(vessel[0]["time_s"]), 2),
        max_speed_m_s=round(max(s for s in speed if s is not None) / 1000, 3),
        late_speed_mm_s=round(max(number(v["speed_mm_s"]) for v in last), 2),
        min_grounded=min(int(v["grounded"]) for v in vessel),
        wheels=int(vessel[0]["wheels"]),
        share_none_grounded=round(sum(1 for v in vessel if int(v["grounded"]) == 0) / len(vessel), 3),
        share_bottomed=round(sum(1 for v in vessel if int(v["bottomed"]) > 0) / len(vessel), 3),
        supported_pct_median=round(percentile([number(v["supported_pct"]) for v in vessel], 0.5) or 0, 1))
    # How far the vessel turned about the vertical, its yaw rate summed over the recording, and its fastest turn.
    yaw = [(float(v["time_s"]), number(v["yaw_rate_deg_s"]) or 0) for v in vessel]
    result["turned_deg"] = round(sum(rate * (t1 - t0) for (t0, rate), (t1, _) in zip(yaw, yaw[1:])), 2)
    result["max_yaw_rate_deg_s"] = round(max(abs(rate) for _, rate in yaw), 3)
    # The wheels on the ground over the last third: how far they sink, and their spring.
    late_wheels = [w for r in rows[len(rows) * 2 // 3:] for w in r if w["is_grounded"] in ("1", "True", "true")]
    if late_wheels:
        result["late_compression_mean"] = round(
            sum(number(w["compression"]) or 0 for w in late_wheels) / len(late_wheels), 3)
        result["late_spring_mean"] = round(
            sum(number(w["spring_kN_m"]) or 0 for w in late_wheels) / len(late_wheels), 1)
    if moving_from is not None:
        moving = [v for v in vessel if (number(v["speed_mm_s"]) or 0) > moving_from * 1000]
        if moving:
            supported = [number(v["supported_pct"]) for v in moving]
            result["moving"] = dict(
                seconds=round(len(moving) * (result["seconds"] / max(len(vessel) - 1, 1)), 1),
                supported_pct=[round(percentile(supported, p), 1) for p in (0.05, 0.5, 0.95)],
                lateral_m_s_p95=round(percentile([abs(number(v["lateral_m_s"]) or 0) for v in moving], 0.95), 3),
                drift_deg_p95=round(percentile([abs(number(v["drift_deg"]) or 0) for v in moving], 0.95), 2))
        wheel_rows = [w for r in rows if (number(r[0]["speed_mm_s"]) or 0) > moving_from * 1000 for w in r]
        if wheel_rows:
            result["moving"]["share_wheels_skidding"] = round(
                sum(1 for w in wheel_rows if w["regime"].startswith("skids")) / len(wheel_rows), 3)
    return result


def record(seconds=None, drive=None):
    """Records KSP Diag - Suspension for the given seconds, or while the rover drives, and returns the CSV's path."""
    call("suspension_record_start")
    try:
        if drive:
            call("drive", **drive)
        else:
            call("wait", seconds=seconds)
    finally:
        path = call("suspension_record_stop")["returned"]["csv"]
    return path


def trial(n, name, case, options, out):
    """Plays the case once and returns what it read."""
    start = log_size(options.ksp) if options.ksp else 0
    if "craft" in case:
        if call("get_state")["scene"] != "SPACECENTER":
            call("go_to_scene", scene="SPACECENTER")
        call("launch_vessel", craft=case["craft"])
    else:
        call("load_save", folder=options.folder, save=case["save"])
    row = dict(trial=n)
    if "vessel" in case:
        switch(case["vessel"])
    brake_part, held = parking_brake()
    if held:
        row["parked_still"] = release_parking_brake(brake_part)
    if case.get("hinges"):
        lower_hinges()
    if options.brakes is not None:
        set_brakes(options.brakes)
    if "coast" in case:
        # Steering let go, and SAS off: nothing steers the rover but its own wheels.
        call("set_flight", sas=False)
        call("set_controls", wheel_throttle=case["coast"], brakes=False)
    if "ground" in case:
        # KSP Diag - Suspension reads nothing of the leg: its window would only hide the scene.
        call("suspension_show_window", visible=False)
        place_leg(case)
        # The leg is not the active vessel: a screenshot every few seconds shows where it went.
        for k in range(int(case["seconds"] // 4)):
            call("wait", seconds=4)
            call("screenshot", path=os.path.join(out, "%s-%s-%02d-%02ds.png" % (name, options.label, n, 4 * (k + 1))),
                 return_image=False)
        call("suspension_show_window", visible=True)
    else:
        if case.get("drive"):
            call("wait", seconds=3)
        parts = len(call("list_parts"))
        path = record(seconds=case.get("seconds"), drive=case.get("drive"))
        kept = os.path.join(out, "%s-%s-%02d.csv" % (name, options.label, n))
        shutil.copyfile(path, kept)
        row.update(summary(kept, moving_from=20 if case.get("drive") else None))
        row["parts"] = [parts, len(call("list_parts"))]
        if "coast" in case:
            call("set_controls")
    call("screenshot", path=os.path.join(out, "%s-%s-%02d.png" % (name, options.label, n)), return_image=False)
    if options.ksp:
        row["log"] = log_lines(options.ksp, start)
    return row


def main():
    global URL
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("case", choices=sorted(CASES))
    parser.add_argument("--folder", required=True, help="the sandbox game under saves/ the save was copied into")
    parser.add_argument("--trials", type=int, default=1, help="how many trials, the save loaded before each")
    parser.add_argument("--seconds", type=float, help="how long to record (default: the case's)")
    parser.add_argument("--ground", type=int, nargs=2, metavar=("X", "Y"),
                        help="for the lone leg, the point of the ground it is set down at, in pixels of the KSP window "
                        "(default: the case's, for a window of 1286 x 795)")
    parser.add_argument("--brakes", type=float,
                        help="the brake force of every wheel, in percent as its slider gives it (default: the save's)")
    parser.add_argument("--label", default="run", help="a word for the files of this run: stock, modded...")
    parser.add_argument("--ksp", help="the KSP folder, to count the lines KSP.log gains")
    parser.add_argument("--out", default="out", help="where lines.json, the CSV files and the screenshots go")
    parser.add_argument("--port", type=int, default=8770, help="the port of KSP-Util-MCPServer")
    parser.add_argument("--keep-running", action="store_true", help="leave KSP running at the end")
    options = parser.parse_args()
    sys.stdout.reconfigure(encoding="utf-8")
    URL = "http://127.0.0.1:%d/mcp/" % options.port
    out = os.path.abspath(options.out)
    os.makedirs(out, exist_ok=True)
    case = dict(CASES[options.case])
    if options.seconds:
        case["seconds"] = options.seconds
    if options.ground and "ground" in case:
        case["ground"] = tuple(options.ground)

    # KSP-Util-MCPServer loads a save from the space centre or in flight only, as a player does: from the main
    # menu, the game is opened at the space centre first.
    if call("get_state")["scene"] == "MAINMENU":
        call("open_game", folder=options.folder)
    rows = []
    for n in range(1, options.trials + 1):
        row = trial(n, options.case, case, options, out)
        rows.append(row)
        log(json.dumps(row))
    with open(os.path.join(out, "lines-%s-%s.json" % (options.case, options.label)), "w", newline="") as f:
        json.dump(rows, f, indent=1)
    log("done")
    if not options.keep_running:
        call("quit_game")


if __name__ == "__main__":
    main()
