#!/usr/bin/env bash
# dmx_sweep_rotation : identifie les canaux pan/tilt d'une lyre.
#
# Envoie une rampe progressive 0->255->0 (triangle) sur CHAQUE canal de la
# lyre, un a la fois. Contrairement a dmx_sweep.sh qui envoie 255 statique
# (test "on/off"), cette rampe rend un mouvement continu evident :
#   - pan   : la lyre tourne lentement d'un cote a l'autre.
#   - tilt  : la lyre monte et descend.
#   - dimmer: l'intensite lumineuse varie (mais pas de mouvement).
#   - autres: rien ou variation de couleur/strobe/etc.
#
# Note ce que tu observes a chaque canal, on batit la vraie table pan/tilt.
#
# Usage: ./scripts/dmx_sweep_rotation.sh <lyre1|lyre2|lyre3|lyre4>
#
# Variables d'env : IP, CONFIG, SECONDS_PER_CH (defaut 8s), HOLD_BETWEEN (2s)

set -u

APPAREIL="${1:-lyre1}"
IP="${IP:-192.168.1.48}"
CONFIG="${CONFIG:-configs/ecran.json}"
SECONDS_PER_CH="${SECONDS_PER_CH:-8}"
HOLD_BETWEEN="${HOLD_BETWEEN:-2}"

case "$APPAREIL" in
  lyre1) BASE=10; NAME="Lyre 1" ;;
  lyre2) BASE=30; NAME="Lyre 2" ;;
  lyre3) BASE=50; NAME="Lyre 3" ;;
  lyre4) BASE=70; NAME="Lyre 4" ;;
  *) echo "Appareil inconnu: $APPAREIL (lyre1|lyre2|lyre3|lyre4)"; exit 1 ;;
esac

MAX=13

echo "======================================================================"
echo "  Sweep ROTATION : $NAME ($MAX canaux DMX)"
echo "  Rampe 0->255->0 sur chaque canal, ${SECONDS_PER_CH}s par canal."
echo "  Base entite = $BASE (entite $BASE = canal DMX 1 de la lyre)."
echo ""
echo "  Cherche a chaque canal :"
echo "    - Mouvement horizontal continu   -> PAN"
echo "    - Mouvement vertical continu     -> TILT"
echo "    - Variation d'intensite          -> DIMMER"
echo "    - Rien / bruit / flash           -> speed/strobe/couleur/reset"
echo "======================================================================"
echo

declare -a NOTES

for ((c=1; c<=MAX; c++)); do
  entity=$((BASE + c - 1))
  echo
  echo "  --- Canal $c (entite $entity) : rampe ${SECONDS_PER_CH}s ---"
  echo "  Regarde la $NAME : bouge-t-elle en continu ?"
  dotnet run --project csharp/Mappa.Cli -- ramp "$CONFIG" \
    --ip "$IP" --entity "$entity" --seconds "$SECONDS_PER_CH" \
    2>/dev/null || echo "  (erreur d'envoi, verifier reseau/config)"
  read -rp "  Reaction ? [p=pan, t=tilt, d=dimmer, s=strobe, c=couleur, r=rien, autre=libre] : " ans
  NOTES[$c]="$ans"
  sleep "$HOLD_BETWEEN"
done

echo
echo "======================================================================"
echo "  Cartographie ROTATION de $NAME"
echo "======================================================================"
for ((c=1; c<=MAX; c++)); do
  entity=$((BASE + c - 1))
  printf "  canal %2d  (entite %2d)  ->  %s\n" "$c" "$entity" "${NOTES[$c]:-?}"
done

echo
echo "Interpretation :"
echo "  - Si 2 canaux CONSECUTIFS sont 'pan' (ex: canal 1 et 2), la lyre"
echo "    est en 16 bits (pan_hi + pan_lo). LyreController.cs actuel est OK."
echo "  - Si UN SEUL canal est 'pan' et le suivant fait 'tilt' directement,"
echo "    la lyre est en 8 bits pur. Bascule sur LyreController8bit.cs :"
echo "    voir docs/PROJECTEURS.md section 'Profil 8 bits'."
echo "  - Si un canal 'rien' est intercale, il faut peut-etre y mettre 255"
echo "    (speed a fond) pour que la lyre reagisse aux mouvements suivants."
