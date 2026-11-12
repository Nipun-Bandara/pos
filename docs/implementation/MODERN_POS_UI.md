# Modern Windows POS interface

Branch: `codex/modern-pos-ui`, created from updated `development` at `1aa2f1c`.
Scope is presentation only. This record does not establish Windows or physical acceptance.

## 1. Shared design and application shell

MaterialDesignInXAML is the sole added UI toolkit. `MaterialDesignThemes` is centrally pinned
at **5.3.2**, a stable package with a native `net10.0-windows7.0` WPF asset and matching
MaterialDesignColors dependency. Its required Microsoft.Xaml.Behaviors dependency is restored
transitively. See [package metadata](https://www.nuget.org/packages/MaterialDesignThemes/5.3.2)
and the [official integration guide](https://github.com/MaterialDesignInXAML/MaterialDesignInXamlToolkit/wiki/Getting-Started).
The application retains .NET 10, WPF, the existing composition root and local SQLite.

`Design/PosTheme.xaml` defines light surfaces, one blue accent, restrained borders, spacing,
buttons, outlined inputs, tables, wrapped labels and a visible keyboard focus outline.
Primary actions have a filled treatment. Ripples on buttons are disabled. The font stack
includes Nirmala UI, Segoe UI and Iskoola Pota; actual Sinhala fallback/shaping remains a
Windows visual review. Resource-key parity alone cannot establish readable Sinhala output.

`MainWindow.xaml` keeps six-section navigation, header and status outside changing content.
The header contains current user/role, language, lock and sign-out. Busy state disables
navigation and business actions while retaining the existing scan queue behavior. Selected
navigation has a visual and accessible current-section indication. Backup health and
application messages remain visible in the footer. Setup, sign-in and lock use a bounded
surface with the existing PIN handling; owner approval uses the same visual resources and
retains exact command review, owner selection, PIN clearing and approval behavior.

## 2. Billing workspace

`Design/BillingWorkspace.xaml` separates barcode entry, searchable product selection,
resizable basket, quantity/removal actions and a fixed-width payment area. The checkout
button and totals/tender area sit outside the scrolling approval/history options. Expanded
product selection scrolls within its own area to preserve basket and payment space. The
existing 800×600 minimum is unchanged; the normal window defaults to 1280×850.

The main basket shows the original Sinhala receipt name, three-decimal quantity with unit,
charged unit price and total, using proportional wrapped columns without horizontal scrolling.
Selected-item details retain SKU, Sinhala/English names, normal/tier pricing, band, savings,
manual final price and reason. Search and category selection still use the saved catalogue.

The basket disables Material Design's outer cell padding because `PosWrappedCell` already
supplies text padding. Applying both narrowed text unnecessarily and caused the first row
to extend below the basket in the Windows minimum-size check. Other tables retain their
existing styling. Layout captures are saved before bounds assertions so a failed Windows
review still provides the rendered screen. This correction requires a Windows CI rerun;
the Ubuntu build alone does not establish that the rendered row fits.
The desktop test project rebuild after this correction passed with zero warnings/errors;
theme XML parsing and `git diff --check` also passed. WPF execution remains unavailable here.

Existing queued barcode input, repeated-scan aggregation, Enter, F2/Escape scan focus,
Ctrl+F search, F4 tender, F6 removal and F9 completion handlers are retained. The existing
shift, discount/negative-stock approvals, cash/card safeguards, rounding and idempotent
completion calls are unchanged. The payment amount is never calculated by the new layout.

## 3. Consistent remaining screens

Products, Stock, bill history, Returns, Reports and Settings use the shared theme. Adjacent
fields form wrapping rows; adjacent actions share wrapping toolbars. Primary tables have
localized empty-state overlays. Read-only stock, report and historical return columns use
explicit one-way bindings; product active-state binding remains one-way. Editable return
quantities/disposition retain their existing update bindings. Detailed tables can scroll
internally without adding a horizontal scrollbar around an entire form.

Existing workflows remain in their six sections, including bill/return printing, exports,
owner administration, backup configuration and guarded restore. Application authorization
is not replaced by visibility or enabled-state checks. No domain/application/persistence
source, migration, receipt renderer or report renderer is changed by this branch.

## Verification record — 2026-11-12

Executed on Ubuntu 24.04.5 with .NET SDK 10.0.113/runtime 10.0.13:

- Release solution build with warnings as errors: passed, zero warnings/errors. This builds
  the WPF assemblies/XAML but does not run them.
- Domain: 158 passed. Application/file-backed foundation: 90 passed; its opt-in performance
  benchmark was not run. Architecture: 11 passed. SQLite integration: 10 passed; three
  optional legacy PostgreSQL server tests were skipped. Total executed/passed: 269.
- SQLite and retained PostgreSQL model/migration checks: no pending model changes. No
  migration was added or modified. Domain/application/persistence and receipt renderer source
  are unchanged from the starting development commit.
- Resources: 499 unique, nonempty matching Sinhala/English keys; source XAML parses and
  `git diff --check` passes. This is structural validation, not native translation approval.
- Self-contained Windows x64 publish: passed. Verified the executable, coreclr native runtime,
  MaterialDesignThemes.Wpf, MaterialDesignColors and Microsoft.Xaml.Behaviors in the payload.
  An initial offline-cache publish lacked Windows runtime packs; an authorized online restore
  obtained those packs and the publish succeeded.
- Windows workflow runner: attempted, unavailable (`Microsoft.WindowsDesktop.App` missing on
  Linux). PowerShell/Inno compilation and installer smoke cannot execute on this host.

Local logs and the cross-published payload are in ignored `artifacts/modern-ui`, outside
committed source. No Windows screenshots or installer executable have been produced here.

## Windows review and screenshot gate — not executed here

This environment is Ubuntu and cannot load `Microsoft.WindowsDesktop.App`. The real WPF test
runner was attempted and exited before executing tests because that framework is unavailable.
No screenshot from this new interface has been rendered or manually accepted here. No HTML
mockup, headless substitute or generated image is presented as a Windows screenshot.

The real dispatcher workflow tests retain their existing financial/security assertions and
unchanged timeouts. They now exercise Enter scanning and two rapid scans, assert scan-focus
restoration, explicitly open selected-item details, check basket/header/payment client bounds
and produce the following **planned Windows captures**:

- `modern-setup-*`, `modern-setup-pin-*`, `modern-login-*`, `modern-locked-*`.
- `modern-billing-basket-*` with a real tier/weighted basket; a complete visible first row and no
  horizontal basket overflow are asserted at 800×600 and 1280×850 in both languages.
- `modern-products-*`, `modern-stock-*`, `modern-returns-*`, `modern-reports-*`,
  `modern-settings-*`, `modern-approval-{en,si}.png`, plus existing receipt/recovery workflow captures.

These files are produced in the desktop test output's `artifacts` directory when Windows
execution reaches the capture points. Existing CI uploads them as `windows-ui-previews`.
Review all captures for wrapping/clipping, long Sinhala names, keyboard focus and contrast;
exercise search with the product picker expanded at minimum size. Manually verify Tab order,
all shortcuts, validation and modal approval/cancel behavior in both languages. Automated
bounds checks do not establish translation quality, font shaping or general usability.

Run on a disposable Windows checkout/CI profile:

```powershell
dotnet restore
dotnet build --no-restore --configuration Release
dotnet tests/RetailCore.Desktop.Tests/bin/Release/net10.0-windows/RetailCore.Desktop.Tests.dll
./scripts/build-installer.ps1
./scripts/test-installer.ps1
```

The installer smoke script intentionally requires a disposable GitHub Actions profile and
refuses existing shop data. The retained Windows packaging job uses its pinned Inno compiler.
A self-contained cross-publish does not establish that the installer builds or runs. Actual
installer compilation and install/upgrade/uninstall smoke remain pending on Windows.

Physical acceptance remains separate: use the shop's keyboard-mode scanner to confirm rapid
scans, suffix/keyboard settings and focus; use the Xprinter 80 mm Windows queue to verify
Sinhala receipts, wrapping, totals, readable barcode and safe print retry after disconnection.
Keyboard event simulation and a missing-queue test cannot establish physical acceptance.

Do not merge into `development` until required Windows build/workflow/layout/installer checks
pass. No remote push is authorized by this request.

## Windows minimum-size follow-up

The [Windows run for `9143f7a`](https://github.com/Nipun-Bandara/pos/actions/runs/43220043322)
reached the Sinhala 800×600 review after the English sizes, then failed the client-bounds
assertion (bottom 569 against client limit 562). Its actual screenshot shows header actions
wrapping to a second row and Add Barcode occupying multiple lines, while the payment's auto
rows push completion below the available area. The basket name column was also squeezed.

The follow-up uses short bilingual header/Add/completion captions, preserving original full
accessible names, tooltips, automation IDs and handlers. Basket columns explicitly use
proportional widths. The basket derives directly from the named shared table style, retaining
its existing cell-padding correction without relying on an implicit-style resource lookup.
Bounds assertions retain their one-pixel tolerance and now identify the control, language,
window size and rectangle. The review also checks checkout/basket bounds with product
selection expanded and saves a separate capture. No assertion or timeout was relaxed.

503 unique, nonempty bilingual resource keys match. The desktop-test project Release build
passed with zero warnings/errors. The native Windows rerun remains pending: Linux can compile
the views/tests but cannot establish that the corrected Windows layout passes. The branch remains
unmerged pending that rerun and the other recorded Windows acceptance gates.

## Deferred Expander content in the Windows workflow

The subsequent supplied Windows run reached the discounted-sale editor after completing the
minimum/normal-size checks in both languages and the preceding cash/card examples. It then
looked for `RequestedFinalPrice` immediately after setting `IsExpanded`, before that field
was present in the visual tree. The pinned toolkit's
[Expander template](https://github.com/MaterialDesignInXAML/MaterialDesignInXamlToolkit/blob/v5.3.2/src/MaterialDesignThemes.Wpf/Themes/MaterialDesignTheme.Expander.xaml)
changes content visibility through a storyboard; a synchronous `UpdateLayout` does not
advance that transition.

The test now uses its existing bounded dispatcher wait to require exactly one loaded,
visible, measured control whose on-screen size has finished expanding before editing. It
brings each discount field into view and
asserts keyboard focus and client bounds. Product-picker checks likewise wait for the real
product table to appear before inspecting the expanded layout. The 20-second readiness
limit, three-minute outer timeout, financial assertions and owner-approval flow are unchanged.
No logical-tree shortcut, artificial field, fixed sleep or application authorization change
was introduced. The desktop-test Release build passes with zero warnings/errors; native
Windows execution of this synchronization correction remains pending.
