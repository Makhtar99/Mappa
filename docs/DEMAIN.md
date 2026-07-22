# DEMAIN - Test rapide projecteurs & lyres

**Cible** : 192.168.1.48 (contrôleur ctrl-4), univers ArtNet **33**.
**Matériel** : 4 lyres (14 canaux DMX chacune) + 1 projecteur (1 canal).

**Setup demain** : tout tourne sous Windows (Unity Editor + Mappa.Ui + CLI).
Tous les exemples ci-dessous se lancent depuis un terminal PowerShell/cmd
Windows a la racine du repo.

Reference detaillee (arbre de decision, hotfix profils DMX, etc.) : voir
[`PROJECTEURS.md`](./PROJECTEURS.md).

---

## Plan de test en 5 etapes (~15 min total)

**Regle d'or** : on part du **bas** de la chaine (Art-Net brut) vers le haut
(Unity + effets). Si une etape echoue, on ne passe pas a la suivante.

### 0. Prerequis (1 min)

```bash
cd ~/ecole/Mappa                              # (ou chemin equivalent Windows)
./scripts/diag_reseau.sh                      # ping 192.168.1.48 + pare-feu
```

- Ping OK ? → continue.
- Ping KO ? → **stop, regle le reseau d'abord** : verifie le cable, l'IP de
  ta machine (dans le meme /24 que .48), et le pare-feu Windows/WSL.

### 1. Blast Art-Net brutal (2 min) - le test qui prouve le plus

**Une seule commande.** Envoie 255 sur **tous les canaux** des univers 0..33
en simultane, pendant 5 secondes, vers le BC216. Court-circuite completement
Unity et Mappa.Ui. Si le materiel repond, on est certain que
**quelque chose** va reagir (dimmer force ouvert sur tous les appareils).

```bash
dotnet run --project csharp/Mappa.Cli -- scan --ip 192.168.1.48 --universes 34 --hold 5
```

- **Quelque chose reagit** (lyres et/ou projo s'allument, meme partiellement)
  → excellent, tout le bas de la chaine (reseau, BC216, cablage, alimentation
  lyres) est bon. Passe a l'etape 2.
- **Rien du tout** → va a l'etape 1bis.

Astuce : si tu n'as vu qu'une ou deux lyres reagir sur 4, les autres ont
probablement une adresse DMX differente ou sont sur une autre sortie du
BC216 - non bloquant pour l'instant, continue.

### 1bis. Diagnostic si l'etape 1 n'a rien allume (2 min)

Deux hypotheses : soit l'univers ArtNet du BC216 n'est pas 33, soit c'est un
probleme reseau/materiel.

```bash
# Balaye les univers 0..39 UN PAR UN, 3s chacun. Regarde a quel moment
# une lyre reagit -> tu as trouve le vrai univers du BC216.
dotnet run --project csharp/Mappa.Cli -- scan --ip 192.168.1.48 \
  --broadcast --universes 40 --step --hold 3
```

- **Une lyre reagit sur l'univers X** → note X. Si X != 33, change dans
  `configs/ecran.json` (cherche `"artnet_universe": 33`, remplace par X)
  ou reconfigure le BC216 pour ecouter l'univers 33.
- **Rien du tout sur aucun univers** → probleme materiel/reseau :
  - Cable DMX pas branche sur la lyre / autre sortie du BC216 ?
  - Lyre pas alimentee ou en mode "auto/programme" (pas DMX) ?
  - `./scripts/diag_reseau.sh` pour verifier le reseau UDP.
  - Cross-check avec un autre logiciel (QLC+, LightJams) pour isoler la
    panne : si eux non plus n'y arrivent pas, ce n'est pas notre code.

### 2. Cartographie des 14 canaux de la lyre (5-8 min)

Une fois qu'une lyre reagit, il faut savoir **quel canal fait quoi** (pan,
tilt, dimmer, RGB...).

```bash
./scripts/dmx_sweep_rotation.sh lyre1
```

Ce script envoie une rampe 0->255->0 sur chaque canal, 8s chacun, et te
demande ta reaction. Note pour chaque canal :

- `p` = mouvement horizontal (**pan**)
- `t` = mouvement vertical (**tilt**)
- `d` = variation d'intensite (**dimmer**)
- `s` = strobe / flash
- `c` = changement de couleur (R, G, B ou W)
- `r` = rien

**A la fin, tu obtiens la table canal -> fonction reelle de ta lyre.**

Compare avec l'ordre code (defaut dans `LyreController.cs`) :
```
canal 1  : pan_hi     canal 8  : R
canal 2  : pan_lo     canal 9  : G
canal 3  : tilt_hi    canal 10 : B
canal 4  : tilt_lo    canal 11 : W
canal 5  : speed      canal 12 : macro
canal 6  : dimmer     canal 13 : auto
canal 7  : strobe     canal 14 : reset
```

**Si l'ordre correspond** → parfait, saute a l'etape 4.

**Si l'ordre est different** → passe a l'etape 3.

### 3. Ajuster l'ordre des canaux dans le code (5 min, seulement si necessaire)

Ouvre `assets/Scripts/LyreController.cs` et reorganise les `_ch[i]` selon ce
que le sweep a montre. Exemple : si ton sweep dit "canal 1 = dimmer" au lieu
de "canal 1 = pan_hi", change :

```csharp
_ch[0] = (byte)(dimmer * 255f);   // canal 1 : dimmer
_ch[1] = (byte)(pan16 >> 8);      // canal 2 : pan_hi
// ... etc, garde 14 lignes
```

