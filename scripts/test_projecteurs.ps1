# Test progressif pour allumer les projecteurs/lyres (univers 33 sur 192.168.1.48).
# Se lance depuis la racine du repo :  .\scripts\test_projecteurs.ps1
#
# Meme deroulement que test_projecteurs.sh mais en PowerShell (Windows natif).

param(
  [string]$Ip     = "192.168.1.48",
  [string]$Config = "configs/ecran.json"
)

$ErrorActionPreference = "Continue"
$Cli = "dotnet run --project csharp/Mappa.Cli --"

if (-not (Test-Path $Config)) {
  Write-Host "ERREUR: config introuvable: $Config (lance ce script depuis la racine du repo)" -ForegroundColor Red
  exit 1
}

Write-Host "======================================================================"
Write-Host "  Test projecteurs/lyres  ->  IP=$Ip  config=$Config"
Write-Host "======================================================================"

function Ask-YesNo($msg) {
  while ($true) {
    $ans = Read-Host "$msg [o/n]"
    if ($ans -match '^(o|oui|y|yes)$') { return $true }
    if ($ans -match '^(n|non|no)$')    { return $false }
  }
}

function Run($cmd) {
  Write-Host ""
  Write-Host "----> $cmd" -ForegroundColor Cyan
  Invoke-Expression $cmd
  Write-Host "(retour: $LASTEXITCODE)"
}

# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "TEST 1 - Scan full univers 33 (RGB plein a 255 sur tous les canaux)"
Write-Host "         C'est le test le plus brutal: si le materiel/reseau marche,"
Write-Host "         quelque chose DOIT reagir (dimmer force ouvert)."
Run "$Cli scan --ip $Ip --universes 34 --hold 5"

if (Ask-YesNo "Est-ce que QUELQUE CHOSE s'est allume/agite pendant le scan ?") {
  Write-Host "OK: sortie physique validee. On peut affiner." -ForegroundColor Green
} else {
  Write-Host ""
  Write-Host "STOP: le materiel/reseau ne repond pas. A verifier AVANT de continuer:" -ForegroundColor Yellow
  Write-Host "  1) Es-tu bien sur le reseau du panneau (GLASS) ?"
  Write-Host "  2) Ping 192.168.1.48 depuis cette machine ?"
  Write-Host "  3) Le controleur .48 est-il sous tension et patche sur univers 33 ?"
  Write-Host "  4) Pare-feu Windows: autoriser dotnet.exe (UDP sortant vers 6454) ?"
  if (-not (Ask-YesNo "Continuer quand meme (pour tester le pas-a-pas) ?")) { exit 1 }
}

# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "TEST 2a - Scan pas-a-pas: on balaye univers 0 -> 33, 3s chacun."
Write-Host "          Note l'univers qui allume les projecteurs (attendu: 33)."
Run "$Cli scan --ip $Ip --universes 34 --step --hold 3"

# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "TEST 2b - Envoi cible par entite (via ecran.json)."
Write-Host "          Attention: 'send' n'ecrit que RGB (3 canaux) sur l'entite."
Write-Host "          Si la lyre a un DIMMER separe non ouvert, elle restera noire."

foreach ($entity in 1, 10, 30, 50, 70) {
  Write-Host ""
  Write-Host "  --- Entite $entity (5s en blanc) ---"
  Run "$Cli send $Config --ip $Ip --entity $entity --color 255,255,255 --frames 200 --hz 40"
  if (Ask-YesNo "  Reaction sur l'entite $entity ?") {
    Write-Host "    => note: entite $entity reagit" -ForegroundColor Green
  } else {
    Write-Host "    => note: entite $entity NE reagit PAS" -ForegroundColor Yellow
  }
}

# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "======================================================================"
Write-Host "  Termine. Recap des tests dispo pour la suite:"
Write-Host "    Reallumer full univers 33 :"
Write-Host "      $Cli scan --ip $Ip --universes 34 --hold 5"
Write-Host "    Reallumer une entite      :"
Write-Host "      $Cli send $Config --ip $Ip --entity <id> --color 255,255,255"
Write-Host "======================================================================"
