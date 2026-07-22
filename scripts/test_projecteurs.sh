#!/usr/bin/env bash
# Test progressif pour allumer les projecteurs/lyres (univers 33 sur 192.168.1.48).
# Se lance depuis la racine du repo:  ./scripts/test_projecteurs.sh
#
# Chaque etape est explicite: on montre la commande, on lance, puis on te demande
# si tu as vu quelque chose reagir avant de passer a la suivante. Ctrl+C a tout
# moment interrompt le test en cours (la commande envoie du noir pour eteindre).

set -u

IP="${IP:-192.168.1.48}"
CONFIG="${CONFIG:-configs/ecran.json}"
CLI="dotnet run --project csharp/Mappa.Cli --"

if [[ ! -f "$CONFIG" ]]; then
  echo "ERREUR: config introuvable: $CONFIG (lance ce script depuis la racine du repo)"
  exit 1
fi

echo "======================================================================"
echo "  Test projecteurs/lyres  ->  IP=$IP  config=$CONFIG"
echo "======================================================================"
echo

pause_yes_no() {
  local msg="$1"
  local ans
  while true; do
    read -rp "$msg [o/n] " ans
    case "${ans,,}" in
      o|oui|y|yes) return 0 ;;
      n|non|no)    return 1 ;;
    esac
  done
}

run() {
  echo
  echo "----> $*"
  eval "$*"
  local rc=$?
  echo "(retour: $rc)"
  return $rc
}

# ---------------------------------------------------------------------------
echo "TEST 1 - Scan full univers 33 (RGB plein a 255 sur tous les canaux)"
echo "         C'est le test le plus brutal: si le materiel/reseau marche,"
echo "         quelque chose DOIT reagir (dimmer force ouvert)."
echo
run "$CLI scan --ip $IP --universes 34 --hold 5"

if pause_yes_no "Est-ce que QUELQUE CHOSE s'est allume/agite pendant le scan ?"; then
  echo "OK: sortie physique validee. On peut affiner."
else
  echo
  echo "STOP: le materiel/reseau ne repond pas. A verifier AVANT de continuer:"
  echo "  1) Es-tu bien sur le reseau du panneau (GLASS) ?"
  echo "  2) Ping 192.168.1.48 depuis cette machine ?"
  echo "  3) Le controleur .48 est-il sous tension et patche sur univers 33 ?"
  echo "  4) Pare-feu Windows: autoriser dotnet.exe (UDP sortant vers 6454) ?"
  echo
  if ! pause_yes_no "Continuer quand meme (pour tester le pas-a-pas) ?"; then
    exit 1
  fi
fi

# ---------------------------------------------------------------------------
echo
echo "TEST 2a - Scan pas-a-pas: on balaye univers 0 -> 33, 3s chacun."
echo "          Note l'univers qui allume les projecteurs (attendu: 33)."
echo
run "$CLI scan --ip $IP --universes 34 --step --hold 3"

# ---------------------------------------------------------------------------
echo
echo "TEST 2b - Envoi cible par entite (via ecran.json)."
echo "          Attention: 'send' n'ecrit que RGB (3 canaux) sur l'entite."
echo "          Si la lyre a un DIMMER separe non ouvert, elle restera noire"
echo "          meme bien adressee. C'est normal, on le documente."
echo

for ENTITY in 1 10 30 50 70; do
  echo
  echo "  --- Entite $ENTITY (5s en blanc) ---"
  run "$CLI send $CONFIG --ip $IP --entity $ENTITY --color 255,255,255 --frames 200 --hz 40"
  pause_yes_no "  Reaction sur l'entite $ENTITY ?" && echo "    => note: entite $ENTITY reagit" \
                                                   || echo "    => note: entite $ENTITY NE reagit PAS"
done

# ---------------------------------------------------------------------------
echo
echo "======================================================================"
echo "  Termine. Recap des tests dispo pour la suite:"
echo "    Reallumer full univers 33 :"
echo "      $CLI scan --ip $IP --universes 34 --hold 5"
echo "    Reallumer une entite      :"
echo "      $CLI send $CONFIG --ip $IP --entity <id> --color 255,255,255"
echo "======================================================================"
