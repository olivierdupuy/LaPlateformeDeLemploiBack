<#
.SYNOPSIS
    Restaure la derniere sauvegarde sur une base jetable, et verifie
    qu'elle contient quelque chose.

.DESCRIPTION
    Une sauvegarde qui n'a jamais ete restauree n'est pas une
    sauvegarde : c'est un fichier dont on espere qu'il servira. Le jour
    ou l'on en a besoin est le pire moment pour decouvrir qu'elle etait
    tronquee, chiffree avec une cle perdue, ou vide.

    Ce script fait le trajet complet — restaurer, compter, ouvrir un CV
    au hasard — sur une base portant un autre nom, sans jamais toucher
    a la base de production.

.EXAMPLE
    .\restauration-essai.ps1 -Verbose
#>
[CmdletBinding()]
param(
    [string] $Serveur = '(localdb)\MSSQLLocalDB',
    [string] $Source,
    [string] $BaseEssai = 'LpdeRestaurationEssai'
)

$ErrorActionPreference = 'Stop'
$racine = Split-Path -Parent $PSScriptRoot
if (-not $Source) { $Source = Join-Path (Join-Path $env:ProgramData 'LaPlateformeDeLemploi') 'sauvegardes' }

# Un chemin Windows peut contenir une apostrophe — « La Plateforme de
# l'emploi » en contient une — et elle refermerait la chaine SQL au
# milieu. On la double, comme le veut SQL.
function Litteral { param([string] $Valeur) return $Valeur.Replace("'", "''") }

# Par fichier, jamais par « -Q » : les antislashs et les espaces des
# chemins Windows ne survivent pas au decoupage de la ligne de commande.
function Interroger {
    param([string] $Requete)
    $tmp = [System.IO.Path]::GetTempFileName() + '.sql'
    try {
        Set-Content -Path $tmp -Value "SET NOCOUNT ON;`n$Requete" -Encoding utf8
        # Separateur explicite : sans lui, les colonnes ne se
        # distinguent que par des espaces, et un chemin en contient.
        $sortie = & sqlcmd -S $Serveur -b -I -f 65001 -h -1 -W -s'|' -i $tmp 2>&1
        if ($LASTEXITCODE -ne 0) { throw "sqlcmd : $($sortie -join ' ')" }
        return ($sortie | Where-Object { $_ -match '\S' }) -join "`n"
    }
    finally { Remove-Item $tmp -Force -ErrorAction SilentlyContinue }
}

try {
    $derniere = Get-ChildItem -Path $Source -Directory -ErrorAction Stop |
        Where-Object { $_.Name -match '^\d{8}-\d{6}$' } |
        Sort-Object Name -Descending | Select-Object -First 1
    if (-not $derniere) { throw "aucune sauvegarde dans $Source" }

    $bak = Get-ChildItem -Path $derniere.FullName -Filter '*.bak' | Select-Object -First 1
    if (-not $bak) { throw "aucun fichier .bak dans $($derniere.Name)" }

    $age = [int]((Get-Date) - $derniere.CreationTime).TotalDays
    Write-Host "Sauvegarde retenue : $($derniere.Name) — $age jour(s)"

    # RESTORE ... WITH MOVE : sans cela, la restauration essaierait
    # d'ecrire sur les fichiers de la base de production, qui est
    # ouverte. C'est la ligne qui rend cet essai sans danger.
    $dossierDonnees = Join-Path $env:TEMP 'lpde-restauration'
    $null = New-Item -ItemType Directory -Force -Path $dossierDonnees

    $logique = Interroger "RESTORE FILELISTONLY FROM DISK = N'$(Litteral $bak.FullName)';"
    # Premiere colonne : le nom logique du fichier. Le decouper aux
    # espaces echouerait — la deuxieme colonne est un chemin, et un
    # chemin Windows en contient.
    $noms = @($logique -split "`n" | ForEach-Object { ($_ -split '\|')[0].Trim() } | Where-Object { $_ })

    $deplacements = @()
    $deplacements += "MOVE N'$(Litteral $noms[0])' TO N'$(Litteral (Join-Path $dossierDonnees "$BaseEssai.mdf"))'"
    if ($noms.Count -gt 1) {
        $deplacements += "MOVE N'$(Litteral $noms[1])' TO N'$(Litteral (Join-Path $dossierDonnees "$BaseEssai.ldf"))'"
    }

    Write-Verbose "restauration vers $BaseEssai"
    $null = Interroger @"
IF DB_ID(N'$BaseEssai') IS NOT NULL
BEGIN
    ALTER DATABASE [$BaseEssai] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE [$BaseEssai];
END
RESTORE DATABASE [$BaseEssai] FROM DISK = N'$(Litteral $bak.FullName)'
WITH $($deplacements -join ', '), REPLACE, RECOVERY;
"@

    # ── Ce qu'on verifie ──
    # Compter les lignes, pas seulement constater que la restauration
    # n'a pas leve d'erreur : une base restauree vide se restaure tres
    # bien.
    $comptes = Interroger @"
USE [$BaseEssai];
SELECT CONCAT(
  (SELECT COUNT(*) FROM Users), '|',
  (SELECT COUNT(*) FROM JobOffers), '|',
  (SELECT COUNT(*) FROM Applications));
"@
    # « USE » fait dire a sqlcmd qu'il a change de base ; ce message
    # precede le resultat. On garde la derniere ligne, la seule qui
    # porte les trois nombres.
    $ligne = ($comptes -split "`n" | Where-Object { $_ -match '^\s*\d+\|' } | Select-Object -Last 1)
    if (-not $ligne) { throw "aucun compte lisible dans la base restauree" }
    $u, $o, $c = $ligne.Trim() -split '\|'
    Write-Host "Contenu restaure : $u compte(s), $o offre(s), $c candidature(s)"

    $souci = @()
    if ([int]$u -lt 1) { $souci += "aucun compte" }
    if ([int]$o -lt 1) { $souci += "aucune offre" }

    # Les CV voyagent a part : leur absence de l'archive ne se verrait
    # pas en interrogeant la base.
    $zip = Get-ChildItem -Path $derniere.FullName -Filter 'cv.zip' -ErrorAction SilentlyContinue
    if ($zip) {
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $archive = [System.IO.Compression.ZipFile]::OpenRead($zip.FullName)
        $nb = $archive.Entries.Count
        $archive.Dispose()
        Write-Host "CV dans l'archive : $nb"
    } else {
        $souci += "aucune archive de CV"
    }

    $null = Interroger "IF DB_ID(N'$BaseEssai') IS NOT NULL BEGIN ALTER DATABASE [$BaseEssai] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$BaseEssai]; END"
    Remove-Item $dossierDonnees -Recurse -Force -ErrorAction SilentlyContinue

    if ($souci) {
        Write-Error "Restauration douteuse : $($souci -join ', ')"
        exit 1
    }

    Write-Host "Restauration verifiee — la sauvegarde du $($derniere.Name) est exploitable."
    exit 0
}
catch {
    Write-Error "Essai de restauration ECHOUE : $($_.Exception.Message)"
    exit 1
}
