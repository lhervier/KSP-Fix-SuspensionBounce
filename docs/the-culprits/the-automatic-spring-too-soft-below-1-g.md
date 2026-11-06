# Quatrième coupable : sous 1 g, le ressort automatique est trop mou

Part of [Suspension Bounce Fix](../../README.md), [the culprits](../../README.md#the-culprits) : un coupable qui met
les roues en butée.

*(Page en français, à traduire avant le commit.)*

Sur Kerbin, un rover au repos s'enfonce aux trois quarts de la course de ses suspensions. Sur la Mune, le même rover,
s'il est lourd, s'enfonce jusqu'au bout : ses roues reposent sur leur butée. Ce n'est pas que le rover y pèse trop, ni
que ses roues soient trop faibles : c'est que KSP lui donne, sous une gravité plus faible que celle de Kerbin, un
ressort bien plus mou qu'il ne le ferait sur Kerbin pour la même charge.

## Le renfort du ressort

KSP règle le ressort d'une suspension, à chaque pas où sa roue touche le sol, dans
`ModuleWheelSuspension.SuspensionSpringUpdate`. Il part de la charge que la roue porte (`sprungMassGravity`, qui suit
déjà la gravité du corps : sur la Mune, une roue porte six fois moins que sur Kerbin) et la multiplie par un renfort
qui grandit à mesure que la suspension s'enfonce :

```csharp
boost = 1f + Mathf.Clamp(suspCompression * BoostCurve((float)base.vessel.mainBody.GeeASL * (float)base.vessel.gravityMultiplier, 1f), -1f, boostClamp);
...
spring = Mathf.Clamp(springTweakable * springRatio * sprungMassGravity * BoostCurve(boost, boostRatio + autoBoost), 0.01f, springClampMax);
```

Ce renfort est ce qui permet à une roue de rover de porter son poids dans sa course : sans lui, le ressort d'une
TR-2L ou d'une RoveMax ne porte, enfoncé jusqu'au bout, que la moitié environ de la charge pour laquelle il est
calculé. Avec lui, sur Kerbin, une roue aux trois quarts de sa course a un ressort trois fois plus raide, et tient.

## La courbe de gravité

Mais la vitesse à laquelle ce renfort grandit avec l'enfoncement est pondérée par une courbe de la gravité du corps,
`BoostCurve(GeeASL, 1)`, qui vaut 1 / |1 − 2 / G| (G la gravité de surface en g), arrondie au dixième :

```csharp
private float BoostCurve(float b, float power)
{
    return Mathf.Round(Mathf.Clamp(1f / Mathf.Abs(1f - 2f / Mathf.Pow(Mathf.Clamp(b, 0f, 2f), Mathf.Clamp(power, 0.01f, autoBoostClamp))), 0.01f, 10000000f) * 10f) / 10f;
}
```

Elle vaut 1 sur Kerbin, plus de 1 au-dessus (5,7 sur Eve), et tombe vite en dessous :

| Corps | Gravité | Courbe |
|---|---|---|
| Eve | 1,7 g | 5,7 |
| Kerbin | 1 g | 1 |
| Laythe, Tylo | 0,8 g | 0,7 |
| Moho, Duna | 0,28-0,3 g | 0,2 |
| Mune, Ike, Dres, Vall, Eeloo | 0,11-0,24 g | 0,1 |
| Minmus, Gilly, Bop, Pol | 0,05 g et moins | 0 |

Le renfort plafonne à 1,5 (`boostClamp`). Sur Kerbin, une roue l'atteint aux deux tiers de sa course : son ressort est
alors trois fois plus raide. Sur la Mune, enfoncée jusqu'au bout, elle n'atteint que 1,1 : son ressort n'est que 1,2
fois plus raide. Sur Minmus, il n'y a plus de renfort du tout.

Rien, dans la physique, ne justifie cette courbe : le ressort part déjà d'une charge qui suit la gravité. Une roue qui
porte six fois moins, sous un ressort calculé pour six fois moins, s'enfoncerait autant que sur Kerbin si le renfort
était le même. Avec la courbe, elle s'enfonce bien plus, et un rover assez lourd arrive en butée.

## Le plancher d'une demi-tonne

Un autre patch s'en mêle : la charge pour laquelle KSP calcule le ressort ne descend jamais sous le poids d'une
demi-tonne sur Kerbin, environ 4,9 kN, quel que soit le corps (le plancher, voir
[The patches](the-patches.md)). Sur un corps léger, ce plancher pèse lourd : une roue y est souvent calculée pour
bien plus qu'elle ne porte, et son ressort, même sans renfort, la tient sans peine.

- **Sur Minmus** (0,05 g), une roue d'un rover de 10 t sur six roues porte environ 0,8 kN. Son ressort, calculé pour
  4,9 kN, en tient à peu près la moitié au bout de sa course sans aucun renfort : bien plus que sa charge. Pour qu'une
  TR-2L y arrive en butée, il lui faudrait porter près de cinq tonnes.
- **Sur la Mune** (0,17 g), la même roue porte environ 2,8 kN, toujours sous le plancher. Mais la moitié de 4,9 kN,
  multipliée par le renfort de 1,2 que la courbe lui laisse, n'en tient que 2,7 : la roue arrive en butée.

C'est le plancher qui place le trou sur la Mune. Sans lui, Minmus serait pire, puisque sa courbe vaut 0 ; avec lui,
un vaisseau de taille raisonnable n'y arrive pas en butée. Le coupable frappe là où la gravité est assez faible pour
que la courbe casse le renfort, mais assez forte pour que la charge d'une roue dépasse ce que tient un ressort
calculé sur le plancher : la Mune, et d'après ces formules (seule la Mune est mesurée ici) les corps de gravité voisine,
Ike, Dres, Vall, Eeloo, et dans une moindre mesure Duna et Moho. Sur Minmus, Gilly, Bop et Pol, seul un rover très
lourd y arrive. Laythe et Tylo perdent peu : leur courbe vaut 0,7, et le renfort y atteint encore son plafond.

## Ce qu'on voit

Un rover en butée tombe sous le [troisième coupable](a-bottomed-wheel-hides-its-weight-from-the-tyres.md) : la butée
porte le reste de son poids, que les pneus ne voient pas. Sur la Mune, un rover lourd garé freins serrés glisse :
sur une pente, il descend lentement, à vitesse constante, sans jamais s'arrêter.

Le joueur qui le remarque passe ses ressorts en manuel et les durcit au curseur, jusqu'à sortir ses roues de la
butée ; ou il les règle au hasard, faute de savoir ce qui se passe.

## Ce que change le mod

Sous 1 g, le mod refait le ressort que KSP vient de calculer, avec les mêmes formules, mais la courbe de Kerbin, 1, à
la place de celle du corps. Une roue s'enfonce alors comme sur Kerbin, sur tous les corps plus légers. Il ne garde ce
ressort que s'il est plus raide que celui de KSP, et ne l'écrit sur la roue qu'avec les mêmes seuils que KSP. En mode
automatique comme en manuel : la courbe n'est pas un curseur, et un ressort réglé à la main en dépend aussi.

Sur Kerbin et au-dessus, la courbe vaut 1 ou plus : le mod ne change rien. Le plancher reste celui de KSP : sur un
corps très léger, le ressort d'une roue peu chargée était déjà plus raide que sa charge ne le demande, et le renfort,
qui grandit avec l'enfoncement, y reste faible puisqu'elle s'enfonce peu. Le réglage `kerbinCurve` du fichier
`settings.cfg` coupe cette partie du mod.

## Le voir

Avec [KSP Diag - Suspension](https://github.com/lhervier/KSP-Diag-Suspension), qui donne roue par roue
l'enfoncement de la suspension, la butée et le ressort. Le même rover de 10 t sur six TR-2L, freins serrés, ressorts
laissés au jeu, chargé sur Kerbin, sur la Mune et sur Minmus : les sauvegardes et le script sont dans
[A heavy rover on Kerbin, the Mun and Minmus](../checked/a-heavy-rover-on-kerbin-the-mun-and-minmus.md). Sans le
mod, il s'enfonce aux trois quarts sur Kerbin, à moitié sur Minmus, et jusqu'en butée sur la Mune.

Attention à une lecture du Diag : la part du poids qu'il montre portée par les suspensions est celle que lisent les
pneus. Avec le mod, elle comprend la force de la butée que le mod leur donne (le troisième coupable) : elle ne dit rien
de ce coupable-ci, que montrent l'enfoncement et la butée.

## Dans le code du mod

Un postfixe de `ModuleWheelSuspension.SuspensionSpringUpdate`, dans
[Src/SpringGravityCurve.cs](../../Src/SpringGravityCurve.cs), qui passe avant celui de l'amortisseur (celui-ci part de
l'amortisseur écrit ici). Il ne fait rien quand la courbe du corps n'est pas plus faible que celle de Kerbin ; il lit
la gravité du corps seule, sans `Vessel.gravityMultiplier`, que KSP abaisse le temps de poser un vaisseau au
chargement, sur Kerbin aussi :

```csharp
float gee = (float)s.vessel.mainBody.GeeASL;
float geeCurve = BoostCurve(s, gee, 1f);
float kerbinCurve = BoostCurve(s, 1f, 1f);
if (geeCurve >= kerbinCurve)
{
    return;
}
```

Puis les formules de KSP, la courbe de Kerbin à la place de celle du corps :

```csharp
float boost = 1f + Mathf.Clamp(s.suspCompression * kerbinCurve, -1f, BoostClamp(s));
float spring = Mathf.Clamp(s.springTweakable * s.springRatio * s.sprungMassGravity *
    BoostCurve(s, boost, s.boostRatio + s.autoBoost), 0.01f, SpringClampMax(s));
```
