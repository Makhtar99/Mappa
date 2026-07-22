#!/usr/bin/env python3
"""Sniffer eHuB : ecoute UDP:8765 en local et affiche les entites recues.

Sert a prouver "Unity envoie-t-il vraiment ?" sans dependre du materiel reel.
Les lyres/projecteur n'ont AUCUN rendu visuel dans Unity : c'est le seul moyen
fiable de verifier que les IDs 1..82 (appareils) partent bien sur le reseau.

Usage:
    python3 scripts/sniff_ehub.py                 # ecoute et affiche tout
    python3 scripts/sniff_ehub.py --devices       # filtre les IDs 1..99 (appareils)
    python3 scripts/sniff_ehub.py --port 8765     # port explicite

/!\\ Ne peut PAS tourner en meme temps que Mappa.Ui (meme port).
     Arreter Mappa.Ui (decocher "Reception eHuB") avant, ou binder sur un autre
     port et changer l'ip/port du DeviceEmitter Unity temporairement.
"""

import argparse
import gzip
import socket
import struct
import sys
import time
from collections import defaultdict

MAGIC = b"eHuB"
TYPE_UPDATE = 2

def parse(pkt):
    """Retourne (universe, [(id, r, g, b, w), ...]) ou None si invalide."""
    if len(pkt) < 10 or pkt[:4] != MAGIC:
        return None
    ptype = pkt[4]
    universe = pkt[5]
    count = struct.unpack_from("<H", pkt, 6)[0]
    gzlen = struct.unpack_from("<H", pkt, 8)[0]
    if ptype != TYPE_UPDATE:
        return universe, []  # type=1 = config eHuB, on ignore les entites
    try:
        payload = gzip.decompress(pkt[10:10 + gzlen])
    except OSError:
        return None
    entities = []
    for i in range(0, len(payload), 6):
        if i + 6 > len(payload):
            break
        eid = struct.unpack_from("<H", payload, i)[0]
        r, g, b, w = payload[i + 2], payload[i + 3], payload[i + 4], payload[i + 5]
        entities.append((eid, r, g, b, w))
    return universe, entities


def main():
    ap = argparse.ArgumentParser(description="Sniffer eHuB UDP")
    ap.add_argument("--port", type=int, default=8765)
    ap.add_argument("--devices", action="store_true",
                    help="filtre les IDs < 100 (les appareils: 1, 10-22, 30-42, ...)")
    ap.add_argument("--every", type=float, default=1.0,
                    help="affiche un resume toutes les N secondes (defaut 1s)")
    args = ap.parse_args()

    s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    s.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    try:
        s.bind(("0.0.0.0", args.port))
    except OSError as e:
        print(f"ERREUR bind UDP:{args.port} -> {e}")
        print("Probable cause: Mappa.Ui ecoute deja ce port (case 'Reception eHuB').")
        print("Decoche-la ou arrete Mappa.Ui avant de sniffer.")
        sys.exit(1)
    s.settimeout(0.5)

    print(f"Ecoute UDP:{args.port}. Ctrl+C pour arreter.")
    if args.devices:
        print("Filtre: entites d'ID < 100 (les appareils)")
    print()

    last_seen = {}         # eid -> (r, g, b, w)
    universes_seen = set()
    n_packets = 0
    n_entities = 0
    t_start = time.time()
    t_last_report = t_start

    try:
        while True:
            try:
                pkt, addr = s.recvfrom(65535)
            except socket.timeout:
                pkt = None

            if pkt:
                res = parse(pkt)
                if res is not None:
                    universe, entities = res
                    universes_seen.add(universe)
                    n_packets += 1
                    for eid, r, g, b, w in entities:
                        if args.devices and eid >= 100:
                            continue
                        last_seen[eid] = (r, g, b, w)
                        n_entities += 1

            now = time.time()
            if now - t_last_report >= args.every:
                t_last_report = now
                elapsed = now - t_start
                print(f"[{elapsed:6.1f}s] paquets={n_packets}  entites_ecrites={n_entities}"
                      f"  univers vus={sorted(universes_seen)}  distinctes={len(last_seen)}")
                if last_seen:
                    # affiche les 20 entites au plus bas ID (utile pour les appareils)
                    to_show = sorted(last_seen.items())[:20]
                    for eid, (r, g, b, w) in to_show:
                        marker = " <- appareil" if eid < 100 else ""
                        print(f"    id={eid:5d}  R={r:3d} G={g:3d} B={b:3d} W={w:3d}{marker}")
                    print()
    except KeyboardInterrupt:
        print("\nArrete.")


if __name__ == "__main__":
    main()
