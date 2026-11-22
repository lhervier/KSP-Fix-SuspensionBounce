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

Each case has its page: the save or craft it starts from, how to play it by hand, the script that plays it, and what it
shows without and with this mod. With this mod, they were played without Parking Brake, which it is not compatible with
([Installing](../README.md#installing)), with its default settings, then again with Fixed Tuning alone
(`damperBounce = false` and `endStopSlide = false` in its `settings.cfg`), and with Fixed Tuning and this fix
(`damperBounce = false`). This mod hides the game's sliders: where a case needs soft springs, its save or craft holds
them twice, the game's sliders at their lowest for the game without this mod, and the setting this mod's suspension
window makes (*Auto* with a typed mass of 0.01 t) for this mod.

The saves were made, and the cases played, with KSP's terrain detail on *High* (*Settings*, *Graphics*): loaded at
another detail, a save puts its vessel on a ground a little off.

### A rover on its end-stops

A rover whose springs are set soft, on Kerbin: its wheels rest on their end-stops. Without this mod, its tyres read a
fraction of its weight: it slides sideways on flat ground, and slides down a slope, brakes on. With Fixed Tuning
alone, they read a little over a third of it: the rover already keeps its line on flat ground, but slides down the
slope at about 0.9 m/s. With
this mod, the tyres read the whole weight, and it holds.

**→ Full chapter: [A rover on its end-stops](checked/a-rover-on-its-end-stops.md)**

### A lander dropped onto its end-stops

A lander on four LT-1 legs, its springs set soft, dropped from its launch clamps onto the runway: it comes to rest on
its end-stops. Without this mod, its feet read none of its weight, and it slowly turns and slides; with it, it stays
still. With Fixed Tuning alone, it bounces back up from the fall at up to 6 m/s, its legs leaving the ground, in two
trials out of three; with this fix, the legs catch it on their end-stops, and it comes back up at a third of a metre
per second at most.

**→ Full chapter: [A lander dropped onto its end-stops](checked/a-lander-dropped-onto-its-end-stops.md)**

### A rover on its end-stops after a time warp

The rover of the slope, its time warped on rails for a minute, as a player does near a rover at rest: not a culprit,
a check that this fix still holds it once the game is back to normal time: its tyres read the whole weight of the
rover, as before the warp.

**→ Full chapter: [A rover on its end-stops after a time warp](checked/a-rover-on-its-end-stops-after-a-time-warp.md)**

### A rover driving in a straight line

A rover set off from standstill on flat ground, throttle held, its steering let go: not a culprit, a check that this
mod does not make a rover turn on its own. Set off from the same place, it turns by about half a degree in 30 s
without this mod, and by about a tenth of a degree with it, in each of its configurations.

**→ Full chapter: [A rover driving in a straight line](checked/a-rover-driving-in-a-straight-line.md)**

## The fix

The mod holds the end-stops of the wheels on the ground itself, and tells the tyres what it holds. A single mechanism
answers both faults of the end-stop: the force is its own, it no longer has to look for it.

### The vessel taken as one body

At each step where it follows the vessel ([When it works](#when-it-works)), the mod hands every wheel on the ground to
the contact solver of [Vessel Bodies](../vessel-bodies/README.md#the-contact-solver), which takes the vessel as the
rigid body VPP expects, all its wheels at once: its mass, its centre of mass, its inertia, its velocity and its
rotation, and the forces known for the step, among them the spring of each wheel and the force of each tyre, which the
mod hands it too. The solver checks the result through a balance of the whole vessel
([Checking it](../vessel-bodies/README.md#checking-it)).

### One step ahead

For each wheel on the ground, the mod knows the travel it has left before its end-stop. The solver looks for the
smallest forces, never pulling, such that by the end of the coming step no wheel has gone past the end of its travel.
The wheels are handled together: a force on one wheel also tilts the vessel, and changes what the others have to hold.
A wheel resting on its end-stop gets its share of the load, beyond what its spring carries; a wheel leaving it, or
still far from it, gets nothing; a wheel coming fast is slowed just enough to reach it. These forces are applied to
the wheels' parts, at the contact point, along the axis of the suspension, as the end-stop they replace: PhysX's
end-stop has nothing left to correct, and no longer beats. The axis matters at speed: measured along the normal of the
ground instead, a normal tilted for a single step, by a bump or a seam between two pieces of terrain, would turn part
of the forward speed into a speed towards the end-stop, and call for a force many times the weight.

Among the endless ways four wheels can carry the same vessel, the solver takes the evenest
([Sharing between the contacts](../vessel-bodies/README.md#sharing-between-the-contacts)).

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

### After a time warp

A vessel put on rails, by a time warp or when it is moved, gets new wheels from VPP when it comes back, on the same
parts. The mod forgets a wheel that VPP has not updated for more than two and a half steps: the old one would
otherwise be held as a wheel of its own, and take its share of the load away from the tyres that remain.

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

The fix is in [Src/EndStopSlide/EndStopSlide.cs](../Src/EndStopSlide/EndStopSlide.cs); the solver it hands the wheels
to, and how it finds and applies the forces, are in
[Vessel Bodies](../vessel-bodies/README.md#in-the-mods-code).

The work is done in a postfix of `VehicleBase.CalculateSuspensionForces`, which VPP calls at each step for each wheel,
once the spring force is read and before the tyre's load is drawn from it; only once per vessel and per step, at its
first wheel (`Solve`). Each wheel has a probe (`Probe`), which is also its contact for the solver. Each step starts by
forgetting the wheels VPP no longer updates ([After a time warp](#after-a-time-warp)):

```csharp
if (p.lastTime >= 0f && now - p.lastTime > 2.5f * dt)
{
    probes.Remove(p.state);
    Probes.RemoveAt(i);
}
```

It then reads the contact of each wheel: the axis of its suspension (`p.axis`), how fast it closes its travel, along
that axis over the square of the axis's share along the ground normal (`p.rate`), and the travel it has left, moved on
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

When it does, it hands the solver every wheel of the vessel, those on the ground, and the forces it knows acted on them
over the last step: the spring, along the contact normal, and the tyre force VPP applied, which a postfix of
`VehicleBase.ApplyTireForce` records (`ApplyTireForcePostfix`). `Hold` says whether the solver applies the forces it
finds:

```csharp
known.Add(new ContactSolver.KnownForce { force = p.hit.normal * p.springForce, point = p.hit.point });
if (p.lastTime >= 0f && Mathf.Abs(now - p.lastTime - dt) < dt * 0.25f)
{
    known.Add(new ContactSolver.KnownForce { force = p.lastTyreForce, point = p.lastTyrePoint });
}
normal += p.hit.normal;
touching.Add(p);
...
ContactSolver.Solve(balance, contacts, touching, known, normal, Hold);
```

The force the solver applied to a wheel at this step is added to the force VPP is about to read for it:

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
