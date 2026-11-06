# Troisième coupable : une roue en butée cache son poids aux pneus

Part of [Suspension Bounce Fix](../../README.md), [the culprits](../../README.md#the-culprits) : un coupable qui fait
glisser.

*(Page en français, à traduire avant le commit.)*

Les pneus de KSP ne sont pas ceux de Unity : les roues, les jambes et les trains passent par Vehicle Physics Pro (VPP),
une bibliothèque de physique de véhicules que KSP embarque. La suspension, elle, reste celle de Unity : un
`WheelCollider`, ressort et amortisseur, qui mesure son enfoncement et pousse le vaisseau.

## Ce que VPP lit dans la suspension

Pour savoir ce que peut faire un pneu, VPP a besoin de savoir combien la roue porte. Il ne pèse pas le vaisseau : il
lit la force que la suspension vient d'appliquer, ressort et amortisseur, et s'en sert trois fois.

- **L'adhérence** : la force la plus grande que le pneu peut donner, en freinant, en accélérant ou en travers, est
  proportionnelle à cette charge. Une roue qui porte deux fois moins tient deux fois moins.
- **Le maintien sur une pente** : à l'arrêt ou presque, VPP pousse chaque roue vers l'amont de ce qu'il faut pour
  que la charge qu'elle porte ne glisse pas.
- **Le rappel contre le glissement** : quand la roue glisse sur le sol, VPP la freine d'autant plus fort que la masse
  qu'elle porte est grande, pour arrêter ce glissement en un pas.

Tant que la suspension porte tout le poids, ces trois calculs sont justes.

## La butée

Une suspension a une course. Quand la roue porte plus que ce que le ressort peut tenir dans cette course, elle arrive
en butée, et c'est la butée qui porte le reste : rien d'anormal, une vraie voiture fait pareil. Dans KSP, une roue
arrive en butée sur un rover lourd sur la Mune, sous la roue basse d'un rover en travers d'une pente, sous les roues
avant au freinage, ou quand le joueur règle ses ressorts très mous.

Mais la force de la butée, la physique l'applique sans la rapporter : la force de contact que lit VPP ne contient que
celle du ressort. Pour VPP, une roue en butée porte ce que son ressort pousse, pas ce qu'elle porte vraiment. La part
qui passe par la butée disparaît de ses trois calculs :

- l'adhérence baisse, jusqu'au plancher que VPP garde à chaque pneu ;
- le maintien sur la pente ne compense plus qu'une partie de la pente ;
- le rappel contre le glissement s'affaiblit d'autant.

Aucune force n'est inventée : c'est une force qui manque, et le rover ne reçoit plus celle qui devrait le retenir.

## Ce qu'on voit

**Sur du plat**, toutes roues en butée, le pneu ne tient presque plus rien en travers. Le rover garde son cap mais
glisse de côté en roulant, comme sur de la glace : sur la navball, le marqueur de vitesse s'écarte du cap.

<ROVER QUI GLISSE SUR DU PLAT>

**Sur une pente**, même freins serrés et pneus sous leur limite, le rover descend. Il ne prend pas de vitesse : le
pneu ne retient la part manquante qu'en glissant, et retient d'autant plus qu'il glisse vite. Le rover trouve la
vitesse où les deux s'équilibrent, et la garde.

<ROVER QUI GLISSE DANS UNE PENTE SANS ACCELERER>

## Le voir

[KSP Diag - Suspension](https://github.com/lhervier/KSP-Diag-Suspension) affiche, roue par roue, l'enfoncement de la
suspension, la butée, la force du ressort et l'adhérence que VPP en tire, et pour le vaisseau, la part de son poids
que les ressorts portent, sa vitesse latérale et l'écart entre sa vitesse et son cap. Sur n'importe quel rover, en
jeu, avec l'option *Advanced Tweakables* :

1. Dans la fenêtre d'une roue, passer *Ressorts/Amortisseurs* en *Manuel*, puis mettre *Force ressorts* au minimum.
   Faire de même sur toutes les roues.
2. Rouler sur du plat.

Toutes les roues sont en butée, la part portée par les ressorts tombe près de zéro, et l'adhérence de chaque pneu à
son plancher : le rover glisse de côté.

## Dans le code

Dans `VehiclePhysics.VehicleBase`, la force de contact `hit.force` est celle que le `WheelCollider` de Unity a
appliquée au dernier pas :

```csharp
wheel.grounded = wheelCol.GetGroundHit(out wheel.hit);
```

La charge du pneu, d'où vient son adhérence, en est tirée, avec un plancher (`CalculateDownforce`) :

```csharp
wheel.downforce = wheel.hit.force + wheel.wheelCol.runtimeExtraDownforce;
...
if (wheel.downforce < minimumdownForce)
{
    wheel.downforce = minimumdownForce;
}
```

Le maintien sur la pente et la masse portée aussi (`CalculateLocalFrame`) :

```csharp
Vector3 verticalForce = gravity.Up * wheel.hit.force / upDotNormal;
wheel.surfaceForce = verticalForce - Vector3.Project(verticalForce, wheel.hit.normal);
...
float supportedMass = (wheel.hit.force + wheel.wheelCol.runtimeExtraDownforce) * (1f / gravity.Magnitude);
...
Vector2 impulseForce = (0f - tireImpulseRatio) * supportedMass * wheel.localWheelVelocity / Time.deltaTime;
wheel.externalTireForce = impulseForce + wheel.localSurfaceForce;
```

## Ce que change le mod

Le mod ne touche pas à la butée : il retrouve sa force, et la donne à VPP. Tout le travail est de la retrouver, car ni
PhysX ni Unity ne la donnent.

### Une force que personne ne rapporte

La butée d'un `WheelCollider` est une contrainte que PhysX résout avec le reste de la physique, comme un contact. Unity
ne la rapporte nulle part : `WheelHit.force`, la seule force que donne le `WheelCollider`, est celle du ressort et de
l'amortisseur. Il n'y a rien d'autre à lire.

### Le chemin direct, fermé par Unity

L'idée la plus simple est un bilan sur la seule pièce de la roue. La physique dit, à chaque pas, de combien sa vitesse
a changé ; on connaît sa masse, la gravité, le ressort et la force du pneu. Il manquerait les forces des liaisons qui
la tiennent au reste du vaisseau : Unity les donne en principe par `Joint.currentForce`. Mais sur les liaisons des
pièces de KSP, `Joint.currentForce` vaut toujours zéro, même sous une charge de plusieurs tonnes : un bug de Unity
connu depuis des années ([Joint.currentForce and Joint.currentTorque are always
zero](https://discussions.unity.com/t/joint-currentforce-and-joint-currenttorque-are-always-zero/675232)). Sans ces
forces, le bilan de la pièce n'a pas de sens.

### Le bilan du vaisseau entier

Le mod fait donc le bilan sur tout le vaisseau, où ces forces n'ont plus besoin d'être connues : une liaison pousse
une pièce autant qu'elle tire l'autre, et dans la somme sur toutes les pièces, ses deux forces s'annulent.

À chaque pas, le mod relève pour chaque pièce la variation de sa vitesse et de sa rotation : la somme, sur tout le
vaisseau, de ce que gagnent ou perdent sa quantité de mouvement et son moment cinétique, c'est la somme de toutes les
forces extérieures reçues pendant le pas, et de leurs couples. Le mod en retire toutes celles qu'il connaît :

- la gravité, telle que KSP l'applique à chaque pièce ;
- les forces et couples que les modules de KSP ont appliqués (moteurs, roues de réaction…) ;
- la force de chaque ressort, le long de la normale du contact, à son point de contact ;
- la force de chaque pneu, telle que VPP l'a appliquée.

Ce qui reste, c'est ce que le sol a donné en plus des ressorts : la force des butées. Quand une seule roue est en
butée, elle est toute à elle. Quand il y en a plusieurs, le mod la partage de façon à expliquer aussi les couples :
une roue avant et une roue arrière, une roue en haut et une roue en bas d'une pente, ne portent pas la même part. Il
cherche les forces, toutes positives, qui expliquent au mieux à la fois la force qui reste et ses couples autour des
deux axes couchés sur le sol.

Le mod ajoute la part de chaque roue à la force que VPP lit pour elle, juste avant qu'il s'en serve. VPP voit alors ce
que la roue porte vraiment : son adhérence, son maintien sur la pente et son rappel contre le glissement retrouvent
toute la charge.

### Le reste

Une roue qui n'est pas en butée n'est pas touchée : VPP lit son ressort, comme sans le mod. La butée ne tient pas le
poids d'un bloc mais par à-coups, d'un pas à l'autre (voir les *Limits* du [README](../../README.md#limits)) : le mod
donne à VPP sa force moyenne sur un dixième de seconde. Le module des dégâts des roues, qui lit lui aussi la charge du
pneu pour y repérer les chocs, garde celle qu'il lisait sans le mod.

Le réglage `bumpStopLoad` du fichier `settings.cfg` coupe cette partie du mod. Le rover qui glisse sur du plat, et
celui qui descend la pente freins serrés, avec et sans elle :
[A rover on its end-stops](../checked/a-rover-on-its-end-stops.md).

## Dans le code du mod

Tout est dans [Src/BumpStopLoad.cs](../../Src/BumpStopLoad.cs).

Ce que KSP donne à chaque pièce pour le pas qui vient se note dans un préfixe de `FlightIntegrator.Integrate(Part)`
(`IntegratePrefix`) : l'accélération du vaisseau (`vessel.precalc.integrationAccel`, la gravité comprise), et les
forces et couples que ses modules ont rassemblés (`part.force`, `part.forces`, `part.torque`). La force que VPP vient
d'appliquer à chaque pneu se note dans un postfixe de `VehicleBase.ApplyTireForce` (`ApplyTireForcePostfix`).

Le bilan se fait dans un postfixe de `VehicleBase.CalculateSuspensionForces`, que VPP appelle à chaque pas pour chaque
roue, une fois la force du ressort lue et avant d'en tirer la charge du pneu ; une seule fois par vaisseau et par pas,
à sa première roue (`Solve`). Pour chaque pièce, sa variation de quantité de mouvement moins ce que KSP lui a donné :

```csharp
Vector3 change = body.mass * (body.velocity - state.velocity) / dt;
Vector3 known = body.mass * a.acceleration + a.force;
Vector3 excess = change - known;
force += excess;
torque += Vector3.Cross(arm, excess) + (Spin(body) - state.spin) / dt - a.torque;
```

puis, pour chaque roue au sol, la force de son ressort, que PhysX applique le long de la normale du contact, et celle
de son pneu :

```csharp
Vector3 spring = p.hit.normal * p.springForce;
force -= spring;
torque -= Vector3.Cross(p.hit.point - centre, spring);
```

Le reste, le long de la normale moyenne du sol, et ses couples autour des deux axes couchés sur le sol, se partagent
entre les roues en butée (`Share`) : les forces positives qui les expliquent au mieux, par moindres carrés. La part de
chaque roue, moyennée, s'ajoute à la force que VPP va lire :

```csharp
probe.smoothed += (probe.missing - probe.smoothed) * weight;
...
wheel.hit.force += probe.injected;
```

Un postfixe de `VehicleBase.CalculateDownforce` garde la charge du pneu telle qu'elle aurait été sans le mod, et un
préfixe et un postfixe de `ModuleWheelDamage.FixedUpdate` la montrent au module des dégâts le temps de sa mise à
jour.
