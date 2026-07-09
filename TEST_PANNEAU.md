# Tester sur le vrai panneau LED (mur Glassworks)

Guide pas-à-pas pour rejouer ce qui a été fait : envoyer des couleurs, du texte
et des animations **sur le vrai mur LED** via Art-Net.

> **En bref** : le mur est piloté en **Art-Net (DMX512 sur UDP, port 6454)** par
> **4 contrôleurs** (`192.168.1.45` à `.48`). On se connecte à leur **Wi-Fi**, et
> on envoie les paquets depuis **Windows / PowerShell**. La config exacte du mur
> est dans [`configs/ecran.json`](configs/ecran.json).

---

## 1. Le matériel et le réseau

Le mur (fourni par le Groupe LAPS, doc : <https://learn.glassworks.tech/led/arch/ecran-led/>) :

- Cadre 2 m × 2 m, **128 × 128 LED visibles**, **64 bandes** de 259 LED.
- Chaque bande monte puis descend ; les LED en positions 1 / 129 / 259 sont des
  **fixations invisibles**.
- Une bande = **2 univers** (170 + 89 LED).
- **4 contrôleurs**, chacun gère **32 univers (0 → 31)** :

  | Entités        | Univers | IP contrôleur   |
  | -------------- | ------- | --------------- |
  | 100 – 4858     | 0 → 31  | `192.168.1.45`  |
  | 5100 – 9858    | 0 → 31  | `192.168.1.46`  |
  | 10100 – 14858  | 0 → 31  | `192.168.1.47`  |
  | 15100 – 19858  | 0 → 31  | `192.168.1.48`  |

### Wi-Fi du panneau

- **SSID** : `GLASS_RESEAUX`
- **Mot de passe** : `networks`

> **Attention** : une fois connecté à `GLASS_RESEAUX`, **il n'y a plus d'accès
> Internet**. Il faut donc **tout installer et compiler AVANT** (voir §2), puis
> basculer sur ce Wi-Fi pour envoyer.

---

## 2. Préparation (AVEC Internet, sur Wi-Fi normal)

Tout se fait **côté Windows / PowerShell** (le Wi-Fi appartient à Windows ; depuis
WSL2 l'UDP local passe mal à cause du NAT).

```powershell
# 1. Installer le SDK .NET 8
winget install Microsoft.DotNet.SDK.8

# 2. Rouvrir PowerShell, puis vérifier
dotnet --version           # doit afficher 8.0.xxx

# 3. Compiler le projet (le code est dans WSL, accessible via \\wsl$)
cd \\wsl$\Ubuntu\home\thibaut\ecole\Mappa\csharp
dotnet build
```

> **Recompile après toute modification du code C#** (`dotnet build`).

---

## 3. Se connecter au panneau

1. Connecter Windows au Wi-Fi **`GLASS_RESEAUX`** (mot de passe `networks`).
2. Vérifier le réseau :

```powershell
ipconfig      # l'IP Wi-Fi doit être en 192.168.1.x
arp -a        # doit lister 192.168.1.45 (et .46/.47/.48 s'ils sont présents)
```

---

## 4. Les commandes de test

Toutes se lancent depuis `...\Mappa\csharp`. **`Ctrl+C`** arrête et éteint les LED.

### a) Allumer 1 pixel — calibration / test de base

```powershell
dotnet run --project Mappa.Cli -- pixel ..\configs\ecran.json --x 0 --y 0 --color 255,0,0
```

- `--x` / `--y` : position logique (0-127, origine **en haut à gauche**).
- `--color r,g,b` : couleur 0-255.

Sert à vérifier l'orientation : `--x 0 --y 0` = coin haut-gauche, `--x 120` = à droite.

### b) Afficher du texte

```powershell
dotnet run --project Mappa.Cli -- text ..\configs\ecran.json "FAIT AVEC CLAUDE LE MEILLEUR" --color 0,200,255
```

- `--color r,g,b` : couleur du texte.
- `--x` / `--y` : coin haut-gauche du texte (défaut `0,0`).
- `--flip-x` / `--flip-y` : inverse l'axe si le rendu est en miroir / à l'envers.
- `--preview` : **aperçu ASCII dans le terminal, sans rien envoyer** (pratique
  hors panneau).
- `--once` : envoie une seule frame au lieu de la boucle.

Aperçu sans matériel :

```powershell
dotnet run --project Mappa.Cli -- text ..\configs\ecran.json "CLAUDE" --preview
```

### c) Animer le texte

```powershell
dotnet run --project Mappa.Cli -- anim ..\configs\ecran.json "CLAUDE" --color 0,200,255
```

Enchaîne en boucle : **clignotement** → **apparition en haut** → **apparition en
bas** → **grossissement centré** → **sauts aléatoires** (taille + couleur).

- `--speed 2` : 2× plus rapide (`0.5` = plus lent).
- `--loops 3` : joue 3 cycles puis s'arrête (sinon boucle infinie).

> Astuce : le grossissement rend mieux avec un **texte court** (« CLAUDE », un
> prénom). Une phrase longue remplit déjà la largeur et ne peut pas grossir.

### d) Diagnostic — si rien ne s'allume

```powershell
dotnet run --project Mappa.Cli -- scan --ip 192.168.1.45 --broadcast --universes 32 --hold 8
```

Envoie une couleur vive sur **tous les canaux de tous les univers 0-31**, en
unicast **et** broadcast. Si quoi que ce soit peut s'allumer, ça s'allume.

- `--step` : allume **un univers à la fois** (pour repérer quelle zone = quel univers).
- `--universes n` : nombre d'univers balayés.

---

## 5. Comment ça marche (le pipeline)

```
  Couleur/Texte/Anim          RoutingPlan                 ArtNetSender
 ┌────────────────┐   écrit  ┌──────────────┐   Render   ┌──────────────┐
 │  Text.Draw*    ├─────────▶│    State     ├───────────▶│ paquets      │──▶ UDP 6454
 │  (x,y → entité)│   RVBW   │ (buffer RVBW)│  {u:byte[]} │ ArtDMX       │   vers .45/.46/
 └────────────────┘          └──────────────┘            └──────────────┘   .47/.48
```

1. **`Text`** convertit une position logique **(x, y)** en **ID d'entité** selon la
   géométrie réelle du mur (`Text.WallEntityId`), et écrit la couleur dans le `State`.
2. **`RoutingPlan.Render(state)`** projette le `State` en **un paquet DMX de 512
   octets par univers**.
3. **`ArtNetSender.SendPlan(config, packets)`** encapsule chaque univers en paquet
   **ArtDMX** et l'envoie à l'**IP du contrôleur** qui le pilote.

### Le point clé qui faisait tout planter

Chaque contrôleur a sa **propre** numérotation d'univers **0-31**. En interne, la
config a besoin d'un identifiant d'univers **unique** (`index` : 0-31 pour `.45`,
100-131 pour `.46`, etc.). Mais le numéro **réellement envoyé dans le paquet
Art-Net** doit être l'univers **local (0-31)**, pas l'index interne.

C'est le rôle du champ **`artnet_universe`** (classe `Universe`) : `SendPlan`
envoie `EffectiveArtNetUniverse` (0-31) à la bonne IP. Sans ça, seuls les univers
0-31 (contrôleur `.45`) répondaient et le texte était **coupé au premier quart**.

---

## 6. Mapping (x, y) → entité (résumé)

Pour une position logique `(col, row)`, `row = 0` en haut :

```
band          = col / 2                       # 64 bandes physiques
descente      = (col % 2) != 0                # colonne paire = montée, impaire = descente
bandeDansCtrl = band % 16
controleur    = band / 16                     # 0=.45, 1=.46, 2=.47, 3=.48
entitéDeBase  = 100 + bandeDansCtrl*300 + controleur*5000
ledIndex      = (montée)  1 + (127 - row)     # 1..128
              = (descente) 130 + row          # 130..257
entité        = entitéDeBase + ledIndex
```

> **Calibrable** : si le rendu est inversé sur le vrai mur, `--flip-x` / `--flip-y`
> corrigent l'orientation sans recompiler.

---

## 7. Générer une config depuis un Excel (import dynamique)

Les fichiers JSON de `configs/` ne sont **pas écrits à la main** : ils sont générés
à partir d'un Excel d'adressage (format eHuB). Pour réagir à **différents panneaux**,
il suffit d'importer un autre `.xlsx` — aucune ligne de code à changer.

```powershell
# Génère configs/Ecran.json à partir de l'Excel
dotnet run --project Mappa.Cli -- import "$env:USERPROFILE\Downloads\Ecran.xlsx"

# Ou en choisissant la sortie / le nom / la feuille
dotnet run --project Mappa.Cli -- import panneau2.xlsx --out ..\configs\panneau2.json --name panneau2
```

Sortie type :

```
Importe    : Ecran.xlsx
Lignes     : 133 plages d'entites
Controleurs: 4 (192.168.1.45, 192.168.1.46, 192.168.1.47, 192.168.1.48)
Univers    : 129
Entites    : 16633
Geometrie  : 128 x 128 (serpentin, 259 LED/bande, 16 bandes/controleur)
Validation : OK
-> ecrit : ../configs/Ecran.json
```

Ensuite toutes les commandes (`text`, `image`, `anim`, `pixel`) marchent sur cette
config générée, **sans passer `--cols`/`--rows`** : la géométrie est déduite
automatiquement.

### Ce que fait l'import (l'architecture)

- **Colonnes lues** dans l'Excel : `Name`, `Entity Start`, `Entity End`, `ArtNet IP`,
  `ArtNet Universe` (les autres colonnes sont ignorées).
- **Couche d'ingestion** (CLI, `EhubXlsx.cs`) : lit le `.xlsx` (un ZIP de XML) sans
  aucune dépendance externe (`System.IO.Compression` + `System.Xml.Linq`).
- **Cœur `Mappa` (`ConfigBuilder`)** : transforme des lignes `EhubRow` en `Config`.
  Chaque IP distincte → un contrôleur ; chaque univers reçoit un `index` global unique
  **et** son `artnet_universe` local (0-31) ; les canaux sont empilés si plusieurs
  lignes partagent le même univers. Ce code est **pur** → réutilisable par Unity plus
  tard (Unity fournira sa propre lecture d'Excel et appellera `ConfigBuilder`).
- **Géométrie (`WallGeometry.Infer`)** : déduit largeur/hauteur/serpentin du schéma
  d'adressage. Les appareils auxiliaires (lyres, projecteur — tailles de bande
  différentes) sont automatiquement ignorés pour le calcul du mur.

> Note : `configs/ecran.json` (écrit à la main historiquement) et `configs/Ecran.json`
> (généré par `import`) décrivent le même mur ; ils diffèrent seulement par la
> numérotation des `index` d'univers, ce qui n'a aucun impact (c'est
> `artnet_universe` qui part sur le fil).

---

## 8. Dépannage rapide

| Symptôme                              | Cause probable / solution                                         |
| ------------------------------------- | ----------------------------------------------------------------- |
| Rien ne s'allume du tout              | Pare-feu Windows (autoriser l'app), ou pas sur `GLASS_RESEAUX`.   |
| Seul le quart gauche s'allume         | `artnet_universe` manquant → vérifier `configs/ecran.json`.       |
| Texte à l'envers / en miroir          | Ajouter `--flip-y` et/ou `--flip-x`.                              |
| Texte coupé à droite                  | Texte trop long ; utiliser `text` (pas `anim` qui est mono-ligne). |
| `dotnet` introuvable                  | Installer le SDK 8 **avant** de passer sur `GLASS_RESEAUX`.       |
| Pas d'Internet une fois connecté      | Normal : `GLASS_RESEAUX` n'a pas de sortie Internet.              |
```
