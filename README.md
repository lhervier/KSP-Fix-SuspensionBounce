# Suspension Bounce Fix

**⚠️ Not finished yet.** This is a new fix, not a finished mod. The code and the conclusions on these pages can still
change.

A mod for stock KSP 1.12: several fixes for suspensions, those of landing legs, wheels and landing gear alike, plus a
change of how suspensions are set, which changes how you play with them. Each has its own folder below: what goes
wrong in game, why, how it was checked, and what the mod does about it.

Two other faults move a vessel standing on its wheels, outside its suspensions, and have their own fixes:
[Wheel Creep Fix](https://github.com/lhervier/KSP-Fix-WheelCreep) for a parked vessel that creeps and a rover that
turns on itself as it comes to rest, and [Docking Wheel Jump Fix](https://github.com/lhervier/KSP-Fix-DockingWheelJump)
for a landed vessel that jumps at a docking.

**How this was made.** The investigation and the code were written with Claude, Anthropic's AI assistant.
Everything here was reviewed and validated by a human — me. I am saying so up front, because contributions made with
an AI deserve a closer look than others, and because some people would rather stop reading here. That look should be
quick: the stock code involved is named and quoted on these pages, each fix's code is quoted and explained in its own
folder, and the player's setting, which replaces the game's, is explained in [Fixed Tuning](fixed-tuning/README.md).

## How KSP sets a suspension

How KSP 1.12.5 sets a suspension's spring and damper on its own: a simple base model, then the patches it got for the
cases where that model was not enough, each with its side effects. The stock game only, its code quoted.

**→ Full chapter: [How KSP sets a suspension](how-ksp-sets-a-suspension/README.md)**

## Vessel Bodies

Not a fix: the read of a vessel as one rigid body, once per vessel and per physics step, which the fixes share. It
gives the mass the vessel answers with at a wheel's contact, which depends on how its mass is spread, never on what
holds it up; holds all the wheels of a vessel at once against their end-stops; and says what the mod costs.

**→ Full chapter: [Vessel Bodies](vessel-bodies/README.md)**

## Fixed Tuning

The player sets each suspension, a spring and a damping ratio, from a window that predicts them or measures them on
the ground, and nothing changes them afterwards. A light probe stays on the runway, a light rover rests on Eve: the
damper is sized for the mass it carries. In return, a setting no longer follows the load.

**→ Full chapter: [Fixed Tuning](fixed-tuning/README.md)**

## Damper Bounce

A heavy rover on Eve hops until its wheels break, a landing leg set down alone flies off: a damper too strong for
what it moves within a physics step sends a wheel or a leg back faster than it came. The mod bounds every damper, at
every physics step.

**→ Full chapter: [Damper Bounce](damper-bounce/README.md)**

## End-Stop Slide

A rover whose wheels rest on their end-stops slides sideways, and down a slope with its brakes on: its tyres only read
the force of their springs. The mod holds the end-stops itself, and tells the tyres the weight they carry.

**→ Full chapter: [End-Stop Slide](end-stop-slide/README.md)**

## Checked

Each fix's checked cases have their pages in its folder: the save each starts from, how to play it by hand, the script
that plays it, and what it shows without and with this mod. One script,
[diag/automation/check.py](diag/automation/check.py), plays the cases of every fix, but for a case whose page names
its own script in its folder. The scripts drive KSP through
[KSP-Util-MCPServer](https://github.com/lhervier/KSP-Util-MCPServer), and need nothing but Python 3; their figures
come from [KSP Diag - Suspension](https://github.com/lhervier/KSP-Diag-Suspension) and from `KSP.log`.

## Installing

Requires:

- [HarmonyKSP](https://github.com/KSPModdingLibs/HarmonyKSP) (`GameData/000_Harmony`);
- [ModuleManager](https://github.com/sarbian/ModuleManager), which adds the player's setting to every part with a
  suspension;
- [Terrain Precision Fix](https://github.com/lhervier/KSP-Fix-TerrainPrecision): its `fixLonePartMass` removes the
  4 t, or 20 t below 0.8 g, that `ModuleCargoPart.MakePartSettle` gives a part placed alone for its first seconds on
  the ground.

Copy `GameData/KSPFixSuspensionBounce` into the `GameData` folder of KSP.

**Not compatible with [Parking Brake](https://github.com/jarosm/KSP-ParkingBrake).** Remove it from `GameData`
before installing this mod. Parking Brake sets the speed of every part of a braked vessel back to zero at every
physics step. With the player's setting of [Fixed Tuning](fixed-tuning/README.md), a vessel loaded off its resting
height then rocks a little harder at each step, until it leaves the ground; Parking Brake lets go of a vessel that
leaves the ground, unless it is a rover, and the vessel may land on its side. Parking Brake works around vessels that
will not stay still; the fixes of this mod, with its two siblings above, are meant to make it unnecessary.

## Building

Set `KSPDIR` to your KSP install folder, then run `build.bat`: it compiles
`GameData/KSPFixSuspensionBounce/KSPFixSuspensionBounce.dll`.

## License

MIT, see [LICENSE](LICENSE).
