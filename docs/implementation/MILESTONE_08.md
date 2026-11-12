# Milestone 08 — Install safely and recover the shop

Branch: `codex/backup-installer-and-pilot`, from merged milestone 07 on `development`
(`0a5268b`). This document records source work and evidence, not production approval.
Windows, physical hardware and the owner's full pilot week remain required gates.

## Backup and restore workflow

Owner Settings contains backup folders, retention count, manual backup, verified primary and
second-copy timestamps, backup status and persistent maintenance outcome/safety-copy location.
A status line is visible to cashiers without exposing backup paths. Owner authorization is
reloaded in application commands; restore additionally requires an exact owner PIN grant,
reason, archive checksum and a closed current shift. Backup reads do not require closing a
shift. After restore, all current-process credentials are discarded and the restored shop's
credentials are required. Cashiers cannot configure, inspect or execute interactive maintenance.

`Microsoft.Data.Sqlite.SqliteConnection.BackupDatabase` creates consistent live snapshots,
including committed WAL data. Archives are normalized to standalone DELETE-journal SQLite
files before publication. Never copy a live database/WAL as an ordinary file. Backup files
are verified with SQLite integrity/foreign-key checks and their known migration schema;
second-copy transfer verifies SHA-256. Temporary writes publish by rename after completion.
Primary success is retained and reported separately if USB/second-copy transfer fails.

Default primary: the database directory's sibling `RetailCore-backups` (for default Windows
data paths). Destinations must be outside the live database directory and separate from one
another. The owner must choose and physically verify an external/USB second destination;
two directories on one disk are not independent disaster protection. Missing second folders
are never created automatically. Reconnect the configured destination and retry. Retention
keeps the most recent configured count (default 30, range 2–365) of this application's exact
manual/daily filename pattern, never arbitrary files or safety/pre-upgrade copies.

A daily copy runs at application startup and is checked every 15 minutes while the application
is open. The installer registers a 21:00 checkout Windows-local-time Task Scheduler task (expected Asia/Colombo; verify the PC clock) for the current Windows
account using an interactive token, without storing a password. StartWhenAvailable catches a
missed run at the next eligible login. `--daily-backup` runs without a UI and does not perform
unattended database upgrades. If the application already runs, its scheduler handles the copy.
Daily primary copies are immutable within a shop-local calendar day; recovery or destination
changes start a new verified snapshot rather than adopting an old same-day file. A missing USB can be retried
without replacing that primary copy. A powered-off or logged-out PC cannot be backed up until
it is running/logged in. Manual closing backups are available throughout the day.

Restore stages the selected closed archive, verifies integrity/foreign keys, rejects unknown
or non-prefix migration history, and compares tables/indexes/triggers against the schema
built by those declared migrations. Missing append-only guards are incompatible. Older known
backups migrate only in staging; the original archive is untouched. The preview shows shop,
archive file date, bill/stock-movement/shift counts and checksum. Financial and historical
records are restored together, rather than reconstructed from CSV or current catalogue data.

All application units of work and maintenance share a database-specific process semaphore and
OS file lease, across DI providers and processes. Native SQLite exclusive locking additionally
protects the destination during restore. Connections do not pool, and contexts cannot survive
a maintenance operation. A verified safety snapshot and durable pending journal precede the
copy. Trading is blocked while that journal exists. SQLite's destination transaction prevents
half-copied database pages. At startup, an interrupted restore without its committed result
marker rolls back to the verified safety copy. A committed restore is kept and validated;
its audit/result is not duplicated. Safety copies are not automatically aged out.

The backup destination/status file is host-local, outside the restored shop data, so restoring
another machine's backup does not silently redirect future copies to that machine's folders.
Settings requests are audited before atomic file replacement; backup status is operational
metadata, not an alteration of stock or completed financial records. Restore appends a system
audit with the authorizing original identity/checksum/reason, which remains meaningful when
that user's ID does not exist in the restored shop. Backups contain business data and password
hashes; keep storage under the owner's control.

## Installation and upgrades

