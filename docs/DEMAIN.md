# DEMAIN - Faire fonctionner les projecteurs

**Objectif du jour** : allumer les projecteurs (le statique en priorite,
puis les 4 lyres). Le mur LED est deja OK.

**Cible reseau** : `192.168.1.48` (controleur ctrl-4), univers ArtNet **33**.

**Setup** : tout tourne sous Windows (Unity Editor + Mappa.Ui + CLI).
Commandes en PowerShell a la racine du repo.

**Reference materiel** : [learn.glassworks.tech/led/arch/other-devices](https://learn.glassworks.tech/led/arch/other-devices)

## Mapping DMX officiel (univers 33)

| Appareil | Entites eHuB | Canaux DMX | Adresse DMX physique |
|---|---|---|---|
| Projecteur statique | 1..4 (R,V,B,W) | 1..4 | **001** |
| Lyre 1 | 10..22 (13 canaux) | 10..22 | **010** |
| Lyre 2 | 30..42 (13 canaux) | 30..42 | **030** |
| Lyre 3 | 50..62 (13 canaux) | 50..62 | **050** |
| Lyre 4 | 70..82 (13 canaux) | 70..82 | **070** |

Total : 82 canaux occupes sur 512 dans l'univers 33.

---

## Etape 1 : allumer le PROJECTEUR STATIQUE (le plus simple)

Le projo statique est un RVBW pur (4 canaux). Aucun canal dimmer, aucun
mouvement : si tu envoies 255 sur le canal 1, il devient rouge. C'est le
test le plus fiable.

### 1.a Prerequis

```powershell
cd C:\Users\thiba\Mappa
```

Assure-toi que le projo statique est bien configure a l'adresse DMX **001**
sur son ecran (LCD au dos). C'est l'unique reglage physique a faire pour
lui.

### 1.b Envoi Art-Net direct - test brutal

Blast 255 sur tous les canaux de l'univers 33 pendant 10s :

```powershell
dotnet run --project csharp\Mappa.Cli -- scan --ip 192.168.1.48 --only 33 --hold 10
```

**Attendu** : le projo doit devenir **blanc plein** (R=V=B=W=255).
Les lyres aussi vont bouger dans tous les sens (leurs 13 canaux sont
aussi a 255), c'est normal a ce stade.

- **Projo blanc** : bas de la chaine OK (reseau, BC216, univers 33,
  cable DMX, adresse 001, alim). Passe a 1.c.
- **Projo eteint** :
  - L'adresse DMX est-elle bien 001 sur l'ecran du projo ?
  - Le cable DMX est-il branche sur la sortie 16 du BC216 (celle
    configuree pour l'univers 33) ?
  - Le projo est-il en mode "DMX" (pas "sound"/"auto") ?
  - `dotnet run --project csharp\Mappa.Cli -- scan --ip 192.168.1.48 --broadcast --start 30 --universes 41 --step --hold 4`
    balaie les univers 30..40 un par un ; si le projo reagit sur un
    univers X != 33, le BC216 n'est pas patche comme prevu.

### 1.c Test canal par canal

Pour verifier que R, V, B, W sont bien dans cet ordre :

```powershell
dotnet run --project csharp\Mappa.Cli -- send configs\ecran.json --ip 192.168.1.48 --entity 1 --color 255,0,0
```

Doit passer le projo en **ROUGE plein**. Ctrl+C, puis :

```powershell
dotnet run --project csharp\Mappa.Cli -- send configs\ecran.json --ip 192.168.1.48 --entity 2 --color 255,0,0
```

Doit passer en **VERT** (entite 2 = canal DMX 2 = V). Continue avec
`--entity 3` (bleu) et `--entity 4` (blanc).

- Si les couleurs sortent dans le bon ordre : mapping parfait, passe a
  l'etape 2.
- Si l'ordre est different : le profil DMX de ton projo n'est pas
  R/V/B/W. Note l'ordre observe, dis-le moi, on adapte
  `ProjectorController.cs`.

### 1.d Pilotage depuis Unity

Ouvre `Demo.unity`, lance le Play, selectionne le GameObject `Projector`.
Dans l'Inspector `ProjectorController` :

- `color = white`, `dimmer = 1`, `white = 0` -> projo devient blanc
  (canaux R=V=B=255, W=0).
- Change `color` en rouge pur -> projo rouge.
- Monte `white = 1`, `color = black` -> projo en blanc froid via W.

Le sniffer local peut confirmer l'emission :

```powershell
python scripts\sniff_ehub.py --devices
```

Doit afficher `id=1..4` avec les valeurs RVBW envoyees.

Cote reseau : lance `Mappa.Ui` en parallele, coche `Reception eHuB` +
`Send ArtNet`, clique `Start`. Le compteur `Paquets ArtNet` doit
augmenter et le projo suit ce que tu regles dans Unity en temps reel.

---

## Etape 2 : faire fonctionner les LYRES (13 canaux chacune)

Une fois le projo statique OK, on attaque les lyres. Plus complexe
parce que 13 canaux et l'ordre exact des canaux n'est pas dans la doc
glassworks.

### 2.a Reglage physique

Regle chaque lyre a son adresse DMX sur son LCD :
- Lyre 1 = **010**, Lyre 2 = **030**, Lyre 3 = **050**, Lyre 4 = **070**.
- Mode = **DMX** (pas "sound"/"auto"/"programme").
- Verifie aussi qu'elles sont en mode 13 canaux (souvent
  configurable via un menu "channels" ou "mode" sur la lyre).

### 2.b Test brutal

Le blast univers 33 (etape 1.b) fait aussi bouger les lyres si elles
sont bien adressees. Si Etape 1.b marche pour le projo mais AUCUNE
lyre ne bouge : c'est probablement les adresses DMX physiques.

### 2.c Test canal par canal (13 canaux)

```powershell
.\scripts\dmx_sweep.sh lyre1
```

Rampe 0 -> 255 -> 0 sur chacun des 13 canaux, ~8s par canal. Note ce
qui bouge (pan, tilt, dimmer, R, V, B, W, strobe, macro).

Ordre suppose dans le code (`LyreController.cs`) :
```
canal 1  : pan_hi       canal 8  : R
canal 2  : pan_lo       canal 9  : V
canal 3  : tilt_hi      canal 10 : B
canal 4  : tilt_lo      canal 11 : W
canal 5  : speed        canal 12 : macro
canal 6  : dimmer       canal 13 : auto/reset
canal 7  : strobe
```

Si l'ordre observe est different, reordonne les `_ch[i]` dans
`assets\Scripts\LyreController.cs` (voir profils
[`PROJECTEURS.md`](./PROJECTEURS.md#profils-courants-prets-a-coller)).

### 2.d Pilotage depuis Unity

Selectionne `Lyre 1`, dans l'Inspector :
- `LyreMovement.pattern` = `Static` (pour eviter les mouvements
  intempestifs pendant le test).
- `LyreController.dimmer = 1`, choisis une couleur vive.

La lyre doit s'allumer immediatement dans la couleur choisie.

---

## Si rien ne marche : arbre de decision

| Symptome | Cause probable | Action |
|---|---|---|
| Ping .48 KO | Reseau physique | Cable, IP locale (dans /24 de .48), pare-feu Windows |
| Etape 1.b : projo eteint | Adresse DMX projo pas 001, ou pas branche sur sortie 16 du BC216, ou univers 33 pas patche | Verifier LCD projo + patch BC216 |
| Etape 1.c : les canaux sont dans le desordre | Profil DMX projo different (ex: R,G,B,W vs W,R,G,B) | Reordonner dans `ProjectorController.cs` |
| Etape 2.b : projo marche mais aucune lyre | Adresses DMX lyres pas 010/030/050/070, ou lyres pas en mode DMX | LCD des lyres |
| Etape 2.c : lyre s'allume mais dans le mauvais canal | Profil DMX 13 canaux different | Reordonner dans `LyreController.cs` |
| Unity : sniffer voit rien | `DeviceEmitter` inactif ou Play non lance | Verifier hierarchie Unity + Play mode |
| Sniffer voit tout, Mappa.Ui `Paquets 0` | Case `Reception eHuB` non cochee, ou pare-feu Windows bloque dotnet.exe sur UDP 8765 | Cocher les cases + autoriser dotnet dans le pare-feu prive |

---

## Ce qui est deja teste (tu peux faire confiance)

Verifie automatiquement par les tests unitaires (`dotnet test csharp\Mappa.sln`) :

- Le format binaire eHuB (`DeviceEmitter.cs` cote Unity) est bien
  decode par `EhubReceiver` cote Mappa.Ui.
- Chemin complet Unity->eHuB->State->RoutingPlan->ArtNet aboutit aux
  bons octets DMX sur l'univers 33.
- `configs/ecran.json` valide, 0 collision entre entites appareils
  (1..4, 10..22, 30..42, 50..62, 70..82) et entites mur (100..).
- Mapping officiel doc glassworks respecte : projo 4 canaux
  (DMX 1..4), lyres 13 canaux (DMX 10..22, 30..42, 50..62, 70..82).

**Non testable ici** (depend du materiel) :
1. L'adresse DMX physique de chaque appareil (LCD).
2. L'ordre exact des canaux dans le profil DMX du projo et des lyres.
3. Le patch du BC216 sur l'univers 33.

Ces 3 points sont a valider physiquement demain dans l'ordre des
etapes ci-dessus.
