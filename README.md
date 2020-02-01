# MultiDatabaseRestorer
A library and a couple of client apps for coordinated restoration of all or selected databases on SQL Server instance.
Given an organized backup directory structure it scans available (full, diff and log) backups, creates and executes 'optimal' restoration plan for selected databases for specific point in time or latest available.

Supports dynamic (growing) collections of 'split' databases (e.g. Documents1, Documents2,...) for cases where (express) edition limits the size of the database and new database is created when previous reaches the limit.
The whole collection is restored despite the fact that the exact list of databases is not known when apps are configured.

Console app supports restoration to the latest point only and is suitable for automated maintenance of e.g. a standby server.
The GUI app supports restoration to a specific point in time for manual data recovery.

