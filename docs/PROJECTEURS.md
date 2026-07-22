# Projecteurs (lyres + projecteur) - Guide demain

Univers ArtNet : **33** sur `192.168.1.48` (contrôleur ctrl-4).

## 1. Etat du code apres correction

- Convention : **1 canal DMX = 1 entite eHuB** (`LedType.RAW1` cote config).
- Source du mapping : la doc officielle
  [glassworks.tech/led/arch/other-devices](https://learn.glassworks.tech/led/arch/other-devices)
  qui donne explicitement les canaux DMX pour projo et lyres.
- `ProjectorController` emet 4 entites (R, V, B, W) sur canaux DMX 1..4.
  `baseEntityId = 1` par defaut.
- `LyreController` emet 13 entites (13 canaux DMX). Ordre par defaut :
  `pan16, tilt16, speed, dimmer, strobe, R, G, B, W, macro, auto/reset`.
  Modifiable dans le script si le profil DMX reel est different.
- `configs/ecran.json` mappe les entites en `led_type: "RAW1"` :

  | Appareil  | Entites eHuB | Canaux DMX (univ 33) |
  |-----------|--------------|----------------------|
  | Projector | 1 .. 4       | 1 .. 4               |
  | Lyre 1    | 10 .. 22     | 10 .. 22             |
  | Lyre 2    | 30 .. 42     | 30 .. 42             |
  | Lyre 3    | 50 .. 62     | 50 .. 62             |
  | Lyre 4    | 70 .. 82     | 70 .. 82             |

  ID d'entite eHuB = canal DMX. Simple.

  **Adresses DMX physiques a regler sur les appareils** (LCD/dip switches) :
  Projo = **001**, Lyre 1 = **010**, Lyre 2 = **030**, Lyre 3 = **050**,
  Lyre 4 = **070**.

  (Rappel : dans les fichiers, `channel_start` est **0-indexe** ; les tableaux
  ci-dessus utilisent la numerotation DMX **1-indexee**.)

- Nouvelle commande CLI `ramp` : envoie une rampe temporelle 0->255->0 sur UNE
  entite (=1 canal DMX). Utile pour distinguer un canal de **mouvement** (la
  lyre tourne en continu) d'un canal de **dimmer** (l'intensite varie sans
  mouvement). Voir section 7.F pour la procedure "rotations".

## 2. A ton arrivee : lancer les tests

Depuis la racine du repo :

```bash
./scripts/test_projecteurs.sh                # Linux / WSL
```

ou

```powershell
.\scripts\test_projecteurs.ps1               # Windows PowerShell
```

Le script est **interactif** : il te demande apres chaque etape si tu as vu
quelque chose s'allumer. Deroulement :

1. **Scan full univers 33** (RGB plein a 255 sur tous les canaux). C'est la
   commande la plus brutale : si le materiel / reseau marche, quelque chose
   DOIT reagir (dimmer force ouvert). Si rien -> stopper et regler le reseau.
2. **Scan pas-a-pas** univers 0..33 (3s chacun). Permet de reperer sur quel
   univers les projecteurs sont patches (attendu : 33).
