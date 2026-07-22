#!/usr/bin/env bash
# dmx_sweep : cartographie automatique du brochage DMX d'un appareil.
# Envoie 255 sur chaque canal a tour de role et te demande ce qui reagit
# physiquement. Note ton observation, on batit la table canal -> fonction.
#
# Usage: ./scripts/dmx_sweep.sh <appareil>
#   appareil : proj | lyre1 | lyre2 | lyre3 | lyre4
#
# Resultat: un tableau canal -> fonction que tu pourras reporter dans
# LyreController.cs / ProjectorController.cs si l'ordre par defaut est faux.

set -u

APPAREIL="${1:-lyre1}"
HOLD="${HOLD:-4}"          # secondes par canal
IP="${IP:-192.168.1.48}"
CONFIG="${CONFIG:-configs/ecran.json}"

case "$APPAREIL" in
  proj|projector|projo) BASE=1;  MAX=1;  NAME="Projecteur" ;;
  lyre1)                BASE=10; MAX=14; NAME="Lyre 1" ;;
  lyre2)                BASE=30; MAX=14; NAME="Lyre 2" ;;
  lyre3)                BASE=50; MAX=14; NAME="Lyre 3" ;;
  lyre4)                BASE=70; MAX=14; NAME="Lyre 4" ;;
  *) echo "Appareil inconnu: $APPAREIL (proj|lyre1|lyre2|lyre3|lyre4)"; exit 1 ;;
esac

echo "======================================================================"
echo "  Sweep DMX : $NAME ($MAX canaux), $HOLD secondes par canal"
echo "  Note ce qui reagit a chaque canal (pan, tilt, dimmer, couleur...)"
echo "======================================================================"
echo

declare -a NOTES

for ((c=1; c<=MAX; c++)); do
  entity=$((BASE + c - 1))
  echo
  echo "  --- Canal $c (entite $entity) : ${HOLD}s a 255 ---"
  echo "  Regarde le $NAME et note ce qui bouge/change."
  dotnet run --project csharp/Mappa.Cli -- send "$CONFIG" \
    --ip "$IP" --entity "$entity" --color "255,0,0" --frames "$((HOLD * 40))" --hz 40 \
    >/dev/null 2>&1 || true
  read -rp "  Reaction sur canal $c ? (ex: pan / tilt / dimmer / rouge / rien / bruit) : " ans
  NOTES[$c]="$ans"
done

echo
echo "======================================================================"
echo "  Cartographie de $NAME (a reporter dans LyreController.cs)"
echo "======================================================================"
for ((c=1; c<=MAX; c++)); do
  printf "  canal %2d  ->  %s\n" "$c" "${NOTES[$c]:-?}"
done
echo
echo "Si l'ordre est different de celui code dans LyreController.cs, reorganise"
echo "les affectations _ch[i] dans ce fichier (voir docs/PROJECTEURS.md section 3)."
