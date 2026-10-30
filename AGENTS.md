# AGENTS.md — Project Rules & Conventions

Instructions for AI agents / future sessions working in this repository.

## Core project rule: version bump on functionality change

- **Bump an EXE's version only when THAT EXE's functionality actually changes** — update BOTH
  `src/<project>/<project>.csproj` (`<Version>` + `<FileVersion>`) and `.data2\manifest.json`
  (tool-id → `version`) in the SAME session as the change. Log it in `PROJECT_LOG.md`.
- If an EXE's functionality did not change, **leave its version untouched** — even when it is
  rebuilt/republished alongside others. Each EXE's version reflects its own revision history,
  so version numbers across tools will naturally diverge.
- Shared-code (`src/Htxb.Host`) changes bump only the tools whose user-visible behaviour really
  changes (e.g. a save-shim fix used by three tools → bump those three; a purely internal
  refactor → bump none). Judge per tool, do not blanket-bump.
- **Version format — applies to ALL EXE UI builds** (matches the original HTML tools' scheme):
  `V6.MM.DDR` → stored numerically as `Y.M.DDR.0`, e.g. **V6.09.081** = `6.9.81.0`. Breakdown:
  - `V6`  = year 2026 (always `6` for 2026; `Y` = year − 2020, so 2026 → 6)
  - `MM`  = month, 2 digits (September → 09)
  - `DD`  = day of month, 2 digits (8th → 08)
  - `R`   = increment updates running number (1st update that day → 1, 2nd → 2, …), counted
    **per EXE**, not globally. It resets to 1 each new day/date.
- Examples: first change to a tool on 2026-09-08 → **V6.09.081** (`6.9.81.0`); second change to
  that same tool that day → **V6.09.082** (`6.9.82.0`); a tool last changed on Sep 4 keeps
  **V6.09.041** (`6.9.41.0`). Reference: first change on 2026-08-21 → V6.08.211; third on that
  day → V6.08.213.
- **UI version display rule — applies to ALL EXE tools' UI version formats**: the version shown
  on each tool's UI must be **aligned with the version listed for that tool on the Tools &
  Utilities cover page** and must follow the same `V6.MM.DDR` display format as the EXE version
  above. The cover page reads each tool's header version at runtime and displays it; therefore
  the header (`.logo-tag` badge / `Version:` line) and the cover page entry must always show the
  same number, and the `V6.MM.DDR` running-number scheme applies to the UI display too. Keep the
  UI header version in sync whenever the EXE version is bumped. Example of a corrected Lot
  Extractor update sequence on 08-08: `V6.08.081`, then next updates **`V6.08.082`** and
  **`V6.08.083`** (not `V6.08.191`).

## Core project rule: keep the history log current

- **Every time you create, modify, or delete a file/folder under this repo, append an entry
  to `PROJECT_LOG.md`** (at repo root) with:
  - date (YYYY-MM-DD)
  - change type (`created` / `modified` / `deleted` / `release`)
  - list of affected paths (relative to repo root)
  - 1–2 sentence summary of what/why
- Log the change in the SAME session that made it — do not wait for a request.
- If a change is a milestone deliverable (M0–M7), mark it so in the entry.
- Read `PROJECT_LOG.md` and `PHASE2_KICKOFF.md` before starting new work.

## Project context

- Repo root: `D:\_TTI-Dev\_VSProj\VSProj_00_Main` (== PHASE2_KICKOFF.md "Phase 2 root"; moved from old-PC `E:\...` on 2026-09-19)
- Phase 2 goal: wrap each HTML tool in `D:\_TTI-Dev\proj01 Main Page` into a native Windows EXE
  (WebView2 host). HTML is embedded/protected; big libs live in the shared `data2` folder.
- Phase 1 reference: `D:\_TTI-Dev\proj01 Main Page\PROJECT_HISTORY.md`
- Pilot reference: `D:\_TTI-Dev\Proj7 Temperature Uniformity Graph\TTI_TempUniformity\`
- Tools to wrap (manifest ids): `lot-extractor` (M0/M1, DONE) → `temp-uniformity` (M2) →
  `bidf-analysis` (M3) → `recipe-translator` + `rcp-std-table` (M4) → `p2-analysis` (M5) →
  `launcher` (M6) → packaging (M7).
- Launcher auto-hide (`HostOptions.EnableEdgeAutoHide`, launcher only): after load the
  window collapses into a small `» TTI Tools` tab pinned at the top-left corner once the
  mouse stays outside it (2.5 s grace + 0.9 s dwell — focus is deliberately NOT required,
  the launcher commonly stays foreground while the mouse is elsewhere). Hovering/clicking
  the tab or activating the window expands it back to full working-area height at the
  corner (width remembered, never a short window); the window also starts at full
  working-area height on first launch (`MainForm.FullWorkingAreaHeight`).
  Collapse is suppressed while the page reports busy (`window.tools.SetBusy`, e.g. barcode
  overlay open) or a native dialog is up. Shared-host code (`HostForm` collapse/expand +
  `NativeBridge.GetOwnerWidth` reporting the full width while collapsed) — only the
  launcher opts in, so only it gets bumped for such changes.

## Tech stack

- C# / .NET 8 (`net8.0-windows`), WinForms + Microsoft.Web.WebView2 (NuGet pkg)
- VS Code + `dotnet` CLI. New PC has SDK 8.0.425 only (old PC had SDK 10.0.302); `.slnx` needs
  SDK 10, so `build.ps1` builds per-project (works on both). .NET 8 Desktop Runtime present on dev machine.

## Architecture conventions

- Shared host library: `src/Htxb.Host` — `WebHost.cs` (virtual host `https://app/` + routing),
  `HostForm.cs` creates the WebView2 environment explicitly with offline-first flags
  (`--no-proxy-server --disable-background-networking --disable-component-update
  --disable-sync --host-resolver-rules="MAP app 127.0.0.1"`): every page is served
  locally so there is no legitimate network use, and without the resolver rule Chromium
  burns ~2 s on DNS timeouts for the fake `app` hostname on every launch. Do not remove
  these flags; measure with launch-timing marks if launch speed is ever revisited.
  `Data2Loader.cs` (serves `data2` from `AppContext.BaseDirectory\data2`), `NativeBridge.cs`
  (COM-visible `window.tools` object via `AddHostObjectToScript`), `Manifest.cs`, `HostForm.cs`.
- One wrapper project per tool under `src/Htxb.*` — embed the deployed HTML from
  `proj01 Main Page` via `<EmbeddedResource ... LogicalName="index.html">` (never edit the source).
- Per-tool polyfill JS (e.g. `Resources/polyfill.js`) is embedded with `LogicalName="polyfill.js"`
  and injected at serve time by `WebHost` (replaces `showDirectoryPicker` / save-picker shims).
- **EXE-only tools** (no canonical HTML under `proj01 Main Page`) keep their page in the repo at
  `src/<project>/Resources/index.html` — currently Barcode Generator and **P2 Data Analysis - Time**
  (`p2-analysis-time`, `TTI_P2DataAnalysisTime.exe`). Their launcher card is injected at runtime by
  `src/Htxb.Launcher/Resources/polyfill.js` (the canonical cover page is never edited) and the card's
  version is read from `data2\manifest.json`. P2 Data Analysis - Time reads BiDF logs through the
  shared host: `SelectDirectory` + recursive `ListDirectory` + `ReadFileBase64`, gunzip in-page via
  `DecompressionStream`, then `sql.js` (`tbl_run` / `tbl_testflow`); it reports preset (`exec_set`)
  vs actual (`exec_time`) test time per run and per step, slot-to-slot and lot-to-lot.
- Impacts on `data2`: `manifest.json` (tool-id → version) + `lib/` + `assets/` + `runtime/`.
- **DUT package image rule (BiDF Analysis):** `data2\assets\dut_package\` is user data, not just
  seed art — custom `*.png`/`*.jpg` for different packages must be committed to the repo copy
  (`.data2\assets\dut_package\`), never added only to `dist\` or the deployed site (both are
  wiped/re-copied on every publish). `build.ps1` additionally backs up and restores any
  `dut_package` files missing from the repo copy so a republish never deletes site-added images.
  No EXE ever writes to or deletes from this folder (host has no delete API; page only reads).
- **Print/PDF header date rule — applies to EVERY tool with a print-to-PDF function
  (existing or new):** the printed PDF must carry the print day in Long Form
  (`Month D, YYYY`, e.g. `October 9, 2026`). The pages themselves never stamp the
  print day (the only header date would otherwise be the browser's
  locale-dependent short date), so implement it polyfill-side in
  `Resources/polyfill.js` (canonical HTML stays untouched): inject a print-only
  `#tti-print-date` line as the first row of the printed content, hidden on
  screen (`display:none`) and shown only under `@media print`; render the date
  with explicit month names (never `toLocaleDateString`, so every machine is
  identical); re-stamp it on every `beforeprint` so long-open windows print the
  current day. Reference implementations: `src/Htxb.TempUniformity` and
  `src/Htxb.BiDFAnalysis` polyfills (`ttiLongFormToday` +
  `ensurePrintDateHeader`). Adding print output (or the header) to a tool counts
  as a functionality change → bump that EXE's version per the rule above.
- Per-tool settings live in `configuration\tools.ini` (repo copy is the seed; `build.ps1` copies
  it to `dist\configuration\`). One `[tool-id]` section per tool; the shared host
  (`ToolSettings`) reads/writes `LastDir=` so file/folder dialogs reopen at the user's last
  picked location. Section names must match `manifest.json` tool ids.
- **Default directory rule — all EXE tools EXCEPT the Utilities Launcher**: the user predefines
  each tool's default open/search directory in `configuration/default_dir.ini` (repo copy is the
  seed; `build.ps1` copies it to `dist\configuration\`). Each line is `EXEname = "path"` — the
  EXE filename (without extension, e.g. `TTI_LotExtractor`) as the key and the directory path in
  double quotes as the value:
  ```ini
  TTI_LotExtractor = "D:\COBIS-II-632\COBIS-II\Document"
  ```
  The shared host reads this file at startup and uses each tool's entry as the initial directory
  for its folder/file pickers. The Utilities Launcher (`TTI_Utils_Launcher`) is excluded from
  this rule and does not read / need a `default_dir.ini` entry.
- Distribution layout (`dist/`): **only per-tool EXEs at root + shared `data2` folder at root** —
  no DLLs/runtime files in the EXE directory. Publish is **framework-dependent single-file**
  (`PublishSingleFile=true`, `SelfContained=false`, `IncludeNativeLibrariesForSelfExtract=true`):
  each EXE embeds the shared host + WebView2 assemblies and stays < 5 MB; native libs
  (`WebView2Loader.dll`) self-extract to `%TEMP%\.net` on first run. No .NET runtime bundled
  (machine needs .NET 8 Desktop Runtime). `AppContext.BaseDirectory` == EXE directory in this mode,
  so `Data2Loader` (`data2\`) still resolves from next to the EXE. WebView2 user-data/profile is
  redirected to `%LOCALAPPDATA%\TTI HTXB\<toolId>` to keep `dist\` clean.

## Commands

- Build:    `dotnet build TTI_HtxbSuite.slnx -c Release`
- Publish:  `powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1`
  (build → single-file publish each tool → move EXEs to `dist\` root → copy `.data2` → trim extras → size-check)
- Add a new tool wrapper (edit `build.ps1` `$tools` + `data2\manifest.json` + new `src/Htxb.*` project).

### Recipe audit tool (deliberately NOT a suite tool)

`src\Htxb.RecipeCore` + `src\Htxb.RecipeTool` read a customer recipe workbook and judge
it against a `Std_*.xlsx` limits table. It is a console audit tool for the pipeline
work, not a launcher tile, so it is **kept out** of `build.ps1` `$tools`,
`.data2\manifest.json` and `dist\` on purpose:

- Build/publish: `powershell -ExecutionPolicy Bypass -File build-recipetool.ps1`
  → `dist-recipetool\TTI_RecipeTool.exe`
- Checks: `powershell -ExecutionPolicy Bypass -File test-recipetool.ps1`
  (26 checks; the BidfGen2 `.\dev-check.ps1` calls this as a hard gate)
- `Htxb.RecipeCore` must stay **package-free** (`System.IO.Compression` +
  `System.Xml.Linq` only) so it still compiles under the .NET Framework `csc.exe`
  if it ever has to ship inside the offline installer.

## Do not

- Do not modify the HTML sources under `proj01 Main Page` — that folder is the canonical deployed copy.
- Do not commit secrets; do not run `git commit` unless the user explicitly asks.