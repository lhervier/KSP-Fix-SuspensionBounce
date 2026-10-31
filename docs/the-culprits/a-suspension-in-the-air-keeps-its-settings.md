# Deuxième coupable : une suspension en l'air garde ses anciens réglages

Partie de [Suspension Bounce Fix](../../README.md), [les coupables](../the-culprits.md) : un coupable qui projette.

En l'air, la roue garde le ressort et l'amortisseur de son dernier contact
(voir [le modèle de base](the-base-model.md)). Au contact suivant, elle pousse d'abord avec ces anciens réglages, puis
KSP les recalcule. Ce n'est pas grave si le vaisseau a gardé sa masse entre-temps.

Une pièce posée seule en construction EVA, non : pendant ses trois premières secondes au sol, KSP la fait peser 4 t,
et même 20 t sous faible gravité. Une jambe qui quitte le sol pendant ce temps garde une suspension réglée pour 20 t.
Quand son pied retouche le sol, revenue à ses propres 50 kg, cette suspension la projette.

![Une jambe d'atterrissage posée seule sur Minmus, en stock, avec sa masse incrustée](../../imgs/landing-leg-stock-minmus-mass.gif)

*KSP 1.12.5 et KSP Community Fixes, sans ce mod ni Terrain Precision Fix : sur Minmus, la sauvegarde
[`landing-leg-minmus.sfs`](https://github.com/lhervier/KSP-Fix-TerrainPrecision/blob/main/diag/lone-parts/limits/landing-leg/landing-leg-minmus.sfs)
de Terrain Precision Fix ; en construction EVA, Bill Kerman prend une jambe de l'atterrisseur et la pose sur son pied
(voir [A landing leg placed alone](../../README.md#a-landing-leg-placed-alone)). Incrustée en bas à droite, la masse
de la jambe, ajoutée au montage : 20 t et le temps qui reste, à partir du clic qui la pose, compté sur l'horloge du jeu
(`ModuleCargoPart.MakePartSettle`), puis ses propres 50 kg.*

Avec [Terrain Precision Fix](https://github.com/lhervier/KSP-Fix-TerrainPrecision), la jambe n'a jamais 20 t : elle a
ses 50 kg dès qu'elle est posée, et ce coupable disparaît. Le symptôme, lui, est immédiat : la jambe, encore debout
sur son pied, reçoit l'amortisseur trop fort du [premier coupable](a-damper-too-strong-for-a-light-vessel.md), et elle
est projetée dès qu'elle touche le sol.

## Ce que le mod change

En l'air aussi, il ramène l'amortisseur sous [la limite](a-damper-too-strong-for-a-light-vessel.md#ce-que-le-mod-change),
pour la plus petite part que la jambe portera : celle qu'elle aura quand toutes les roues du vaisseau toucheront. Au
contact suivant, l'amortisseur est déjà à la mesure de la jambe. Le ressort, lui, garde son dernier réglage.

## Dans le code du mod

Un postfix de `ModuleWheelSuspension.FixedUpdate`, que KSP appelle à chaque pas, dans
[Src/SuspensionDamper.cs](../../Src/SuspensionDamper.cs) (`FixedUpdatePostfix`). Il laisse une roue qui touche au
postfix du [premier coupable](a-damper-too-strong-for-a-light-vessel.md#dans-le-code-du-mod), et une pièce sur rails
(`packed`) à KSP :

```csharp
if (!HighLogic.LoadedSceneIsFlight || wheel == null || wheel.wheelCollider == null || vessel == null ||
    __instance.part == null || __instance.part.packed || wheel.IsGrounded)
{
    return;
}
```

En l'air, la part est la masse du vaisseau partagée entre toutes ses suspensions, et `Apply` borne l'amortisseur que
KSP a calculé en dernier, pas celui de la roue, qui peut déjà être celui du mod :

```csharp
float share = (float)vessel.totalMass / Mathf.Max(count.All, 1);
Apply(__instance, wheel, share, StockDamper(__instance), true);
```

Dans `Apply`, une suspension bornée reste marquée comme telle tant que sa roue est en l'air : au contact suivant, si
l'amortisseur de KSP n'est plus à borner, le mod l'écrit sur la roue à la place du sien.

```csharp
else if (offGround)
{
    // Off the ground, the damper on the wheel is not the game's: one lowered earlier stays marked, so that
    // the game's own damper is set back at the next contact.
    wasLimited = false;
}
```
