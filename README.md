# LedNetwork — partie réseau (C#)

Couche réseau de l'installation LED, en C# / .NET 8. Elle implémente les **deux liens réseau** du schéma d'architecture :

```
┌─────────────────────┐   UDP perso (port 7000)   ┌──────────────────┐   Art-Net UDP (6454)   ┌──────────────┐
│ Outil de conception │ ────────────────────────▶ │ Outil de routage │ ─────────────────────▶ │ Contrôleurs  │
│    (artistique)     │   "état souhaité" RGBW    │   (ce projet)    │   paquets ArtDMX       │    BC216     │ ─▶ Rubans LED
└─────────────────────┘                           └──────────────────┘                        └──────────────┘
```

## Structure

| Dossier | Rôle |
|---|---|
| `src/LedNetwork.Core/ArtNet/` | Modèle des paquets Art-Net (ArtDMX) — sérialisation/parsing binaire |
| `src/LedNetwork.Core/Transport/` | Émetteur (`ArtNetSender`) et récepteur (`ArtNetReceiver`) UDP Art-Net |
| `src/LedNetwork.Core/DesignProtocol/` | Protocole UDP personnalisé conception → routage (`DesignStateMessage`) |
| `src/LedNetwork.Core/Routing/` | Cœur du routage : patch entité→DMX (`FixturePatch`) et `DmxRouter` |
| `src/LedNetwork.Host/` | Programme console de démonstration (l'outil de routage en marche) |

## Prérequis

Le **SDK .NET 8** n'est pas encore installé sur cette machine. Installez-le :

```bash
# Ubuntu / WSL
sudo apt-get update && sudo apt-get install -y dotnet-sdk-8.0
# ou via le script officiel :
curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 8.0
```

## Compiler et lancer

```bash
cd led-network
dotnet build
dotnet run --project src/LedNetwork.Host
```

Dans la fenêtre de l'hôte, appuyez sur **Entrée** pour émettre une frame de test :
elle simule l'outil de conception (loopback UDP), traverse le routeur et produit
des paquets ArtDMX vers les IP de contrôleurs configurées dans `Program.cs`.

## Points à adapter à votre installation

- **Adresses IP des contrôleurs BC216** et **mapping univers** : `Program.cs`.
- **Table de patch** (entité → univers / canal / ordre couleur) : `Program.cs` /
  à externaliser dans un fichier de config (JSON).
- **Format du protocole de conception** : `DesignStateMessage` — alignez-le sur ce
  que produit réellement votre outil de conception.
- **DMX512 / pixel SPI** : côté contrôleur (hardware). Ce projet s'arrête à l'Art-Net,
  le BC216 se charge de la conversion vers SPI/DMX physique.

## Prochaines étapes possibles

- ArtPoll / ArtPollReply pour la **découverte automatique** des contrôleurs.
- Chargement de la config (patch + contrôleurs) depuis un fichier JSON.
- Cadencement à fréquence fixe (~40 Hz) plutôt que déclenché à la réception.
- Outils de **surveillance** entrée/sortie (via `ArtNetReceiver`).
