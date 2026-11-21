"""Plays one of the checked cases of the fixes (the checked folder of each fix's folder in the repository), several
times in a row, and sums up what KSP Diag - Suspension read.

It drives KSP through KSP-Util-MCPServer, a mod that answers HTTP requests on 127.0.0.1, and needs nothing but
Python 3: no AI, no package to install. Copy the case's save (or craft) into a sandbox game, start KSP with
KSP-Util-MCPServer and KSP Diag - Suspension installed (and this mod, or not, to compare), wait for the main menu,
then run, for instance:

    python check.py light-rover-eve --folder <your sandbox game> --ksp <your KSP folder> --label stock

Cases, and the save or craft each one loads, in the saves folder of the fix whose checked cases use it
(fixed-tuning/saves, damper-bounce/saves, end-stop-slide/saves or vessel-bodies/saves of the repository):

    probe            fixed-tuning: SuspensionBounce.craft, launched on the runway (copy it into Ships/SPH of the
                     game)
    leg-kerbin       damper-bounce: SuspensionBounceLegKerbin: an engineer places a landing leg alone, foot down, on
                     Kerbin
    lander           damper-bounce: SuspensionBounceLegKerbin: the lander next to the engineer, made the active vessel
    lander-drop      end-stop-slide: SuspensionBounceLegKerbinSuspended.craft: a lander on four LT-1 legs, launched on
                     the runway hanging from three launch clamps, let go from 4.6 m (copy it into Ships/SPH of the game)
    lander4          SuspensionBounceLegKerbin.craft, not in the repository, launched on the runway: the lander on its
                     four bottom legs only, with a probe core, so that the gear button folds and deploys them (copy it
                     into Ships/SPH)
    light-rover-eve  fixed-tuning: SuspensionBounceLightRoverEve: a rover of a quarter of a ton, loaded on Eve
    heavy-rover-eve  damper-bounce: SuspensionBounceHeavyRoverEve: a rover of 14 t on six TR-2L wheels, three tanks of
                     ore on board, loaded on Eve
    bulldozer-eve    damper-bounce: SuspensionBounceBulldozerEve: the same with a blade of 3.5 t in place of one of the
                     ore tanks, 14 t in all (the blade needs KSP-Mod-Bulldozer)
    hinge-rover-eve  damper-bounce: SuspensionBounceHingeRoverEve: a rover on Eve lowered on its wheels by its hinges
    hinge-rover-eve-manual  damper-bounce: SuspensionBounceHingeRoverEveManual: the same, its springs set by hand
    crane-mun        vessel-bodies: SuspensionBounceCraneMun: a crane of 33 t on the Mun, brakes on, its springs set by
                     hand
    crane-mun-auto   vessel-bodies: SuspensionBounceCraneMunAuto: the same, its springs left to the game
    end-stops-flat   end-stop-slide: SuspensionBounceEndStopsFlat: a rover on soft springs, resting on its end-stops,
                     driven at 24 m/s
    end-stops-slope  end-stop-slide: SuspensionBounceEndStopsSlope (SuspensionBounceEndStopsSlopeStock with Parking
                     Brake installed): the same rover, braked across a slope of about 20 deg
    end-stops-warp   end-stop-slide: the same saves: the same rover on its slope, the time warped on rails for a minute,
                     as a player does near a rover at rest, before it is recorded
    straight-line    end-stop-slide: SuspensionBounceRoverFlat: a rover driven from standstill on flat ground, wheel
                     throttle held, steering, brakes and SAS let go

The soft springs of end-stops-flat, end-stops-slope and lander-drop are in their save or craft, twice: this mod's
setting, the one its suspension window makes (toolbar button Suspensions: Auto for Kerbin, a mass of 0.01 t typed,
shared by the centre of mass, compression 33 %, damping ratio 0.7), which it uses, and the game's own sliders, at
their lowest (0.05; 0.3 on the slope), which the game uses without this mod. The rovers were saved once settled on
their end-stops.

A configuration of this mod is chosen in its settings.cfg (GameData/KSPFixSuspensionBounce/PluginData), read when KSP
starts: fixedTuning, damperBounce and endStopSlide, each true or false (Fixed Tuning alone: damperBounce = false and
endStopSlide = false). Change it with KSP closed, then start KSP again; to play a case without this mod, remove its
folder from GameData.

The saves (but the crafts) were made with Parking Brake on, for the trials without this mod: installed, it may hold the
vessel still as it loads, wherever the game puts it, and let it go once it has nothing left to cancel, brakes kept as
saved; but it lets go of a vessel that leaves the ground as it loads, which then settles on its own. The rover on its
slope has two saves: SuspensionBounceEndStopsSlopeStock, made with Parking Brake on, which the script loads when
Parking Brake is installed, and SuspensionBounceEndStopsSlope, made without it, for the trials with this mod: the rover
saved under Parking Brake sinks past its end-stops, and is thrown off them as it loads when nothing holds it.
Install Parking Brake only to play without this mod, or with fixedTuning = false: it sets the vessel's speed back to
zero at every physics step, and under Fixed Tuning's springs that makes the vessel rock a little harder at each step,
as it loads, until it leaves the ground. The script refuses to start when Parking Brake is installed with Fixed Tuning
on.

For each trial it loads the save (or launches the craft), lets the parking brake go if it holds the vessel, does what
the case asks a player to do, records KSP Diag - Suspension (its button Record CSV) for --seconds seconds or for the
whole drive, and takes a screenshot at the end. --brakes and --friction are set beforehand on each wheel's window, as
the player does: its Brakes slider; its friction button (automatic friction off) and Friction Control slider. It then
prints one line per trial, read from the recording, one line per wheel at each physics step: the speed over the ground
(speed_mm_s), how many wheels touch the ground and are bottomed out, on their end-stops (grounded, bottomed), the share
of the weight the suspensions carry (supported_pct), how far the vessel turned (yaw_rate_deg_s summed), and, while it
drives, its speed across its heading (lateral_m_s), the angle between its speed and its heading (drift_deg) and the
share of wheel readings where the tyre skids (regime); with --ksp, the lines KSP.log gained ("damaged wheel",
"Exploded"); and the vessel's parts before and after.
The lone leg is not the active vessel, which KSP Diag - Suspension reads: for it, only the log lines and a
screenshot every 4 s. The dropped lander hangs from its clamps for 2 s, then is let go by the next stage, KSP Diag -
Suspension recording from just before: from that recording come how fast it touched the ground (vertical_m_s, the
game's vertical speed, as the gauge shows it, at the first step with a leg on the ground) and, over the 3 s after,
how deep its legs went (compression, 1 on the end-stop), the share of the steps with a leg on its end-stop
(is_bottomed), how fast it bounced back up and how many steps it spent with no leg on the ground; see fall(). Then it
is recorded at rest, as the other cases. A vessel with legs then folds them by the gear button and deploys them again,
each phase recorded; how far the legs went is read in their windows (their state line and their deploy or retract
button), compared with what the same windows showed before, whatever the game's language. Everything goes to --out:
lines.json, the CSV files, the screenshots. It never saves the game, and quits KSP at the end (unless --keep-running
is given).
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
    "probe": dict(craft="SPH/SuspensionBounce.craft", seconds=20, legs=True),
    "leg-kerbin": dict(save="SuspensionBounceLegKerbin", seconds=20, heading=17.19, ground=(530, 450)),
    "lander": dict(save="SuspensionBounceLegKerbin", seconds=30, vessel="SuspensionBounce Lander", legs=True),
    "lander-drop": dict(craft="SPH/SuspensionBounceLegKerbinSuspended.craft", seconds=8, drop=True),
    "lander4": dict(craft="SPH/SuspensionBounceLegKerbin.craft", seconds=30, legs=True),
    "light-rover-eve": dict(save="SuspensionBounceLightRoverEve", seconds=30),
    "heavy-rover-eve": dict(save="SuspensionBounceHeavyRoverEve", seconds=30),
    "bulldozer-eve": dict(save="SuspensionBounceBulldozerEve", seconds=30),
    "hinge-rover-eve": dict(save="SuspensionBounceHingeRoverEve", seconds=45, hinges=True),
    "hinge-rover-eve-manual": dict(save="SuspensionBounceHingeRoverEveManual", seconds=45, hinges=True),
    "crane-mun": dict(save="SuspensionBounceCraneMun", seconds=60, legs=True),
    "crane-mun-auto": dict(save="SuspensionBounceCraneMunAuto", seconds=60, legs=True),
    "end-stops-flat": dict(save="SuspensionBounceEndStopsFlat", drive=dict(heading=201, speed=24, distance=800)),
    "end-stops-slope": dict(save="SuspensionBounceEndStopsSlope", stock_save="SuspensionBounceEndStopsSlopeStock",
                            seconds=30),
    "end-stops-warp": dict(save="SuspensionBounceEndStopsSlope", stock_save="SuspensionBounceEndStopsSlopeStock",
                           seconds=30, warp=60),
    "straight-line": dict(save="SuspensionBounceRoverFlat", seconds=30, coast=0.3),
}

# The parts whose legs or gear deploy, by name.
DEPLOYABLE = ("landingLeg1", "landingLeg1-2", "miniLandingLeg", "GearSmall", "GearMedium", "GearLarge", "SmallGearBay",
              "roverWheelM1-F")
# How long each phase of the legs lasts: folded, then deployed again.
LEG_SECONDS = 12
# How long the dropped lander is followed from the moment its legs touch the ground, in seconds; and how long it is
# recorded from the moment it is let go: its fall of 4.6 m, a second at most, and those seconds after the touch.
TOUCH_SECONDS = 3
DROP_SECONDS = 4.5

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


def when_unlocked(tool, **args):
    """Calls a tool that works a part's window, again every half second for 20 s at most while the game ignores the
    windows' controls: just after a save is loaded, it locks them while the physics eases in."""
    for attempt in range(40):
        try:
            return call(tool, **args)
        except RuntimeError as e:
            if attempt == 39 or "now: input locked by" not in str(e):
                raise
            call("wait", seconds=0.5)


