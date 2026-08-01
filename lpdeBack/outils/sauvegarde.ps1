<#
.SYNOPSIS
    Sauvegarde la base et les fichiers deposes.

.DESCRIPTION
    Il n'y avait rien : la perte du disque du serveur etait la perte de
    tout — comptes, candidatures, messages, offres, CV.

    Le script fait trois choses, et il les fait bruyamment. Une
    sauvegarde qui echoue en silence est pire que pas de sauvegarde du
    tout : elle donne le sentiment d'etre couvert.

      1. Sauvegarde complete de la base (BACKUP DATABASE).
      2. Copie des CV, qui ne sont pas dans la base. Une base restauree
         sans eux ne rend que la moitie du service.
      3. Ecrit « etat.json », que la sonde de sante relit. Le retard
         d'une sauvegarde se voit donc dans l'application, sans avoir a
         penser a regarder ici.

    Le depot HORS DU SERVEUR reste a decider : un disque sauvegarde sur
    lui-meme ne survit pas a l'incendie. Passez -Distant pour un partage
    reseau, ou branchez-y votre outil de synchronisation.

.EXAMPLE
    .\sauvegarde.ps1 -Verbose
    .\sauvegarde.ps1 -Distant "\\nas\sauvegardes\lpde" -Retention 30
#>
[CmdletBinding()]
param(
    # La base a sauvegarder.
    [string] $Serveur = '(localdb)\MSSQLLocalDB',
    [string] $Base = 'LpdeJobBoard',

    # Ou ecrire. Par defaut, a cote de l'application.
    [string] $Destination,

    # Les CV, qui vivent hors de la base.
    [string] $Fichiers,

    # Un second exemplaire, ailleurs. C'est celui qui compte le jour ou
    # le serveur brule.
    [string] $Distant,

    # Combien de jours on garde.
    [int] $Retention = 14
)

$ErrorActionPreference = 'Stop'
$racine = Split-Path -Parent $PSScriptRoot

# Hors du dossier de l'application, imperativement : le deploiement
# se fait par « msdeploy -verb:sync », qui rend la destination
# identique a la source. Une sauvegarde ecrite chez l'application
# disparaitrait a la mise en ligne suivante — c'est-a-dire au pire
# moment possible.
$commun = Join-Path $env:ProgramData 'LaPlateformeDeLemploi'
if (-not $Destination) { $Destination = Join-Path $commun 'sauvegardes' }
if (-not $Fichiers)    { $Fichiers    = Join-Path $commun 'cv' }

$horodatage = Get-Date -Format 'yyyyMMdd-HHmmss'
$dossier = Join-Path $Destination $horodatage
$etat = Join-Path $Destination 'etat.json'

# ── Parler a SQL Server ──
# Par fichier, jamais par « -Q » : les chemins Windows contiennent des
# antislashs et des espaces, que la ligne de commande decoupe a sa
# facon. Le fichier passe le texte tel quel.
# Un chemin Windows peut contenir une apostrophe — « La Plateforme de
# l'emploi » en contient une — et elle refermerait la chaine SQL au
# milieu. On la double, comme le veut SQL.
function Litteral { param([string] $Valeur) return $Valeur.Replace("'", "''") }

function Invoke-Sql {
    param([string] $Requete)
    $tmp = [System.IO.Path]::GetTempFileName() + '.sql'
    try {
        Set-Content -Path $tmp -Value $Requete -Encoding utf8
        $sortie = & sqlcmd -S $Serveur -b -I -f 65001 -i $tmp 2>&1
        if ($LASTEXITCODE -ne 0) { throw ($sortie -join ' ') }
        return $sortie
    }
    finally { Remove-Item $tmp -Force -ErrorAction SilentlyContinue }
}

# ── L'etat, quoi qu'il arrive ──
# Ecrit meme en cas d'echec : c'est precisement l'echec que la sonde
# doit pouvoir signaler.
function Ecrire-Etat {
    param([string] $Resultat, [string] $Detail, [long] $Octets = 0)
    $null = New-Item -ItemType Directory -Force -Path $Destination
    @{
        resultat  = $Resultat
        detail    = $Detail
        quand     = (Get-Date).ToUniversalTime().ToString('o')
        octets    = $Octets
        base      = $Base
        distant   = [bool] $Distant
    } | ConvertTo-Json | Set-Content -Path $etat -Encoding utf8
}

try {
    Write-Verbose "Sauvegarde de $Base vers $dossier"
    $null = New-Item -ItemType Directory -Force -Path $dossier

    # ── 1. La base ──
    $fichierBase = Join-Path $dossier "$Base.bak"
    $requete = @"
BACKUP DATABASE [$Base] TO DISK = N'$(Litteral $fichierBase)'
WITH FORMAT, INIT, CHECKSUM,
     NAME = N'$(Litteral $Base) — sauvegarde complete';
"@
    Invoke-Sql $requete | Write-Verbose
    if (-not (Test-Path $fichierBase)) { throw "le fichier de sauvegarde n'a pas ete cree" }

    # COMPRESSION n'existe pas sur toutes les editions ; si le fichier
    # est enorme, ce n'est pas une erreur, seulement une remarque.
    $tailleBase = (Get-Item $fichierBase).Length
    Write-Verbose ("base : {0:N1} Mo" -f ($tailleBase / 1MB))

    # ── 2. Les fichiers deposes ──
    if (Test-Path $Fichiers) {
        $archive = Join-Path $dossier 'cv.zip'
        Compress-Archive -Path (Join-Path $Fichiers '*') -DestinationPath $archive -CompressionLevel Optimal -ErrorAction SilentlyContinue
        if (Test-Path $archive) {
            Write-Verbose ("CV : {0:N1} Mo" -f ((Get-Item $archive).Length / 1MB))
        }
    } else {
        Write-Verbose "aucun dossier de CV a $Fichiers — rien a joindre"
    }

    # ── 3. Hors du serveur ──
    if ($Distant) {
        if (-not (Test-Path $Distant)) { throw "destination distante injoignable : $Distant" }
        Copy-Item -Path $dossier -Destination (Join-Path $Distant $horodatage) -Recurse -Force
        Write-Verbose "copie deposee sur $Distant"
    }

    # ── 4. Ce qui a fait son temps ──
    $limite = (Get-Date).AddDays(-$Retention)
    foreach ($racineCopies in @($Destination, $Distant | Where-Object { $_ })) {
        Get-ChildItem -Path $racineCopies -Directory -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -match '^\d{8}-\d{6}$' -and $_.CreationTime -lt $limite } |
            ForEach-Object {
                Write-Verbose "purge de $($_.FullName)"
                Remove-Item $_.FullName -Recurse -Force -ErrorAction SilentlyContinue
            }
    }

    $total = (Get-ChildItem $dossier -Recurse -File | Measure-Object -Property Length -Sum).Sum
    $detail = if ($Distant) { "base et CV sauvegardes, copie deposee hors du serveur" }
              else { "base et CV sauvegardes — AUCUNE copie hors du serveur" }
    Ecrire-Etat -Resultat 'reussi' -Detail $detail -Octets $total

    Write-Host ("Sauvegarde reussie : {0:N1} Mo dans {1}" -f ($total / 1MB), $dossier)
    if (-not $Distant) {
        Write-Warning "Aucune copie hors du serveur. Passez -Distant pour qu'elle survive a la perte du disque."
    }
    exit 0
}
catch {
    Ecrire-Etat -Resultat 'echec' -Detail $_.Exception.Message
    Write-Error "Sauvegarde ECHOUEE : $($_.Exception.Message)"
    exit 1
}