Profils tout prets a coller dans [`PROJECTEURS.md`](./PROJECTEURS.md#profils-courants-prets-a-coller)
(section 7.D).

Sauvegarde, Unity recompile automatiquement au focus.

### 4. Test complet Unity -> DMX (3 min)

On branche toute la chaine : Unity emet eHuB -> Mappa.Ui recoit et retransmet
en Art-Net -> BC216 -> lyres.

**4a. Prepare Unity**

- Ouvre Unity, charge la scene `Demo.unity`, lance le Play.
- Selectionne `Lyre 1` dans la hierarchie.
- Dans l'Inspector :
  - `LyreController` : monte `dimmer` a 1, choisis une couleur vive.
  - `LyreMovement` : garde `pattern` sur `Static` pour ce premier test (dimmer
    et couleur seuls, sans mouvement). Une fois la lyre allumee, tu passeras
    en `Sweep` avec `amplitude = 0.1`.

**4b. Verifie que Unity emet vraiment**

```powershell
# Terminal 1 (a cote d'Unity) : sniffer eHuB local
python scripts\sniff_ehub.py --devices
```

Doit afficher `id=10..23` (les 14 canaux de Lyre 1).

- Rien → verifie que le GameObject `DeviceEmitter` est actif dans la scene,
  et que Unity est bien en mode Play.

**4c. Lance Mappa.Ui et l'emission Art-Net**

```powershell
# Terminal 2
dotnet run --project csharp\Mappa.Ui
```

Dans l'UI :
1. Charge `configs/ecran.json` (chemin affiche en haut).
2. **Coche `Reception eHuB`** (port 8765).
3. **Coche `Send ArtNet`**.
4. Clique `▶ Start`.

**Attendu** :
- Le compteur `Paquets ArtNet` monte (~40 par univers actif par seconde).
- Sur le materiel, Lyre 1 doit reagir (dimmer plein + couleur vive).

---

## Si ca marche : etapes suivantes (bonus)

Une fois Lyre 1 controlee en direct depuis Unity :
- Duplique les reglages sur Lyre 2, 3, 4.
- Ajuste `centerPan` / `centerTilt` de chaque `LyreMovement` pour viser le
  public plutot qu'un mur/plafond.
- Monte progressivement `amplitude` (0.1 -> 0.3) pour eviter les butees.
- Essaie les autres `pattern` : `Circle`, `Figure8`, `Random`.
- Pour le projecteur : selectionne `Projector`, monte `dimmer` a 1. Le
  canal DMX 169 recevra 255 (a supposer que le canal 1 du projo = dimmer,
  ce qui est le cas courant).

---

## Si rien ne marche : arbre de decision rapide

| Symptome | Cause probable | Action |
|---|---|---|
| Ping .48 KO | Reseau physique | Cable, IP locale, pare-feu |
| Etape 1 : rien ne reagit | BC216 pas patche sur univ 33 ou reseau | Etape 1bis (scan pas-a-pas) |
| Etape 1bis : rien sur aucun univers | Cable/alim/mode lyre ou reseau | `diag_reseau.sh` + cross-check QLC+ / LightJams |
| Etape 1bis : reaction sur univers X != 33 | Patch BC216 different | Change `artnet_universe` dans `ecran.json` ou reconfigure BC216 |
| Etape 4b : sniffer ne voit rien | Unity ne route pas | Verifier GameObject `DeviceEmitter` actif dans la scene, Play lance, IP=127.0.0.1, port=8765 |
| Etape 4c : sniffer OK mais Mappa.Ui `Paquets 0` | Mappa.Ui n'a pas la case `Reception eHuB` cochee OU le pare-feu Windows bloque | Verifier les cases dans l'UI + autoriser dotnet dans le pare-feu prive |
| Etape 4b : Paquets ArtNet > 0 mais lyre inerte | Ordre des canaux faux | Etape 3 (reordonner `_ch[i]`) |
| Lyre s'allume mais ne bouge pas | Canal "speed" a 0 = bloque, ou pattern=Static | Monte `speed` a 0.5, verifie `pattern != Static` |
| Lyre bouge mais dans le mauvais sens | Origine physique differente | Ajuste `centerPan`/`centerTilt` |
| Je bouge `pan` manuellement, ca marche pas | `LyreMovement` ecrase pan/tilt chaque frame | Desactive le composant `Lyre Movement` pour test manuel |

---

## Ce qui est deja teste (tu peux faire confiance)

Verifie automatiquement par **37 tests unitaires** (execute
`dotnet test csharp/Mappa.sln` pour les rejouer) :

- Le format binaire eHuB de `DeviceEmitter.cs` (Unity) est bien decode par
  `EhubReceiver` (Mappa.Ui) - test `DeviceEmitterFormat_IsDecodedByEhubReceiver`.
- Le chemin **complet** Unity->eHuB->State->RoutingPlan->ArtNet aboutit aux
  bons octets DMX sur l'univers 33 - test `EndToEnd_UnityPacket_ProducesCorrectArtNetPacket`.
- `configs/ecran.json` est valide, 16633 entites, 129 univers, 0 collision.
- Les 4 lyres sont routees sur les canaux 1..14, 43..56, 85..98, 127..140.
- Le projecteur est route sur le canal 169.

**Non testable ici** (dependant du materiel) : le BC216, l'adresse DMX de
la lyre, le profil DMX exact des canaux. Ce sont les 3 seuls points a
valider physiquement demain, dans l'ordre des etapes ci-dessus.
