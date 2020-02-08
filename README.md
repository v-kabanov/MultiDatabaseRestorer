# MultiDatabaseRestorer
A library and a couple of client apps for coordinated restoration of all or selected user databases on SQL Server instance.
Given an organized backup directory structure it scans available (full, diff and log) backups, creates and executes 'optimal' restoration plan for selected databases for specific point in time or latest available.

Supports dynamic (growing) collections of 'split' databases (e.g. Documents1, Documents2,...) for cases where (express) edition limits the size of the database and new database is created when previous reaches the limit.
The whole collection is restored even though the exact list of databases is not known when apps are configured.

Console app supports restoration to the latest point only and is suitable for automated maintenance of e.g. a standby server.
The GUI app supports restoration to a specific point in time for manual data recovery.

Sample powershell script for automated backup of all user databases is included (see 'scripts' folder).
It can be scheduled in windows task scheduler to run e.g. every 5 minutes, no SQL Server agent required (agent is not available in Express edition).
It will automatically create directories, first backup in a month will be full, first daily backup - diff, then all daily log backups go into 1 file.
Separate historical directory trees are created under per-database folders, directory structure under root backup folder is
{db name} / yyyy-MM-MMM / {monthly full, daily diff and log backup files}
Backup folders are thus monthly with up to ~63 backup files.

When databases are moved/copied from one server to another SQL users can optionally be re-associated with equally named SQL logins.

Note that current version is using SMO library v150.18208 distributed via nuget package. The package contains native binaries and every project producing an executable output has to reference the package in order for them to be delivered into output directory. In addition it does not support 'Any CPU' platform, only x86 or x64.
