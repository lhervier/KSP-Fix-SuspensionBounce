"""Plays "A landing leg swung against a fuel tank" of the README, several times in a row.

It drives KSP through KSP-Util-MCPServer, a mod that answers HTTP requests on 127.0.0.1, and needs nothing but
Python 3: no AI, no package to install. Copy diag/SuspensionBounceCulprit3.sfs into a sandbox game, start KSP with
KSP-Util-MCPServer installed (and this mod, or not, to compare), wait for the main menu, then run:

    python run-tank-graze.py --folder <your sandbox game> --trials 10 --out out

For each trial it does what the protocol asks a player to do, in the same order: load the save, undock the two
docking ports with the Undock button of the one whose window offers it, switch to SuspensionBounceCulprit3 Probe,
and set the target angle of its F-12 servo to 179 in the servo's window. It then reads the surface speed of the
active vessel, as the navball shows it, for --seconds seconds, counts its parts at the end, and takes a
screenshot. The vessel is not thrown every time without this mod: play several trials. It never saves the game.
It prints one line per trial, writes them to lines.json, and quits KSP (unless --keep-running is given).
"""
import argparse
import json
import os
import sys
import time
import urllib.request

URL = None
PROBE = "SuspensionBounceCulprit3 Probe"


def call(name, **args):
    """Calls one tool of KSP-Util-MCPServer and returns its answer, decoded from JSON when it is JSON. Right after a
    save is loaded, the game can still say it is not in flight for a moment: the call is then tried again, for up to
    ten seconds."""
    body = json.dumps({"jsonrpc": "2.0", "id": 1, "method": "tools/call",
                       "params": {"name": name, "arguments": args}}).encode()
    for attempt in range(20):
        request = urllib.request.Request(URL, body, {"Content-Type": "application/json"})
        with urllib.request.urlopen(request, timeout=900) as response:
            result = json.loads(response.read())["result"]
        text = result["content"][0].get("text", "") if result["content"] else ""
        if not (result.get("isError") and text.startswith("Not in flight")):
            break
        time.sleep(0.5)
    if result.get("isError"):
        raise RuntimeError(name + ": " + text)
    try:
        return json.loads(text)
    except ValueError:
        return text


def log(*parts):
    print(time.strftime("%H:%M:%S"), *parts, flush=True)


def undock():
    """Undocks the two vessels with the Undock button of a port. They dock again as the save loads, and the game
    offers to undock on one of the two ports only, once the docking is over: wait for it."""
    for attempt in range(20):
        ports = [p for p in call("list_docking_ports") if p["offersUndock"]]
        if ports:
            # The window may take a moment to show the button the port offers.
            call("part_menu", action="open", part=ports[0]["id"])
            try:
                call("press_part_button", part=ports[0]["id"], button="Undock")
                call("part_menu", action="close")
                return
            except RuntimeError:
                pass
        call("wait", seconds=0.5)
    raise SystemExit("no docking port offers to undock")


def switch():
    """Makes the probe the active vessel, as the ] key does. The game refuses while the vessel it leaves still
    moves from the undocking: try again until it settles."""
    for attempt in range(20):
        if (call("get_state").get("vessel") or {}).get("name") == PROBE:
            return
        try:
            call("switch_vessel", vessel=PROBE)
            return
        except RuntimeError:
            call("wait", seconds=0.5)
    raise SystemExit("the game never let the probe be switched to")


def trial(n, options, out):
    """Plays the protocol once and returns what it read."""
    call("load_save", folder=options.folder, save=options.save)
    undock()
    switch()
    call("set_camera", distance=options.camera[0], heading=options.camera[1], pitch=options.camera[2])
    parts = call("list_parts")
    servo = [p["id"] for p in parts if p["name"] == "rotoServo.02"]
    if len(servo) != 1:
        raise SystemExit("%s has %d F-12 servos" % (PROBE, len(servo)))
    call("wait", seconds=1)
    call("part_menu", action="open", part=servo[0])
    call("set_part_menu_value", part=servo[0], field="targetAngle", value=options.angle)
    call("part_menu", action="close")
    started = time.time()
    top = 0.0
    while time.time() - started < options.seconds:
        state = call("get_state")
        vessel = state.get("vessel") or {}
        top = max(top, vessel.get("surfaceSpeed") or 0.0)
    call("screenshot", path=os.path.join(out, "trial%02d.png" % n), return_image=False)
    after = len(call("list_parts", vessel=PROBE)) if any(
        v["name"] == PROBE for v in call("list_vessels")) else 0
    return dict(trial=n, max_speed=top, parts_before=len(parts), parts_after=after)


def main():
    global URL
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--folder", required=True, help="the sandbox game under saves/ the save was copied into")
    parser.add_argument("--save", default="SuspensionBounceCulprit3", help="the save, without .sfs")
    parser.add_argument("--trials", type=int, default=10, help="how many trials, the save loaded before each")
    parser.add_argument("--angle", type=float, default=179,
                        help="the target angle given to the servo (the slider counts its steps of 1 from -179.99: 179 gives "
                        "179.01, but 180 or 179.9 give 180.01, taken for -179.99, and the servo turns the other way)")
    parser.add_argument("--seconds", type=float, default=8, help="how long to read after the servo starts")
    parser.add_argument("--camera", type=float, nargs=3, default=[8, 0, 35],
                        metavar=("DISTANCE", "HEADING", "PITCH"),
                        help="where to put the camera, in metres from the vessel and degrees")
    parser.add_argument("--out", default="out", help="where lines.json and the screenshots go")
    parser.add_argument("--port", type=int, default=8770, help="the port of KSP-Util-MCPServer")
    parser.add_argument("--keep-running", action="store_true", help="leave KSP running at the end")
    options = parser.parse_args()
    sys.stdout.reconfigure(encoding="utf-8")
    URL = "http://127.0.0.1:%d/mcp/" % options.port
    out = os.path.abspath(options.out)
    os.makedirs(out, exist_ok=True)

    # KSP-Util-MCPServer loads a save from the space centre or in flight only, as a player does: from the main
    # menu, the game is opened at the space centre first.
    if call("get_state")["scene"] == "MAINMENU":
        call("open_game", folder=options.folder)
    rows = []
    for n in range(1, options.trials + 1):
        row = trial(n, options, out)
        rows.append(row)
        broken = row["parts_after"] < row["parts_before"]
        log("trial %d  top speed %5.2f m/s  parts %d -> %d%s" % (
            n, row["max_speed"], row["parts_before"], row["parts_after"], "  BROKEN" if broken else ""))
    with open(os.path.join(out, "lines.json"), "w", newline="") as f:
        json.dump(rows, f, indent=1)
    log("done")
    if not options.keep_running:
        call("quit_game")


if __name__ == "__main__":
    main()
