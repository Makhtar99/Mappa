# Outil de débogage (P8)

Onglet **Débogage** de `Mappa.Ui`. Il affiche, étape par étape, la traduction
d'un signal d'animation (**eHuB**, venu d'Unity) en signal lumière
(**ArtNet/DMX**, envoyé aux contrôleurs).

Il n'ajoute rien au fonctionnement du système : il sert à **localiser une panne**.

---

## Le pipeline

```
[Simulation]  →  ①  →  ②  →  ③  →  ④
 (faux Unity)
```

| Nœud | Ce qu'il montre | Question à laquelle il répond |
|------|-----------------|-------------------------------|
| **①** Réception UDP | les paquets eHuB qui arrivent (port 8765) | Unity me parle-t-il ? |
| **②** Détail du paquet | quelles entités, quelles couleurs | Unity dit-il la bonne chose ? |
| **③** Emitter Group → DMX | ces couleurs converties en canaux DMX | ma traduction est-elle correcte ? |
| **④** DMX → ArtNet | l'en-tête du paquet ArtNet : IP, univers, canaux | où et quoi vais-je envoyer ? |

**Méthode :** on lit de gauche à droite. La **première** fenêtre dont l'image est
fausse localise la panne — tout ce qui est à sa gauche fonctionne.

Seul **①** écoute le réseau. ② ③ ④ sont des rendus de calculs internes : ils
s'affichent même sans matériel branché, dès que ① a reçu quelque chose.

---

## Les deux modes

| Mode | Comportement |
|------|--------------|
| **🔍 Observation** (défaut) | Écoute seule. Le panneau Simulation est masqué, toute émission est coupée, l'IP du nœud ④ est en lecture seule. L'outil **ne peut pas perturber** le système. |
| **🧪 Test** | Le panneau Simulation apparaît, les boutons d'émission sont actifs, un bandeau d'alerte s'affiche. L'outil **émet réellement** sur le réseau. |

---

## Lancer l'outil

Depuis `Mappa/csharp` :

```bash
dotnet run --project Mappa.Ui
```

### Recompiler après une modification

```bash
dotnet build Mappa.sln
```

`dotnet run` recompile automatiquement, donc en pratique un simple `dotnet run`
suffit. Il faut en revanche **fermer la fenêtre** avant de relancer, sinon
l'ancienne instance garde le port UDP 8765.

### Relancer proprement (tuer l'instance en cours puis démarrer)

```bash
pkill -f "Mappa.Ui/bin"; dotnet run --project Mappa.Ui
```

### Produire un exécutable autonome (macOS Apple Silicon)

```bash
dotnet publish Mappa.Ui -c Release -r osx-arm64 --self-contained \
  -p:PublishSingleFile=true -o ../dist
```

Le binaire se trouve alors dans `Mappa/dist/Mappa.Ui`.

### Lancer les tests

```bash
dotnet test Mappa.sln
```

---

## Injecter un vrai signal (sans le simulateur intégré)

La commande `emit` du CLI joue le rôle d'Unity **depuis un autre processus**.
Le paquet traverse donc réellement la pile réseau : si ① l'affiche, la réception
eHuB est prouvée pour de bon, pas seulement l'application qui se parle à elle-même.

**Avant :** dans l'outil, nœud ① → **▶ Écouter** (port 8765).

Puis, dans un second terminal, depuis `Mappa/csharp` :

```bash
dotnet run --project Mappa.Cli -- emit --count 20 --color 255,0,0
```

20 entités rouges, envoyées en boucle à 40 Hz. **Ctrl+C** pour arrêter.

### Une seule frame (image figée, pratique pour analyser)

```bash
dotnet run --project Mappa.Cli -- emit --count 20 --color 0,255,0 --frames 1
```

### Options

| Option | Défaut | Rôle |
|--------|--------|------|
| `--host <ip>` | `127.0.0.1` | machine destinataire |
| `--port <n>` | `8765` | port eHuB |
| `--count <n>` | `10` | entités `1..n` à allumer |
| `--color <r,g,b>` | `255,255,255` | couleur |
| `--hz <n>` | `40` | fréquence d'envoi |
| `--frames <n>` | illimité | nombre de frames puis arrêt |
| `--universe <n>` | `0` | univers eHuB |

### Depuis une autre machine

```bash
dotnet run --project Mappa.Cli -- emit --host 192.168.1.20 --count 50
```

`127.0.0.1` signifie « moi-même » : le paquet ne quitte pas la machine. Toute
autre adresse l'envoie réellement sur le réseau.

---

## Les deux numérotations d'univers

Un même univers porte **deux numéros** dans la config :

| Champ | Sens | Exemple (`ecran.json`) |
|-------|------|------------------------|
| `index` | numéro **global**, unique dans l'installation | `34` |
| `artnet_universe` | numéro **local au contrôleur** (0..31) | `0` (sur `ctrl-2`) |

Deux contrôleurs ont donc chacun un « univers 0 » : c'est le couple
**(IP, univers local)** qui désigne une sortie physique, jamais le numéro seul.

Le nœud ④ accepte les deux numérotations en saisie, affiche la traduction
(`index global 34 → univers ArtNet 0`) et **émet toujours le numéro local**,
comme le fait le routage réel. Sans cette traduction, un index global partirait
tel quel dans l'en-tête et le contrôleur ignorerait la trame en silence.

---

## Envoyer vers le vrai matériel

Le nœud ④ émet de l'ArtNet pour de bon (UDP, port 6454).

1. Passer en mode **🧪 Test**
2. Charger une config (l'IP se remplit automatiquement depuis l'univers saisi)
3. Ajuster l'univers, vérifier l'IP affichée
4. **Blanc** / **Noir** pour un test brut, ou **Envoyer la trame** pour la trame courante
5. **▶ Continu** si les LEDs doivent rester allumées — les contrôleurs oublient
   un paquet isolé, ils ont besoin d'un flux continu

Les IP des contrôleurs sont définies dans les configs (`Mappa/configs/*.json`).
À noter : `mini.json` utilise `127.0.0.1`, donc il n'envoie rien sur le réseau —
c'est une config de test volontairement inoffensive.
