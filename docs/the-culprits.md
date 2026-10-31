# Les coupables

Partie de [Suspension Bounce Fix](../README.md). Comment KSP 1.12.5 règle le ressort et l'amortisseur d'une
suspension : un modèle de base simple, les rustines qu'il a reçues pour les cas où il ne suffisait pas, puis ce que
ces rustines laissent sans réponse, et qui projette en l'air un vaisseau léger, ou une pièce posée seule.

## Le modèle de base

Dans KSP, une suspension, c'est un ressort et un amortisseur, que KSP règle tout seul selon quatre règles. La
suspension mesure son enfoncement par un rayon tendu le long de son axe : elle marche sur toute surface, mais de biais,
la mesure devient instable. Le ressort suit le poids, partagé à parts égales entre les roues au sol : tous les
vaisseaux s'enfoncent à peu près autant. L'amortisseur suit le ressort, et ne croît donc que comme la racine de la
masse : juste pour une jambe qui porte de l'ordre d'une tonne, plus mou au-delà sans que ça gêne, trop dur en deçà. Enfin, le réglage
n'a lieu qu'au contact : en l'air, la roue garde ses anciens réglages.

**→ Chapitre complet : [Le modèle de base](the-culprits/the-base-model.md)**

## Les rustines

KSP a reçu plusieurs corrections pour les cas où ce modèle ne suffisait pas. Chacune a ses effets de bord, et
plusieurs font les coupables plus bas :

- **Le plancher d'une demi-tonne** : chaque roue porte au moins le poids d'une demi-tonne sur Kerbin, pour qu'un tout
  petit ressort ne soit pas arrondi à zéro ; un vaisseau léger reçoit alors l'amortisseur de la demi-tonne.
- **L'arrondi et le seuil d'écriture** : le ressort n'est écrit que s'il change de plus de 3, sans doute contre une
  boucle entre le ressort et l'enfoncement ; un petit ressort reste à zéro, d'où le plancher.
- **Le renfort à l'enfoncement** : le ressort durcit jusqu'à trois fois près de la butée, et l'amortisseur avec lui,
  au moment du choc.
- **La correction des gros vaisseaux** : au-delà d'une quarantaine de tonnes, l'amortisseur durcit jusqu'à près de
  quatre fois, selon la masse du vaisseau entier, pas selon ce que porte chaque jambe.
- **Le réglage automatique des curseurs** : réglés au premier contact, l'amortisseur jusqu'à 2 sur Eve.
- **La forte gravité** : au-delà de 1,5 g, donc sur Eve, le ressort est triplé et l'amortisseur multiplié par 1,2.

**→ Chapitre complet : [Les rustines](the-culprits/the-patches.md)**

## Ce que le stock ne résout pas : les coupables

**Pourquoi un amortisseur peut projeter.** Dans la réalité, un amortisseur trop fort ne projette rien : il rend
seulement la jambe lente à se poser. Mais KSP calcule la physique par petits pas, cinquante par seconde. À chaque pas,
l'amortisseur freine la jambe selon la vitesse qu'elle avait au pas précédent. Sur une jambe chargée, ce freinage
retire un peu de vitesse à chaque pas. Sur une jambe qui ne porte presque rien, il est si fort qu'en un seul pas il
fait plus que l'arrêter : il la renvoie dans l'autre sens, plus vite qu'elle n'est venue. Le pas suivant recommence, et
en quelques pas le vaisseau part à des dizaines de mètres par seconde. Les trois coupables mènent tous là.

### Premier coupable : un amortisseur trop fort pour un vaisseau léger

À cause du plancher, une jambe qui porte 50 kg reçoit l'amortisseur d'une jambe qui porte une demi-tonne, beaucoup
trop fort pour elle : il la projette. Un monde lourd l'aggrave : un petit rover immobile sur Kerbin rebondit sans fin
sur Eve. Le mod ramène l'amortisseur sous une limite.

**→ Chapitre complet : [Premier coupable : un amortisseur trop fort pour un vaisseau léger](the-culprits/a-damper-too-strong-for-a-light-vessel.md)**

### Deuxième coupable : une suspension en l'air garde ses anciens réglages

En l'air, une roue garde le ressort et l'amortisseur de son dernier contact. Une pièce posée seule en construction EVA,
que KSP fait peser jusqu'à 20 t ses premières secondes, garde ainsi une suspension réglée pour 20 t, qui la projette
quand elle retouche le sol à sa propre masse. Le mod ramène l'amortisseur sous la limite dès que la roue est en l'air.

**→ Chapitre complet : [Deuxième coupable : une suspension en l'air garde ses anciens réglages](the-culprits/a-suspension-in-the-air-keeps-its-settings.md)**

### Troisième coupable : une suspension qui pousse de biais

Quand la pièce penche ou se couche, l'enfoncement que lit sa suspension saute d'un pas à l'autre, et KSP la règle
comme si elle était droite : les poussées changent de sens à chaque pas et grandissent. Une jambe couchée dont le pied
frôle le sol finit projetée. Le mod retire peu à peu le ressort et l'amortisseur au-delà de 40° d'inclinaison.

**→ Chapitre complet : [Troisième coupable : une suspension qui pousse de biais](the-culprits/a-suspension-pushing-at-a-slant.md)**
