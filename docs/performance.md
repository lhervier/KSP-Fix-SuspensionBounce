# Performance

Part of [Suspension Bounce Fix](../README.md): what the mod costs, measured against the game's own wheels.

The mod works at every physics step, on every wheel of every vessel in flight: it sizes each damper, reads each
wheel's contact, and, while a wheel nears the end of its travel, holds the end-stops of its vessel. **On a rover driving
on flat ground, it adds about 0.02 ms per physics step, which no frame shows.** The dearest case found is a crane of
112 parts resting on its end-stops: **about 0.14 ms more per physics step, or about 0.05 ms per frame** (269 frames
per second instead of 273 in the runs below).

## Two instruments

- **The mod's own measure**, off by default: `measureCost = true` in its `settings.cfg`. Off, it is not installed at
  all, and the mod runs as if it did not exist. On, it times the mod and the game's wheels at every physics step,
  and writes one line to `KSP.log` every 10 s of flight ([What it times](#what-the-measure-times)).
- [KSPProfiler](https://github.com/KSPModdingLibs/KSPProfiler), which times each phase of Unity's game loop in
  every frame. It needs nothing from the mod: it tells what a frame pays, the mod's measure tells where.

The two never run together: the mod's measure adds a little work to the steps the profiler times.

## What the measure times

The mod's patches run at Harmony's normal priority. The measure starts a clock in a patch of higher priority, which
Harmony runs just before them, and stops it in one of lower priority, run just after; the game's wheel methods are
timed the same way, as a whole, from their first patch to their last: `VehicleBase.FixedUpdate`, where Vehicle
Physics Pro drives each wheel, `ModuleWheelSuspension.FixedUpdate` and `ModuleWheelDamage.FixedUpdate`. A line reads:

```
[KSPFixSuspensionBounce] Cost over 500 physics steps (10 s), 10.0 suspensions per step, active vessel 'SuspensionBounce
Crane': wheels and mod 402.8 µs/step, of which the game's wheels 55.3 and the mod 347.5 (dampers and springs 50.0;
end-stops 297.6, of which force records 8.4 and the solver 226.7, run 501 times)
```

- **Wheels and mod**: the figure to compare between runs with and without the fixes. Unity syncs the transforms of
  the whole scene when a body's pose is first read after they moved, and whoever reads first in a physics step pays
  for it: without the mod, Vehicle Physics Pro does; with the mod holding end-stops, its solver does, and the game's
  wheels get cheaper by as much. Taken apart, the two parts would mislead.
- **Dampers and springs**: the first, second and fourth fixes, which size each suspension from the vessel's mass,
  centre of mass and inertia.
- **End-stops**: the third fix. *Force records* is what it notes of the forces KSP hands each part, once per part and
  step (`FlightIntegrator.Integrate`), wheels or not; *the solver* is the part that holds the end-stops, run only at
  the steps a wheel can reach its end-stop.

The measure costs a little itself: with the fixes off, it still reads 4 µs per step on the crane, 1 µs on a rover.

The fixes can be turned off one by one in the same `settings.cfg`, which compares them with the game's own behaviour
in the same KSP, the measure installed: `damperLimit` (first and second culprits), `kerbinCurve` (fourth) and
`bumpStopLoad` (third), each `true` by default. The lines are written at `logLevel` `Info` or `Debug`.

## The scenarios

Each loads a save of [`diag/`](../diag/), copied into a sandbox game, and waits 10 s before measuring.

| Scenario | Save | What is done after the load |
|---|---|---|
| A crane at rest | `SuspensionBounceCraneMunAuto` | Nothing: a crane of 33 t and 112 parts on six TR-2L wheels and four LT-2 legs, on the Mun, brakes on, its springs left to the game. No wheel is near its end-stop. |
| A crane on its end-stops | `SuspensionBounceCraneMunAuto` | 10 s after the load, every TR-2L wheel's *Spring Strength* to 0.5, then 5 s later to its minimum, in each wheel's window (*Spring/Damper* on *Override* first, which needs *Advanced Tweakables*); 30 s more before measuring, instead of 10. The crane sinks onto its end-stops, and the solver runs at every step. |
| A rover driving | `SuspensionBounceRoverFlat` | Wheel throttle held at 30 %, brakes and SAS off: a rover of 19 parts on four wheels, near the Space Center, reaches about 26 m/s. |
| A rover driving on its end-stops | `SuspensionBounceEndStopsFlat` | The same, its springs at their minimum: the solver runs at every step. |
| A light rover on Eve | `SuspensionBounceLightRoverEve` | Nothing. |

A save of the crane already on its end-stops is not used: loading it breaks the crane, a wheel going through the
ground and parts exploding as it settles. Lowered onto them after the load, it holds. The tanker it carries keeps its
four legs on their automatic springs.

## Taking a measurement

By hand:

1. Install the mod with `measureCost = true` in `GameData/KSPFixSuspensionBounce/PluginData/settings.cfg`.
2. Load the scenario's save, do what the table says, wait 10 s, then let the game run 35 s without touching it.
3. Read the lines `KSP.log` gained meanwhile, the first excepted: its 10 s began before the measure.
4. Quit KSP, set `damperLimit`, `kerbinCurve` and `bumpStopLoad` to `false`, start KSP again and play the same
   scenarios. Take the two configurations in turn, several times each: one run against one tells little.

By script: [`diag/automation/run-cost.py`](../diag/automation/run-cost.py) plays the scenarios one after the other in
one KSP, through [KSP-Util-MCPServer](https://github.com/lhervier/KSP-Util-MCPServer), a mod that answers HTTP
requests on 127.0.0.1, and Python 3, nothing else. It asks nothing of the game during a measure: it sleeps. Its first
lines say how to run it. Whether KSP-Util-MCPServer, installed and not called, changes what a frame costs was checked
on [its own page](https://github.com/lhervier/KSP-Util-MCPServer/blob/main/docs/performance.md#installed-and-nobody-calls-it):
nothing measurable.

## The campaign

KSP 1.12.5 with Harmony, ModuleManager, KSP Community Fixes 1.41.1 and KSP-Util-MCPServer, nothing else: no Terrain
Precision Fix, no KSP Diag - Suspension, which do their own work at every step. My desktop, KSP **full screen** at
1280×720 (`FULLSCREEN = True`), vertical sync off (`SYNC_VBL = 0`), no frame limit. Each run is a fresh KSP playing
all the scenarios by script, which checks that KSP's window is in front of every other at the start and at the stop
of each measure.

Two configurations, the mod installed with its measure in both: the fixes on, and the three fixes off. Three runs
each, taken in turn. The first two were played with Windows' lock screen up for most of their measures, KSP's window
not in front, and read about a tenth higher; they are in [`diag/perfs/`](../diag/perfs/) with the others, but not in
the table below, which gives the range of the four other runs, KSP's window in front at every check.

## What a physics step pays

Microseconds per physics step, mean of the 10 s lines of each run (two or three per scenario and run):

| Scenario | Wheels and mod, fixes on | Wheels and mod, fixes off | Difference | The mod's own time, fixes on |
|---|---|---|---|---|
| A crane at rest | 330 – 339 | 247 – 267 | about +80 | 72 – 86: dampers 50 – 65, end-stops 21 (force records 9) |
| A crane on its end-stops | 394 – 403 | 248 – 266 | about +140 | 342 – 348: dampers 49 – 50, end-stops 293 – 298 (solver 223 – 227) |
| A rover driving | 51 – 54 | 34.4 – 34.6 | about +18 | 19: dampers 10, end-stops 9 |
| A rover driving on its end-stops | 70 – 72 | 30.6 – 30.8 | about +40 | 38 – 39: dampers 10, end-stops 29 (solver 14) |
| A light rover on Eve | 207 – 214 | 71 – 118 | — | 20 – 21: dampers 10, end-stops 10 |

- **On the crane on its end-stops**, the game's wheels fall from 244 – 262 µs to 52 – 55 µs when the fixes are on:
  the transform sync moves from Vehicle Physics Pro to the solver, which shows 223 – 227 µs. The solver's own work
  is what is left of the difference.
- **The dampers** read the mass, centre of mass and inertia of every part of the vessel at every step, wheels on the
  ground or not: about half a microsecond per part, 50 to 65 µs on the crane, 10 µs on the rover. It is the steady
  part of the cost, the part a vessel at rest pays.
- **The end-stops**, while no wheel nears its end-stop, read each wheel's contact and note the forces of each part.
  Once one does, they read every body of the vessel too, and run the solver.
- **The light rover on Eve** cannot be compared: without the fixes it never comes to rest, and its wheels do other
  work than at rest (in one run without them, it lost three parts during the measure). Only the mod's own time says
  something there.

## What a frame pays

The same two configurations, timed frame by frame with KSPProfiler, which reports for each phase of the frame the
mean, the median, the worst 25 % and the worst 1 % of frames.

### How the frames were timed

`GameData` holding Harmony, ModuleManager, KSP Community Fixes 1.41.1, KSP-Util-MCPServer, KSPProfiler with the KsmUI
library it ships with, and either nothing more, or this mod with its measure off. The same desktop and display
settings as above. The runs used [a fork of KSPProfiler](https://github.com/lhervier/KSP-ExtMod-KSPProfiler) which
only adds a remote control of its window's buttons through KSP-Util-MCPServer; nothing in what it measures was
changed, and the same can be played by hand with KSPProfiler 1.0.0.

Two scenarios only, the dearest and a common one: the crane on its end-stops, and the rover driving. Each run is a
fresh KSP; for each scenario, the script does what the table above says, opens the profiler's window, waits 10 s,
then *Start capture* (10 000 frames, the most the window accepts), *Stop capture* 20 s later, and *Export to CSV*,
asking nothing of the game in between. A capture is worth keeping when it ended on *Stop*, with fewer than 10 000
frames: all twelve did, with 5 354 to 5 490 frames for the crane and 8 516 to 8 642 for the rover. Three runs per
configuration, taken in turn; the CSV files are in [`diag/perfs/`](../diag/perfs/).

### What they read

| | Frames per second, mean | Frame time, mean | Scripts' `FixedUpdate`, per physics step | Physics, per physics step |
|---|---|---|---|---|
| Crane on its end-stops, without the mod | 271.6, 274.6, 272.1 | 3.68, 3.64, 3.67 ms | 0.93, 0.93, 0.96 ms | 0.53 ms |
| Crane on its end-stops, with the mod | 269.7, 267.7, 270.4 | 3.71, 3.73, 3.70 ms | 1.07, 1.07, 1.04 ms | 0.53 ms |
| Rover driving, without the mod | 425.8, 431.1, 431.7 | 2.35, 2.32, 2.32 ms | 0.21, 0.20, 0.20 ms | 0.19 – 0.20 ms |
| Rover driving, with the mod | 428.3, 431.7, 430.3 | 2.34, 2.32, 2.32 ms | 0.21, 0.22, 0.22 ms | 0.19 – 0.20 ms |

The scripts' `FixedUpdate` is the profiler's *FixedUpdate usercode* row, and *Physics* its row of the same name: the
profiler gives them per physics step (their median is not zero, though most frames have no physics step at these
frame rates). On the crane, the scripts pay 0.08 to 0.14 ms more per physics step, as the mod's own measure says;
PhysX itself pays nothing more. A frame, which holds a physics step about one time in five here, pays about 0.05 ms
more on average (0.02 to 0.09 ms from one run to the next). On the rover, the scripts pay 0.01 to 0.02 ms more per
physics step, and the frames show no difference.

## What this says

- **A rover pays about 0.02 ms per physics step, which no frame shows.**
- **The dearest case found, a crane of 112 parts on its end-stops, pays about 0.14 ms per physics step**: at 50 steps
  per second, 7 ms in each second of game time, and about 0.05 ms per frame.
- The steady part, paid by any vessel with suspensions even at rest, is the dampers' reading of every part of the
  vessel at every step. It is the one to make cheaper, by reading each part once per step for all the fixes, and
  keeping what does not change from step to step.
