# Vessel Bodies

Part of [Suspension Bounce Fix](../README.md): a service the fixes share, not a fix of its own. It has no switch: it
runs whenever a fix that needs it does. It reads a vessel as one rigid body, once per vessel and per physics step.

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
- **Only when the end-stops need them**: each rigidbody's velocity and angular momentum, at the steps a wheel can reach
  its end-stop.

From these, the vessel as one body: its mass, its centre of mass, and its inertia about that centre, the inertia of
each rigidbody turned to world axes, plus its mass about the vessel's centre of mass.

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
move less than the whole vessel within a step; the fixes cover them with their own margins
([Damper Bounce](../damper-bounce/README.md#the-fix)).

Damper Bounce takes this mass at each wheel's contact, along the normal of the surface, to bound the wheel's damper.

## All the wheels at once

End-Stop Slide holds the end-stops of every wheel on the ground together: a force on one wheel also tilts the vessel,
and changes what the others have to hold. For each pair of wheels, it needs how a force at one moves the contact of
the other, through the mass and the inertia of the vessel: the matrix J · M⁻¹ · Jᵀ, J the directions of the contacts
and their arms about the centre of mass, M⁻¹ the inverse of the vessel's mass and inertia. For a single contact, it is
the 1 / m above.

### The vessel taken as one body

At each step where it follows the vessel, End-Stop Slide makes the vessel the rigid body Vehicle Physics Pro expects:
its mass, its centre of mass, its inertia (that of each part, brought back to the centre of mass), its velocity and its
rotation, drawn from the momentum and the angular momentum of all its parts. To that it adds the forces known for the
step: gravity, as KSP applies it to each part; the forces and torques of KSP's modules (engines, reaction wheels…); the
spring of each wheel; the force of each tyre, as Vehicle Physics Pro applied it.

### Checking it

What PhysX's end-stop still pushes is read through a balance of the whole vessel: what its momentum and its angular
momentum gain or lose over a step, minus all the known forces and the mod's own. The joints between parts cancel out
in it: a joint pushes one part as much as it pulls the other. The same balance on the wheel's part alone would need the
forces of its joints, which Unity gives in principle through `Joint.currentForce`; but on the joints between KSP parts,
`Joint.currentForce` is always zero, even under a load of several tons, a Unity bug known for years
([Joint.currentForce and Joint.currentTorque are always
zero](https://discussions.unity.com/t/joint-currentforce-and-joint-currenttorque-are-always-zero/675232)). While the
mod holds the end-stops, what is left is close to zero.

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

## Work in progress

- **Seeing these values in game.** Screenshots of
  [KSP Diag - Suspension](https://github.com/lhervier/KSP-Diag-Suspension) will show them here, wheel by wheel: the mass
  the vessel answers with at the contact, the mass the wheel sets in motion within a step, and the damper it gets.
