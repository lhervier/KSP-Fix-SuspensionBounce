# A rover on its end-stops

Part of [Suspension Bounce Fix](../../README.md), [End-Stop Slide](../README.md#checking-the-culprit):
[a bottomed wheel hides its weight from the tyres](../a-bottomed-wheel-hides-its-weight-from-the-tyres.md).

[KSP Diag - Suspension](https://github.com/lhervier/KSP-Diag-Suspension) shows, among others, the share of the
vessel's weight its suspensions carry, as the tyres read it: this mod holds the end-stops itself, and adds the force it
holds to it.

## The saves

Copy them into the folder of a sandbox game, under `saves`, and load them from the quickload menu (Alt+F9 by
default).

- [saves/SuspensionBounceEndStopsFlat.sfs](../saves/SuspensionBounceEndStopsFlat.sfs): `SuspensionBounce Rover`,
  2.4 t on four TR-2L wheels, on flat ground near the space centre, its springs set by hand on the game's sliders at
  their lowest (0.05): all four wheels rest on their end-stops.
- [saves/SuspensionBounceEndStopsSlope.sfs](../saves/SuspensionBounceEndStopsSlope.sfs): the same rover, brakes on,
  on a slope of about 20° west of the space centre, its springs set by hand on the game's sliders at 0.3.

This mod hides the game's sliders, and sets the suspensions itself as the save loads
([Suspensions never set](../../fixed-tuning/README.md#suspensions-never-set)): with it, the springs are made soft
again from its suspension window, once the save is loaded. Open the window from the toolbar, tick all four wheels,
keep Kerbin as the body, type a mass of 0.01 t, share the weight by the centre of mass, and press *Auto*: the springs
are predicted for a rover of 10 kg, and the wheels sink onto their end-stops.

## By hand

- **On flat ground**: drive the rover at full speed, about 24 m/s, in a straight line. Without this mod, it slides
  sideways as it goes, as on ice: on the navball, the speed marker drifts away from the heading. With this mod, it
  holds its line. With Fixed Tuning alone, it holds its line too, though its tyres read only a third of its weight.
- **On the slope**: load the save and wait. Without this mod, and with Fixed Tuning alone, the rover slides down,
  brakes on. With this mod, it holds.

## By script

[diag/automation/check.py](../../diag/automation/check.py) loads the save through
[KSP-Util-MCPServer](https://github.com/lhervier/KSP-Util-MCPServer), sets the springs as above with `--tune auto
--tune-mass 0.01`, and records KSP Diag - Suspension: during a drive of 800 m at 24 m/s on flat ground, for 30 s on
the slope. Its first lines say how to run it:

    python check.py end-stops-flat --folder <your sandbox game> --label stock
    python check.py end-stops-slope --folder <your sandbox game> --label stock
    python check.py end-stops-flat --folder <your sandbox game> --trials 3 --tune auto --tune-mass 0.01 --label modded
    python check.py end-stops-slope --folder <your sandbox game> --trials 3 --tune auto --tune-mass 0.01 --label modded

Played once without this mod, KSP 1.12.5 and KSP Community Fixes, the springs as saved. With this mod, three times in
each configuration, the springs set by *Auto* for 0.01 t: with its default settings; with Fixed Tuning alone,
`damperBounce = false` and `endStopSlide = false` in the mod's `settings.cfg`; and, on the slope, with Fixed Tuning
and this fix, `damperBounce = false`. The rover settles on its own as it loads, but once (see below).

| | Without this mod | Fixed Tuning alone | This mod |
|---|---|---|---|
| **Flat ground, above 20 m/s**: share of the weight the tyres read (5th percentile, median, 95th) | -1 %, 0 %, 1 % | 37 %, 37 %, 37 % | 97 %, 99 %, 100 % |
| Speed across the heading, 95th percentile | 0.15 m/s | 0.02 to 0.03 m/s | 0.03 to 0.04 m/s |
| Angle between the speed and the heading, 95th percentile | 0.36° | 0.06° to 0.07° | 0.08° to 0.09° |
| Wheel readings where the tyre skids | 64 % | 7 % | 2 % |

On flat ground, Fixed Tuning alone already keeps the rover in a straight line: its soft springs still carry a third of
its weight. This fix gives the tyres the whole weight back, and they skid four times less.

| | Without this mod | Fixed Tuning alone | Fixed Tuning and this fix | This mod |
|---|---|---|---|---|
| **Slope**: share of the weight the tyres read (median) | 31 % | 39 to 40 % | 99 % | 99 % |
| Top speed over the ground | — | 0.14, 1.2 and 0.14 m/s | 0.46 to 0.71 m/s | 0.67 to 0.72 m/s |
| How far it turned in 30 s | — | −2.6° to 0.9° | 3.2° to 7.8° | 5.0° to 5.6° |
| Speed over the last 10 s | up to 1.1 m/s | up to 39, 882 and 39 mm/s | up to 0.5 mm/s | up to 0.75 mm/s |

On the slope, this fix is what holds the rover. With it on, the rover lurches at the start, at up to 0.7 m/s, and
turns by a few degrees before it holds; it does not with Fixed Tuning alone. The trial at 882 mm/s is the one where
Parking Brake held the rover as it loaded, and the script let it go after 15 s.