`scripts/build-installer.ps1` publishes a self-contained Windows x64 WPF application and
compiles `installer/RetailCore.iss` with Inno Setup 6.3 or newer in the 6.x series. No separately installed .NET runtime or
internet connection is required on the target checkout. The build computer needs the .NET SDK,
Windows runtime packs and Inno Setup. The build script checks the compiler version for
[UTF-8 source support](https://jrsoftware.org/ishelp/topic_unicode.htm) and the
[x64-compatible architecture setting](https://jrsoftware.org/ishelp/topic_archidentifiers.htm). Generated installers and checksums belong in ignored
`artifacts/installer`. No installer binary built on Linux is claimed.

Installation is per Windows user in `%LOCALAPPDATA%\Programs\RetailCore`. Database:
`%LOCALAPPDATA%\RetailCore\retailcore.db`. The installer neither initializes shop data in its
program folder nor removes database/backups on uninstall. Use one Windows account for this
checkout's owner/cashier application logins; other Windows profiles have independent data.
The app/installer mutex prevents installation over a running instance. Subsequent installed
versions call the old application's `--prepare-upgrade` before replacing binaries: close the
shift and verify a safety backup (plus configured second copy) first. First deployment from
previous un-packaged source builds still takes the mandatory database safety backup at startup.

Startup rejects unknown/future/unmanaged schemas. Before applying pending forward migrations
to an existing database, it requires no open shift, creates a verified pre-upgrade safety copy,
and records a maintenance journal. Failed migrations restore that copy and fail startup;
trading does not continue against a partially upgraded schema. Startup after an interruption
recovers the pre-upgrade state before another deliberate application launch can retry. This
is recovery to a known pre-upgrade snapshot, not an EF down migration. Applied migrations are
unchanged. Keep the previous installer when updating; database rollback cannot reinstall old
program binaries. Startup logs retain only UTC time, application version and error category,
with bounded rotation, without credentials, card data or exception payloads.

English/Sinhala application resources cover the new paths. The installer includes Sinhala
common-wizard overrides and bilingual custom recovery instructions. Native OS/Inno low-level
fallback text and actual Sinhala/native-speaker/keyboard review require Windows acceptance.
Packages are unsigned until a release signing arrangement is supplied; no signing key is in
the repository.

## Recovery after a damaged live database

Do not delete the live file or WAL. Preserve them, the recovery directory and all safety copies.
Normal in-app restore needs a readable current shop for authenticated owner approval. If the
live database is unreadable, use a clean replacement PC/Windows profile, install, create the
local owner account, then restore the verified archive with owner approval. Sign in using the
restored shop's credentials and compare bills, stock and cash before trading. There is no
unauthenticated PIN-bypass repair tool and no claim of repairing physically corrupt media.
An available verified backup and owner credentials are required.

## Validation record

Executed on 2026-11-12 on Ubuntu 24.04.5, .NET SDK 10.0.113/runtime 10.0.13:

- Release solution build, warnings as errors: passed, zero warnings/errors. This cross-builds
  WPF but cannot execute Windows controls.
- Domain: 158 passed. Architecture: 11 passed. SQLite integration: 10 passed, with three
  opt-in legacy PostgreSQL server tests skipped. Both retained provider model/migration
  consistency checks report no pending model changes; no new schema migration is needed.
- Application/file-backed foundation: full existing/maintenance suite 89 passed, with the
  explicit benchmark excluded; the added pack-only search/current-conversion regression
  passed separately (90 distinct application cases passed). Recovery cases use real SQLite
  files/native backups, including committed WAL bills/stock/cash, owner/open-shift checks,
  three interruption points, clean-profile recovery, corrupt/future/modified backups,
  disconnected second storage, retention, concurrent transactions and failed upgrade.
- A Rs. 150 sale with Rs. 200 cash and Rs. 50 change restored with exactly one bill, stock
  99 from an opening 100, and expected cash 1,150 including opening float 1,000. Catalogue
  changes made after backup were replaced by the archived state. Failed/interrupted restore
  blocked trading and restart selected either the complete safety image or committed restore.
- Sinhala/English resources: 487 unique, nonempty matching keys. CI YAML parses;
  `git diff --check` passes. Translation accuracy, shaping and keyboard-only use remain
  native Windows/owner review, rather than being inferred from matching resource keys.
- Actual Windows test execution was attempted and could not start: this Linux host has no
  `Microsoft.WindowsDesktop.App` runtime. Inno Setup/PowerShell installation and installer
  smoke execution are unavailable here. These are pending checks, not successful mocks.

### Performance evidence and remaining target

An explicit disposable SQLite diagnostic seeded 10,000 actual products, scanned 30 times,
settled a sale and reopened services. It never accesses shop data. The first run exposed full
catalogue aggregate loading on each scan and settlement. Checkout now reads fresh financial
aggregates only for the basket/selected barcode plus stock roots, while querying the full
search list as a lightweight projection. This does not cache pricing, conversion, approval
or stock inputs across writes. Existing stale-price/security/atomicity checks and a pack-only
search selection with conversion changed after preview pass.

| Linux diagnostic | Before | After |
|---|---:|---:|
| Backend scan p95 (30 samples) | 596.98 ms | 202.38 ms |
| Cash settlement | 881.59 ms | 276.34 ms |
| Full catalogue load | 577.76 ms | 529.06 ms |
| In-memory Sinhala filter p95 | 1.16 ms | 2.27 ms |
| Service initialization after reopen | 64.70 ms | 68.82 ms |

The benchmark passed its data-integrity/count assertion; it deliberately does not certify
latency targets on an arbitrary machine. **Scan still exceeds the planned 100 ms target on
this host.** No WPF cold-start, bound-table filter, physical scanner or printer measurement
was taken. Run `scripts/measure-checkout.ps1` on the intended Windows PC and measure the
actual UI separately. Further scan optimization may be necessary before pilot entry.

## Release gates not satisfied in this environment

Use [the Windows/device/pilot record](PILOT_ACCEPTANCE.md). No Windows installation, actual
printer/scanner/USB disconnection, intended-PC WPF performance or trading week has been
performed from this Linux environment. CI source includes real Windows dispatcher maintenance
controls and installer smoke checks; their existence is not a passing result. The pilot must
start only after install, restore, original Sinhala receipts/barcodes, stock and cash checks pass.
