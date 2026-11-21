# End-Stop Slide

Part of [Suspension Bounce Fix](../README.md): a fix of what the tyres read when a wheel rests on the end of its
travel. Here is what it fixes:

> **In KSP, a rover whose wheels rest on their end-stops slides: sideways on flat ground, and down a slope, brakes
> on.**

<ROVER ON ITS END-STOPS, GOING ANYWHERE AS IT DRIVES>

The cause: the tyres read the load of a wheel in the force of its spring alone, and the end-stop that carries the rest
of the weight reports it nowhere (see [The culprits](#the-culprits)).

## Why it matters

A wheel reaches its end-stop more often than it seems: on a heavy rover on the Mun, under the low wheel of a rover
standing across a slope, under the front wheels when braking, when a crane lifts a heavy load, at a hard landing, or
with springs set soft. The player's setting ([Fixed Tuning](../fixed-tuning/README.md)) changes nothing there: a
setting no longer follows the load, and a crane that lifts a heavy load sinks lower on its wheels, as a real one
would, down to its end-stops if the load is heavy enough. On its end-stops, a rover loses the grip that should hold it
back: it slides sideways as it drives, as on ice, and goes down a slope, brakes on, at a constant speed, without ever
stopping.

## The culprits

### A bottomed wheel hides its weight from the tyres

KSP's tyres go through Vehicle Physics Pro (VPP), which does not weigh the vessel: it reads each wheel's load in the
force of its suspension, and draws from it the tyre's grip, what holds it on a slope and what stops it sliding. When a
wheel reaches its end-stop, the end-stop carries the rest of the weight, but its force is reported nowhere: VPP
believes the wheel carries less than it does. And the end-stop only acts at the steps that start with the wheel
already past the end of its travel: it holds the vessel in jolts, one step on, one step off, and the tyres slip at
each one. VPP is written for a car made of one rigid body; KSP makes each wheel a vehicle of its own, on its own part.

**→ Full chapter: [A bottomed wheel hides its weight from the tyres](a-bottomed-wheel-hides-its-weight-from-the-tyres.md)**

## Checking the culprit

Each case has its page: the save or craft it starts from, how to play it by hand, the script that plays it, and what
it shows without and with this mod. With this mod, they were played with its default settings, then again with Fixed
Tuning alone (`damperBounce = false` and `endStopSlide = false` in its `settings.cfg`), and, for the rover on a slope
and the lander, with Fixed Tuning and this fix (`damperBounce = false`). This mod hides the game's sliders: where a
case needs soft springs, they are set from its suspension window, *Auto* with a typed mass of 0.01 t.

### A rover on its end-stops

A rover whose springs are set soft, on Kerbin: its wheels rest on their end-stops. Without this mod, its tyres read a
fraction of its weight: it slides sideways on flat ground, and slides down a slope, brakes on. With Fixed Tuning
alone, they read about a third of it: the rover already keeps its line on flat ground, but slides down the slope. With
this mod, the tyres read the whole weight, and it holds.

**→ Full chapter: [A rover on its end-stops](checked/a-rover-on-its-end-stops.md)**

### A lander dropped onto its end-stops

A lander on four LT-1 legs, its springs set soft, dropped from its launch clamps onto the runway: it comes to rest on
its end-stops. Without this mod, its feet read none of its weight, and it slowly turns and slides; with it, it stays
still. With Fixed Tuning alone, it bounces back up from the fall much faster than with this mod: this fix and Damper
Bounce each take part of that bounce away.

**→ Full chapter: [A lander dropped onto its end-stops](checked/a-lander-dropped-onto-its-end-stops.md)**

### A rover driving in a straight line

A rover set off from standstill on flat ground, throttle held, its steering let go: not a culprit, a check that this
mod does not make a rover turn on its own. Set off from the same place, it keeps its line within a degree in 30 s,
with or without this mod, and with Fixed Tuning alone.

**→ Full chapter: [A rover driving in a straight line](checked/a-rover-driving-in-a-straight-line.md)**

## The fix

The mod holds the end-stops of the wheels on the ground itself, and tells the tyres what it holds. A single mechanism
answers both faults of the end-stop: the force is its own, it no longer has to look for it.

### The vessel taken as one body

At each step where it follows the vessel ([When it works](#when-it-works)), the mod takes the vessel as the rigid body
VPP expects, all its wheels at once, from the read of [Vessel Bodies](../vessel-bodies/README.md#all-the-wheels-at-once):
its mass, its centre of mass, its inertia, its velocity and its rotation, and the forces known for the step. The same
read checks the result, through a balance of the whole vessel
([Checking it](../vessel-bodies/README.md#checking-it)).

### One step ahead

For each wheel on the ground, the mod knows the travel it has left before its end-stop. It looks for the smallest
forces, never pulling, such that by the end of the coming step no wheel has gone past the end of its travel. The
wheels are handled together: a force on one wheel also tilts the vessel, and changes what the others have to hold. A
wheel resting on its end-stop gets its share of the load, beyond what its spring carries; a wheel leaving it, or still
far from it, gets nothing; a wheel coming fast is slowed just enough to reach it. These forces are applied to the
wheels' parts, at the contact point, along the axis of the suspension, as the end-stop they replace: PhysX's end-stop
has nothing left to correct, and no longer beats. The axis matters at speed: measured along the normal of the ground
instead, a normal tilted for a single step, by a bump or a seam between two pieces of terrain, would turn part of the
forward speed into a speed towards the end-stop, and call for a force many times the weight.

### Sharing between the wheels

Four wheels for three equations (one force, two torques): there are endless ways to carry the same vessel. The mod
takes the evenest. A sharing left free to drift from one step to the next changes neither the weight carried nor the
torques, but ends up putting the whole vessel on two wheels across a diagonal: the other two tyres have no load left,
and the rover slides. The mod works it out in passes: the first finds the evenest sharing, the next ones keep that
sharing and make up what the first left short.

### What the tyres see

The force the mod applies to a wheel is added, as it is, to the one VPP reads for it, at the same step: grip, holding
on the slope and the pull back against sliding get the whole load back. The wheel damage module, which also reads the
tyre's load to spot impacts in it, keeps the one it read without the mod.

### When it works

The mod only follows a vessel while one of its wheels could reach its end-stop during the coming step: while the travel
the wheel has left is no more than what it would cover in a step, at its speed towards the end-stop plus a step of
free fall. The rest of the time, it only reads the wheels' contacts.

The `endStopSlide` setting of the mod's `settings.cfg` file turns this fix off. **This fix is not supported with
`fixedTuning = false`.**

### In the mod's code

Everything is in [Src/EndStopSlide/EndStopSlide.cs](../Src/EndStopSlide/EndStopSlide.cs).

What KSP gives each part for the coming step is recorded in a prefix of `FlightIntegrator.Integrate(Part)`
(`IntegratePrefix`): the vessel's acceleration (`vessel.precalc.integrationAccel`, gravity included), and the forces
and torques its modules gathered (`part.force`, `part.forces`, `part.torque`). The force VPP has just applied to each
tyre is recorded in a postfix of `VehicleBase.ApplyTireForce` (`ApplyTireForcePostfix`).

The work is done in a postfix of `VehicleBase.CalculateSuspensionForces`, which VPP calls at each step for each wheel,
once the spring force is read and before the tyre's load is drawn from it; only once per vessel and per step, at its
first wheel (`Solve`). It first reads the contact of each wheel, and follows the vessel only if one of them could reach
its end-stop during the step:

```csharp
float closing = -Vector3.Dot(p.body.GetPointVelocity(p.hit.point), p.axis);
near = (1f - p.compression) * travel <= Mathf.Max(0f, closing + fall) * dt;
```

The vessel's rigidbodies come from [Vessel Bodies](../vessel-bodies/README.md), read once per vessel and per step for
the whole mod (`VesselBodies.Of`). `Solve` adds what only the end-stops need, read at the steps it follows the vessel:
each body's velocity and angular momentum (`ReadMotion`). Why the reads are kept this short:
[Keeping the cost low](../vessel-bodies/performance.md#keeping-the-cost-low).

```csharp
VesselBodies read = VesselBodies.Of(vessel);
read.ReadMotion();
```

The vessel as one body (`HoldEndStops`): the momentum, the angular momentum and the inertia of all its parts, about
its centre of mass:

```csharp
momentum += p;
angularMomentum += b.spin + Vector3.Cross(offset, p);
inertia.AddPrincipal(b.frame, b.inertia);
inertia.AddPoint(b.mass, offset);
...
Vector3 velocity = momentum / mass;
Vector3 spin = inverse.Times(angularMomentum);
Vector3 acceleration = known / mass;
Vector3 angularAcceleration = inverse.Times(knownTorque);
```

For each wheel on the ground, along the axis of its suspension (`normal` is that axis, read with the contact), the
speed at which its contact would move away from its end-stop at the end of the step without the mod's force, plus the
speed that would just close the travel it has left; the mod's forces must not bring it below zero:

```csharp
float gap = Mathf.Max(0f, (1f - p.compression) * travel);
float separating = Vector3.Dot(normal, velocity + Vector3.Cross(spin, arm));
rhs[k] = separating + dt * (Vector3.Dot(normal, acceleration) + Vector3.Dot(holdArm[k], angularAcceleration)) +
    gap / dt;
```

How a force on one wheel moves the contact of another, through the mass and the inertia of the vessel, is
`a[k * n + j]`. The forces are found by projected Gauss-Seidel, none negative, in passes (`Sweep`): the first pulls
them towards zero, the next ones towards the answer of the pass before:

```csharp
float s = -rhs[k] / dt + lambda * target[k];
for (int j = 0; j < n; j++)
{
    if (j != k)
    {
        s -= a[k * n + j] * f[j];
    }
}
float value = s / (a[k * n + k] + lambda);
value = float.IsNaN(value) || value < 0f ? 0f : value;
```

Each force is applied to its wheel's part, at the contact point:

```csharp
p.heldForce = p.axis * f[k];
p.heldPoint = p.hit.point;
if (f[k] > 0f && p.body != null)
{
    p.body.AddForceAtPosition(p.heldForce, p.heldPoint, ForceMode.Force);
}
```

and added to the force VPP is about to read for that wheel:

```csharp
probe.injected = probe.held;
wheel.hit.force += probe.injected;
```

A postfix of `VehicleBase.CalculateDownforce` keeps the tyre's load as it would have been without the mod, and a
prefix and a postfix of `ModuleWheelDamage.FixedUpdate` show it to the damage module while it updates.

### Limits

- **A wheel landing hard enough to cross its whole travel in a single physics step still meets the game's end-stop.**
  The mod holds an end-stop one step ahead; a wheel that goes from the top of its travel to past the end of it within
  one step is caught by PhysX's end-stop, as without this mod, and held by the mod from the next step on.

## Work in progress

- **A rover lurching as this fix takes hold on a slope.** The rover of
  [A rover on its end-stops](checked/a-rover-on-its-end-stops.md), loaded on its slope, its springs then set soft from
  the suspension window (*Auto*, a typed mass of 0.01 t): with this fix, it lurches at the start, at up to 0.7 m/s,
  and turns by a few degrees before it holds; with `endStopSlide = false`, it does not lurch, but slides down. Why it
  lurches is not known yet.
- **A lander bouncing off its end-stops.** The lander of
  [A lander dropped onto its end-stops](checked/a-lander-dropped-onto-its-end-stops.md), its springs set soft, dropped
  onto the runway: how high it bounces back, with this fix and without it. Watch it fall and bounce from its launch
  clamps, with the mod as installed, then with `endStopSlide = false`.
