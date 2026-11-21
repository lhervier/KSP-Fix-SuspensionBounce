# A lander dropped onto its end-stops

Part of [Suspension Bounce Fix](../../README.md), [End-Stop Slide](../README.md#checking-the-culprit):
[a bottomed wheel hides its weight from the tyres](../a-bottomed-wheel-hides-its-weight-from-the-tyres.md), on landing legs rather than
on wheels.

The LT-1 landing leg is the one stock wheel or leg whose collider sits off its part's centre (0.119 m lower). The game
leaves that offset out when it works out how far a suspension is compressed: an LT-1 resting on its end-stop reads
0.873 of its travel, never 1. This mod adds the offset back, so that it sees an LT-1 on its end-stop.
[KSP Diag - Suspension](https://github.com/lhervier/KSP-Diag-Suspension) reads the game's figure: it shows an LT-1
short of its end-stop, and never counts it as bottomed out; the share of the weight the tyres read, which it also
shows, is right.

## The craft

[saves/SuspensionBounceLegKerbinSuspended.craft](../saves/SuspensionBounceLegKerbinSuspended.craft): a Mk1 command
pod, an OKTO2 probe core, four Z-100 batteries, an FL-T100 tank, empty, and a telescopic ladder, on four LT-1 legs,
1.2 t in all, hanging from three launch clamps; the legs' springs are set by hand on the game's sliders at their
lowest (0.05), so that the lander ends up resting on its end-stops. Copy it into `Ships/SPH` of a sandbox game.

This mod hides the game's sliders, and sets the suspensions itself at the first physics step
([Suspensions never set](../../fixed-tuning/README.md#suspensions-never-set)): with it, the springs are made soft
again from its suspension window, while the lander hangs from its clamps. Open the window from the toolbar, tick all
four legs, keep Kerbin as the body, type a mass of 0.01 t, share the weight by the centre of mass, and press *Auto*.

## By hand

Launch the craft on the runway (with this mod, from the SPH, answer *Launch as they are* to *Suspensions not set*),
set the springs as above with this mod, wait for it to hang still, and stage: the clamps let it go, and it falls
4.6 m onto its legs. Then watch it, and the navball's heading.

Without this mod, the legs reach their end-stops, and the lander rests on them; but its feet read none of its weight:
it slowly turns on itself, a degree or more in a few seconds, and slides. With this mod, it rests on its end-stops and
stays still.

## By script

[diag/automation/check.py](../../diag/automation/check.py) launches the craft through
[KSP-Util-MCPServer](https://github.com/lhervier/KSP-Util-MCPServer), sets the springs as above with `--tune auto
--tune-mass 0.01`, lets it hang for 2 s, stages, and records KSP Diag - Suspension for the 8 s after the touch. Its
first lines say how to run it:

    python check.py lander-drop --folder <your sandbox game> --trials 3 --label stock
    python check.py lander-drop --folder <your sandbox game> --trials 3 --tune auto --tune-mass 0.01 --label modded

Played three times in each configuration, KSP 1.12.5, KSP Community Fixes, Terrain Precision Fix and AutoStrut Fix:
without this mod, the springs as saved; with this mod, the springs set by *Auto* for 0.01 t, with its default
settings, with Fixed Tuning alone (`damperBounce = false` and `endStopSlide = false` in the mod's `settings.cfg`), and
with Fixed Tuning and this fix (`damperBounce = false`). The table gives the range of the three trials.

| | Without this mod | Fixed Tuning alone | Fixed Tuning and this fix | This mod |
|---|---|---|---|---|
| Share of the weight the tyres read (median) | 0 % | 73 to 79 % | 92 to 93 % | 93 % |
| Turn over 8 s | -2.6° to 1.3° | -3.5° to 0.2° | -1.1° to 0.1° | 0.0° |
| Speed over the last third | 4.2 to 4.3 mm/s | 0.8 to 1.0 mm/s | 0.2 mm/s | 0.2 to 0.4 mm/s |
| Parts at the end, `KSP.log` | 12 of 12, nothing | 12 of 12, nothing | 12 of 12, nothing | 12 of 12, nothing |
