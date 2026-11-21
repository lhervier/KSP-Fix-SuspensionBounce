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
Tuning alone (`damperBounce = false` and `endStopSlide = false` in its `settings.cfg`), and with Fixed Tuning and this
fix (`damperBounce = false`). This mod hides the game's sliders: where a case needs soft springs, its save or craft
holds them twice, the game's sliders at their lowest for the game without this mod, and the setting this mod's
suspension window makes (*Auto* with a typed mass of 0.01 t) for this mod.

### A rover on its end-stops

A rover whose springs are set soft, on Kerbin: its wheels rest on their end-stops. Without this mod, its tyres read a
fraction of its weight: it slides sideways on flat ground, and slides down a slope, brakes on. With Fixed Tuning
alone, they read about a third of it: the rover already keeps its line on flat ground, but slides down the slope. With
this mod, the tyres read the whole weight, and it holds.

**→ Full chapter: [A rover on its end-stops](checked/a-rover-on-its-end-stops.md)**

### A lander dropped onto its end-stops

A lander on four LT-1 legs, its springs set soft, dropped from its launch clamps onto the runway: it comes to rest on
its end-stops. Without this mod, its feet read none of its weight, and it slowly turns and slides; with it, it stays
still. With Fixed Tuning alone, it bounces back up from the fall at 5 m/s, its legs leaving the ground; with this fix,
the legs catch it on their end-stops, and it comes back up at a quarter of a metre per second.

**→ Full chapter: [A lander dropped onto its end-stops](checked/a-lander-dropped-onto-its-end-stops.md)**

### A rover on its end-stops after a time warp

The rover of the slope, its time warped on rails for a minute, as a player does near a rover at rest: not a culprit,
a check that this fix still holds it once the game is back to normal time: its tyres read the whole weight of the
rover, as before the warp.

**→ Full chapter: [A rover on its end-stops after a time warp](checked/a-rover-on-its-end-stops-after-a-time-warp.md)**

### A rover driving in a straight line

A rover set off from standstill on flat ground, throttle held, its steering let go: not a culprit, a check that this
mod does not make a rover turn on its own. Set off from the same place, it keeps its line within a degree in 30 s,
with or without this mod. With Fixed Tuning and this fix but without Damper Bounce, it fell on its side as it loaded,
before it was set off, in two trials out of eight.

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

### Where the wheel is now

The contact the game reports for a wheel is the one found at the start of the last step: the wheel has moved since,
by its speed over that step. For a wheel at rest, it makes no difference. For a wheel coming fast, it does: a lander
falling at 9 m/s onto its LT-1 legs closes a fifth of their travel at each step. Read as reported, a leg seems a step
short of its end-stop when it is already past it; PhysX's end-stop then pushes the whole overshoot back out within
that step, whatever force the mod adds, and the lander bounces back up. So the mod moves each wheel coming towards
its end-stop on by its speed over a step before it measures the travel it has left; a wheel at rest on its end-stop,
or leaving it, is measured as reported.

The travel is measured along the axis of the suspension, down to the ground. A leg that leans out, as the LT-1 does,
closes its travel faster than the lander comes down, and the lander comes down faster than the leg moves along its
axis: the speed along the axis is divided by the square of the axis's share along the normal of the ground. Only that
share comes from the ground, not the direction of its normal, for the reason above.

### A part of the way at a time, when slow

The vessel answers a force at a wheel more than the rigid body the mod takes it for: a force found in full makes it
overshoot, and the next step swings back. On a vessel at rest on its end-stops, that rocking keeps going: one long
side of a rover held at a step, the other at the next, the whole rover tilting back and forth, and creeping down its
slope. So, while the vessel is slower than 2 m/s, each force moves only 30 % of the way from the one of the step
before to the one found: the rocking dies out. Faster, the forces move in full at every step, so that the tyres keep
reading the weight over the bumps.

The forces that stop a hard landing are many times the vessel's weight: kept at 70 % a step once the vessel is slow,
they would throw it back up. They are let go at once: the forces move only part of the way while those of the step
before add up to no more than twice the weight.

