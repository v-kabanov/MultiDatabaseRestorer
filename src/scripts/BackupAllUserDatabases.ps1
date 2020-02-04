Write-Host $(Get-Date -format 'yyyy-MM-dd HH:mm:ss');

$serverName = "localhost";
# root folder under which the directory tree structure is: <db name> / yyyy-MM-MMM / <monthly full, daily diff and log backup files>,
# multiple log backups saved in 1 daily file; script can be scheduled e.g. every 5 minutes, it automatically creates
# appropriate kinds of backups and purges old ones
$backupDirectory = "\\fs\backup\database";
$monthsToStoreBackups = 12;
#if model is "Simple" log backups are impossible; this option allows to set Model to "Full" if it's not already
[bool]$setRecoveryModelFull      = $true;

#param(
#    $serverName,
#    $backupDirectory,
#    $daysToStoreBackups
#)

[string]$monthDirNameRegex = "\d{4}-\d{2}-[A-Za-z]{3}";
[string]$monthDirNameFormat = "yyyy-MM-MMM";

 
[System.Reflection.Assembly]::LoadWithPartialName("Microsoft.SqlServer.SMO") | Out-Null
[System.Reflection.Assembly]::LoadWithPartialName("Microsoft.SqlServer.SmoExtended") | Out-Null
[System.Reflection.Assembly]::LoadWithPartialName("Microsoft.SqlServer.ConnectionInfo") | Out-Null
[System.Reflection.Assembly]::LoadWithPartialName("Microsoft.SqlServer.SmoEnum") | Out-Null

$server = New-Object ("Microsoft.SqlServer.Management.Smo.Server") $serverName

[System.DateTime]$scriptStartTime = Get-Date;

function Parse-TimeString ([String]$timeString, [String[]]$format)
{
   [DateTime]$result = New-Object DateTime
 
   $convertible = [DateTime]::TryParseExact(
      $timeString,
      $format,
      [System.Globalization.CultureInfo]::InvariantCulture,
      [System.Globalization.DateTimeStyles]::None,
      [ref]$result)
 
   if ($convertible) { $result }
}

function GetDatabaseRootBackupDirectoryPath([Microsoft.SqlServer.Management.Smo.Database]$database)
{
    $dbName = $database.Name
 
    "$script:backupDirectory\$dbName";
}

function GetMonthlyBackupDirectoryPath([Microsoft.SqlServer.Management.Smo.Database]$database)
{
    $dbName = $database.Name
 
    $timestamp = $script:scriptStartTime.ToString($monthDirNameFormat);

    "$(GetDatabaseRootBackupDirectoryPath $database)\$timestamp";
}

function GetFullBackupFilePath([Microsoft.SqlServer.Management.Smo.Database]$database)
{
    $dbName = $database.Name

    [string]$dbBackupDir = GetMonthlyBackupDirectoryPath $database;
 
    $timestamp = $script:scriptStartTime.ToString($monthDirNameFormat);

    "$dbBackupDir\$timestamp-$dbName-full.bak"
}

function GetDiffBackupFilePath([Microsoft.SqlServer.Management.Smo.Database]$database)
{
    $dbName = $database.Name;

    [string]$dbBackupDir = GetMonthlyBackupDirectoryPath $database;
 
    $timestamp = $script:scriptStartTime.ToString("yyyy-MM-MMM-dd");

    "$dbBackupDir\$timestamp-$dbName-diff.bak";
}

function GetLogBackupFilePath([Microsoft.SqlServer.Management.Smo.Database]$database)
{
    $dbName = $database.Name

    [string]$dbBackupDir = GetMonthlyBackupDirectoryPath $database;
 
    $timestamp = $script:scriptStartTime.ToString("yyyy-MM-MMM-dd");

    "$dbBackupDir\$timestamp-$dbName-log.trn";
}