3. **Send par entite** (1, 10, 30, 50, 70). Attention : avec `LedType.RAW1`
   utilise pour les lyres/projecteur, `send` n'ecrit qu'UN seul canal DMX par
   entite (l'octet R du triplet `--color`). Ex: `--entity 10 --color 255,0,0`
   pousse 255 au canal DMX 1 de Lyre 1 UNIQUEMENT (pas aux canaux 2/3). Si le
   canal 1 n'est pas le dimmer, la lyre peut rester noire meme avec cette
   commande. C'est normal, elle sert d'abord a valider le routing
   entite -> univers 33 : voir 7.B pour cartographier les canaux.

## 3. Cartographier le vrai brochage DMX (si besoin)

Une fois le TEST 1 concluant, tu peux verifier canal par canal en poussant une
valeur precise sur un canal precis. Utilise `send` en ciblant l'entite qui
correspond au canal :

- Canal DMX 1 de Lyre 1 = entite 10
- Canal DMX 2 de Lyre 1 = entite 11
- ... etc.

Exemple : ouvrir le dimmer de Lyre 1 (si le canal 6 = dimmer selon le script
Unity actuel, donc entite 15) :

```bash
dotnet run --project csharp/Mappa.Cli -- send configs/ecran.json --ip 192.168.1.48 --entity 15 --color 255,0,0
```

(Rappel : avec `LedType.RAW1`, seul l'octet R de la couleur est utilise ; les
octets G/B sont ignores. Pour forcer une valeur donnee `v`, envoie
`--color v,0,0`.)

Si l'ordre reel des canaux de la lyre est different (ex. dimmer au canal 1),
ajuste **l'ordre des `_ch[i]`** dans `assets/Scripts/LyreController.cs` -
c'est le seul fichier a toucher, le routing suit automatiquement.

## 4. Animer en direct depuis Unity

Une fois les projecteurs qui s'allument confirmes :

1. Lance l'app `Mappa.Ui` :
   ```bash
   dotnet run --project csharp/Mappa.Ui
   ```
2. Verifie que `configs/ecran.json` est charge (chemin affiche dans l'UI).
3. **Coche `Reception eHuB`** (port par defaut 8765).
4. **Coche `Send ArtNet`**.
5. Lance la scene Unity (`Demo.unity`).
6. Selectionne un objet `Lyre 1` (ou 2/3/4) dans la hierarchie, dans
   l'Inspector monte `dimmer` a 1, choisis une `color`, et sur `LyreMovement`
   choisis un `pattern` (Circle par exemple).
7. Idem pour `Projector` : `dimmer` a 1, une `color`.
8. La reaction doit etre immediate sur le materiel.

**Piege connu** : dans la scene actuelle, tous les `dimmer` sont a **0** par
defaut (choix explicite pour que tu fasses tes effets). Sans monter le
dimmer, rien ne s'allume, meme avec des couleurs vives. C'est normal.

## 5. Fichiers modifies pour ce correctif

- `csharp/Mappa/Config.cs` : ajout `LedType.RAW1 = 1`.
- `csharp/Mappa/RoutingPlan.cs` : gestion du cas 1 canal dans `Render()`.
- `configs/ecran.json` : 5 lignes appareils passees en `RAW1` avec une entite
  par canal DMX.
- `assets/Scripts/LyreController.cs` : emet 1 canal par entite (13 canaux,
  conforme a la doc glassworks/other-devices).
- `assets/Scripts/ProjectorController.cs` : emet 4 canaux R/V/B/W
  (`baseEntityId: 1`, ChannelCount fixe a 4).
- `assets/Demo.unity` : `Projector` prend `baseEntityId: 1`.

## 6. Tests de non-regression (mur LED)

Le mur utilise toujours `LedType.RGB` et son propre `EhubEmitter` : le
correctif n'a **pas** touche au chemin du mur. Aucun risque de regression sur
le panneau LED.

---

## 7. Si ca ne marche pas : arbre de decision

Ordre des scripts dispo pour gagner du temps demain :

```
                     +-----------------------------------+
                     |  ./scripts/diag_reseau.sh         |  <- avant tout
                     +-----------------------------------+
                                    |
                                    v
                     +-----------------------------------+
                     |  ./scripts/test_projecteurs.sh    |
                     +-----------------------------------+
                       |               |             |
    TEST 1 rien          TEST 1 OK       TEST 2 rien
       |                     |              |
       v                     v              v
   voir 7.A            voir 7.B           voir 7.C
```

### 7.A - Rien ne s'allume au scan full (TEST 1 echoue)

Cause : le paquet ArtNet ne sort pas ou n'atteint pas le materiel.

1. Lance `./scripts/diag_reseau.sh` : ping du .48, verif interface,
   pare-feu WSL, dotnet dispo.
2. Si sous Windows/WSL : lance PowerShell **en admin** et essaie plutot
   `.\scripts\test_projecteurs.ps1` (WSL a parfois des soucis d'UDP sortant).
3. Verifie qu'un autre outil (LightJams, QLC+, autre logiciel ArtNet) sait
   allumer QUELQUE CHOSE sur le .48 : si oui, c'est notre code, si non, c'est
   materiel/reseau (contrôleur pas patche, univers 33 pas sur output 16,
   cable coupe...).
4. Cross-check : `wireshark`/`tcpdump -i any port 6454` -> tu dois voir nos
   paquets sortir vers .48 quand scan tourne.

### 7.B - Scan OK mais `send --entity` n'allume rien (TEST 2 echoue)

Cause probable : le canal dimmer de la lyre n'est PAS a l'offset 0 (canal 1).
`send --entity 10 --color 255,0,0` allume seulement le canal DMX 1 de Lyre 1
avec la valeur 255. Si le canal 1 est "pan" et le "dimmer" est ailleurs, la
lyre reste noire.

Solution : cartographie automatique avec le sweep DMX :

```bash
./scripts/dmx_sweep.sh lyre1   # test 1 canal a la fois, 4s chacun
```

Ou en cible manuelle :

```bash
./scripts/dmx_probe.sh lyre1 6 255   # essaie canal 6 (souvent dimmer)
./scripts/dmx_probe.sh lyre1 1 128   # essaie canal 1 a mi-course (pan)
```

Une fois le canal du dimmer trouve, tu peux soit :
- laisser tel quel et juste dire a Unity "envoie 255 au canal N" en montant
  la variable correspondante de `LyreController` (par ex. si canal 6 = dimmer,
  le comportement par defaut du script marche deja),
- OU reorganiser l'ordre des `_ch[i]` dans `LyreController.cs` pour coller
  au vrai profil de la lyre (voir 7.D).

### 7.C - Unity envoie-t-il vraiment ? (le mur marche, les projecteurs non)

Rappel critique : dans Unity, les lyres/projecteur n'ont AUCUN rendu visuel.
Bouger `pan/tilt/dimmer` dans l'Inspector ne prouve PAS que des paquets sortent.

Pour verifier sans dependre du materiel :

1. Ferme `Mappa.Ui` (ou decoche "Reception eHuB") -> libere le port 8765.
2. Lance le sniffer :
   ```bash
   python3 scripts/sniff_ehub.py --devices
   ```
3. Lance la scene Unity (dimmer=1, couleur mise sur une lyre).
4. Attendu : `id=1` (projecteur), `id=10..23` (lyre 1), `id=30..43`,
   `id=50..63`, `id=70..83`. Si tu vois les IDs -> Unity emet OK.
5. Si tu ne vois RIEN -> le DeviceEmitter n'est pas actif, ou pas cable.
   Verifie dans la scene que le GameObject `DeviceEmitter` existe et que
   son `emitter` est reference par les 4 lyres + projecteur (deja fait,
   voir fileID 2000000003 dans `assets/Demo.unity`).

Rebranche `Mappa.Ui` ensuite pour re-transmettre en ArtNet.

### 7.D - Reordonner les canaux de la lyre (hotfix rapide)

Si le sweep DMX (7.B) montre que l'ordre reel des canaux n'est pas celui
code, edite `assets/Scripts/LyreController.cs` autour de la ligne 34 :

```csharp
_ch[0]  = (byte)(pan16 >> 8);      // canal DMX 1
_ch[1]  = (byte)(pan16 & 0xFF);    // canal DMX 2
_ch[2]  = (byte)(tilt16 >> 8);     // canal DMX 3
_ch[3]  = (byte)(tilt16 & 0xFF);   // canal DMX 4
_ch[4]  = (byte)(speed * 255f);    // canal DMX 5
_ch[5]  = (byte)(dimmer * 255f);   // canal DMX 6
_ch[6]  = (byte)(strobe * 255f);   // canal DMX 7
_ch[7]  = (byte)(color.r * 255f);  // canal DMX 8
_ch[8]  = (byte)(color.g * 255f);  // canal DMX 9
_ch[9]  = (byte)(color.b * 255f);  // canal DMX 10
_ch[10] = (byte)(white * 255f);    // canal DMX 11
_ch[11] = (byte)(macro * 255f);    // canal DMX 12 (macro couleur / gobo)
_ch[12] = 0;                       // canal DMX 13 (auto)
_ch[13] = 0;                       // canal DMX 14 (reset)
```

L'indice `_ch[i]` correspond au canal DMX `i + 1` de la lyre. Reorganise les
lignes selon ce que ton sweep a montre.

#### Profils courants prets a coller

**Profil A - Dimmer au canal 1 (frequent sur petites tetes mobiles chinoises)**
```csharp
_ch[0]  = (byte)(dimmer * 255f);   // canal 1 : dimmer
_ch[1]  = (byte)(pan16 >> 8);      // canal 2 : pan (8 bits)
_ch[2]  = (byte)(tilt16 >> 8);     // canal 3 : tilt (8 bits)
_ch[3]  = (byte)(color.r * 255f);  // canal 4 : rouge
_ch[4]  = (byte)(color.g * 255f);  // canal 5 : vert
_ch[5]  = (byte)(color.b * 255f);  // canal 6 : bleu
_ch[6]  = (byte)(white * 255f);    // canal 7 : blanc
_ch[7]  = (byte)(strobe * 255f);   // canal 8 : strobe
_ch[8]  = (byte)(speed * 255f);    // canal 9 : speed
_ch[9]  = 0; _ch[10] = 0; _ch[11] = 0; _ch[12] = 0; _ch[13] = 0;
```

**Profil B - Pan/Tilt 16 bits, dimmer en milieu (defaut, 13 canaux)**
Ordre par defaut du script actuel (deja code) : pan_H, pan_L, tilt_H, tilt_L,
speed, dimmer, strobe, R, G, B, W, macro, auto/reset.

**Profil C - RGB simple sans mouvement (barres LED type)**
```csharp
_ch[0] = (byte)(color.r * dimmer * 255f);
_ch[1] = (byte)(color.g * dimmer * 255f);
_ch[2] = (byte)(color.b * dimmer * 255f);
_ch[3] = (byte)(white * 255f);
_ch[4] = (byte)(strobe * 255f);
for (int i = 5; i < 13; i++) _ch[i] = 0;
```

Astuce : garde toujours 13 lignes `_ch[0..12]` (le tableau reste dimensionne
a 13), et laisse a `0` les canaux inutilises. Pas besoin de recompiler la
config C# ni d'ajuster `ecran.json` : tu ne changes que l'ordre des octets
emis, le routing suit.

### 7.F - Rotations (pan / tilt) : les lyres bougent-elles ?

Distinct de "s'allument-elles" : une lyre peut allumer sa lampe (dimmer OK) sans
bouger si les canaux pan/tilt sont mal identifies. `LyreController` par defaut
suppose du **16 bits** (canaux 1+2 = pan hi+lo, canaux 3+4 = tilt hi+lo). Si la
lyre est en **8 bits** (frequent sur petites tetes mobiles), tout est decale.

**Etape 1 : identifier pan et tilt par rampe visuelle**

```bash
./scripts/dmx_sweep_rotation.sh lyre1
```

Ce script envoie une rampe lente 0->255->0 sur chaque canal de la lyre, 8s par
canal. Regarde la lyre :

- **Mouvement horizontal continu** = tu as trouve le canal **pan**.
- **Mouvement vertical continu** = tu as trouve le canal **tilt**.
- **Intensite qui varie** = c'est un canal dimmer, pas un canal de rotation.
- **Rien** = canal inactif, ou il faut d'abord ouvrir un canal "speed" ou
  "control" ailleurs (voir Etape 3).

Note le n° de canal (1..13) pour pan et pour tilt. Le script te le demande apres
chaque canal.

**Etape 2 : interpretation**

- Si pan est sur **canal 1** et le canal 2 (pan fine) n'a **aucun effet
  visible** ->  la lyre est probablement en 16 bits, code actuel OK.
- Si pan est sur canal 1 et tilt est sur canal 2 (adjacents, tous les deux
  provoquent un mouvement franc, pas de "fine") -> **8 bits pur**. Bascule
  vers le profil 8 bits (Etape 4).
- Si pan et tilt sont sur des canaux **non consecutifs** (ex: pan=1, tilt=3),
  ordre atypique -> reorganise `_ch[i]` selon 7.D avec l'ordre trouve.

**Etape 3 : le canal "speed" bloque parfois tout**

Beaucoup de lyres exigent un canal "speed" ou "reset" en position >0 pour que
pan/tilt reagissent. Si l'etape 1 ne montre AUCUN mouvement sur AUCUN canal,
essaie d'abord de forcer le canal 5 (speed dans le profil actuel) a mi-course :

```bash
dotnet run --project csharp/Mappa.Cli -- send configs/ecran.json \
  --ip 192.168.1.48 --entity 14 --color 128,0,0 --frames 40 &
# entite 14 = canal 5 (speed) de lyre1 ; garde en fond pendant le sweep
./scripts/dmx_sweep_rotation.sh lyre1
```

**Etape 4 : basculer vers le profil 8 bits (si necessaire)**

Un profil alternatif `assets/Scripts/LyreController8bit.cs` est fourni : 9
canaux au lieu de 13, sans les octets "fine".

Pour basculer une lyre :

1. Dans la scene Unity, selectionne le GameObject `Lyre 1` (ou 2/3/4).
2. Dans l'Inspector, **desactive** le composant `Lyre Controller` (decoche la
   case a gauche du nom).
3. Ajoute le composant `Lyre Controller 8bit` (Add Component -> chercher
   "Lyre Controller 8bit").
4. Recopie sur ce nouveau composant : `emitter` (drag&drop du GameObject
   DeviceEmitter) et `baseEntityId` (10, 30, 50 ou 70).
5. Modifie `configs/ecran.json` : pour cette lyre, remplace `entity_end` par
   `baseEntityId + 8` (ex: Lyre 1 -> `entity_end: 18` au lieu de `22`).

Le routing C# passe automatiquement de 13 canaux a 9 pour cette lyre. Les
autres restent en 13 canaux si tu ne les modifies pas.

**Etape 5 : bouger depuis Unity**

Une fois pan/tilt identifies :

- Sur `LyreController` (ou 8bit) : les curseurs `pan` (0..1) et `tilt` (0..1)
  centrent la lyre a 0.5. Ajuste `centerPan` / `centerTilt` sur `LyreMovement`
  pour orienter le mouvement vers le public plutot que vers un mur.
- `LyreMovement.pattern` : essaie `Sweep` en premier (mouvement horizontal
  simple), c'est le plus rapide a valider visuellement.
- `LyreMovement.amplitude` : commence a 0.1 (petit mouvement) puis augmente
  progressivement pour eviter que la lyre tape en butee.

### 7.E - Rebuild rapide apres modif

```bash
dotnet build csharp/Mappa.sln          # verifier la compilation
dotnet run --project csharp/Mappa.Cli -- send configs/ecran.json --entity 15 --color 255,0,0
```

Cote Unity : re-play la scene (Unity recompile automatiquement les scripts
au retour focus).