def set_brakes(percent):
    """Sets the brake force of every wheel of the active vessel, as its slider in the wheel's window does (a landing
    leg has none)."""
    for part in call("list_parts"):
        if "wheel" in part and item_named(open_menu(part["id"]), "brakeTweakable"):
            when_unlocked("set_part_menu_value", part=part["id"], field="brakeTweakable", value=percent)
    call("part_menu", action="close")


def set_friction(value):
    """Sets the friction of every wheel of the active vessel by hand, from its window: the friction button turns
    automatic friction off (the window then shows the friction slider, which it hides while automatic friction is on),
    and the slider is set to value; the game applies it again at every step."""
    for part in call("list_parts"):
        if "wheel" not in part:
            continue
        items = open_menu(part["id"])
        if not item_named(items, "EvtAutoFrictionToggle"):
            continue  # a wheel whose friction the game does not let the player set
        if not item_named(items, "frictionMultiplier"):
            when_unlocked("press_part_button", part=part["id"], button="EvtAutoFrictionToggle")
            # The window shows the slider from its next update.
            for attempt in range(8):
                items = next((m["items"] for m in call("read_part_menus") if m["part"] == part["id"]), [])
                if item_named(items, "frictionMultiplier"):
                    break
                call("wait", seconds=0.25)
        when_unlocked("set_part_menu_value", part=part["id"], field="frictionMultiplier", value=value)
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