### After a time warp

A vessel put on rails, by a time warp or when it is moved, gets new wheels from VPP when it comes back, on the same
parts. The mod forgets a wheel that VPP has not updated for more than two and a half steps: the old one would
otherwise be held as a wheel of its own, and take its share of the load away from the tyres that remain.

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

Each step starts by forgetting the wheels VPP no longer updates ([After a time warp](#after-a-time-warp)):

```csharp
if (p.lastTime >= 0f && now - p.lastTime > 2.5f * dt)
{
    probes.Remove(p.state);
    Probes.RemoveAt(i);
}
```

What KSP gives each part for the coming step is recorded in a prefix of `FlightIntegrator.Integrate(Part)`
(`IntegratePrefix`): the vessel's acceleration (`vessel.precalc.integrationAccel`, gravity included), and the forces
and torques its modules gathered (`part.force`, `part.forces`, `part.torque`). The force VPP has just applied to each
tyre is recorded in a postfix of `VehicleBase.ApplyTireForce` (`ApplyTireForcePostfix`).

The work is done in a postfix of `VehicleBase.CalculateSuspensionForces`, which VPP calls at each step for each wheel,
once the spring force is read and before the tyre's load is drawn from it; only once per vessel and per step, at its
first wheel (`Solve`). It first reads the contact of each wheel: how fast it closes its travel, along the axis of its
suspension over the square of the axis's share along the ground normal (`p.rate`), and the travel it has left, moved on
by a step for a wheel coming towards its end-stop ([Where the wheel is now](#where-the-wheel-is-now)). It follows the
vessel only if one of them could reach its end-stop during the step:

```csharp
float upright = Mathf.Max(0.5f, Vector3.Dot(p.hit.normal, p.axis));
p.rate = p.axis / (upright * upright);
float closing = -Vector3.Dot(p.body.GetPointVelocity(p.hit.point), p.rate);
p.gap = (1f - p.compression) * travel;
...
p.gap -= Mathf.Max(0f, closing) * dt;
...
near |= p.gap <= Mathf.Max(0f, closing + fall) * dt;
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

For each wheel on the ground, measured along `p.rate`, the speed at which its contact would open its travel at the end
of the step without the mod's force, plus the speed that would just close the travel it has left; the mod's forces,
along the axis, must not bring it below zero:

```csharp
float gap = p.gap;
float separating = Vector3.Dot(p.rate, velocity + Vector3.Cross(spin, arm));
rhs[k] = separating + dt * (Vector3.Dot(p.rate, acceleration) + Vector3.Dot(holdRow[k], angularAcceleration)) +
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

While the vessel is slow, and the forces of the step before add up to no more than twice its weight, each force moves
only a part of the way from the one of the step before ([A part of the way at a time, when
slow](#a-part-of-the-way-at-a-time-when-slow)):

```csharp
if ((RelaxBelowSpeed < 0f || balance.vessel.srfSpeed < RelaxBelowSpeed) && before <= RelaxBelowWeights * weight)
{
    for (int k = 0; k < n; k++)
    {
        Probe p = groundedProbes[k];
        float previous = Mathf.Abs(now - p.heldTime - dt) < dt * 0.25f ? p.held : 0f;
        f[k] = previous + Relaxation * (f[k] - previous);
    }
}
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

- **A rover falling on its side as it loads, without Damper Bounce.** The rover of
  [A rover driving in a straight line](checked/a-rover-driving-in-a-straight-line.md), with Fixed Tuning and this fix
  (`damperBounce = false`), fell on its side as the save loaded in two trials out of eight; not with the mod's default
  settings, nor with Fixed Tuning alone. Why is not known yet.
- **A rover on its end-stops driving slowly.** Below 2 m/s, each force moves only a part of the way at each step
  ([A part of the way at a time, when slow](#a-part-of-the-way-at-a-time-when-slow)): how much of the weight the tyres
  read over bumps at that speed is not checked yet.
- **Why the vessel answers a force more than the rigid body taken for it.** It is what makes the relaxation
  necessary; where the difference comes from is not known yet.