function BackupDatabase([Microsoft.SqlServer.Management.Smo.Database]$database)
{
    [Microsoft.SqlServer.Management.Smo.Backup]$smoBackup = New-Object ("Microsoft.SqlServer.Management.Smo.Backup");

    [string]$backupTypeCode = "Full";

    [string]$fullBackupFilePath = GetFullBackupFilePath $database;
    [string]$backupFilePath = $fullBackupFilePath;

    [string]$backupDirPath = [System.IO.Path]::GetDirectoryName($backupFilePath);
    [System.IO.Directory]::CreateDirectory($backupDirPath) | Out-Null;

    $smoBackup.Action = [Microsoft.SqlServer.Management.Smo.BackupActionType]::Database;

    [DateTime]$today = [DateTime]::Today;

    [DateTime]$startOfMonth = $today.AddDays(-$today.Day + 1);

    [Microsoft.SqlServer.Management.Smo.RecoveryModel]$recoveryModelAtStart = $database.RecoveryModel;

    # if month's backup file exists and database has already been backed up this month, may do diff or log backup, otherwise only full
        
    if ((Test-Path -Path $backupFilePath) -and $database.LastBackupDate -gt $startOfMonth)
    {
        $backupFilePath = GetDiffBackupFilePath $database;

        $smoBackup.Incremental = $true;
        $backupTypeCode = "Differential";
        if ($recoveryModelAtStart -eq [Microsoft.SqlServer.Management.Smo.RecoveryModel]::Simple)
        {
            Write-Warning "Database $($database.Name) is using simple recovery model, cannot perform log backup";
        }
        else
        {
            # first backup in a day is diff, the rest - log
            if (-not (Test-Path -Path $backupFilePath))
            {
                Write-Warning "Diff backup file for database $($database.Name) does not exist yet; making Diff backup";
            }
            else
            {
                if ($database.LastDifferentialBackupDate -ge $today -and (Test-Path -Path $backupFilePath))
                {
                    # diff backup has already been performed today, so doing log backup
                    $smoBackup.Action = [Microsoft.SqlServer.Management.Smo.BackupActionType]::Log;
                    $backupTypeCode = "Log";
                    $backupFilePath = GetLogBackupFilePath $database;
                }
            }
        }
    }

    if ($setRecoveryModelFull -and ($database.RecoveryModel -ne [Microsoft.SqlServer.Management.Smo.RecoveryModel]::Full))
    {
        $database.RecoveryModel = [Microsoft.SqlServer.Management.Smo.RecoveryModel]::Full;
        Write-Warning "Setting recovery model of $($database.Name) to full";
        $database.Alter();
    }

    $smoBackup.BackupSetName          = "$($database.Name)@$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')";
    $smoBackup.BackupSetDescription   = $smoBackup.BackupSetName;
    $smoBackup.MediaName              = "Backups of $($database.Name) for " + $startOfMonth.ToString("MMM, yyyy");
    $smoBackup.MediaDescription       = $smoBackup.MediaName;

    $smoBackup.CompressionOption      = [Microsoft.SqlServer.Management.Smo.BackupCompressionOptions]::Default;

    $smoBackup.Database               = $database.Name
    $smoBackup.RetainDays             = $daysToStoreBackups;

    $smoBackup.Devices.AddDevice($backupFilePath, "File");

    Write-Host "$(Get-Date -Format HH:mm:ss):    Starting $backupTypeCode backup of $($database.Name)"

    $smoBackup.SqlBackup($server);
 
    Write-Host "$(Get-Date -Format HH:mm:ss):    Backed up $($database.Name) ($serverName) to $backupFilePath";

    #purging old backups
    #Get-ChildItem "$backupDirPath\*.bak" |? { $_.lastwritetime -le (Get-Date).AddDays(-$daysToStoreBackups)} |% {Remove-Item $_ -force }
    Purge-OldBackups $(GetDatabaseRootBackupDirectoryPath $database) $script:monthsToStoreBackups;
}

function Purge-OldBackups([string]$databaseBackupPath, [int]$fullMonthsToKeep)
{
    [DateTime]$threshold = $script:scriptStartTime.Date.AddDays(-$script:scriptStartTime.Day + 1).AddMonths(-$local:fullMonthsToKeep);

    Get-ChildItem $local:databaseBackupPath |? { $_.PSIsContainer } |? { ($_.Name -match $script:monthDirNameRegex) } |% {

    $monthStart = Parse-TimeString $_.Name $script:monthDirNameFormat;
        
    if ($monthStart -ne $null -and $monthStart -lt $threshold)
    {
            Write-Host "$(Get-Date -Format HH:mm:ss):    Deleting folder $($_.FullName)";
            [System.IO.Directory]::Delete($_.FullName, $true) | Out-Null;
    }
    }
}

foreach ($database in $server.Databases | where { -not $_.IsSystemObject -and -not $_.Name.Contains("TempDB") })
{
    try
    {
        $Error.Clear();
        Write-Debug "Database $($database.Name) has status of $($database.Status)";
        if ($database.Status -ne [Microsoft.SqlServer.Management.Smo.DatabaseStatus]::Normal)
        {
            Write-Warning "Database $($database.Name) has status of $($database.Status) and will not be backed up";
        }
        else
        {
            BackupDatabase $database;
        }
    }
    catch
    {
        Write-Host "$(Get-Date -Format 'dd MMM, HH:mm:ss') Error $($Error[0])";

        Write-Host $Error[0];
        Write-Host $Error[0].InnerException;
    }
}

Write-Host "$(Get-Date -Format HH:mm:ss):    finish";
