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
