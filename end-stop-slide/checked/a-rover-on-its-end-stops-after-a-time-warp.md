# A rover on its end-stops after a time warp

Part of [Suspension Bounce Fix](../../README.md), [End-Stop Slide](../README.md#checking-the-culprit): not a culprit,
a check that this fix still holds a rover on its end-stops after the time is warped on rails, as a player does near a
rover at rest ([After a time warp](../README.md#after-a-time-warp)).

[KSP Diag - Suspension](https://github.com/lhervier/KSP-Diag-Suspension) shows, among others, the share of the
vessel's weight its suspensions carry, as the tyres read it: this mod holds the end-stops itself, and adds the force it
holds to it.

## The save

[saves/SuspensionBounceEndStopsSlope.sfs](../saves/SuspensionBounceEndStopsSlope.sfs), the one of
[A rover on its end-stops](a-rover-on-its-end-stops.md) for the trials with this mod: `SuspensionBounce Rover`, 2.4 t
on four TR-2L wheels, brakes on, on a slope of about 20° west of the space centre, on soft springs, resting on its
end-stops. Copy it into the folder of a sandbox game, under `saves`, and load it from the quickload menu (Alt+F9 by
default).

## By hand

Load the save, wait a few seconds, and warp the time on rails for a minute (the `.` key, or *Warp To* from the time
display). Back to normal time, watch the rover: with this mod, it holds, as it did before the warp.

## By script

[diag/automation/check.py](../../diag/automation/check.py) loads the save through
[KSP-Util-MCPServer](https://github.com/lhervier/KSP-Util-MCPServer), waits 3 s, warps the time for 60 s, as *Warp To*
does, and records KSP Diag - Suspension for 30 s once the game is back to normal time. Its first lines say how to run
it, and how to choose a configuration of this mod (its `settings.cfg`, KSP started again):

    python check.py end-stops-warp --folder <your sandbox game> --trials 3 --label modded

Played three times in each configuration, KSP 1.12.5, KSP Community Fixes, Terrain Precision Fix, KSP-Fix-AutoStrut
and Parking Brake, the springs as saved: with this mod's default settings, and with this fix off, `endStopSlide =
false` in the mod's `settings.cfg`.

| | `endStopSlide = false` | This mod |
|---|---|---|
| Share of the weight the tyres read (median) | 37 % | 99 % |
| Top speed over the ground | 0.04 m/s | 0.006, 0.006 and 0.10 m/s |
| How far it turned in 30 s | −0.05° to 0.02° | −0.05° to 0.6° |
| Speed over the last 10 s | up to 0, 0 and 43 mm/s | up to 0.19 mm/s |
| Parts at the end, `KSP.log` | 19 of 19, nothing | 19 of 19, nothing |

After the warp, this fix still gives the tyres the whole weight of the rover, as before it. The trial at 0.10 m/s is
the one where Parking Brake held the rover as it loaded: in the first second after the warp, its tyres read less than
a third of its weight, then all of it within a third of a second; it moved meanwhile, then held.
