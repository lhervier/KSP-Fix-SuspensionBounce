# Performance

Part of [Suspension Bounce Fix](../README.md): what the mod costs, measured against the game's own wheels.

The mod works at every physics step, on every wheel of every vessel in flight: it sizes each damper, reads each
wheel's contact, and, while a wheel nears the end of its travel, holds the end-stops of its vessel. **On a rover driving
on flat ground, it adds about 0.02 ms per physics step, which no frame shows.** The dearest case found is a crane of
112 parts resting on its end-stops: **about 0.14 ms more per physics step, or about 0.05 ms per frame** (269 frames
per second instead of 273 in the runs below).

Those figures are those of the campaign below. The mod has since read each vessel once per physics step for all its
fixes, and kept from step to step what does not change: the crane on its end-stops costs 0.02 to 0.03 ms less per
physics step, a rover a few microseconds less ([Keeping the cost low](#keeping-the-cost-low)).

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

Two configurations, the mod installed with its measure in both: the fixes on, and the three fixes off. The mod is the
version before the shared read of [Keeping the cost low](#keeping-the-cost-low). Three runs each, taken in turn. The first two were played with Windows' lock screen up for most of their measures, KSP's window
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
- **The dampers**, in this version, read the mass, centre of mass and inertia of every part of the vessel at every
  step, wheels on the ground or not: about half a microsecond per part, 50 to 65 µs on the crane, 10 µs on the rover.
  It is the steady part of the cost, the part a vessel at rest pays; [Keeping the cost low](#keeping-the-cost-low)
  says what was done about it.
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
  vessel at every step. It was made cheaper since: [Keeping the cost low](#keeping-the-cost-low).

## Keeping the cost low

Two of the fixes take the vessel as one rigid body: the dampers, to know the mass the vessel sets in motion at each
wheel ([first culprit](the-culprits/a-damper-too-strong-for-a-light-vessel.md#in-the-mods-code)), and the end-stops,
to hold the wheels at the end of their travel
([third culprit](the-culprits/a-bottomed-wheel-hides-its-weight-from-the-tyres.md#in-the-mods-code)). Both need every
rigidbody of the vessel: a vessel at rest pays for it at every physics step, and a crane has a hundred of them. Each
call into Unity to read a rigidbody costs, so the mod makes as few as it can.

### What is read, and when

All in `VesselBodies` ([Src/VesselBodies.cs](../Src/VesselBodies.cs)):

- **Once per vessel and per physics step, for both fixes**: whichever asks first reads the vessel, the other uses that
  read. A shift of the floating origin within the step, which moves every part, has the vessel read again.
- **At every step**: each rigidbody's centre of mass, and its rotation, read from the part's transform:
  `Rigidbody.rotation` would first sync the transforms of the whole scene, the most expensive read there is.
- **Kept from step to step**: each rigidbody's mass, and its own inertia (its principal moments and their axes in the
  part). They only change with the part's resources and the vessel's structure. KSP writes the mass on the rigidbody
  at each step, in `FlightIntegrator.UpdateMassStats`, from fields of the part (`Part.physicsMass`, the part's
  `MinimumRBMass`, half of it on each body of a robotic part): the mod works out the same value from those fields,
  which costs no call into Unity, and reads the rigidbody again as soon as the kept mass no longer matches. Fuel that
  burns is seen at once, and so is the first step after a vessel is unpacked, when its rigidbodies still have a mass
  that is not theirs. A rigidbody read again for that reason, or new in the vessel, is read once more at the next
  step: Unity scales its inertia to a new mass only once the physics has run. Every 50 steps, every rigidbody is read
  again in any case.
- **Only when the end-stops need them**: the velocities, at the steps a wheel can reach its end-stop
  (`VesselBodies.ReadMotion`).

The sums over the parts are written out in full, component by component: the axes of each part's inertia are drawn
from its rotation without a call per axis, to the same values to the last bit (`VesselBodies.Axes`).

The results are those of the version that read everything at every step, to the rounding of a float: a part's
rotation read from its transform differs from the rigidbody's in the last bits, about one part in ten million of the
inertia. The one difference beyond that is a part whose mass has just changed: its mass is seen at the same step as
before, its inertia one step later.

### What it saves

The same five scenarios as above, played by [`run-cost.py`](../diag/automation/run-cost.py) with the mod's own
measure. `GameData` holding Harmony, ModuleManager, KSP Community Fixes 1.41.1, KSP-Util-MCPServer,
[Terrain Precision Fix](https://github.com/lhervier/KSP-Fix-TerrainPrecision),
[KSP-Fix-AutoStrut](https://github.com/lhervier/KSP-Fix-AutoStrut),
[Parking Brake](https://github.com/jarosm/KSP-ParkingBrake) 0.4.4 (the saves load with it on, and the script lets it
go), and this mod, in three configurations: the version before the shared read, the version with it, and the version
with it and the three fixes off. The same desktop and display settings as [the campaign](#the-campaign), KSP full
screen, its window in front at every check. Three runs per configuration, a fresh KSP each, taken in turn; the data are
in [`diag/perfs/shared-read/`](../diag/perfs/shared-read/).

Microseconds per physics step, range over the three runs of each run's mean:

| Scenario | Wheels and mod, before | Wheels and mod, with the shared read | Wheels and mod, fixes off | The mod's own time, before → with | Dampers and springs, before → with |
|---|---|---|---|---|---|
| A crane at rest | 292 – 319 | 285 – 308 | 227 – 241 | 67 – 70 → 55 – 60 | 46 – 49 → 36 – 39 |
| A crane on its end-stops | 367 – 369 | 339 – 350 | 225 – 230 | 316 – 319 → 289 – 299 | 48 → 19 – 20 |
| A rover driving | 50.3 – 51.0 | 49.1 – 49.6 | 33.8 – 34.3 | 18.4 – 18.6 → 17.1 – 17.2 | 9.7 – 9.8 → 8.5 – 8.7 |
| A rover driving on its end-stops | 69.9 – 70.3 | 65.4 – 66.2 | 30.2 – 30.4 | 38.5 – 39.2 → 34.2 – 34.9 | 9.6 → 6.8 – 7.0 |
| A light rover on Eve | 200 – 202 | 201 – 203 | 92 – 131 | 19.6 – 19.8 → 18.4 – 18.8 | 9.5 → 8.4 – 8.8 |

- **On the crane on its end-stops**, the dampers fall from 48 to 19 µs: the end-stops read the vessel first in the
  step, and the dampers use that read. The solver itself does not change (200 – 203 µs before, 206 – 216 with), as it
  still pays the sync of the scene's transforms (see [What the measure times](#what-the-measure-times)). What the
  fixes add to the game's wheels goes from 137 – 144 µs to 109 – 124 µs per physics step.
- **On the crane at rest**, the mod's own time falls by about 12 µs; the totals overlap, the game's wheels varying by
  more than that from one run to the next.
- **On the rovers**, a few microseconds: they have 19 parts.
- **What is left to the dampers** on the crane: 36 – 39 µs when they read the vessel themselves, at rest, 19 – 20 µs
  when the end-stops have read it for them. The read of a hundred parts is about half of it, their own work for each of
  the ten suspensions the other half.
