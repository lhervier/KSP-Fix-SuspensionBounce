# Vessel Bodies

Part of [Suspension Bounce Fix](../README.md): a service the fixes share, not a fix of its own. It has no switch: it
runs whenever a fix that needs it does. It reads a vessel as one rigid body, and the contact of each of its wheels
with the ground, once per vessel and per physics step, and holds contacts of the vessel within their limits for the
fixes that hand it some.

## One read per vessel and per step

[Damper Bounce](../damper-bounce/README.md) and [End-Stop Slide](../end-stop-slide/README.md) both take the vessel as
one rigid body: they need its mass, its centre of mass and its inertia, from every rigidbody of the vessel. A vessel at
rest pays for that read at every physics step, and a crane has a hundred rigidbodies: the vessel is read once per
vessel and per step, by whichever fix asks first, and the other uses that read. A shift of the floating origin within
the step, which moves every part, has the vessel read again.

- **Every rigidbody counts.** A part without physics has none: the game adds its mass to its parent's. A robotic part
  has two, its own and its moving half's (`Part.servoRb`), and KSP gives each half of the part's mass: both count.
- **At every step**: each rigidbody's centre of mass, and its rotation, read from the part's transform:
  `Rigidbody.rotation` would first sync the transforms of the whole scene.
- **Kept from one step to the next**: each rigidbody's mass and its own inertia, which only change with the part's
  resources and the vessel's structure. They are read again as soon as the mass KSP gives the part changes
  (`Part.physicsMass`, which `FlightIntegrator.UpdateMassStats` writes on the rigidbody at each step), as when fuel
  burns, and every 50 steps in any case.
- **Only when the contact solver needs them**: each rigidbody's velocity and angular momentum, at the steps a fix hands
  it contacts: for End-Stop Slide, the steps a wheel can reach its end-stop.

From these, the vessel as one body: its mass, its centre of mass, and its inertia about that centre, the inertia of
each rigidbody turned to world axes, plus its mass about the vessel's centre of mass.

## One read of the wheels' contacts per vessel and per step

Both fixes look at where each wheel touches the ground: Damper Bounce counts the wheels on the ground and takes the
point and the normal of each contact, End-Stop Slide measures the travel each wheel has left before its end-stop. They
read it once per vessel and per step, the same read, by whichever fix asks first.

- **The contact is a step old.** PhysX finds each wheel's contact at the start of its step, before it moves the
  vessel; the game reads it at the next step. Vehicle Physics Pro reads it in each wheel's own update, so that within a
  step a wheel not updated yet still holds the contact of the step before: the read asks PhysX itself, for every wheel
  of the vessel at once.
