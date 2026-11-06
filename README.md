# Suspension Bounce Fix

**⚠️ Not finished yet.** This is a new fix, not a finished mod. The code and the conclusions on this page can still
change.

A fix for stock KSP 1.12, kept small: a few Harmony patches, in three short source files. Here is what it fixes:

> **In KSP, landing legs and wheels often go haywire.** A light probe leaves the runway on its own legs, a landing leg
> set down alone in EVA construction flies off, a light rover that stands still on Kerbin never comes to rest on Eve,
> a heavy rover on the Mun slides down a slope with its brakes on.

<table>
<tr>
<td width="50%"><img src="imgs/small-probe.gif" alt="A light probe launched on the runway"></td>
<td width="50%"><img src="imgs/minirover-stock-eve.gif" alt="A light rover loaded on Eve"></td>
</tr>
<tr>
<td valign="top"><em>A probe of 100 kg on three LT-05 legs, launched on the runway
(<a href="#a-light-probe-on-the-runway">how</a>).</em></td>
<td valign="top"><em>A rover of a quarter of a ton on four RoveMax S2 wheels, set down on Eve, saved, and loaded
there.</em></td>
</tr>
<tr>
<td><img src="imgs/landing-leg-stock-minmus.gif" alt="A landing leg set down alone on Minmus"></td>
<td><img src="imgs/landing-leg.gif" alt="A landing leg set down alone on Kerbin, with Terrain Precision Fix"></td>
</tr>
<tr>
<td valign="top"><em>On Minmus, a landing leg taken off a lander and set down on its foot in EVA construction
(<a href="#a-landing-leg-placed-alone">how</a>).</em></td>
<td valign="top"><b>⚠️ Only with Terrain Precision Fix:</b> <em>the same on Kerbin. In stock, the leg lies down there
and stays there.</em></td>
</tr>
<tr>
<td><img src="imgs/bulldozer-stock-eve.gif" alt="A heavy rover loaded on Eve"></td>
<td><img src="imgs/culprit3-stock.gif" alt="A landing leg swung against a fuel tank"></td>
</tr>
<tr>
<td valign="top"><em>A rover of 14 t on six TR-2L wheels, set down on Eve, saved, and loaded there.</em></td>
<td valign="top"><em>A vessel of 400 kg on the runway swings a landing leg on a rotation servo against a fuel tank
(<a href="#a-landing-leg-swung-against-a-fuel-tank">how</a>).</em></td>
</tr>
</table>

*KSP 1.12.5 and KSP Community Fixes, without this mod.*

