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
    crane-mun        SuspensionBounceCraneMun: a crane of 33 t on the Mun, brakes on, its springs set by hand,
                     held by Parking Brake (which must be installed) until it is still, then let go
    crane-mun-auto   SuspensionBounceCraneMunAuto: the same, its springs left to the game
    end-stops-flat   SuspensionBounceEndStopsFlat: a rover whose springs are at their lowest, driven at 24 m/s
    end-stops-slope  SuspensionBounceEndStopsSlope: a rover with soft springs, braked across a slope of about 20 deg

For each trial it loads the save, does what the case asks a player to do, records KSP Diag - Suspension (its button
Record CSV) for --seconds seconds or for the whole drive, and takes a screenshot at the end. It then prints one line
per trial from the recording: the speed over the ground, how many wheels touch the ground and are bottomed out, the
share of the weight the suspensions carry, and, with --ksp, the lines KSP.log gained ("damaged wheel", "Exploded").
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
    "crane-mun": dict(save="SuspensionBounceCraneMun", seconds=60, parking=True),
    "crane-mun-auto": dict(save="SuspensionBounceCraneMunAuto", seconds=60, parking=True),
    "end-stops-flat": dict(save="SuspensionBounceEndStopsFlat", drive=dict(heading=201, speed=24, distance=800)),
    "end-stops-slope": dict(save="SuspensionBounceEndStopsSlope", seconds=30),
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


def lower_hinges():
    """Plays the rover's KAL-1000 sequence backwards at full speed: its hinges lower the rover onto its wheels."""
    for method, args in (("SetPlaySpeed", [100]), ("ToggleDirection", []), ("SequencePlay", [])):
        call("call_method", type="ModuleRoboticController", method=method, arguments=args)


def set_brakes(percent):
    """Sets the brake force of every wheel of the active vessel, as its slider in the wheel's window does."""
    for part in call("list_parts"):
        if "wheel" in part:
            try:
                call("set_member", type="ModuleWheelBrakes", match="part.persistentId=%d" % part["id"],
                     member="brakeTweakable", value=percent)
            except RuntimeError:
                pass  # a suspension without brakes: a landing leg


def release_parking_brake():
    """Waits until Parking Brake has nothing left to cancel, then lets the vessel go, as its button in the window of a
    command part does. The save was made with the parking brake on, the vessel at rest: loaded, the vessel is pushed
    out of the ground and set in motion, which the parking brake stops at every physics step."""
    still = 0
    for attempt in range(120):
        state = call("get_member", type="FlightGlobals",
                     members=["ActiveVessel.srfSpeed", "ActiveVessel.angularVelocity"])["values"]
        spin = state["ActiveVessel.angularVelocity"]
        turning = (spin["x"] ** 2 + spin["y"] ** 2 + spin["z"] ** 2) ** 0.5
        still = still + 1 if state["ActiveVessel.srfSpeed"] < 0.01 and turning < 0.01 else 0
        if still >= 4 and attempt >= 6:
            call("call_method", type="ParkingBrake.ParkingBrakeModule", method="ToggleParkingBrake")
            return
        call("wait", seconds=0.5)
    raise RuntimeError("the vessel never came to rest under the parking brake")


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
    if case.get("hinges"):
        lower_hinges()
    if options.brakes is not None:
        set_brakes(options.brakes)
    if case.get("parking"):
        release_parking_brake()
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
