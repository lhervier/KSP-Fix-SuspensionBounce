# Fixed Tuning

Part of [Suspension Bounce Fix](../README.md): the player sets each suspension, and nothing changes that setting
behind the player's back. A change of how suspensions are set, which changes how you play with them. Here is what it
fixes:

> **In KSP, suspensions throw light vessels off the ground, and push vessels up when something else carries them.** A
> light probe leaves the runway on its own legs, a light rover that stands still on Kerbin bounces on and on on Eve, a
> leg swung against a fuel tank throws its vessel sideways, a rover docked to a base standing on its own legs or
> girders jumps up on its wheels, and a crane that grabs a vessel still resting on the ground is pushed up.

<ROVER DOCKED TO A BASE STANDING ON THE GROUND, JUMPING UP ON ITS WHEELS>

The cause: KSP sizes every spring for the whole vessel's weight shared among the wheels on the ground, whatever else
carries the vessel, and no rule could size it right in every case; and it works the damper out from that spring alone,
too strong for a light vessel (see [The culprits](#the-culprits)).

## Why it matters

To size a spring, you must know the weight it carries. KSP's answer is a simple rule, worked again at every physics
step: each wheel on the ground carries the whole vessel's weight, divided by the number of wheels on the ground. That
is close enough for a rover standing on its wheels alone. It goes wrong as soon as something else carries part of the
vessel:

- **A rover docks to a base that stands on its own legs or girders.** Both become one vessel: each wheel of the rover
  now gets a share of the base's weight too, though the base still stands on its own legs. The rover's springs stiffen
  many times over, at once, and push it up off the ground.
- **A crane grabs a vessel still resting on the ground.** Its springs stiffen for a weight the ground still carries,
  and push the crane up.

The damper is no better off. KSP works it out from the spring alone, and from a weight of at least half a ton: for a
vessel of a few hundred kilograms, it is so strong that it throws the vessel off the ground, on Eve more than
anywhere.

## The culprits

### The whole vessel's weight on the wheels on the ground

KSP shares the whole vessel's weight equally among its wheels on the ground, again at every physics step, whatever
else carries the vessel ([the base model](../how-ksp-sets-a-suspension/the-base-model.md)).
`SuspensionLoadBalancer.FixedUpdate`, a vessel module, gives each suspension the weight it carries,
`sprungMassGravity`: the vessel's mass times the surface gravity in g, rounded to the tenth, shared equally between the
wheels on the ground:

```csharp
num = Math.Round(num * vessel.gravityMultiplier * vessel.mainBody.GeeASL * 10.0) / 10.0;
...
share = Mathf.Round(100f / divisor) / 100f;
suspensionModules[j].sprungMassGravity = Mathf.Max((float)(num * (double)share * (double)multiplier), minMass);
```

`ModuleWheelSuspension.SuspensionSpringUpdate` then turns that weight into a spring, and the spring into a damper.

### No rule can do better

A better rule would not do. There are only two ways to know what a wheel carries:

- **Work it out**, from the vessel's mass and the wheels' positions. That holds while the vessel stands on its wheels
  alone. Once a base's legs, or the ground under a grabbed vessel, carry part of it, how the weight splits between them
  and the wheels no longer follows from the masses: it depends on how stiff each support is, so on the very springs
  being sized.
- **Measure it**: the physics engine knows the force on each wheel. But that force is the one the spring gives now: a
  spring sized from it follows its own output, not the weight. Done at every step, it chases a compression, whatever
  holds the vessel; done once, on flat ground, on a vessel standing on its wheels alone, it gives the right spring.

Real suspensions do not follow the load either: a coil spring or a torsion bar keeps its stiffness, and a loaded car
sits lower.

### A damper too strong for a light vessel

KSP works a suspension's damper out from its spring alone, so that it only grows as the square root of the mass, and
acts as if each wheel carried at least half a ton: a probe of 100 kg gets the damper of a far heavier vessel, and is
thrown by it. On Eve, the automatic sliders and the high gravity patch make that damper stronger still, and a light
rover never comes to rest there. Against the side of a fuel tank, a light vessel's leg reads a compression it does not
make, and its damper throws it sideways.

**→ Full chapter: [A damper too strong for a light vessel](a-damper-too-strong-for-a-light-vessel.md)**

## Checking the culprit

Each case has its page: the save or craft it starts from, how to play it by hand, the script that plays it, and what
it shows without and with this mod. With this mod, they were played with its default settings, then again with Fixed
Tuning alone (`damperBounce = false` and `endStopSlide = false` in its `settings.cfg`): the player's setting cures them
on its own. The bench of a rover docked to a base standing on the ground is in [Work in progress](#work-in-progress).

### A light probe on the runway

A probe of 100 kg on three LT-05 legs, launched on the runway: without this mod, it leaves the runway the moment
physics starts, and bounces on and on; with Fixed Tuning, alone or with the mod's other fixes, it settles on its legs.

**→ Full chapter: [A light probe on the runway](checked/a-light-probe-on-the-runway.md)**

### A light rover on Eve

A rover of a quarter of a ton on four RoveMax S2 wheels, loaded on Eve: without this mod, it bounces higher and
higher and breaks a wheel; with Fixed Tuning, alone or with the mod's other fixes, it stays on its wheels.

**→ Full chapter: [A light rover on Eve](checked/a-light-rover-on-eve.md)**

### A landing leg swung against a fuel tank

A light vessel swings a landing leg on a rotation servo against the side of a fuel tank: without this mod, it is
thrown sideways, often hard enough to break apart; with Fixed Tuning, alone or with the mod's other fixes, it is
pushed aside, undamaged, in twenty trials out of twenty.

**→ Full chapter: [A landing leg swung against a fuel tank](checked/a-landing-leg-swung-against-a-fuel-tank.md)**

## The fix

Each suspension gets a spring, in kN/m, and a damping ratio, saved with the craft and the vessel. Nothing changes them
behind the player's back: not a docking, not a grab, not a burn, not a wheel leaving the ground. A rover docked to a
base keeps the springs it had, and each of its wheels goes on carrying what it carried before: it stays where it was.

In return, a setting no longer follows the load. A crane that lifts a heavy load sinks lower on its wheels, as a real
one would; a lander that has burnt its fuel stands higher; a rover set for Kerbin sinks less on the Mun. When the load
changes for good, set the suspensions again.

The damper comes from the damping ratio and the mass the suspension was set to carry, with no floor: it grows as the
mass, and a light vessel gets a damper sized for it ([a damper too strong for a light vessel](a-damper-too-strong-for-a-light-vessel.md)).
[Damper Bounce](../damper-bounce/README.md#the-fix) then sizes it for what it moves within a physics step.

### The suspension window

A button with a spring on the toolbar, in the editors and in flight, opens the *Suspensions* window. It lists the
vessel's suspensions, each with its spring and its damping ratio, and in flight where its wheel is: folded, in the
air, or its compression and its load on the ground. A toggle on each chooses which ones the buttons set; *All* and
*None* tick or untick them all.

- **Auto** predicts the springs of the vessel standing on the chosen suspensions alone, on flat ground: for a body,
  picked with the arrows (in flight, the one the vessel is at, which *Here* brings back), for the vessel's mass or a
  typed one (a rover inside a fairing weighs less than the rocket it rides), its weight shared by the vessel's centre
  of mass or equally, at the compression asked for (a third of the travel by default) and the damping ratio asked for
  (0.7 by default: 1 stops a wheel without a bounce, 0.7 lets it overshoot a little, below 0.3 it bounces). With the
  centre of mass, a tall vessel on soft springs gets stiffer ones, so that it does not tip over.
- **Level on the ground**, in flight, measures the chosen suspensions of a vessel standing still, and sets each spring
  so that it sinks to the compression asked for under the load it carries, in a few passes. It needs flat ground: the
  window warns on a slope or on uneven ground.

### Suspensions never set

A vessel launched from the editor with suspensions never set stops on a question, *Suspensions not set*: *Set them for
&lt;body&gt; and launch*, the body chosen in the window, from the vessel's mass and centre of mass; *Launch as they
are*; or *Cancel*.

A suspension never set is set at the first physics step the mod sees it: a part placed alone in EVA construction, a
leg taken out of a container and attached by a Kerbal, a vessel from before the mod was installed, a vessel launched
as it is. It gets the setting predicted from the vessel's mass shared equally among all its suspensions, on the body
the vessel is at, at the default compression and damping ratio (0.7).

### The part's window

The part's window no longer shows the game's *Spring Strength* and *Damper Strength* sliders, nor its automatic mode,
but *Spring* and *Damping ratio* (see [Work in progress](#work-in-progress)).

### The settings

In the mod's `settings.cfg` file: `fixedTuning = false` gives the suspensions back to the game, as without this mod;
the mod's other fixes are not supported then. `fixedTuningCompression` and `fixedTuningDamping` are the compression
and the damping ratio the window offers by default, and that a suspension never set gets. `fixedTuningTipMargin` is
how many times the springs' righting moment must exceed the weight's tipping moment for a prediction made with the
centre of mass; 0 removes the bound.

### In the mod's code

Everything is in [Src/FixedTuning/FixedTuning.cs](../Src/FixedTuning/FixedTuning.cs), with the window in
[Src/FixedTuning/TuningWindow.cs](../Src/FixedTuning/TuningWindow.cs), the levelling in [Src/FixedTuning/Leveller.cs](../Src/FixedTuning/Leveller.cs) and the question
at launch in [Src/FixedTuning/LaunchCheck.cs](../Src/FixedTuning/LaunchCheck.cs). The setting itself is a part module,
`ModuleSuspensionTuning`, which [ModuleManager](https://github.com/sarbian/ModuleManager) adds to every part with a
`ModuleWheelSuspension`.

A prefix of `ModuleWheelSuspension.SuspensionSpringUpdate` skips the game's setting of a suspension that has one:

```csharp
ModuleSuspensionTuning tuning = Tuning(__instance);
return tuning == null || !tuning.IsTuned;
```

A prefix of `ModuleWheelSuspension.FixedUpdate` writes the spring at every step, and the damper in the module's own
field, which Damper Bounce starts from. The damper aims at 2 ζ √(k m), ζ the damping ratio, k the spring, m the mass
the suspension was set to carry:

```csharp
double aimed = 2.0 * damping * Math.Sqrt(Math.Max(spring, 0f) * Math.Max(share, 0f));
```

The spring is written at every step, because the game sets it back:

```csharp
// The game sets the spring back to 0 when the vessel's part count changes, or it docks: write at every step.
if (wheel.wheelCollider.springRate != spring)
{
    wheel.wheelCollider.springRate = spring;
}
```

### Limits

- **A leg against the side of another vessel still gets kicks.** The suspension takes the side of a tank for ground;
  its ray, nearly parallel to a curved side, can meet it already deep inside from one step to the next, and the damper
  pushes back against a compression the leg does not make. The vessel is pushed aside, but no longer broken apart:
  [A landing leg swung against a fuel tank](checked/a-landing-leg-swung-against-a-fuel-tank.md).

## Work in progress

- **A rover docked to a base standing on the ground.** A rover docked to a base resting on its own legs or girders,
  with [Docking Wheel Jump Fix](https://github.com/lhervier/KSP-Fix-DockingWheelJump) and
  [Wheel Creep Fix](https://github.com/lhervier/KSP-Fix-WheelCreep): without this mod, the rover rises on its wheels
  at the docking and stays there; with it, it should stay where it was.
- **Typing a value in the part's window.** The spring and the damping ratio shown in the part's window do not take a
  typed value reliably yet: set them from the suspension window.
- **The checked cases with levelled suspensions.** The checked cases of every fix were played with suspensions set
  by prediction: as the vessel loads, or from the suspension window's *Auto*. Played again with their suspensions set
  by *Level on the ground*, a vessel that bounces, slides or tips over where it stood still would be a fault of the
  levelling.
