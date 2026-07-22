#!/usr/bin/env bash
# dmx_probe : envoie une valeur sur UN canal DMX precis de UN appareil precis,
# via la vraie config (donc passe par univers 33 -> .48).
#
# Sert a cartographier empiriquement le brochage DMX reel des lyres/projecteur:
# on pousse 255 sur un canal a la fois et on regarde ce qui reagit
# physiquement (pan bouge ? dimmer ouvre ? couleur change ?).
#
# Correspondance canaux DMX -> IDs entites (via config ecran.json actuel) :
#   Projector  canal 1       -> entite 1        (mono-canal par defaut)
#   Lyre 1     canaux 1..14  -> entites 10..23
#   Lyre 2     canaux 1..14  -> entites 30..43
#   Lyre 3     canaux 1..14  -> entites 50..63
#   Lyre 4     canaux 1..14  -> entites 70..83
#
# Note: si le projecteur est en fait RGB (3 canaux) ou RGBW+dimmer (5), il
# faut ajuster channelCount dans la scene Unity ET etendre la ligne
# 'Projector' de configs/ecran.json (entity_end et channel_start suivants).
#
# Usage:
#   ./scripts/dmx_probe.sh <appareil> <canal 1..N> [valeur 0..255]
#   Ex: ./scripts/dmx_probe.sh lyre1 6 255   # canal 6 de Lyre 1 a fond
#   Ex: ./scripts/dmx_probe.sh proj 1 255    # canal 1 du projecteur a fond
#
# Astuce cartographie: lance en boucle sur les 14 canaux d'une lyre pour voir
# lequel fait quoi. Utilise ./scripts/dmx_sweep.sh pour l'auto-cartographie.

set -eu

if [[ $# -lt 2 ]]; then
  cat <<EOF
Usage: $0 <appareil> <canal> [valeur]
  appareil : proj | lyre1 | lyre2 | lyre3 | lyre4
  canal    : 1 (proj) ou 1..14 (lyre)
  valeur   : 0..255 (defaut 255)
Exemples:
  $0 lyre1 6 255     # test canal 6 lyre 1 (souvent dimmer) plein
  $0 proj  1 255     # test canal 1 projecteur (souvent dimmer) plein
EOF
  exit 1
fi

APPAREIL="$1"
CANAL="$2"
VAL="${3:-255}"
IP="${IP:-192.168.1.48}"
CONFIG="${CONFIG:-configs/ecran.json}"

# Mapping appareil -> base entity ID (voir ecran.json)
case "$APPAREIL" in
  proj|projector|projo) BASE=1;  MAX=1;  NAME="Projecteur" ;;
  lyre1)                BASE=10; MAX=14; NAME="Lyre 1" ;;
  lyre2)                BASE=30; MAX=14; NAME="Lyre 2" ;;
  lyre3)                BASE=50; MAX=14; NAME="Lyre 3" ;;
  lyre4)                BASE=70; MAX=14; NAME="Lyre 4" ;;
  *) echo "Appareil inconnu: $APPAREIL"; exit 1 ;;
esac

if [[ "$CANAL" -lt 1 || "$CANAL" -gt "$MAX" ]]; then
  echo "Canal $CANAL hors bornes 1..$MAX pour $NAME"
  exit 1
fi

ENTITY=$((BASE + CANAL - 1))

echo "[$NAME] canal DMX $CANAL -> entite $ENTITY -> valeur $VAL (5 secondes)"
echo "  (Avec led_type RAW1, seul l'octet R de la couleur passe. Donc --color $VAL,0,0)"

dotnet run --project csharp/Mappa.Cli -- send "$CONFIG" \
  --ip "$IP" --entity "$ENTITY" --color "$VAL,0,0" --frames 200 --hz 40