def pin_menus(parts):
    """Opens and pins the window of each part, so that one reading shows them all."""
    for part in parts:
        open_menu(part)
        call("part_menu", action="pin", part=part)


def unpin_menus(parts):
    """Unpins the windows of the parts and closes them."""
    for part in parts:
        try:
            call("part_menu", action="unpin", part=part)
        except RuntimeError:
            pass  # a window the game closed meanwhile
    call("part_menu", action="close")


def leg_states(legs):
    """What the pinned window of each leg shows of its deployment, as read_part_menus reads it, by leg: the line of
    its state (field stateDisplayString) and the label of its deploy or retract button (event EventToggle). Both are
    in the game's language: they are only compared with what the same window showed before. The button reads the same
    while the leg moves and once it is retracted, and changes once it is fully deployed."""
    menus = {m["part"]: m["items"] for m in call("read_part_menus")}
    states = {}
    for leg in legs:
        items = menus.get(leg, [])
        state, button = item_named(items, "stateDisplayString"), item_named(items, "EventToggle")
        states[leg] = (state and state.get("text"), button and button.get("label"))
    return states


def focus_ksp():
    """Gives the focus to the window of KSP, which the gear button needs, by kspwindow.py of KSP-Util-MCPServer's
    bridge when it sits next to this repository (kspmod); prints why when it could not."""
    bridge = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..", "KSP-Util-MCPServer", "bridge")
    if bridge not in sys.path:
        sys.path.append(bridge)
    try:
        import kspwindow
    except ImportError:
        log("no kspwindow.py next to this repository: give the focus to KSP by hand")
        return
    log(kspwindow.focus_window())


def gear_pressed(legs, down, before):
    """Presses the landing gear button by the altimeter, as the player does: down sends the legs out, up folds them.
    Returns the legs' states once one of them moved, None when none did within 2 s: the legs were already that way."""
    call("set_controls", gear=down)
    for attempt in range(8):
        states = leg_states(legs)
        if any(states[leg] != before[leg] for leg in legs):
            return states
        call("wait", seconds=0.25)
    return None


