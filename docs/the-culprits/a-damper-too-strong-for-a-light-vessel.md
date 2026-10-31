# Premier coupable : un amortisseur trop fort pour un vaisseau léger

Partie de [Suspension Bounce Fix](../../README.md), [les coupables](../the-culprits.md) : un coupable qui projette.

À cause du [plancher](the-patches.md#le-plancher-dune-demi-tonne), une jambe qui porte 50 kg reçoit la raideur d'une
jambe qui porte une demi-tonne, et l'amortisseur qui va avec. Pour cette jambe, l'amortisseur est beaucoup trop fort,
et il la projette (voir [pourquoi un amortisseur peut projeter](../the-culprits.md#ce-que-le-stock-ne-résout-pas--les-coupables)).
Une sonde de 100 kg sur trois jambes en est projetée dès que la physique démarre. Une jambe posée seule en construction
EVA aussi, quand [Terrain Precision Fix](https://github.com/lhervier/KSP-Fix-TerrainPrecision) lui donne sa propre
masse dès le départ ; en stock, c'est [le deuxième coupable](a-suspension-in-the-air-keeps-its-settings.md) qui la
projette.

<table>
<tr>
<td width="50%"><img src="../../imgs/small-probe.gif" alt="Une sonde légère lancée sur la piste"></td>
<td width="50%"><img src="../../imgs/landing-leg.gif" alt="Une jambe d'atterrissage posée seule sur Kerbin, avec Terrain Precision Fix"></td>
</tr>
<tr>
<td valign="top"><em>KSP 1.12.5 et KSP Community Fixes, sans ce mod : la sonde de
<a href="../../README.md#a-light-probe-on-the-runway">A light probe on the runway</a>, lancée du SPH sur la
piste.</em></td>
<td valign="top"><b>Pas en stock : sans Terrain Precision Fix, la même jambe se couche sur Kerbin et y reste.</b><br>
<em>KSP 1.12.5, KSP Community Fixes et Terrain Precision Fix, sans ce mod : sur Kerbin, la sauvegarde
<a href="https://github.com/lhervier/KSP-Fix-TerrainPrecision/blob/main/diag/lone-parts/limits/landing-leg/landing-leg-kerbin.sfs"><code>landing-leg-kerbin.sfs</code></a>
de Terrain Precision Fix ; en construction EVA, Bill Kerman prend une jambe de l'atterrisseur et la pose sur son
pied.</em></td>
</tr>
</table>

Un monde lourd aggrave ce coupable. Sur Kerbin, un vaisseau de quelques centaines de kilos reste juste sous la limite :
son amortisseur, celui de la demi-tonne du plancher, retire à chaque pas presque toute la vitesse de la jambe, sans la
renvoyer. Sur Eve, le même amortisseur, deux à trois fois plus fort
([curseur réglé automatiquement](the-patches.md#le-réglage-automatique-des-curseurs) et
[forte gravité](the-patches.md#la-forte-gravité)), passe la limite : un petit rover qui reste immobile sur la piste de
Kerbin rebondit sur Eve de plus en plus haut, et ne se pose plus. Il peut tenir un moment, roues presque immobiles ; au
premier écart, une bosse, une roue qui retouche le sol, chaque pas amplifie de nouveau le mouvement.

![Un rover léger chargé sur Eve, en stock](../../imgs/minirover-stock-eve.gif)

*KSP 1.12.5 et KSP Community Fixes, sans ce mod : un rover d'un quart de tonne sur quatre roues RoveMax S2, posé sur
Eve, sauvegardé, et chargé là.*

## Ce que le mod change

Il laisse l'amortisseur de KSP, sauf s'il est trop fort pour la masse que la jambe doit freiner. Il le ramène alors à
une limite : l'amortisseur qui retire au plus la moitié de la vitesse de la jambe en un pas, loin de la renvoyer. Cette
masse, c'est la part du poids que porte la jambe, ou moins pour une roue loin du centre du vaisseau : poussé là, le
vaisseau tourne autant qu'il se soulève, et la roue n'en met en mouvement qu'une partie. Un vaisseau ordinaire reste
loin de cette limite, et se comporte exactement comme sans le mod ; seuls les vaisseaux très légers, et les rovers
légers sur un monde lourd, l'atteignent.

<table>
<tr>
<td width="50%"><img src="../../imgs/smallprobe-modded.gif" alt="La même sonde lancée sur la piste, avec le mod"></td>
<td width="50%"><img src="../../imgs/landingleg-tpf-modded.gif" alt="La même jambe posée seule sur Kerbin, avec Terrain Precision Fix et le mod"></td>
</tr>
<tr>
<td valign="top"><em>KSP 1.12.5, KSP Community Fixes et ce mod : la sonde de
<a href="../../README.md#a-light-probe-on-the-runway">A light probe on the runway</a>, lancée du SPH sur la
piste.</em></td>
<td valign="top"><em>KSP 1.12.5, KSP Community Fixes, Terrain Precision Fix et ce mod : la même jambe, posée de la
même façon sur Kerbin depuis la sauvegarde
<a href="https://github.com/lhervier/KSP-Fix-TerrainPrecision/blob/main/diag/lone-parts/limits/landing-leg/landing-leg-kerbin.sfs"><code>landing-leg-kerbin.sfs</code></a>.</em></td>
</tr>
</table>

![Le même rover chargé sur Eve, avec le mod](../../imgs/minirover-modded.gif)

*KSP 1.12.5, KSP Community Fixes et ce mod : le même rover, chargé sur Eve depuis la même sauvegarde.*

## Dans le code du mod

Un postfix de `ModuleWheelSuspension.SuspensionSpringUpdate`, que KSP appelle à chaque pas où la roue touche quelque
chose, dans [Src/SuspensionDamper.cs](../../Src/SuspensionDamper.cs) (`SuspensionSpringUpdatePostfix`). La part de la
jambe est la masse du vaisseau partagée entre les roues au sol, comptées une fois par vaisseau et par pas (`Count`) ;
`Apply` part de l'amortisseur que KSP vient de calculer :

```csharp
float share = (float)vessel.totalMass / Mathf.Max(count.Grounded, 1);
Apply(__instance, wheel, share, StockDamper(__instance), false);
```

Dans `Apply`, la part est bornée par la masse que le vaisseau met en mouvement au point de contact, calculée par
`VesselInertia.EffectiveMass` dans [Src/VesselInertia.cs](../../Src/VesselInertia.cs) :

```csharp
share = Mathf.Min(share, (float)inertia.EffectiveMass(point, normal));
```

Puis l'amortisseur est ramené à la limite s'il la dépasse ; `cos` est l'inclinaison du
[troisième coupable](a-suspension-pushing-at-a-slant.md#dans-le-code-du-mod), 1 pour une suspension droite :

```csharp
float limit = share * Mathf.Max(cos, 0f) * Mathf.Max(cos, 0f) / (2f * Time.fixedDeltaTime);
bool isLimited = damper > limit;
```

Le mod n'écrit sur la roue que ce qu'il change, et une dernière fois au pas où il cesse de le changer : KSP n'écrit
lui-même qu'un amortisseur qui diffère de plus de 1,05 de celui de la roue
([l'arrondi et le seuil d'écriture](the-patches.md#larrondi-et-le-seuil-décriture)), et garderait sinon celui du mod :

```csharp
if (isLimited || wasLimited || isFaded || wasFaded)
{
    wheel.wheelCollider.damperRate = damper;
}
```
