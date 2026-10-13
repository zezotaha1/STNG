# Development API logging

The IIS-hosted API writes daily rolling log files outside the mirrored site directory.
The development server uses:

`C:\Deploy\STNG\Logs\Api\stng-api-.log`

Serilog retains 31 files. The IIS Application Pool supplies the path through
`STNG__Logging__FilePath`, which maps to `STNG:Logging:FilePath`.

## Provision the development server

Run the following from an elevated Windows PowerShell 5.1 session on `ZEZOSERVER`,
using a trusted checkout of this repository:

```powershell
Set-Location C:\path\to\STNG
.\.github\scripts\provision-stng-api-logging.ps1
```

The script:

- creates the external API log directory;
- archives existing files from `C:\MySites\STNG\Logs` without deleting the originals;
- restricts the API log directory to SYSTEM, Administrators, and `IIS AppPool\STNG`;
- grants the application-pool identity Modify rather than Full Control;
- configures the IIS application-pool environment variable; and
- validates the resulting path, ACL, and IIS configuration.

The script deliberately does not recycle the application pool. The normal deployment's
`app_offline.htm` cycle starts the next worker process with the configured environment.

## Required pre-deployment verification

Immediately before merging or manually dispatching the first deployment, run:

```powershell
Set-Location C:\path\to\STNG
.\.github\scripts\provision-stng-api-logging.ps1 -VerifyOnly
```

Do not deploy unless this command reports that verification passed. Rollback restores
application files but cannot repair a missing application-pool environment variable.

After deployment:

1. Confirm the HTTPS health endpoint returns a successful response.
2. Confirm a dated `stng-api-*.log` file exists under `C:\Deploy\STNG\Logs\Api`.
3. Exercise one API request and confirm the external file's timestamp advances.
4. Confirm the application has not recreated `C:\MySites\STNG\Logs`.
5. Confirm `IIS AppPool\STNG` has Modify, not Full Control, on the external API log directory.

The deployment script mirrors only the publish directory and its backups. It does not
target `C:\Deploy\STNG\Logs`, so deployment, rollback, and backup-retention cleanup cannot
delete the persistent API logs.