def toggle_legs():
    """Folds the legs of the active vessel if they are out, deploys them otherwise, by the landing gear button by the
    altimeter, as the player does, and waits until they are all at the end of their travel (30 s at most), as the
    window of each tells. Returns the state they were sent to, Retracted or Deployed, or why they did not move: the
    button does nothing on a vessel that cannot be controlled."""
    legs = [p["id"] for p in call("list_parts") if p["name"] in DEPLOYABLE]
    if not legs:
        return None
    pin_menus(legs)
    try:
        before = leg_states(legs)
        focus_ksp()
        # The button sends the gear group the other way from where the game holds it. A group already that way (legs
        # moved by hand) moves nothing: the button pressed again, as a player would, sends them the other way.
        down = not call("get_state")["vessel"]["gear"]
        states = gear_pressed(legs, down, before)
        if states is None:
            down = not down
            states = gear_pressed(legs, down, before)
        if states is None:
            return "no effect (vessel not controllable?)"
        target = "Deployed" if down else "Retracted"
        # Each leg's state line changes twice, to the move, then to its end; once fully out, its button changes too.
        lines = {leg: [before[leg][0]] for leg in legs}
        for attempt in range(120):
            for leg in legs:
                if states[leg][0] != lines[leg][-1]:
                    lines[leg].append(states[leg][0])
            if all(len(lines[leg]) >= 3 or (down and states[leg][1] != before[leg][1]) for leg in legs):
                return target
            call("wait", seconds=0.25)
            states = leg_states(legs)
        return target + " (not all after 30 s)"
    finally:
        unpin_menus(legs)


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


def drop(seconds=DROP_SECONDS):
    """Lets the active vessel go off its launch clamps (its next stage), KSP Diag - Suspension recording from just
    before until the given seconds after, and returns the CSV's path."""
    call("suspension_record_start")
    try:
        call("stage")
        call("wait", seconds=seconds)
    finally:
        path = call("suspension_record_stop")["returned"]["csv"]
    return path


def is_set(text):
    return text in ("1", "True", "true")


def fall(path, seconds=TOUCH_SECONDS):
    """Reads the fall and the touch from a recording of drop(), one entry per physics step: how fast the vessel came
    down at the touch (the game's vertical speed at the start of the first step with a leg on the ground: the speed
    the step before left it with, the step in which the leg met the ground), and, over the given seconds from the
    touch on, the deepest a leg went (its compression, 1 on its end-stop), the share of the steps with a leg on its
    end-stop (bottomed), how fast the vessel went back up at most, and how many steps it spent with no leg on the
    ground, out of the steps followed (steps_after_touch: 150 at 50 steps a second)."""
    rows = steps(path)
    touch = next((k for k, r in enumerate(rows) if any(is_set(w["is_grounded"]) for w in r)), None)
    if touch is None:
        return dict(touched=False)
    start = float(rows[touch][0]["time_s"])
    after = [r for r in rows[touch:] if float(r[0]["time_s"]) - start < seconds]
    vertical = [number(r[0]["vertical_m_s"]) for r in after]
    return dict(
        touch_speed_m_s=round(-number(rows[touch][0]["vertical_m_s"]), 2),
        peak_compression=round(max(number(w["compression"]) or 0 for r in after for w in r), 3),
        share_on_end_stop=round(sum(1 for r in after if any(is_set(w["is_bottomed"]) for w in r)) / len(after), 3),
        rebound_m_s=round(max(v for v in vertical if v is not None), 2),
        steps_in_air=sum(1 for r in after if not any(is_set(w["is_grounded"]) for w in r)),
        steps_after_touch=len(after))


