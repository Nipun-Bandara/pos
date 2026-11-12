# Windows installation, recovery, hardware and pilot acceptance

Status: **NOT RUN**. Fill this record with the owner on the actual checkout. Store customer
exports, database files and physical receipt photographs outside Git. A mock printer or a
source-code build does not satisfy any physical check below. No pilot trading dates are
invented here; select seven actual shop trading days after the entry gates pass.

## Record the equipment and installation

| Input | Actual value / evidence |
|---|---|
| Intended Windows PC, CPU, RAM, disk and Windows version | Pending |
| Installer version, SHA-256 and signing status | Pending |
| One Windows account used for checkout data | Pending |
| Xprinter 80 mm Windows queue/driver and actual printable width | Pending |
| Sinhala font/shaping, receipt wrapping and longest names | Pending |
| USB keyboard-mode scanner and prefix/suffix/keyboard layout | Pending |
| Primary backup folder and retention | Pending |
| External/USB second destination and independent physical storage | Pending |
| Owner responsible for PIN, daily count and backup checks | Pending |

## Entry gates — all pending

1. Install on a clean Windows profile with no separately installed .NET. Open both languages,
   set up the owner and confirm data is outside installation files. Validate Sinhala installer
   common pages and any native fallback messages with the owner. Confirm scheduled task exists.
2. Make real cash/card bills with change, weighted goods and quantity tiers; receive and waste
   stock, perform partial/damaged returns and all exchange directions. Close/count the shift.
3. Create primary and external backups, disconnect USB and check visible partial failure,
   reconnect and retry. Restart and inspect the persisted primary/second and maintenance status.
4. Restore on a separate clean checkout profile with owner approval. Compare receipt numbers,
   original names/prices/tax/cost, returns, signed stock, open/closed shifts and expected/counted
   cash. Reprint the original bill after catalogue changes and confirm original content.
5. Reject a truncated/corrupt archive, unknown schema, missing audit/stock trigger and changed
   file after review. Try cashier/direct application commands and wrong/mismatched approval.
6. Exercise interrupted restore and failed-upgrade recovery on disposable copies, never the
   only live database. Keep the safety copy. Confirm no trading while maintenance is pending.
7. Upgrade with a closed shift and verified safety copy, reject an upgrade with an open shift
   or unavailable configured second storage. Uninstall/reinstall and verify data survives.
8. Print on actual Xprinter: Sinhala shaping/wrapping, totals, quantities, savings, linked return
   documents and scanner-readable bill barcode. Disconnect printer, restart and retry without
   another payment or stock posting. A spool-accepted job is not a physical receipt pass.
9. Scan the same product five times; switch UI language, search, approve a discount, edit weight
   to 1.250 and settle with keyboard only. Confirm focus returns to scan entry and each scan
   counts once. Manually read Sinhala/English errors and every new backup/restore path.
10. Open filtered Sinhala CSV/XLSX/PDF in Windows applications; verify text barcodes, protected
    formula-like text, typed numbers/dates, period/filters and legible PDF continuation pages.
11. Run `scripts/measure-checkout.ps1` on the intended PC (disposable 10,000-product SQLite data).
    Record backend scan p95, catalogue/filter, settlement and service restart. Separately time
    actual WPF cold start, bound-table search and scan-to-line including scanner input; print
    time is separate. Targets: scan <100 ms, search <150 ms, cold start <5 s, typical save <500 ms.
    Do not substitute another computer's timings or backend-only measurements for these targets.
    Current Linux diagnostic scan p95 is 202 ms, above the 100 ms target; investigate and
    demonstrate acceptable intended-PC performance before marking this gate passed.

## Seven actual trading days — begin only after all entry gates pass

| Day / date | Cash/card/returns/exchanges checked | Stock and wastage explained | Expected / physical count / variance / pay-in-out | Primary + external backup checked | Owner review / unresolved issue |
|---|---|---|---|---|---|
| 1 / pending | Pending | Pending | Pending | Pending | Pending |
| 2 / pending | Pending | Pending | Pending | Pending | Pending |
| 3 / pending | Pending | Pending | Pending | Pending | Pending |
| 4 / pending | Pending | Pending | Pending | Pending | Pending |
| 5 / pending | Pending | Pending | Pending | Pending | Pending |
| 6 / pending | Pending | Pending | Pending | Pending | Pending |
| 7 / pending | Pending | Pending | Pending | Pending | Pending |

At each close, count first and explain every variance. Reconcile actual cash/card collections
and external refunds, counting exchange credit only as internal credit. Keep cash rounding
separate. Pause pilot trading for any unexplained stock/cash discrepancy, duplicate bill,
failed restore or missing independent backup. Repeat the failed gate before resuming.

Owner final sign-off / date: **Pending**. Developer/support sign-off: **Pending**.