- **Where the wheel is now.** For a wheel coming towards its end-stop, the travel it has left is moved on by its speed
  over a step: that is what End-Stop Slide needs
  ([Where the wheel is now](../end-stop-slide/README.md#where-the-wheel-is-now)). Damper Bounce takes the contact as
  PhysX reports it. The only change a step brings that could be foreseen, a wheel leaving the ground, would change
  nothing for it: the damper of a wheel off the ground pushes nothing. A wheel that touches the ground at the coming
  step cannot be foreseen: PhysX reports no contact for a wheel in the air, and it is for that step that Damper Bounce
  bounds the damper of every wheel in the air ([Damper Bounce](../damper-bounce/README.md#the-fix)).
- **The wheels are those the game updates.** A vessel put on rails, by a time warp or when it is moved, gets new wheels
  from the game when it comes back, on the same parts: a wheel the game has not updated for more than two and a half
  steps is forgotten, or it would be read as a wheel of its own. At the first step of a vessel loaded or unpacked, the
  read is made again as the game updates each new wheel, so that every fix sees all of them.

## The mass at a contact: inertia, not load

Push a vessel at one point, along a direction n, and that point answers as a mass m: the whole mass M for a push
through the centre of mass, less the farther off that line the push is, since the vessel then turns as much as it
moves:

1 / m = 1 / M + (r × n) · I⁻¹ · (r × n)

r is the point from the centre of mass, I the vessel's inertia about it. This is **inertia, not load**: how much the
vessel resists a push at that point. It only depends on how the vessel's mass is spread, never on what holds it up,
so it can always be worked out, from the rigidbodies alone. The weight a wheel carries is another matter: as soon as
another support carries part of the vessel, it no longer follows from the masses (see
[Fixed Tuning](../fixed-tuning/README.md#the-culprits)).

Other supports, other wheels, the ground under another part, a base the vessel is docked to, can only make the vessel
resist more at that point, never less: the value worked out from the vessel alone is at worst below the true one, and
what is sized from it errs on the side of a lighter vessel. What it leaves out is joints that give, which let a push
move less than the whole vessel within a step; Damper Bounce keeps a margin for them
([Damper Bounce](../damper-bounce/README.md#the-fix)).

Damper Bounce takes this mass at each wheel's contact, along the normal of the surface, to bound the wheel's damper.

## The contact solver

A fix that has to keep several points of the vessel from going too far within the coming physics step hands them to
the contact solver, once per vessel and per step: each contact is a point on one of the vessel's rigidbodies, the
direction a force may push it along, how fast the contact closes or opens at a given velocity, and how much it may
close by the end of the step. The solver finds the smallest forces, never pulling, that keep every contact from
closing more than that, all together: a force at one contact also turns the vessel, and changes what the others need.
For each pair of contacts, it needs how a force at one moves the other, through the mass and the inertia of the
vessel: the matrix J · M⁻¹ · Jᵀ, J the directions of the contacts and their arms about the centre of mass, M⁻¹ the
inverse of the vessel's mass and inertia. For a single contact, it is the 1 / m above.

[End-Stop Slide](../end-stop-slide/README.md) is the fix that uses it: each wheel on the ground is a contact, along the
axis of its suspension, and what it may close is the travel it has left before its end-stop. The solver knows nothing
of wheels or end-stops: the fix says what a contact is, the solver how the vessel answers.

### The vessel taken as one body

At each step where a fix hands it contacts, the solver makes the vessel the rigid body Vehicle Physics Pro expects: its
mass, its centre of mass, its inertia (that of each part, brought back to the centre of mass), its velocity and its
rotation, drawn from the momentum and the angular momentum of all its parts. To that it adds the forces known for the
step: gravity, as KSP applies it to each part, and the forces and torques of KSP's modules (engines, reaction
wheels…), which it records as KSP hands them to the parts; and the forces the fix knows of, for End-Stop Slide the
spring of each wheel and the force of each tyre, as Vehicle Physics Pro applied it.

### One step ahead

For each contact, the solver works out how fast it would open at the end of the coming step without its forces, and
asks that this speed stay above the one that just closes what the contact may close in a step. A contact resting at
the end of what it may close gets its share of the load; a contact opening, or still far from closing, gets nothing; a
contact closing fast is slowed just enough to close it within the step. The forces are found by projected
Gauss-Seidel, none negative, and applied to the contacts' rigidbodies, at their points, along their directions.

### Sharing between the contacts

Four wheels for three equations (one force, two torques): there are endless ways to carry the same vessel. The solver
takes the evenest. A sharing left free to drift from one step to the next changes neither the weight carried nor the
torques, but ends up putting the whole vessel on two wheels across a diagonal: the other two tyres have no load left,
and the rover slides. The solver works it out in passes: the first finds the evenest sharing, the next ones keep that
sharing and make up what the first left short. Only the starting point comes from the step before.

### Checking it

What else pushed the vessel is read through a balance of the whole vessel: what its momentum gains or loses over a
step, minus all the known forces and the solver's own, along the mean normal of the contacts. For End-Stop Slide, it
is what PhysX's end-stop still pushes. The joints between parts cancel out in it: a joint pushes one part as much as
it pulls the other. The same balance on the wheel's part alone would need the forces of its joints, which Unity gives
in principle through `Joint.currentForce`; but on the joints between KSP parts, `Joint.currentForce` is always zero,
even under a load of several tons, a Unity bug known for years ([Joint.currentForce and Joint.currentTorque are always
zero](https://discussions.unity.com/t/joint-currentforce-and-joint-currenttorque-are-always-zero/675232)). While the
solver holds the end-stops, what is left is close to zero.

## What it cannot foresee

The solver plans the coming step from the vessel's velocities as it reads them and from the forces it knows. A
velocity changed after that read throws every force off by as much: Parking Brake, which sets the speed of every part
back to zero at each step, makes the forces holding a vessel at rest on its end-stops overshoot, one long side held at
a step, the other at the next ([Installing](../README.md#installing)). A wheel that crosses its whole travel within a
step meets PhysX's own end-stop, which the solver does not foresee either
([End-Stop Slide](../end-stop-slide/README.md#limits)).

## Performance

Timed with the game setting the springs, by the mod's own measure and by KSPProfiler, against the same scenarios
without the fixes: a rover driving on flat ground pays about 0.02 ms more per physics step, which no frame shows; the
dearest case found, a crane of 112 parts resting on its end-stops, about 0.14 ms more per physics step, or about
0.05 ms per frame.

**→ Full chapter: [Performance](performance.md)**

## In the mod's code

The read is `VesselBodies.Of`, in [Src/VesselBodies/VesselBodies.cs](../Src/VesselBodies/VesselBodies.cs): it reads the vessel at the first
call of a step, and hands the same read to every later call of that step. The vessel's inertia is worked out from it
once per step too, in [Src/VesselBodies/VesselInertia.cs](../Src/VesselBodies/VesselInertia.cs), and the mass at a contact is
`VesselInertia.EffectiveMass`:

```csharp
double inverseMass = 1.0 / mass;
if (inverse != null)
{
    Vector3d a = Vector3d.Cross(point - centerOfMass, direction);
    inverseMass += a.x * (inverse[0] * a.x + inverse[3] * a.y + inverse[4] * a.z)
        + a.y * (inverse[3] * a.x + inverse[1] * a.y + inverse[5] * a.z)
        + a.z * (inverse[4] * a.x + inverse[5] * a.y + inverse[2] * a.z);
}
return 1.0 / inverseMass;
```

`inverse` is the inverse of the inertia tensor about the centre of mass, in world axes; when the tensor cannot be
inverted, the vessel only resists being pushed, not being turned.

### `WheelContacts`

The read of the wheels' contacts is `WheelContacts.Of`, in
[Src/VesselBodies/WheelContacts.cs](../Src/VesselBodies/WheelContacts.cs), installed by whichever fix needs it. A postfix
of `VehicleBase.CalculateSuspensionTravel`, where the game has just read a wheel's contact, follows every wheel the
game updates (`WheelContacts.Wheel`). The read forgets the wheels the game no longer updates, then reads the contact of
each wheel of the vessel from PhysX (`WheelContacts.Read`, which the suspension window and Level also use for the
compression they show), and moves on the travel left for a wheel coming towards its end-stop (`Predict`):

```csharp
float upright = Mathf.Max(0.5f, Vector3.Dot(w.hit.normal, w.axis));
w.rate = w.axis / (upright * upright);
w.closing = -Vector3.Dot(w.body.GetPointVelocity(w.hit.point), w.rate);
w.gap = (1f - w.compression) * w.travel;
...
w.gap -= Mathf.Max(0f, w.closing) * dt;
```

The compression is measured where PhysX's `WheelCollider` really is (`ContactDepth`): Vehicle Physics Pro's own depth
leaves out the centre the part gives the wheel, which the LT-1 leg sets to −0.119 m.

### `ContactSolver`

The solver is `ContactSolver`, in [Src/VesselBodies/ContactSolver.cs](../Src/VesselBodies/ContactSolver.cs). A fix
hands it each contact as a `ContactSolver.Contact`: the rigidbody (`body`), the point (`point`), the direction the force
pushes along (`axis`), the vector a velocity is projected on to give how fast the contact opens (`rate`), and what it
may close in the step (`gap`); the solver writes the force back (`held`). Once per vessel and per step, the fix gets
the vessel's `ContactSolver.Balance` from `ContactSolver.Begin`, then either leaves the vessel alone for the step
(`ContactSolver.Idle`) or hands it all its contacts, those that touch, the forces it knows of and the mean normal of
the contacts (`ContactSolver.Solve`).

What KSP gives each part for the coming step is recorded in a prefix of `FlightIntegrator.Integrate(Part)`
(`IntegratePrefix`): the vessel's acceleration (`vessel.precalc.integrationAccel`, gravity included), and the forces
and torques its modules gathered (`part.force`, `part.forces`, `part.torque`).

`Solve` reads each rigidbody's velocity and angular momentum (`ReadMotion`), on top of the read the whole mod shares,
then sums the known forces and checks what else pushed the vessel at the last step
([Checking it](#checking-it)):

```csharp
VesselBodies read = VesselBodies.Of(balance.vessel);
read.ReadMotion();
```

The vessel as one body (`HoldContacts`): the momentum, the angular momentum and the inertia of all its parts, about
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

For each contact that touches, measured along `c.rate`, the speed at which it would open at the end of the step
without the solver's force, plus the speed that would just close its gap; the forces, along the axis, must not bring
it below zero:

```csharp
float gap = c.gap;
float separating = Vector3.Dot(c.rate, velocity + Vector3.Cross(spin, arm));
rhs[k] = separating + dt * (Vector3.Dot(c.rate, acceleration) + Vector3.Dot(holdRow[k], angularAcceleration)) +
    gap / dt;
```

How a force at one contact moves another, through the mass and the inertia of the vessel, is `a[k * n + j]`: the force
pushes along `axis`, the contact opens along `rate`, with their arms about the centre of mass (`holdTurn`, `holdRow`).
The forces are found by projected Gauss-Seidel, none negative, in passes (`Sweep`): the first pulls them towards zero,
the next ones towards the answer of the pass before ([Sharing between the contacts](#sharing-between-the-contacts)):

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

Each force is applied to its contact's rigidbody, at the contact point:

```csharp
c.heldForce = c.axis * f[k];
c.heldPoint = c.point;
if (f[k] > 0f && c.body != null)
{
    c.body.AddForceAtPosition(c.heldForce, c.heldPoint, ForceMode.Force);
    ...
}
```

## Work in progress

- **Seeing these values in game.** Screenshots of
  [KSP Diag - Suspension](https://github.com/lhervier/KSP-Diag-Suspension) will show them here, wheel by wheel: the mass
  the vessel answers with at the contact, the mass the wheel sets in motion within a step, and the damper it gets.