def trial(n, name, case, options, out):
    """Plays the case once and returns what it read."""
    start = log_size(options.ksp) if options.ksp else 0
    if "craft" in case:
        if call("get_state")["scene"] != "SPACECENTER":
            call("go_to_scene", scene="SPACECENTER")
        call("launch_vessel", craft=case["craft"])
    else:
        # A case with a save of its own for Parking Brake, which only the trials without this mod use.
        stock = "stock_save" in case and parking_brake_installed()
        call("load_save", folder=options.folder, save=case["stock_save"] if stock else case["save"])
    row = dict(trial=n)
    if "vessel" in case:
        switch(case["vessel"])
    # Brakes and friction set while Parking Brake still holds the vessel, before it lets it go.
    if options.brakes is not None:
        set_brakes(options.brakes)
    if options.friction is not None:
        set_friction(options.friction)
    brake_part, held = parking_brake() if parking_brake_installed() else (None, False)
    if held:
        row["parked_still"] = release_parking_brake(brake_part)
    if case.get("hinges"):
        lower_hinges()
    if "warp" in case:
        # The vessel left to settle, then the time warped on rails, as a player does near a vessel at rest: the
        # recording starts once the game is back to normal time.
        call("wait", seconds=3)
        call("warp_to", seconds=case["warp"])
    if "coast" in case:
        # Steering let go, and SAS off: nothing steers the rover but its own wheels.
        call("set_flight", sas=False)
        call("set_controls", wheel_throttle=case["coast"], brakes=False)
    if "ground" in case:
        place_leg(case)
        # The leg is not the active vessel: a screenshot every few seconds shows where it went.
        for k in range(int(case["seconds"] // 4)):
            call("wait", seconds=4)
            call("screenshot", path=os.path.join(out, "%s-%s-%02d-%02ds.png" % (name, options.label, n, 4 * (k + 1))),
                 return_image=False)
    else:
        if case.get("drive"):
            call("wait", seconds=3)
        parts = len(call("list_parts"))
        if case.get("drop"):
            # Hanging still from its clamps first; then the fall and the touch, recorded at every physics step; then
            # the recording below, the vessel at rest.
            call("wait", seconds=2)
            kept = os.path.join(out, "%s-%s-%02d-drop.csv" % (name, options.label, n))
            shutil.copyfile(drop(), kept)
            row.update(fall(kept))
            # Its clamps are vessels of their own from then on.
            parts = len(call("list_parts"))
        path = record(seconds=case.get("seconds"), drive=case.get("drive"))
        kept = os.path.join(out, "%s-%s-%02d.csv" % (name, options.label, n))
        shutil.copyfile(path, kept)
        row.update(summary(kept, moving_from=20 if case.get("drive") else None))
        row["parts"] = [parts, len(call("list_parts"))]
        if "coast" in case:
            call("set_controls")
        if case.get("legs") and options.legs:
            # Folded onto the belly (or out, for a vessel whose legs start folded), then back as they were.
            for phase in ("fold", "back"):
                # Recorded from the gesture on, the legs' travel included.
                call("suspension_record_start")
                try:
                    row[phase + "_target"] = toggle_legs()
                    call("wait", seconds=options.fold_seconds if phase == "fold" else LEG_SECONDS)
                finally:
                    path = call("suspension_record_stop")["returned"]["csv"]
                kept = os.path.join(out, "%s-%s-%02d-%s.csv" % (name, options.label, n, phase))
                shutil.copyfile(path, kept)
                row[phase] = summary(kept)
                call("screenshot", path=os.path.join(out, "%s-%s-%02d-%s.png" % (name, options.label, n, phase)),
                     return_image=False)
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
    parser.add_argument("--friction", type=float,
                        help="the friction of every wheel, set by hand (automatic friction off), 0.01 to 10 "
                        "(default: the save's)")
    parser.add_argument("--label", default="run", help="a word for the files of this run: stock, modded...")
    parser.add_argument("--ksp", help="the KSP folder, to count the lines KSP.log gains")
    parser.add_argument("--out", default="out", help="where lines.json, the CSV files and the screenshots go")
    parser.add_argument("--port", type=int, default=8770, help="the port of KSP-Util-MCPServer")
    parser.add_argument("--keep-running", action="store_true", help="leave KSP running at the end")
    parser.add_argument("--legs", action=argparse.BooleanOptionalAction, default=True,
                        help="for a vessel with landing legs, fold them after the recording, then deploy them again, "
                        "each phase recorded for %d s (default: on)" % LEG_SECONDS)
    parser.add_argument("--fold-seconds", type=float, default=LEG_SECONDS,
                        help="how long the legs stay folded before they are deployed again (default %d)" % LEG_SECONDS)
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
    if parking_brake_installed() and fixed_tuning_active():
        sys.exit("Parking Brake is installed with Fixed Tuning on: remove it from GameData (see the first lines of "
                 "this script)")

    # KSP-Util-MCPServer loads a save from the space centre or in flight only, as a player does: from the main
    # menu, the game is opened at the space centre first.
    if call("get_state")["scene"] == "MAINMENU":
        call("open_game", folder=options.folder)
    # The recording goes on with the window hidden; hidden, it leaves the scene to whoever watches.
    call("suspension_show_window", visible=False)
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
