#!/usr/bin/env bash
# Diagnostic reseau AVANT de lancer les tests DMX.
# Repond a: peux-tu joindre le controleur .48 ? es-tu sur le bon reseau ?
# le port ArtNet 6454 est-il ouvert cote sortie ?
#
# Usage: ./scripts/diag_reseau.sh [IP]  (defaut: 192.168.1.48)

set -u
IP="${1:-192.168.1.48}"
SUBNET="${IP%.*}"  # ex: 192.168.1

echo "======================================================================"
echo "  Diagnostic reseau vers $IP"
echo "======================================================================"
echo

# 1) Interfaces reseau ---------------------------------------------------
echo "-- 1) Interfaces reseau (cherche une IP dans $SUBNET.x) --"
if command -v ip >/dev/null; then
  ip -brief -4 addr | awk '{print "   "$0}'
else
  ifconfig 2>/dev/null | grep -E "inet |^[a-z]" | awk '{print "   "$0}'
fi
if command -v ip >/dev/null && ip -4 addr | grep -q " $SUBNET\."; then
  echo "   -> OK : une interface est sur le sous-reseau $SUBNET.0/24"
else
  echo "   /!\\ Aucune interface sur $SUBNET.x. Es-tu bien connecte au reseau GLASS ?"
fi
echo

# 2) Ping ----------------------------------------------------------------
echo "-- 2) Ping vers $IP (3 paquets) --"
if ping -c 3 -W 1 "$IP" >/dev/null 2>&1; then
  echo "   -> OK : $IP repond au ping"
else
  echo "   /!\\ $IP ne repond pas. Peut etre normal (certains BC216 bloquent ICMP),"
  echo "       mais si le reseau est absent, ArtNet ne partira pas non plus."
fi
echo

# 3) Route ---------------------------------------------------------------
echo "-- 3) Route vers $IP --"
if command -v ip >/dev/null; then
  ip route get "$IP" 2>/dev/null | awk '{print "   "$0}'
fi
echo

# 4) UDP sortant vers 6454 -----------------------------------------------
echo "-- 4) Test UDP sortant vers $IP:6454 (envoi 1 paquet) --"
python3 - <<PYEOF || echo "   /!\\ python3 non dispo, saute ce test"
import socket, sys
s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
s.settimeout(1.0)
try:
    s.sendto(b"Art-Net\x00" + b"\x00"*10, ("$IP", 6454))
    print("   -> Envoi UDP OK (aucune erreur socket)")
except Exception as e:
    print(f"   /!\\ Erreur envoi: {e}")
PYEOF
echo

# 5) Pare-feu Windows (WSL) ---------------------------------------------
if grep -qi microsoft /proc/version 2>/dev/null; then
  echo "-- 5) WSL detecte : rappel pare-feu Windows --"
  echo "   Si dotnet.exe/Mappa.Cli n'est pas autorise a envoyer sur UDP:6454,"
  echo "   Windows silencieusement DROPPE les paquets. Verifier:"
  echo "     Panneau Config -> Pare-feu Defender -> Autoriser une application"
  echo "     ou lancer un test rapide en admin sur Windows natif (PowerShell)."
  echo
fi

# 6) .NET SDK dispo ------------------------------------------------------
echo "-- 6) .NET SDK --"
if command -v dotnet >/dev/null; then
  echo "   -> dotnet $(dotnet --version) present"
else
  echo "   /!\\ dotnet introuvable. Installer le SDK 8 avant les tests."
fi
echo

echo "======================================================================"
echo "  Fini. Si tout est vert (ou juste ping KO acceptable), lance ensuite:"
echo "    ./scripts/test_projecteurs.sh"
echo "======================================================================"
