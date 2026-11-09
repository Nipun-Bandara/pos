# Milestone 01 — app foundation

Branch: `codex/app-foundation`.

## Delivered in source

- A .NET 10 WPF application with Billing, Products, Stock, Returns, Reports and Settings. Only first-run setup, login/unlock and settings/user administration have workflows in this milestone. Other sections display a translated availability message.
- First-run setup atomically creates one local shop, terminal T1 and an owner with no default credentials. Existing populated databases are migrated, never replaced. Multiple shops/terminals or a missing active owner produce an actionable configuration error rather than silently creating another shop.
- Owner/cashier roles, 6–12 digit PINs, salted Argon2id v19 (64 MiB, three passes, one lane), five-failure/15-minute persisted lockout, owner PIN reset/unlock, and protection against removing the last active owner. Historical Manager roles remain in the schema but cannot sign in through the new application or be assigned to new users.
- Owner-only shop/user commands recheck the signed-in active user in the database. Locking, signing out and credential resets invalidate authority; a caller cannot obtain owner access by supplying an owner ID. Shop-setting saves additionally require a two-minute single-use opaque approval bound to the exact settings and the current requester/session and owner credential version.
- Manual lock, five-minute inactivity lock and lock on minimize. Unlock requires the same user's PIN; changing users requires sign-out. Only process-local session state is retained and every restart requires sign-in.
- English/Sinhala resources for new screens, field labels, dialogs and errors; per-user language persistence; Windows Sinhala fonts with Nirmala UI/Iskoola Pota fallback. Culture-aware formatting enabled. Receipt footer is stored as Unicode; rendering/printing remains milestone 05.
- Shop name/legal name, address, phone, timezone (Asia/Colombo initially), receipt footer, optional printer name, paper width and cash increment settings. Currency is fixed after setup; future monetary workflows must explicitly consume configured cash increments. Printer names here are saved preferences, not a claim of driver validation.
- SQLite WAL/FULL/foreign-key configuration on every runtime connection. Each transaction obtains a fresh scoped context; failed units of work cannot be reused. Busy/locked writes retry at most twice after the initial attempt, with fresh contexts. Other failures propagate without blind replay.
- A local completed-command receipt primitive persists operation ID, kind, safe fingerprint and result with the same transaction, returning the previous result for matching retries and rejecting conflicting reuse. **Callbacks must contain database effects only and may run again; never put printing, UI/session changes or other external effects in them. Never persist credentials in fingerprints/results.** Use this primitive with authorization and domain validation when future financial commands land. Authentication itself uses fresh writes so failed attempts are always committed.
- Stock/audit immutability triggers, safe user/settings audit metadata and existing stock/audit handler wiring. Sync or async EF writes dispatch events, missing handlers/dispatchers fail closed, and failed dispatch/save contexts must be discarded. No outbox processor or synchronization service is introduced.

The shared schema changes have a new SQLite migration and a new PostgreSQL compatibility migration; historical migrations are unchanged. English language, 80 mm paper and 100 minor-unit cash increment backfill existing rows explicitly. Existing legacy PIN encodings must be valid Argon2id with the supported parameters to authenticate; no insecure fallback or automatic PIN reset is provided.

## Run on Windows

Install the .NET 10 SDK matching `global.json`, then from the repository root:

```powershell
dotnet restore
dotnet build --configuration Release
dotnet run --project src/RetailCore.Desktop --configuration Release --no-build
```

Normal runtime needs no PostgreSQL, Docker, server or internet. The development build/restore needs NuGet access. The database is `%LOCALAPPDATA%\RetailCore\retailcore.db`, outside the install directory. The application allows one running instance per Windows session. Do not place the database on a network share.

On first launch, select the language, enter shop details and create the first owner. Sign in, open Settings to add cashiers or additional owners, and use the top language selector to save the current user's preference. Saving shop settings requests an owner PIN over a review of the exact values. A cashier can change their own interface language but sees no shop/user settings editor. Updating the currently signed-in owner's security settings signs that user out.

Keep this database as test data. Backup/restore, installer and actual shop trading remain later milestones.

## Automated verification

The required CI now builds the solution, runs domain/architecture tests, local SQLite integration tests and the new file-backed foundation suite, and checks the SQLite migration snapshot. The replacement local checks passed before moving PostgreSQL-only checks out of the normal runtime gate.

A separate Windows job builds WPF and runs `RetailCore.Desktop.Tests` against the real WPF window/dispatcher and a temporary SQLite file. It enters first-run setup and PINs, signs in, switches language, creates a cashier, locks/unlocks, reopens the window, checks persisted language and confirms a cashier cannot see the settings save action. It saves PNG previews as CI artifacts. This checks the actual view composition and resource loading, not just mocked view models.

Optional retained PostgreSQL checks can be run via the CI manual `legacy_postgres` input, or by setting `RETAILCORE_TEST_POSTGRES=1` before running the integration executable. Historical row-level-security tests remain available. SQLite integration runs do not start a PostgreSQL container.

## Windows acceptance still required

This implementation was authored on Linux. A cross-targeted WPF build verifies compilation; it does **not** establish that Windows UI tests or human acceptance passed. Check the required Windows CI result and complete this checklist on the intended PC before considering the milestone accepted:

1. Start with an unused local application-data directory on a test Windows account. Create the shop and first owner; verify no PINs are prefilled or displayed.
2. Check every basic screen, dialog and validation path in Sinhala and English at the real display scale. Confirm Sinhala glyph shaping and keyboard tab order, including long names and small-screen scrolling. Have a Sinhala-speaking reviewer check the translations.
3. Save shop settings with owner approval, switch language, close the application, reopen and sign in. Check the exact saved profile, user language, single shop and terminal.
4. Create a cashier, sign in as that cashier and confirm shop/user commands are inaccessible. Check manual/minimize/inactivity lock, same-user unlock and sign-out/change-user.
5. Fail a PIN five times, close/reopen the application and confirm correct PIN still cannot bypass lockout. Wait 15 minutes or use another owner's reset/unlock, then verify successful login.
6. Run the foundation suite on Windows to exercise a failure injected after SQL writes, then independently read the file and verify neither the business change nor audit committed. Its subsequent fresh retry must succeed once.
7. Try inaccessible storage/full-disk conditions and verify translated recovery guidance without replacement/reset of the existing database. Measure cold start on the target PC; the under-five-second goal is not yet a measured guarantee.

Physical printer/scanner/receipt tests are not part of milestone 01. They are neither run nor claimed here. No physical hardware input blocks this milestone's source implementation.

## Recorded checks in this workspace

- Release solution build, including cross-targeted WPF and its Windows test assembly: passed with zero warnings/errors.
- Domain: 132 passed. Architecture: 11 passed. Existing SQLite integration: 10 passed, with 3 PostgreSQL-only tests explicitly skipped. Foundation/application/file-backed security and transaction suite: 13 passed.
- Both SQLite and retained PostgreSQL migration snapshots: no pending model changes. Existing historical migration files were not modified.
- All 75 English/Sinhala resource keys match; literal view and application validation keys resolve. CI YAML parses successfully.
- Argon2id verification also checks a fixed vector produced independently with argon2-cffi, not only this implementation's own hash output.
- Not run here: Windows WPF runtime tests, target-PC visual acceptance/cold-start measurement, optional PostgreSQL container tests, or physical hardware tests. Windows CI coverage is added but has not been executed from this workspace.