The cause: KSP sizes a suspension with a few simple rules, patched over the years, and some cases slip through: a
light vessel, a part lying alone, or a heavy rover on light wheels, gets a damper that throws it up instead of slowing
it down; and a wheel at the end of its travel hides part of the weight it carries from its tyre, which loses its grip
(see [The culprits](#the-culprits)). This mod lowers a damper only as far as those cases need, with or without
[Terrain Precision Fix](https://github.com/lhervier/KSP-Fix-TerrainPrecision), and tells the tyre of a wheel at the
end of its travel the weight it really carries: every other vessel behaves as without it, give or take a few percent
of damping.

**How this was made.** The investigation and the code were written with Claude, Anthropic's AI assistant.
Everything here was reviewed and validated by a human — me. I am saying so up front, because contributions made with
an AI deserve a closer look than others, and because some people would rather stop reading here. That look should be
quick: the stock code involved is named and quoted on these pages, and the fix is a few patches you can read in a
few minutes.

## The culprits

How KSP 1.12.5 sets the spring and the damper of a suspension: a simple base model, the patches it got for the cases
where it was not enough, then what those patches leave unanswered: what throws a light vessel, or a part placed
alone, into the air, and what makes a rover slide when its wheels reach the end of their travel.

### The base model

In KSP, a suspension is a spring and a damper, which KSP sets on its own by four rules. The suspension measures how far
it has sunk with a ray cast along its axis: it works on any surface. The spring follows the weight, shared equally
between the wheels on the ground: every vessel sinks about as far. The damper follows the spring, and so only grows as
the square root of the mass: right for a leg carrying about a ton, softer above that without harm, too stiff below.
Last, the setting only happens on contact: in the air, the wheel keeps its old settings.

**→ Full chapter: [The base model](docs/the-culprits/the-base-model.md)**

### The patches

KSP got several corrections for the cases where this model was not enough. Each has its side effects, and several
make the culprits below:

- **The half-ton floor**: each wheel carries at least the weight of half a ton on Kerbin, so that a very small spring
  is not rounded down to zero; a light vessel then gets the damper of half a ton.
- **Rounding and the write threshold**: the spring is only written when it changes by more than 3, probably against a
  loop between the spring and the compression; a small spring stays at zero, hence the floor.
- **The boost with compression**: the spring stiffens up to three times near the end of its travel, and the damper
  with it, at the moment of impact.
- **The big-vessel correction**: above about forty tons, the damper stiffens up to nearly four times, from the mass of
  the whole vessel, not from what each leg carries.
- **The automatic sliders**: set at the first contact, the damper up to 2 on Eve.
- **High gravity**: above 1.5 g, so on Eve, the spring is tripled and the damper multiplied by 1.2.

**→ Full chapter: [The patches](docs/the-culprits/the-patches.md)**

### Why a damper can throw a vessel

In real life, a damper that is too strong throws nothing: it only makes the leg slow to settle. But KSP works out
physics in small steps, fifty a second. At each step, the damper slows the leg according to the speed it had at the
start of the step, for the whole step. On a loaded leg, this braking takes off a little speed at each step. On a leg
that carries almost nothing, or a light wheel on a light mount, which the damper pushes before the rest of the vessel
follows, it is so strong that in a single step it does more than stop it: it sends it back the other way, faster than
it came. The next step does it again, and in a few steps the vessel leaves at tens of metres per second. Both culprits
lead there.

### First culprit: a damper too strong for a light vessel

Because of the floor, a leg carrying 50 kg gets the damper of a leg carrying half a ton, far too strong for it: it
throws it. A heavy world makes it worse: a small rover that stands still on Kerbin bounces on and on on Eve, and a
heavy rover whose wheels hang from light trusses bounces higher and higher there, until its wheels break. The mod
brings the damper down under a limit.

**→ Full chapter: [First culprit: a damper too strong for a light vessel](docs/the-culprits/a-damper-too-strong-for-a-light-vessel.md)**

### Second culprit: a suspension in the air keeps its old settings

In the air, a wheel keeps the spring and the damper of its last contact. A part placed alone in EVA construction,
which KSP makes weigh up to 20 t for its first seconds, thus keeps a suspension set for 20 t, which throws it when it
touches the ground again at its own mass. The mod brings the damper down under the limit as soon as the wheel is in
the air.

**→ Full chapter: [Second culprit: a suspension in the air keeps its old settings](docs/the-culprits/a-suspension-in-the-air-keeps-its-settings.md)**

### Troisième coupable : une roue en butée cache son poids aux pneus

*(En français, à traduire avant le commit.)* Les pneus de KSP passent par Vehicle Physics Pro (VPP), qui ne pèse pas
le vaisseau : il lit la charge de chaque roue dans la force de sa suspension, et en tire l'adhérence du pneu, ce qui le
retient sur une pente et ce qui arrête son glissement. Quand une roue arrive en butée, ce qui arrive sur un rover lourd
sur la Mune, en travers d'une pente ou avec des ressorts réglés mous, la butée porte le reste du poids, mais sa force
n'est rapportée nulle part : VPP croit la roue moins chargée qu'elle ne l'est. Le rover perd l'adhérence qui devrait le
retenir : il glisse de côté sur du plat, et descend une pente à vitesse constante, freins serrés. Ni PhysX ni Unity
ne donnent la force de la butée, et un bug de Unity cache celle des liaisons entre les pièces, qui aurait permis de
la déduire roue par roue. Le mod la retrouve par un bilan sur le vaisseau entier, où ces liaisons s'annulent : ce que
gagnent ou perdent sa quantité de mouvement et son moment cinétique, moins toutes les forces connues. Il la partage
entre les roues en butée, et l'ajoute à celle que VPP lit.

**→ Chapitre complet : [Troisième coupable : une roue en butée cache son poids aux pneus](docs/the-culprits/a-bottomed-wheel-hides-its-weight-from-the-tyres.md)**

## Checked

Each case has its page: the save it starts from, how to play it by hand, the script that plays it, and what it shows
without and with this mod. The scripts drive KSP through
[KSP-Util-MCPServer](https://github.com/lhervier/KSP-Util-MCPServer), and need nothing but Python 3; their figures
come from [KSP Diag - Suspension](https://github.com/lhervier/KSP-Diag-Suspension) and from `KSP.log`.

### A light probe on the runway

A probe of 100 kg on three LT-05 legs, launched on the runway: without this mod, it leaves the runway the moment
physics starts, and bounces on and on; with it, it settles on its legs within a fraction of a second.

**→ Full chapter: [A light probe on the runway](docs/checked/a-light-probe-on-the-runway.md)**

### A landing leg placed alone

An LT-1 landing leg taken off a lander and set down alone, foot down, in EVA construction: without this mod, it is
thrown into the air on Minmus, and on Kerbin too with Terrain Precision Fix; with it, it tips over onto its side and
comes to rest.

**→ Full chapter: [A landing leg placed alone](docs/checked/a-landing-leg-placed-alone.md)**

### A landing leg swung against a fuel tank

A light vessel swings a landing leg on a rotation servo against the side of a fuel tank: without this mod, it is
thrown sideways, often hard enough to break apart; with it, it is pushed aside at a walking pace.

**→ Full chapter: [A landing leg swung against a fuel tank](docs/checked/a-landing-leg-swung-against-a-fuel-tank.md)**

### A light rover on Eve

A rover of a quarter of a ton on four RoveMax S2 wheels, loaded on Eve: without this mod, it bounces higher and
higher and breaks a wheel; with it, it stays on its wheels.

**→ Full chapter: [A light rover on Eve](docs/checked/a-light-rover-on-eve.md)**

### A heavy rover on Eve

A rover of 10 t, and the same with a blade of 3.5 t, on six TR-2L wheels at the end of trusses, loaded on Eve:
without this mod, it hops and breaks its wheels; with it, it stays on its wheels.

**→ Full chapter: [A heavy rover on Eve](docs/checked/a-heavy-rover-on-eve.md)**

### A rover lowered onto its wheels on Eve

A base of 5 t on six TR-2L wheels, lowered onto them by its hinges on Eve: without this mod, its wheels kick it up
as they touch, unless its springs are set by hand; with it, it settles with its springs left to the game.

**→ Full chapter: [A rover lowered onto its wheels on Eve](docs/checked/a-rover-lowered-onto-its-wheels-on-eve.md)**

### A rover on its end-stops

Rovers whose wheels rest on their end-stops: soft springs on Kerbin, a heavy rover on the Mun. Without this mod,
their tyres read a fraction of the weight: they slide sideways on flat ground, and slide down a slope, brakes on; with
it, the tyres read the whole weight, and hold.

**→ Full chapter: [A rover on its end-stops](docs/checked/a-rover-on-its-end-stops.md)**


## Limits

- **A rover resting on its end-stops still creeps a little.** The end-stop of a suspension does not hold the weight
  steadily: it pushes in jolts, from one physics step to the next, and the rover shakes on it, too fast to be seen.
  At each shake, the tyres slip a little, and on a slope the rover creeps down, a few centimetres a second where it
  slid at a metre a second (see [A rover on its end-stops](docs/checked/a-rover-on-its-end-stops.md)). This mod tells the tyres the weight the end-stop carries; it
  does not change the end-stop.
- **On a low gravity body, a leg placed alone still hops a little.** Its spring is still the spring of half a ton,
  about 200 times its weight on Minmus: it pushes the leg off the ground as it straightens, and the leg falls back
  slowly. Removing that hop would mean lowering the floor of the spring, which this fix does not do.
- **Without Terrain Precision Fix, a wheel placed alone in EVA construction can jump a few seconds after being
  placed**, for another reason: `ModuleCargoPart.MakePartSettle` makes a part placed alone weigh 4 t (20 t below
  0.8 g) for 3 s, and the suspension sizes its spring for that. This fix does not change it: Terrain Precision Fix's
  `fixLonePartMass` does.

## Work in progress

- **Other parts with a suspension.** The fix applies to every part with `ModuleWheelSuspension`: landing legs, wheels
  and landing gear. It has been played with LT-05 and LT-1 legs, and RoveMax S2 and TR-2L wheels only.

## Installing

Requires [HarmonyKSP](https://github.com/KSPModdingLibs/HarmonyKSP) (`GameData/000_Harmony`). Copy
`GameData/KSPFixSuspensionBounce` into the `GameData` folder of KSP.

## Building

Set `KSPDIR` to your KSP install folder, then run `build.bat`: it compiles
`GameData/KSPFixSuspensionBounce/KSPFixSuspensionBounce.dll`.

## License

MIT, see [LICENSE](LICENSE).
