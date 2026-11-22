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
  2.4 t on four TR-2L wheels, on flat ground near the space centre, on soft springs: all four wheels rest on their
  end-stops.
- [saves/SuspensionBounceEndStopsSlope.sfs](../saves/SuspensionBounceEndStopsSlope.sfs): the same rover, brakes on,
  on a slope of about 20° west of the space centre, on soft springs, resting on its end-stops; for the trials with
  this mod.
- [saves/SuspensionBounceEndStopsSlopeStock.sfs](../saves/SuspensionBounceEndStopsSlopeStock.sfs): the same, saved
  with [Parking Brake](https://github.com/jarosm/KSP-ParkingBrake) on, for the trials without this mod. Parking Brake
  holds the rover still as it loads; this mod is not compatible with it, and without it the rover of this save is
  thrown off its end-stops as it loads.

Each save holds the soft springs twice. Without this mod, the game uses its own sliders, set by hand at their lowest
(0.05) on flat ground, at 0.3 on the slope. This mod hides those sliders and uses its own setting, saved with the rover:
the one its suspension window makes (open it from the toolbar, tick all four wheels, keep Kerbin as the body, type a
mass of 0.01 t, share the weight by the centre of mass, and press *Auto*: the springs are predicted for a rover of
10 kg, and the wheels sink onto their end-stops). The saves were made with this mod, the rover settled on its
end-stops and still.

## By hand

Load the save (on the slope, the one of the configuration played): nothing to set, with this mod or without it.

- **On flat ground**: drive the rover at full speed, about 24 m/s, in a straight line. Without this mod, it slides
  sideways as it goes, as on ice: on the navball, the speed marker drifts away from the heading. With this mod, it
  holds its line. With Fixed Tuning alone, it holds its line too, though its tyres read only a third of its weight.
- **On the slope**: load the save and wait. Without this mod, and with Fixed Tuning alone, the rover slides down,
  brakes on. With this mod, it holds.

## By script

[diag/automation/check.py](../../diag/automation/check.py) loads the save (on the slope, the one with Parking Brake
when Parking Brake is installed) through
[KSP-Util-MCPServer](https://github.com/lhervier/KSP-Util-MCPServer), the springs as saved, and records KSP Diag -
Suspension: during a drive of 800 m at 24 m/s on flat ground, for 30 s on the slope. Its first lines say how to run it,
and how to choose a configuration of this mod (its `settings.cfg`, KSP started again):

    python check.py end-stops-flat --folder <your sandbox game> --label stock
    python check.py end-stops-slope --folder <your sandbox game> --label stock
    python check.py end-stops-flat --folder <your sandbox game> --trials 3 --label modded
    python check.py end-stops-slope --folder <your sandbox game> --trials 3 --label modded

Played once without this mod, KSP 1.12.5 and KSP Community Fixes, the springs as saved. With this mod, KSP 1.12.5, KSP
Community Fixes, Terrain Precision Fix and KSP-Fix-AutoStrut, without Parking Brake, three times in each configuration,
the springs as saved: with its default settings; with Fixed Tuning alone, `damperBounce = false` and
`endStopSlide = false` in the mod's `settings.cfg`; and with Fixed Tuning and this fix, `damperBounce = false`.

| | Without this mod | Fixed Tuning alone | Fixed Tuning and this fix | This mod |
|---|---|---|---|---|
| **Flat ground, above 20 m/s**: share of the weight the tyres read (5th percentile, median, 95th) | -1 %, 0 %, 1 % | 37 %, 37 %, 37 % | 97 to 98 %, 99 %, 100 % | 97 to 98 %, 99 %, 100 % |
| Speed across the heading, 95th percentile | 0.15 m/s | 0.02 to 0.05 m/s | 0.04 m/s | 0.04 to 0.05 m/s |
| Angle between the speed and the heading, 95th percentile | 0.36° | 0.06° to 0.11° | 0.09° to 0.10° | 0.09° to 0.11° |
| Wheel readings where the tyre skids | 64 % | 7 % | 1.6 % | 1.6 % |

On flat ground, Fixed Tuning alone already keeps the rover in a straight line: its soft springs still carry a little
over a third of its weight. This fix gives the tyres the whole weight back, and they skid four times less.

| | Without this mod | Fixed Tuning alone | Fixed Tuning and this fix | This mod |
|---|---|---|---|---|
| **Slope**: share of the weight the tyres read (median) | 31 % | 39 % | 99 % | 99 % |
| Top speed over the ground | — | 1.2 m/s | 0.07 m/s | 0.07 to 0.09 m/s |
| How far it turned in 30 s | — | −7.4° to −7.5° | 1.0° to 1.1° | 1.1° to 1.2° |
| Speed over the last 10 s | up to 1.1 m/s | 0.88 m/s | up to 0.37 mm/s | up to 0.41 mm/s |

On the slope, this fix is what holds the rover. With Fixed Tuning alone, it slides down, brakes on, at about 0.9 m/s,
turning as it goes. With this fix, it turns by about a degree in the first second after the save loads, then holds.
