<#
.SYNOPSIS
    Installe les taches planifiees : sauvegarde chaque nuit, essai de
    restauration chaque mois.

.DESCRIPTION
    A lancer une fois sur le serveur, dans une console ouverte en
    administrateur.

    L'essai de restauration mensuel n'est pas un luxe : c'est lui qui
    fait la difference entre une sauvegarde et un fichier dont on espere
    qu'il servira.

.EXAMPLE
    .\installer-taches.ps1 -Distant "\\nas\sauvegardes\lpde"
    .\installer-taches.ps1 -Desinstaller
#>
[CmdletBinding()]
param(
    [string] $Distant,
    [int] $Retention = 14,
    [string] $HeureSauvegarde = '03:15',
    [switch] $Desinstaller
)

$ErrorActionPreference = 'Stop'

$TACHE_SAUVEGARDE = 'LPDE — sauvegarde nocturne'
$TACHE_ESSAI = 'LPDE — essai de restauration'

if ($Desinstaller) {
    foreach ($nom in @($TACHE_SAUVEGARDE, $TACHE_ESSAI)) {
        if (Get-ScheduledTask -TaskName $nom -ErrorAction SilentlyContinue) {
            Unregister-ScheduledTask -TaskName $nom -Confirm:$false
            Write-Host "retiree : $nom"
        }
    }
    exit 0
}

# Le compte SYSTEM : la tache doit tourner que quelqu'un soit connecte
# ou non. Une sauvegarde qui attend une session ouverte ne se fait pas.
$compte = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest

$reglages = New-ScheduledTaskSettingsSet `
    -StartWhenAvailable `
    -DontStopOnIdleEnd `
    -ExecutionTimeLimit (New-TimeSpan -Hours 4) `
    -RestartCount 2 -RestartInterval (New-TimeSpan -Minutes 15)

function Poser {
    param([string] $Nom, [string] $Script, [string] $Arguments, $Declencheur, [string] $Description)

    $chemin = Join-Path $PSScriptRoot $Script
    if (-not (Test-Path $chemin)) { throw "script introuvable : $chemin" }

    # -NonInteractive et -NoProfile : une tache planifiee ne doit rien
    # attendre de personne, ni dependre du profil d'un utilisateur.
    $action = New-ScheduledTaskAction -Execute 'powershell.exe' `
        -Argument "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File `"$chemin`" $Arguments"

    if (Get-ScheduledTask -TaskName $Nom -ErrorAction SilentlyContinue) {
        Unregister-ScheduledTask -TaskName $Nom -Confirm:$false
    }

    $null = Register-ScheduledTask -TaskName $Nom -Action $action -Trigger $Declencheur `
        -Principal $compte -Settings $reglages -Description $Description
    Write-Host "posee : $Nom"
}

$argSauvegarde = "-Retention $Retention"
if ($Distant) { $argSauvegarde += " -Distant `"$Distant`"" }

Poser -Nom $TACHE_SAUVEGARDE -Script 'sauvegarde.ps1' -Arguments $argSauvegarde `
      -Declencheur (New-ScheduledTaskTrigger -Daily -At $HeureSauvegarde) `
      -Description 'Sauvegarde complete de la base et des CV. Ecrit etat.json, que la sonde de sante relit.'

# Le premier dimanche du mois, apres la sauvegarde de la nuit.
Poser -Nom $TACHE_ESSAI -Script 'restauration-essai.ps1' -Arguments '' `
      -Declencheur (New-ScheduledTaskTrigger -Weekly -DaysOfWeek Sunday -At '05:00') `
      -Description "Restaure la derniere sauvegarde sur une base jetable et compte ce qu'elle contient."

Write-Host ''
Write-Host 'Taches installees.'
if (-not $Distant) {
    Write-Warning @'
Aucune destination hors du serveur n'a ete indiquee.

La sauvegarde sera ecrite a cote de l'application : elle protege d'une
suppression accidentelle, pas de la perte du disque. Relancez ce script
avec -Distant "\\serveur\partage" quand vous aurez choisi ou la deposer.
'@
}
