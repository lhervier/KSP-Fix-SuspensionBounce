# Suspension Bounce Fix

**⚠️ Not finished yet.** This is a new fix, not a finished mod. The code and the conclusions on this page can still
change.

A fix for stock KSP 1.12, kept small: two Harmony postfixes, in one source file. Here is what it fixes:

> **A light probe standing on its landing legs is thrown up into the air.** A probe of 100 kg on three LT-05 legs,
> put on the runway, leaves it the moment physics starts and climbs well over a hundred metres. Put back on the
> ground, it is thrown up again.

![The probe launched on the runway](imgs/small-probe.gif)

*KSP 1.12.5 and KSP Community Fixes, without this mod: the probe of [A light probe on the runway](#a-light-probe-on-the-runway), launched from the SPH on
the runway.*

The cause: KSP sizes the damper of each leg without the mass the leg carries, and on a light vessel, that damper
throws the vessel up instead of slowing it down (see [The culprits](#the-culprits)). By default, this mod only lowers the
dampers that are too stiff for the mass they carry, which only very light vessels have: every other vessel behaves
as without it. A second mode, for players who want it, sizes every damper from the mass its leg carries: see
[The fix](#the-fix).

It also fixes a part lying alone, with or without
[Terrain Precision Fix](https://github.com/lhervier/KSP-Fix-TerrainPrecision):

> **A landing leg set down alone in EVA construction is thrown up into the air.** In stock, on Minmus, a few
> seconds after it is set down. With Terrain Precision Fix, on Kerbin as well, at once; it breaks when it falls back.
> See [A landing leg placed alone](#a-landing-leg-placed-alone) for why.

![A landing leg set down alone on Minmus, in stock](imgs/landing-leg-stock-minmus.gif)

*KSP 1.12.5 and KSP Community Fixes, without this mod or Terrain Precision Fix: on Minmus, the save
[`landing-leg-minmus.sfs`](https://github.com/lhervier/KSP-Fix-TerrainPrecision/blob/main/diag/lone-parts/limits/landing-leg/landing-leg-minmus.sfs)
of Terrain Precision Fix; in EVA construction, Bill Kerman takes a leg off the lander and sets it down on its foot.*

![A landing leg set down alone on Kerbin, with Terrain Precision Fix](imgs/landing-leg.gif)

*KSP 1.12.5, KSP Community Fixes and Terrain Precision Fix, without this mod: on Kerbin, the save
[`landing-leg-kerbin.sfs`](https://github.com/lhervier/KSP-Fix-TerrainPrecision/blob/main/diag/lone-parts/limits/landing-leg/landing-leg-kerbin.sfs)
of Terrain Precision Fix; in EVA construction, Bill Kerman takes a leg off the lander and sets it down on its foot.*

**How this was made.** The investigation and the code were written with Claude, Anthropic's AI assistant.
Everything here was reviewed and validated by a human — me. I am saying so up front, because contributions made with
an AI deserve a closer look than others, and because some people would rather stop reading here. That look should be
quick: the stock code involved is named and quoted on this page, and the fix is two postfixes you can read in a
few minutes.

## The culprits

KSP sets the spring of each suspension from the weight it carries, never below the weight of half a ton on Kerbin, and
the damper from the spring alone. Three things then throw a light vessel, or a part lying alone, into the air.

**A damper too strong for a light vessel.** Because of the floor, a leg carrying a few tens of kilograms gets the
damper of half a ton. KSP works out physics in small steps, and at each step, that damper sends the leg back the other
way, faster than it came.

**A suspension in the air keeps its old settings.** KSP only sets a suspension while its wheel touches something: a
part that left the ground heavy, and touches it again light, pushes with the damper of its heavy self.

**A suspension that pushes at a slant.** When a part leans or lies down, its suspension meets the surface at a slant:
how far it reads it has sunk jumps from one step to the next, and KSP sets it as if it stood upright.

**→ Full chapter: [The culprits](docs/the-culprits.md)**

## The fix

The mod keeps the spring KSP sets, its floor included, and deals with each culprit in turn.

**On the ground, a damper the leg can take.** The mod works out what each leg carries, the vessel's mass shared
between the wheels on the ground, or less for a leg far from the vessel's centre of mass: pushed there, the vessel
turns as much as it rises. It keeps the damper under the one that takes off half the leg's speed in a single physics
step, well short of sending it back.

**Off the ground too.** KSP leaves a wheel in the air with the damper of its last contact; the mod keeps it under the
same limit, for the smallest share the leg can carry, so that it is already right at the first step of the next
contact.

**Leaning.** Past 30° of lean on the surface its ray hits, a suspension loses more and more of its spring and its
damper, and the limit of its damper drops with the lean.

**Two modes.** `Minimal`, the default, only changes a suspension that reaches the limit or leans past 30°: every other
vessel behaves exactly as without the mod. `MassBased` sizes every damper from the mass its leg carries, so that light
and heavy vessels settle on their legs alike.

**→ Full chapter: [The fix](docs/the-fix.md)**

## Checked

### A light probe on the runway

KSP 1.12.5 with KSP Community Fixes. The probe is [diag/SuspensionBounce.craft](diag/SuspensionBounce.craft): an
OKTO2 probe core, three LT-05 micro landing struts, a Z-100 battery and two OX-STAT solar panels, about 100 kg in all,
so about 33 kg per leg. Copy it into `Ships/SPH` of a sandbox game and launch it on the runway.

Without this mod, the probe leaves the runway the moment physics starts: the altimeter, which reads 70 m on the
runway, goes above 230 m, then the probe falls back, bounces up again, and so on. Putting it back on the ground by any
other means (the *Set Position* tool of the debug menu, for one) ends the same way. With this mod, the probe settles
on its legs within a fraction of a second, and stays there.

With `logLevel = Debug` in the settings, the mod writes a line for each leg whose damper it lowers:

```
[KSPFixSuspensionBounce] [DEBUG] Damper of miniLandingLeg (749513586) of SuspensionBounce limited to 1.667 for 0.033 t carried, KSP computed 7
```

### A landing leg placed alone

The same damper throws a landing leg put down alone in EVA construction, but stock hides it most of the time:
`ModuleCargoPart.MakePartSettle` makes a part placed alone weigh 4 t for its first 3 s on the ground, 20 t below
0.8 g. Under that weight, the damper is no longer too stiff, and the leg simply tips over onto its flat side, its
foot, where its suspension is, off the ground. When its own mass comes back:

- on Kerbin, the leg is lying still, and stays there;
- on Minmus, it is still rolling: its foot touches the ground again, and the damper throws it.

[Terrain Precision Fix](https://github.com/lhervier/KSP-Fix-TerrainPrecision), with its `fixLonePartMass` setting
on, gives the part its own mass from the start: its foot is on the ground, and the damper throws it at once, on
Kerbin too. In a sandbox game, with an engineer on EVA next to a lander that has LT-1 landing legs:

1. Open the EVA construction panel and pick the *Place* tool.
2. Click one of the lander's legs that does not touch the ground: it is now held.
3. Click on the ground, foot down.

Without this mod, in stock, the leg tips over onto its side; on Minmus, it leaves the ground a few seconds later and
climbs tens of metres. With Terrain Precision Fix, the leg leaves the ground within a fraction of a second, spinning:
on Kerbin, it rises a few metres and is destroyed when it lands (`landingLeg1 (…) Exploded!!` in `KSP.log`); on
Minmus, it goes much higher. With this mod, with or without Terrain Precision Fix, the leg tips over onto its side,
as a leg standing on its foot would, and may roll or stand up again before it comes to rest; the mod writes:

```
[KSPFixSuspensionBounce] [DEBUG] Damper of landingLeg1 (521620801) of Jambes d'atterrissage LT-1 limited to 2.5 for 0.05 t carried, KSP computed 6
```

## Limits

- **On a low gravity body, a leg placed alone still hops a little.** Its spring is still the spring of half a ton,
  about 200 times its weight on Minmus: it pushes the leg off the ground as it straightens, and the leg falls back
  slowly. Removing that hop would mean lowering the floor of the spring, which this fix does not do.
- **Without Terrain Precision Fix, a wheel placed alone in EVA construction can jump a few seconds after being
  placed**, for another reason: `ModuleCargoPart.MakePartSettle` makes a part placed alone weigh 4 t (20 t below
  0.8 g) for 3 s, and the suspension sizes its spring for that. This fix does not change it: Terrain Precision Fix's
  `fixLonePartMass` does.

## Work in progress

- **`MassBased` on heavy vessels.** A heavy rover or lander should settle on its wheels or legs a little more firmly
  than without the mod, without bouncing, sinking or shaking. To check by hand: land or load the same heavy vessel
  with `mode = Stock`, then with `mode = MassBased`, and compare how it settles.
- **Rovers on rough ground.** A wheel whose ray hits a face leaning more than 30° on its axis, a rock or the edge of
  a crater, gets a softer suspension for as long as it touches it. A rover driving over rough ground should behave as
  without the mod, neither sinking onto its wheels nor bouncing. To check by hand: drive the same rover over the same
  rocks with `mode = Stock`, then with the mod.
- **Tipped over vessels.** A vessel lying on its side keeps its legs and wheels from pushing it about; it should come
  to rest as quickly as without the mod, or quicker. To check by hand: tip a lander over with `mode = Stock`, then with
  the mod, and compare.
- **Other parts with a suspension.** The fix applies to every part with `ModuleWheelSuspension`: landing legs, wheels
  and landing gear. It has been played with LT-05 and LT-1 legs and RoveMax M1 wheels only.

## Installing

Requires [HarmonyKSP](https://github.com/KSPModdingLibs/HarmonyKSP) (`GameData/000_Harmony`). Copy
`GameData/KSPFixSuspensionBounce` into the `GameData` folder of KSP.

## Building

Set `KSPDIR` to your KSP install folder, then run `build.bat`: it compiles
`GameData/KSPFixSuspensionBounce/KSPFixSuspensionBounce.dll`.

## License

MIT, see [LICENSE](LICENSE).
