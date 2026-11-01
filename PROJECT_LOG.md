# PROJECT_LOG.md â€” TTI HTXB Suite (Phase 2) Change History

Project rule (per AGENTS.md): **every** create / modify / delete of a file or folder under this
repo MUST be appended here, in the same session it happened, before the session ends.

- Log format: `YYYY-MM-DD | type | paths | summary`
- Types: `created` / `modified` / `deleted` / `release`
- Milestone tags: `[M0]`â€¦`[M7]` (see PHASE2_KICKOFF.md Â§10)

---

## 2026-08-19 â€” M0: Host template + Lot Extractor wrapper + data2 (milestone `[M0]`)

| Type | Paths | Summary |
|---|---|---|
| created | `TTI_HtxbSuite.slnx` | Solution for the Phase 2 EXE suite (SDK 10 `dotnet new sln`). |
| created | `AGENTS.md`, `PROJECT_LOG.md`, `.gitignore` | Project rules, this log, and ignore rules (`bin/`, `obj/`, `dist/`, `*.user`). |
| created | `src/Htxb.Host/` | Shared host class library (`net8.0-windows`, UseWindowsForms, WebView2 pkg 1.0.4129.50). |
| created | `src/Htxb.Host/HostForm.cs` | Shared WinForms form: full-dock WebView2, devtools/context-menu disabled, registers `window.tools`, navigates to `https://app/index.html`. |
| created | `src/Htxb.Host/WebHost.cs` | Virtual-host resource server: routes `/`, `/polyfill.js`, `/favicon.ico`, `/manifest.json`, `/libs/*`, `/assets/*`; rewrites + polyfill-inject + MIME map. |
| created | `src/Htxb.Host/Data2Loader.cs` | Resolves/serves files under `AppContext.BaseDirectory\data2` with path-escape guard. |
| created | `src/Htxb.Host/NativeBridge.cs` | COM-visible `window.tools`: `SelectDirectory/SelectFile/SelectFiles/SaveFile`, `ReadFile( Bytes)`, `WriteFile(Bytes)`, `CreateDirectory`, `ListDirectory`, path helpers; dialogs marshalled to UI thread. |
| created | `src/Htxb.Host/Manifest.cs` | Parses `data2\manifest.json` (tool-id â†’ name/exe/version). |
| created | `src/Htxb.Host/HostOptions.cs` | Per-tool config (title, tool id, rewrites, polyfill resource name, data2 override). |
| created | `src/Htxb.LotExtractor/` | Wrapper WinExe `net8.0-windows` v6.8.191.0; embeds canonical Lot Extractor HTML (`LogicalName=index.html`) + `Resources/polyfill.js`. |
| created | `src/Htxb.LotExtractor/MainForm.cs` | Subclasses `HostForm`; window title + asset rewrites (`images/...` â†’ `assets/...`). |
| created | `src/Htxb.LotExtractor/Program.cs` | STA entry point. |
| created | `src/Htxb.LotExtractor/Resources/polyfill.js` | File System Access API shim mapping `showDirectoryPicker`/`getDirectoryHandle`/`createWritable` onto `window.tools`. |
| created | `src/Htxb.LotExtractor/Resources/Favicon.ico` | EXE application icon (copied from `proj01 Main Page\images\Company Logos`). |
| created | `.data2/manifest.json` | Data2 version manifest for all 7 tools + lib version listing. |
| created | `.data2/lib/` | Shared libs downloaded from jsdelivr: `xlsx.full.min.js@0.18.5`, `xlsx-js-style.min.js@1.2.0`, `chart.umd.min.js@4.4.1`, `sql-wasm.js/.wasm@1.8.0`. |
| created | `.data2/assets/` | Shared images copied from `proj01 Main Page\images` (Favicon, Trio-Tech Logo, Customer, HiP247 Long Leads). |
| created | `build.ps1` | One-command build â†’ publish â†’ flatten to `dist\` â†’ merge `.data2` â†’ trim (pdb/Wpf) â†’ <5 MB size check. |
| release | `dist/` | `TTI_LotExtractor.exe` (0.21 MB, framework-dependent) + shared `data2`. Smoke-tested: window opens with correct title; HTML embedded (not on disk). |

**Status:** M0 exit criteria met (EXE runs, dir-picker wired via bridge, size < 5 MB). M1 next:
interactive pass of input â†’ parse â†’ status â†’ export workflow.

---

## 2026-08-19 â€” M2: Temp Uniformity Graph wrapper (milestone `[M2]`)

| Type | Paths | Summary |
|---|---|---|
| modified | `src/Htxb.Host/NativeBridge.cs` | Added `ReadFileBase64`/`WriteFileBase64` (binary-safe JSâ†”.NET transfer via Base64, needed for Excel export). |
| modified | `src/Htxb.Host/HostForm.cs` | Added `NewWindowRequested` handler: `target=_blank` external links open in the system default browser instead of a host window. |
| created | `src/Htxb.TempUniformity/` | Wrapper WinExe `net8.0-windows` v6.7.123.0; embeds canonical `TTI Temp_Uniformity_Graph.html` + `Resources/polyfill.js`. |
| created | `src/Htxb.TempUniformity/MainForm.cs` | Subclasses `HostForm`; rewrites CDN xlsx/chart.js URLs â†’ `libs/â€¦` (served from shared `data2`) and images â†’ `assets/â€¦`. |
| created | `src/Htxb.TempUniformity/Program.cs` | STA entry point. |
| created | `src/Htxb.TempUniformity/Resources/polyfill.js` | Overrides `XLSX.writeFile` via global-assignment interception â†’ `SaveFile` + `WriteFileBase64` (no browser-download dependency). |
| created | `src/Htxb.TempUniformity/Resources/Favicon.ico` | EXE application icon. |
| modified | `TTI_HtxbSuite.slnx` | Added `Htxb.TempUniformity` project. |
| modified | `build.ps1` | Registered `Htxb.TempUniformity â†’ TTI_TempUniformity.exe`; fixed exe-rename clobber bug (rename only the current tool's host exe, keep sibling EXEs). |
| release | `dist/` | `TTI_TempUniformity.exe` (0.21 MB). Smoke-tested: window opens with correct title; both EXEs coexist in `dist\` with shared `data2` (no per-EXE lib copy). |

**Status:** M2 wrapper built; graph rendering + Excel export use `data2` libs (`xlsx.full.min.js`, `chart.umd.min.js`). Interactive validation of DAD/xlsx parsing and export pending. Next: M3 BiDF Analysis (sql.js WASM from `data2`).

---

## 2026-08-20 â€” Fix blank-window bug: native `WebView2Loader.dll` missing from `dist\`

Both EXEs showed blank windows when launched from `dist\` (web resource requests never served).
Root cause: `build.ps1` flattened publish output to `dist\` root, then removed the whole publish
folder `-Recurse`, which also deleted `runtimes\win-x64\native\WebView2Loader.dll` (the native
loader required by `Microsoft.Web.WebView2` at runtime; `deps.json` resolves it via `runtimeTargets`).
WebView2 init then failed with `DllNotFoundException`, and `HostForm` dereferenced `WebView.CoreWebView2`
before checking the init args â€” producing a silent `NullReferenceException` and an empty window.

| Type | Paths | Summary |
|---|---|---|
| modified | `build.ps1` | Preserve per-tool `runtimes\` tree during flatten (merge into `dist\runtimes\`, not delete), so `WebView2Loader.dll` survives into the distribution. |
| modified | `src/Htxb.Host/HostForm.cs` | `OnWebViewReady` now checks `CoreWebView2InitializationCompletedEventArgs.IsSuccess` first and shows a message box on init failure (prevents silent NRE/blank window). |
| created | `src/Htxb.Host/DebugLog.cs` | TEMPORARY diagnostics sink (`apphost-debug.log` next to EXE) used to reproduce/intercept the failure. |
| modified | `src/Htxb.Host/HostForm.cs`, `src/Htxb.Host/WebHost.cs`, `src/Htxb.LotExtractor/Program.cs` | Temporary `DebugLog.Append` calls + startup exception hooks + post-load PNG capture for diagnosing. |
| deleted | `src/Htxb.Host/DebugLog.cs` | Removed after root cause found; also removed all `DebugLog` calls, the capture code, and the `Program.cs` exception hooks (kept the permanent INIT-FAILED guard). |
| release | `dist/` | Rebuilt clean: `dist\runtimes\win-{x64,x86,arm64}\native\WebView2Loader.dll` present; both EXEs (0.21 MB) smoke-tested alive with correct titles and content rendered (capture pixel check 100% non-blank). |

**Status:** Blank-window defect resolved; Lot Extractor + Temp Uniformity now serve HTML/polyfill/assets/libs
from `dist\` and render correctly. Next: proceed to M3 BiDF Analysis (sql.js WASM from `data2`).

---

## 2026-08-20 â€” Loading splash overlay ("Loading ... Please Wait ...")

While WebView2 initializes and the page loads (~2â€“3 s) the window was blank white. Added a centered
loading message to the shared host so users know the UI is coming.

| Type | Paths | Summary |
|---|---|---|
| modified | `src/Htxb.Host/HostForm.cs` | New `Label` overlay (full-dock, white background, bold Segoe UI 24 pt, dark-blue text) centered on the form with `Keep: "Loading  ...  ...  ...  Please Wait  ...  ...  ..."`; shown at construction, removed via `HideLoading()` on the first `NavigationCompleted`. |

**Status:** Both EXEs rebuilt into `dist\`; mid-load `PrintWindow` capture verified splash renders
(white background + dark text in centre) and disappears after load. Next: M3 BiDF Analysis.

---

## 2026-08-20 â€” Clean `dist\` layout: root holds only EXEs + `data2`

Goal: consolidate all support files inside `data2` so the distribution root contains nothing but the
per-tool EXEs and the shared `data2` folder. A plain framework-dependent multi-file publish cannot do
this â€” `additionalProbingPaths` does not relocate managed assemblies (checked empirically via
`COREHOST_TRACE`), and the apphost hard-requires the main tool DLL + `deps.json`/`runtimeconfig.json`
next to each EXE. Switched to a **framework-dependent single-file** publish that bundles the shared
host and WebView2 managed/native assemblies into each EXE (still < 5 MB, still needs the machine .NET 8
Desktop Runtime), so the root is clean.

| Type | Paths | Summary |
|---|---|---|
| modified | `build.ps1` | Publish each tool as framework-dependent single-file (`PublishSingleFile=true`, `SelfContained=false`, `IncludeNativeLibrariesForSelfExtract=true`); dist flatten/rename now only rearranges the single EXE (no DLL/runtimes moves); trim step now removes `pdb`/`xml` only. |
| modified | `src/Htxb.Host/HostForm.cs` | WebView2 profile redirected to `%LOCALAPPDATA%\TTI HTXB\<toolId>` (via `CreationProperties.UserDataFolder`) so runtime keeps `dist\` clean. |
| modified | `AGENTS.md` | Documented the framework-dependent single-file convention + clean-root dist layout (replaces the old "NOT single-file" rule). |
| release | `dist/` | New layout verified: root = `TTI_LotExtractor.exe` (1.73 MB) + `TTI_TempUniformity.exe` (1.74 MB) + `data2\` only. Both EXEs smoke-tested alive with correct titles; rendering confirmed via in-app capture (100 % non-blank). |

**Status:** Distribution folder now contains only EXEs + shared `data2`; native libs self-extract to
`%TEMP%\.net` on first run; profiles live under `%LOCALAPPDATA%\TTI HTXB`. Next: M3 BiDF Analysis.
---

## 2026-08-21 â€” M3/M4/M5/M6: all remaining tools wrapped + bridge fix

Wrapped the four remaining sub pages and the main page as native EXEs, completing the tool set:
BiDF Analysis (M3), Recipe Translator + Rcp_Std_Table (M4), P2 Data Analysis (M5), Launcher (M6).
While smoke-testing the launcher it was discovered that `window.tools` was **undefined in every
tool** â€” WebView2 exposes host objects at `chrome.webview.hostObjects.{name}`, not as a global, so
all polyfill shims had been silently disabled since M0. Fixed by aliasing the synchronous proxy
(`window.chrome.webview.hostObjects.sync.tools`) in the injected head script. Also replaced the
XLSX `writeFile` interception with direct post-load patching (SheetJS assigns an empty global
`XLSX` first and populates later, so setter-time interception never saw the methods).

| Type | Paths | Summary |
|---|---|---|
| created | `src/Htxb.BiDFAnalysis/` | Wrapper for BiDF Analysis V6.07.122 (v6.7.122.0): rewrites sql.js/xlsx-js-style/chart.js CDN URLs to `libs/`, sql.js WASM prefix to `libs/`, favicon/logo/Customer.png to `assets/`, `images/DUT Packages/` to `assets/`; XLSX writeFile shim. |
| created | `src/Htxb.RecipeTranslator/` | Wrapper for Recipe Translator V6.06.193 (v6.6.193.0): xlsx-js-style â†’ `libs/`, favicon/logo â†’ `assets/`; XLSX writeFile shim. |
| created | `src/Htxb.RcpStdTable/` | Wrapper for Rcp_Std_Table V6.06.192 (v6.6.192.0): xlsx-js-style â†’ `libs/`, favicon/logo â†’ `assets/`; polyfills `showSaveFilePicker` via SaveFile dialog (cancel â‡’ AbortError) with buffered writable + WriteFileBase64 on close. |
| created | `src/Htxb.P2DataAnalysis/` | Wrapper for P2 Data Analysis V1.0 WIP (v1.0.0.0): xlsx-js-style â†’ `libs/`; XLSX writeFile shim. |
| created | `src/Htxb.Launcher/` | Wrapper for main page V6.07.121 (v6.7.121.0): intercepts `.tool-card` clicks and launches sibling EXEs; syncs card version labels from `/manifest.json`. |
| modified | `src/Htxb.Host/NativeBridge.cs` | New `LaunchExe(exeName)` â€” launches a sibling EXE from `AppContext.BaseDirectory` (name-only validation, returns ok/error). |
| modified | `src/Htxb.Host/WebHost.cs` | Injected head script now aliases `window.tools = window.chrome.webview.hostObjects.sync.tools` before `polyfill.js` â€” **fixes bridge being invisible to all tools**. |
| modified | `src/Htxb.TempUniformity/Resources/polyfill.js` (+ copies in BiDFAnalysis/RecipeTranslator/P2DataAnalysis) | XLSX shim rewritten: patch `XLSX.writeFile` after library load (DOMContentLoaded/load fallback) instead of assignment-time interception, which never fired because SheetJS populates the global after initial empty assignment. |
| modified | `TTI_HtxbSuite.slnx`, `build.ps1` | Registered the 5 new wrapper projects / publish entries (dist names per manifest.json). |
| release | `dist/` | 7 EXEs (1.69â€“1.76 MB each) + `data2`. Verified via CDP remote debugging: bridge live in all 7, launcher card click launches TTI_TempUniformity.exe, version labels sync from manifest, all libs load from data2, showSaveFilePicker + all four XLSX shims active; rendering confirmed by screen capture on all new windows. |

**Status:** All milestones M0â€“M6 complete; only packaging (M7) remains.

---

## 2026-08-21 â€” Launcher slim corner window + no-overlap placement for launched tools

The launcher window (1040x860, centered) covered the tool windows it started. It is now a slim
460x780 window pinned to the screen's top-left corner, and tool EXEs spawned through
`LaunchExe` center themselves in the remaining work area to its right, so the two never overlap.
Tools started standalone (Explorer/double-click) still open centered as before.

| Type | Paths | Summary |
|---|---|---|
| modified | `src/Htxb.Host/HostOptions.cs` | New `MinWindowSize` and `StartAtScreenTopLeft` options; new `ReserveLeftEnvVar` const (`TTI_HTXB_RESERVE_LEFT_PX`). |
| modified | `src/Htxb.Host/HostForm.cs` | Honours `StartAtScreenTopLeft` (Manual @ 0,0); when the reserve env-var is present and there is room, centers the window in the working-area strip right of the reserved width instead of CenterScreen. |
| modified | `src/Htxb.Host/NativeBridge.cs` | `LaunchExe` now starts children with `UseShellExecute=false` and sets `TTI_HTXB_RESERVE_LEFT_PX` = owner window width (+margin via consumer) so children can avoid the launcher strip. |
| modified | `src/Htxb.Launcher/MainForm.cs` | Window slimmed to 460x780 (min 380x300), `StartAtScreenTopLeft = true`. |
| release | `dist/` | Rebuilt all 7 EXEs. Verified: launcher rect (0,0)-(460,780); TempUniformity launched from a card lands at x=550 (no overlap); standalone TempUniformity still centered at x=320. |

**Status:** M0-M6 complete; only packaging (M7) remains.

---

## 2026-08-21 â€” New rule: version bump on functionality change (V<Y>.<MM>.<DD><R>)

Added a core project rule to AGENTS.md: whenever an EXE's functionality changes, its version must
be bumped in BOTH `src/<project>/<project>.csproj` (`<Version>` + `<FileVersion>`) and
`.data2\manifest.json` in the same session, using the original HTML tools' date scheme
`V<Y>.<MM>.<DD><R>` (Y = year-2020; MM = 2-digit month; DD = 2-digit day; R = same-day repeat
counter), stored numerically as `Y.M.DDR.0`.

Applied retroactively to today's functional changes (shared-host bridge alias fix + XLSX shim
rewrite affect every EXE; launcher also got the slim corner layout): all 7 tools bumped to
**V6.08.211** (`6.8.211.0`) â€” first update on 2026-08-21.

| Type | Paths | Summary |
|---|---|---|
| modified | `AGENTS.md` | New "Core project rule: version bump on functionality change" section with format spec + examples. |
| modified | `src/Htxb.*/*.csproj` (7 files) | `<Version>`/`<FileVersion>` â†’ `6.8.211.0` for all tools. |
| modified | `.data2/manifest.json` | All 7 tool versions â†’ `6.8.211.0`; `generated` â†’ 2026-08-21. |
| release | `dist/` | Rebuilt + republished all 7 EXEs; launcher cards and header tag verified showing V6.08.211 via CDP. |

---

## 2026-08-21 â€” Loading splash reworded (two lines, smaller) â€” V6.08.212

Loading splash text changed from the single large line "Loading ... Please Wait" (24 pt bold) to a
smaller two-line message: "UI is loading  ...  ..." / "Please wait  ...  ..." (14 pt regular, same
navy-on-white style). Shared-host change â‡’ all EXEs bumped per the version rule: second update on
2026-08-21 â†’ **V6.08.212** (`6.8.212.0`).

| Type | Paths | Summary |
|---|---|---|
| modified | `src/Htxb.Host/HostForm.cs` | `ShowLoading()`: two-line text via `\n`, font 24 pt bold â†’ 14 pt regular. |
| modified | `src/Htxb.*/*.csproj` (7 files) + `.data2/manifest.json` | Versions `6.8.211.0` â†’ `6.8.212.0`. |
| release | `dist/` | Rebuilt all 7 EXEs; splash verified by early screen capture (two centered 14 pt lines); launcher title alive; FileVersion reports 6.8.212.0. |

---

## 2026-08-21 â€” Version rule refined: bump only EXEs whose functionality changed

Clarified the versioning rule in AGENTS.md: an EXE's version is bumped **only when that EXE's
functionality actually changes**; unchanged tools keep their version even when rebuilt alongside
others. Shared-host changes bump only the tools whose user-visible behaviour really changes
(judged per tool, never blanket-bumped). The repeat counter is counted per EXE.

Corrected today's earlier blanket bump: only the launcher (slim corner layout + splash rework)
keeps **V6.08.212**; the six tools revert to their last real revision **V6.08.211**.

| Type | Paths | Summary |
|---|---|---|
| modified | `AGENTS.md` | Rule rewritten for per-EXE specificity (no blanket bumps; per-EXE repeat counter; divergent versions are expected). |
| modified | `src/Htxb.{LotExtractor,TempUniformity,BiDFAnalysis,RecipeTranslator,RcpStdTable,P2DataAnalysis}/*.csproj` | Reverted `<Version>`/`<FileVersion>` to `6.8.211.0`. |
| modified | `.data2/manifest.json` | Six tool versions reverted to `6.8.211.0`; launcher stays `6.8.212.0`. (An intermediate regex mishap that corrupted six entries was caught and the file rewritten cleanly.) |
| release | `dist/` | Republished all 7 EXEs; FileVersions verified diverged (6x 6.8.211.0 + launcher 6.8.212.0); launcher cards show V6.08.211 and header tag V6.08.212 from manifest. |

---

## 2026-08-21 â€” Launcher cards: version + status on their own line under the tool name â€” V6.08.213

Each tool card now shows the utility's version and Ready/WIP status below the tool's subject line
instead of inline after it; the description follows beneath. Done in the launcher polyfill (the
canonical HTML under `proj01 Main Page` stays untouched): on DOMContentLoaded the `.tool-version`
and `.tool-badge` spans are moved into a block `.tool-meta` div inside `.tool-name`, preserving
their original classes so all font formats are unchanged (version 11 px, badge 10 px). Launcher
only â‡’ third revision today â†’ **V6.08.213** (`6.8.213.0`).

| Type | Paths | Summary |
|---|---|---|
| modified | `src/Htxb.Launcher/Resources/polyfill.js` | DOMContentLoaded handler restructures each card: version span first, then status badge, wrapped in a block-level `.tool-meta` line below the name. |
| modified | `src/Htxb.Launcher/Htxb.Launcher.csproj`, `.data2/manifest.json` | Launcher version `6.8.212.0` â†’ `6.8.213.0`. |
| release | `dist/` | Republished; CDP-verified geometry (name bottom 159 â†’ meta top 161 â†’ desc top 184, ordered=true), fonts unchanged, versions still sync from manifest. |

---

## 2026-08-21 â€” configuration\tools.ini: per-tool last-used open/save directories â€” V6.08.214

Added a `configuration` folder (repo root, copied to `dist\configuration\` by build.ps1) holding
`tools.ini`, which keeps every tool's default folder/file directory â€” e.g. Lot Extractor seeds
`D:\COBIS-II-632\COBIS-II\Document\`. The shared host now persists each pick automatically:
`SelectDirectory` / `SelectFile` / `SelectFiles` / `SaveFile` start in the tool's remembered
directory and write it back after a successful pick (`ToolSettings`, one `[tool-id] LastDir=`
section per tool). Launcher has no pickers â‡’ untouched; all six other tools bumped to
**V6.08.214** (`6.8.214.0`) â€” fourth revision today.

| Type | Paths | Summary |
|---|---|---|
| created | `configuration/tools.ini` | Seed INI with `[tool-id]` sections for all 7 tools; lot-extractor pre-seeded with the COBIS-II Document directory. |
| created | `src/Htxb.Host/ToolSettings.cs` | Minimal INI reader/writer scoped to `<exe dir>\configuration\tools.ini`; get/set `LastDir` per tool id, preserves comments and other sections, best-effort on errors. |
| modified | `src/Htxb.Host/NativeBridge.cs` | Takes toolId; all four dialog methods seed `InitialDirectory`/`SelectedPath` from `LastDir` and persist the picked directory afterwards. |
| modified | `src/Htxb.Host/HostForm.cs` | Passes `options.ToolId` into the bridge. |
| modified | `build.ps1` | Copies `configuration\*` to `dist\configuration\` next to data2. |
| modified | `AGENTS.md` | Documents the tools.ini convention. |
| modified | 6 tool `.csproj` + `.data2/manifest.json` | Versions â†’ `6.8.214.0` (launcher stays `6.8.213.0`). |
| release | `dist/` | Republished; dist root = 7 EXEs + data2 + configuration. ToolSettings round-trip verified via .NET 8 test harness (read seed, update in place, append section, missing â†’ empty); LotExtractor smoke-tested alive. |

---

## 2026-08-21 â€” Loading splash: 18 pt text + Windows 11-style progress bar

Splash reworked: same two-line message ("UI is loading ... ..." / "Please wait ... ...") now at
18 pt bold (was 14 pt), with a rounded Windows 11-style progress bar (6 px track, accent-blue
fill) centred below the text. The fill is driven by a 50 ms timer easing toward a soft cap (~92%),
giving users an estimated-duration feel without knowing true load time; it disappears with the
splash on NavigationCompleted. Shared-host visual change â‡’ all 7 EXEs bumped (per-EXE counters):
launcher `6.8.213.0` â†’ **V6.08.214**, other six `6.8.214.0` â†’ **V6.08.215**.

| Type | Paths | Summary |
|---|---|---|
| created | `src/Htxb.Host/LoadingBar.cs` | Custom double-buffered control: anti-aliased rounded track + fill, TickAdvance() easing. |
| modified | `src/Htxb.Host/HostForm.cs` | Splash now Panel + TableLayoutPanel (label bottom-centred over bar host); timer drives bar; HideLoading disposes timer/panel. |
| modified | 7 `.csproj` + `.data2/manifest.json` | Launcher â†’ `6.8.214.0`; six tools â†’ `6.8.215.0`. |
| release | `dist/` | Republished; screen captures verify two-line 18 pt text (bands 24 px vs 18 px prior) and bar fill growing 75â†’129 px before dismissal. |

---

## 2026-08-28 â€” BiDF file analysis: `(NEW)` column-suffix adaptation â€” V6.08.281

The BiDF tool now parses and displays `.bidf` files identically whether columns carry a trailing
` (NEW)` / `(NEW)` suffix (e.g. `hwbin_desc (NEW)`, `exec_set (NEW)`, `theadid(NEW)`) or their
spec-clean names. Follows `BIDF_NEW_Column_Cleanup.md`: on DB open the polyfill renames any
`(NEW)`-suffixed column to its clean name using the recreate-table method (skips `sqlite_sequence`),
and wraps `database.exec` so the page's own hard-coded queries that still use quoted names like
`"exec_set (NEW)"`/`"hwbin_desc (NEW)"` keep resolving. Only the BiDF wrapper changed - BiDF
`6.8.215.0` -> V6.08.281 (`6.8.281.0`); other six EXEs untouched.

| Type | Paths | Summary |
|---|---|---|
| modified | `src/Htxb.BiDFAnalysis/Resources/polyfill.js` | Added `(NEW)` adaptation: poll for the page's global `let SQL`, wrap `SQL.Database` so every file-opened DB runs `stripNewColumns` (recreate-table, port of the md) and `wrapExec` (rewrites quoted `(NEW)` identifiers); existing XLSX shim unchanged. |
| modified | `src/Htxb.BiDFAnalysis/Htxb.BiDFAnalysis.csproj` | Version `6.8.215.0` -> `6.8.281.0` (`<Version>` + `<FileVersion>`). |
| modified | `.data2/manifest.json` | `bidf-analysis` version -> `6.8.281.0`; `generated` -> 2026-08-28. |
| release | `dist/` | Republished via `build.ps1`; `TTI_BiDFAnalysis.exe` reads as `6.8.281.0`. Self-verified end-to-end in WebView2+sql.js against a real file (`...\ST 34 Steps\...TrialForBiDF.bidf`): 0 `(NEW)` cols remain after load, `"exec_set (NEW)"` -> 34 rows, `"hwbin_desc (NEW)"` -> 75 rows, dashboard shown, "Successfully parsed" toast; algorithm also validated on 2 more real files (75 result rows each) plus the `Template_V1_5.bidf` -> clean match to `Template1_V1_5.bidf`.

| Type | Paths | Summary |
|---|---|---|
| modified | `src/Htxb.BiDFAnalysis/MainForm.cs` | DUT pkg image rewrite `images/DUT Packages/` -> `assets/dut_package/` so the Tbl_Summary HWBIN Map package illustration is served from `data2\assets\dut_package` instead of `data2\assets`. |
| modified | `src/Htxb.Host/WebHost.cs` | Added `ServeDutPackageAsset`: for `assets/dut_package/` requests, fall back across `.png`/`.jpg`/`.jpeg` so the DUT package image displays regardless of file type. Shared host change only affects BiDF (sole `dut_package` consumer). |
| modified | `.data2/assets/` | Created `data2\assets\dut_package\`; moved DUT package illustration `HiP247 Long Leads.png` into it (company logos `Trio-Tech Logo.png`, `Customer.png`, `Favicon.ico` stay in `assets\`). |
| modified | `src/Htxb.BiDFAnalysis/Htxb.BiDFAnalysis.csproj` | Version `6.8.281.0` -> `6.8.282.0` (`<Version>` + `<FileVersion>`). |
| modified | `.data2/manifest.json` | `bidf-analysis` version -> `6.8.282.0`. |
| release | `dist/` | Republished via `build.ps1`; `TTI_BiDFAnalysis.exe` reads `6.8.282.0`; `dist\data2\assets\dut_package\HiP247 Long Leads.png` present; logos unchanged. Verified compiled `assets/dut_package/` literal + Host fallback in published EXE. |

| Type | Paths | Summary |
|---|---|---|
| modified | `src/Htxb.LotExtractor/Resources/polyfill.js` | Accel. large-folder scans: pre-filter by the page's Date From/To **before** reading/parsing, using the timestamp in each CSV filename (`xxx_YYYYMMDDHHMMSS.csv`). (1) `VNode.prototype.values` skips out-of-range CSV entries so their content is never disk-read via `tools.ReadFile` in the `showDirectoryPicker` path; (2) `processFolderFiles` is wrapped to drop non-CSV + out-of-range CSV before the parse loop (legacy input/drag-drop). Mirrors the page's `extractDateTimeKey`/`matchesFilters` date logic. Rule 1 (CSV only) was already enforced at collection; `.csv.bak`/others are never read. |
| modified | `src/Htxb.LotExtractor/Htxb.LotExtractor.csproj` | Version `6.8.215.0` -> `6.9.81.0` (`<Version>` + `<FileVersion>`). |
| modified | `.data2/manifest.json` | `lot-extractor` version -> `6.9.81.0`; `generated` -> 2026-09-08. |
| release | `dist/` | Republished via `build.ps1`; `TTI_LotExtractor.exe` reads `6.9.81.0`. Validated against real `Ref Document (Slot1-24)` (12,707 files / 6,677 CSVs): default last-3-day filter reads+parses only **255** of 6,677 CSVs (skips 6,422, ~96% fewer) - removes the hang/white screen; 0901-0908 -> 609, 0801-0908 -> 2,873, whole folder -> 6,677. |

| Type | Paths | Summary |
|---|---|---|
| modified | `src/Htxb.Host/NativeBridge.cs` | Added `CountFiles(path)` (COM `tools.CountFiles`) - fast recursive summary returning `{totalFiles,csvFiles,subfolders}` via `Directory.Enumerate*` (no file content read). Additive; other tools' behavior unchanged. |
| modified | `src/Htxb.LotExtractor/Resources/polyfill.js` | Show folder summary on the upload screen once a search folder is picked: modern `showDirectoryPicker` path calls `tools.CountFiles(path)`; legacy `#folder-input`/drag-drop path derives totals from picked `File` `webkitRelativePath`. Renders `In this folder: N total files, M CSV files across D slot subfolders.` under the selected path. |
| modified | `src/Htxb.LotExtractor/Htxb.LotExtractor.csproj` | Version `6.9.81.0` -> `6.9.82.0` (`<Version>` + `<FileVersion>`). |
| modified | `.data2/manifest.json` | `lot-extractor` version -> `6.9.82.0`. |
| release | `dist/` | Republished via `build.ps1`; `TTI_LotExtractor.exe` reads `6.9.82.0`. Verified `CountFiles` output against real `Ref Document (Slot1-24)`: 12,707 total files, 6,677 CSV files, 24 slot subfolders (matches requested example format). |

| Type | Paths | Summary |
|---|---|---|
| modified | `AGENTS.md` | Clarified the revision/version format rule (applies to ALL EXE UI builds): `V6.MM.DDR` -> numeric `Y.M.DDR.0`; V6 = year 2026, MM = 2-digit month (09 = September), DD = day (08), R = per-EXE running number of updates that day (resets to 1 each day). Example: `V6.09.081` = `6.9.81.0`, 2nd update same day = `6.9.82.0`. No EXE functionality changed, so no release/version bump. |

| modified | `AGENTS.md` | Added the default-directory rule to Architecture conventions: every EXE tool EXCEPT the Utilities Launcher (`TTI_Utils_Launcher`) reads a user-predefined default open/search directory from `configuration\default_dir.ini` (seed copied to `dist\configuration\` by `build.ps1`). Each line is `EXEname = "path"`, e.g. `TTI_LotExtractor = "D:\COBIS-II-632\COBIS-II\Document"`; the shared host uses it as the initial directory for the tool's folder/file pickers. The launcher is excluded. No EXE functionality changed, so no version bump. |
| modified | `AGENTS.md` | Added the UI version display rule to the version-bump section, covering ALL EXE tools' UI version formats: the version shown on each tool's UI must be aligned with the Tools & Utilities cover page version and follow the same `V6.MM.DDR` display format as the EXE version. The cover page reads each tool's header version (`.logo-tag` / `Version:`) at runtime, so the header and cover page must always match; keep the UI header version in sync whenever the EXE version is bumped. Corrected example: Lot Extractor should show `V6.08.081` / `V6.08.082` / `V6.08.083` (next updates `V6.08.082` and `V6.08.083`), not `V6.08.191`. Documentation-only; no EXE bump. |
| modified | `src/Htxb.LotExtractor/Resources/polyfill.js`, `src/Htxb.LotExtractor/Htxb.LotExtractor.csproj` (6.9.82.0 -> 6.9.83.0), `.data2/manifest.json`, `dist/TTI_LotExtractor.exe`, `dist/data2/` | Lot Extractor: converted the "Lot Keyword" filter from a fixed `<select>` (VC/All) into a free-text input that allows manual key-in, defaulting to `VC` (page's starts-with prefix match preserved; empty or 'All' = no filter). Publish as `V6.09.083` (6.9.83.0), republished single-file to `dist`. |
| modified | `src/Htxb.Host/NativeBridge.cs`, `src/Htxb.LotExtractor/Resources/polyfill.js`, `src/Htxb.LotExtractor/Htxb.LotExtractor.csproj` (6.9.83.0 -> 6.9.84.0), `.data2/manifest.json`, `dist/TTI_LotExtractor.exe`, `dist/data2/` | Fixed Lot Extractor WebView2 out-of-memory white screen (4+ GB) on large slot folders. Root cause: the page materialized every CSV as a File blob and kept full parsed rows in the V8 heap. Fix: new native scan (`NativeBridge.BeginScanLotCsvs`/`PollScanLotCsvs`) reads+parses CSVs on a background thread using an exact port of the page's `parseCSV`/`extractStatus`/`extractDateTime`/slot logic, returning only compact summaries (name/path/dtKey/dtCode/lot/slot/status/rowCount). Polyfill routes the modern picker flow through the native scan, `getFile` no longer loads file content into the renderer, and export re-reads tagged files from disk. Published as `V6.09.084` (6.9.84.0). |
| modified | `src/Htxb.LotExtractor/Resources/polyfill.js`, `dist/TTI_LotExtractor.exe` (rebuilt, version unchanged 6.9.84.0) | Lot Extractor: runtime override of the embedded page's `.logo-tag` header badge to `V6.09.084` so the EXE UI shows the version matching `manifest.json`/EXE (the HTML source still carries the stale `V6.08.191` and is off-limits). Display-only change - no version bump. Republished single-file to `dist`. |
| modified | `src/Htxb.Host/HostForm.cs`, `src/Htxb.LotExtractor/Resources/polyfill.js`, `src/Htxb.LotExtractor/Htxb.LotExtractor.csproj` (6.9.84.0 -> 6.9.85.0), `.data2/manifest.json`, `dist/TTI_LotExtractor.exe`, `dist/data2/manifest.json` | Blocked the WebView2 "Allow this site to view and copy files?" permission dialog from ever appearing inside the Lot Extractor EXE. Two layers: (1) HostForm subscribes to `PermissionRequested` and Denies `FileReadWrite` + `UnknownPermission`; (2) LotExtractor polyfill now always installs the `showDirectoryPicker` shim (dropped the native-API early-return, robustly overrides the property) so the folder picker always goes through the native bridge. Version bumped to `V6.09.085` (6.9.85.0) in csproj + manifest; republished single-file to `dist`. |

| modified | `src/Htxb.Launcher/PcInfo.cs` (created), `src/Htxb.Launcher/MainForm.cs`, `src/Htxb.Launcher/Htxb.Launcher.csproj` (6.8.214.0 -> 6.9.81.0 + `System.Management` 8.0.0), `.data2/manifest.json`, `configuration/pc_info.ini` (created seed) | New hidden task on the Utilities Launcher: on every launch, collect the machine''s unique hardware identifiers (system/motherboard/BIOS/CPU/RAM modules/physical disks with part & serial numbers) via WMI into `configuration\pc_info.ini` (UTF-8 no BOM). Runs detached on a background STA thread; never surfaces UI/errors. Fixed `Win32_DiskDrive` rows failing on the lazy `Index` property by tolerating per-property read failures, so `[Disks]` now captures the disk. Version bumped to `V6.09.081` (6.9.81.0); republished single-file to `dist`. |


| modified | `src/Htxb.LotExtractor/Resources/polyfill.js`, `src/Htxb.LotExtractor/Htxb.LotExtractor.csproj` (6.9.85.0 -> 6.9.86.0), `.data2/manifest.json`, `dist/TTI_LotExtractor.exe`, `dist/data2/manifest.json` | Fixed Lot Extractor picking a folder but never proceeding (stuck on the ''select folder to upload'' file dialog). Root cause: after the permission-dialog fix, `showDirectoryPicker` is now always defined (returns the shim VNode), so the page's `selectInputFolder()` walks via `readDirHandleRecursive()` -> `handle.values()`; but the polyfill's VNode had no real `values()` (an override delegated to a never-defined original, throwing a TypeError that the page caught and fell back to the file input). Added a real `VNode.prototype.values` that enumerates native children through `tools.ListDirectory`. Also fixed the 1-day date filter being slow: `values()` now yields nothing when `__ttiLotRoot` is set (pick flow), so the page skips its full-tree walk (25 ListDirectory bridge round-trips + 6,677 getFile calls whose result was discarded anyway) and goes straight to the native scan, which does its own date filtering (~0.4 s native tree walk + parse of only the in-range CSVs). Version bumped to `V6.09.086` (6.9.86.0); republished single-file to `dist`. |


| modified | `src/Htxb.Host/NativeBridge.cs`, `src/Htxb.LotExtractor/Resources/polyfill.js`, `src/Htxb.LotExtractor/Htxb.LotExtractor.csproj` (6.9.86.0 -> 6.9.87.0), `.data2/manifest.json`, `dist/TTI_LotExtractor.exe`, `dist/data2/manifest.json` | Fixed Lot Extractor wrongly showing 'No CSV files found in the selected folder.' when a narrow date range had no real data. Root cause: `ScanLotCsvsCore`'s `CsvDateInRange` returned true for any file with no timestamp in its name, so a stray empty `null.csv` in the Document root matched every date range, inflating `total` and—when it was the only in-range file (e.g. default 'today' = 2026-09-08, which has no real data)—producing an empty `result` that the polyfill misreported as 'no files'. Fix: `CsvDateInRange` now only admits a no-date file to a full (empty range) scan; `runNativeLotScan` surfaces native `st.error` instead of masking it and shows a range-specific message ('Try a wider Date From / Date To') when a filtered scan finds nothing. Verified via the live NativeBridge against the reference folder: '2026-09-08' -> 0 files (was a phantom 1), '2026-09-01' -> 96 files, full scan -> 6,677. Version bumped to `V6.09.087` (6.9.87.0); republished single-file to `dist`. |

| modified | `src/Htxb.Host/NativeBridge.cs`, `src/Htxb.LotExtractor/Resources/polyfill.js`, `dist/TTI_LotExtractor.exe` (rebuilt, version unchanged 6.9.87.0) | Removed the temporary diagnostic instrumentation added to diagnose the Lot Extractor 'no files found' report (`WriteDiagnostic` native helper + JS-side range/scan logging). Diagnostic log from the user's real EXE run proved the scan is correct: default range 2026-09-07..2026-09-08 returned total=68, resultItems=68, so the tool hands 68 files to the selection screen and no error toast triggers. Earlier 'no files' reports were date ranges past the data (data ends 2026-09-07) or the fixed null.csv phantom. Clean republish to `dist`; no functional change, no version bump. |

| modified | `src/Htxb.Host/NativeBridge.cs` (diag removed, unchanged logic), `src/Htxb.LotExtractor/Resources/polyfill.js`, `src/Htxb.LotExtractor/Htxb.LotExtractor.csproj` (6.9.87.0 -> 6.9.91.0), `.data2/manifest.json`, `dist/TTI_LotExtractor.exe`, `dist/data2/manifest.json` | Fixed Lot Extractor always showing 'No CSV files found for the selected date range' even when the native scan returned files. Root cause: polyfill double-parsed the poll result - `st` is already the object from `JSON.parse(PollScanLotCsvs())`, so `st.result` is an Array, and calling `JSON.parse(st.result)` coerced it to `'[object Object],...'` and threw, leaving summaries empty (native ''scan done'' log showed resultLen=41279 / total=140 while JS read resultItems=0). Fix: `var summaries = Array.isArray(st.result) ? st.result : [];`. Package had been unusable since the native scan shipped in 6.9.84.0. Removed all temporary diagnostics. Version bumped to `V6.09.091` (6.9.91.0); republished single-file to `dist`. |

| modified | `src/Htxb.Launcher/Resources/polyfill.js`, `src/Htxb.Launcher/Htxb.Launcher.csproj` (6.9.81.0 -> 6.9.91.0), `.data2/manifest.json`, `dist/TTI_Utils_Launcher.exe`, `dist/data2/manifest.json` | Fixed the Utilities Launcher cover page showing version labels with a missing leading zero for sub-day-10 versions: `fmt()` rendered the numeric DDR as-is (`6.9.91.0` -> `V6.09.91`) while the tool's own header badge showed `V6.09.091`. `fmt()` now left-pads the DDR segment to 3 digits (`91` -> `091`) so the cover page always matches each tool's `V6.MM.DDR` badge (and the Lot Extractor cover entry now reads `V6.09.091`). Version bumped to `V6.09.091` (6.9.91.0); republished single-file to `dist`. |

---

## 2026-09-19 — Docs: moved repo paths from old-PC `E:\` to `D:\`

| Type | Paths | Summary |
|---|---|---|
| modified | `AGENTS.md`, `PHASE2_KICKOFF.md` | Updated all curated doc paths from old-PC `E:\_TTI-Dev\...` to `D:\_TTI-Dev\...` (repo root, HTML source, Phase 1 + pilot refs, session root). `src/*.csproj` need no change (relative `..\..\..\..\proj01 Main Page\...`). `bin/obj/dist` still contain stale `E:\` strings but are gitignored build artefacts that regenerate on next build. Doc-only; no EXE functionality changed, no version bump. |

---

## 2026-09-19 — Temp Uniformity: GraphTec GL220 CSV input in the same analysis flow — V6.09.191

Studied `VSProj_02_Graphtec\NewGraphTec csv\260919 2202 140C Emthy Load.csv` (GRAPHTEC GL220,
1 s sampling, 49,892 rows 2026-09-18 21:09:58 → 2026-09-19 11:01:29, CH1–CH10 TEMP TC_K degC;
CH1–CH9 100% valid, CH10 open — 8 valid / 49,884 `BURNOUT`). The page only accepted
`.xlsx/.dad/_Rpt.xlsx`, so the CSV is now parsed into the page's own
`allHeaders/allRows/numericCols` shape and flows through the unchanged
populateAxisSelectors → renderGraph → uniformity/range/reference/export pipeline.

| Type | Paths | Summary |
|---|---|---|
| modified | `src/Htxb.TempUniformity/Resources/polyfill.js` | GraphTec CSV support via polyfill (canonical HTML untouched): `Data`-marker header detect, `Date&Time`(+ms) timestamps, `+`-sign/`BURNOUT` handling, capture-phase drop intercept + `handleFile` wrap for `.csv`, csv-aware Export/Print basenames, `<50%`-valid channels (open CH10) deselected by default, upload text + `.version-badge` → `V6.09.191` sync. Existing XLSX offline-export shim unchanged. Parser port verified in PowerShell against the real file: 49,892 rows, 0 skipped. |
| modified | `src/Htxb.TempUniformity/Htxb.TempUniformity.csproj` | Version `6.8.215.0` → `6.9.191.0` (`<Version>` + `<FileVersion>`); first TempUniformity update on 2026-09-19. |
| modified | `.data2/manifest.json` | `temp-uniformity` version → `6.9.191.0`; `generated` → 2026-09-19. Other six EXEs untouched. |
| release | `dist/` | Published (SDK 8.0.425; `build.ps1`'s slnx build step not readable by SDK 8, so per-project `dotnet build` + identical single-file publish loop used instead). All 7 EXEs < 5 MB; `TTI_TempUniformity.exe` = 1.77 MB, FileVersion `6.9.191.0`, new polyfill strings verified embedded, smoke-tested alive with correct window title. `dist\data2\manifest.json` temp-uniformity → `6.9.191.0`. |

---

## 2026-09-19 — Temp Uniformity: stack-safe min/max for large CSVs + data-table default — V6.09.192

Loading the 49,892-row GraphTec CSV failed with `Failed to parse CSV file: Maximum call stack
size exceeded`. Root cause is in the canonical page, not the parser: `updateMetaSummary`
(`Math.min/max.apply(null, allTemps)`, ~450k values) and `renderReferenceTable`
(`Math.min/max.apply(null, vals)`, ~50k values) spread giant arrays through `.apply()`,
whose argument list overflows the call stack past a few thousand elements — never triggered
by small `.dad` files. Fixed polyfill-side with behaviour-identical overrides using an
iterative min/max helper; the data table (~500k cells) is also unchecked by default for
files over 5,000 rows (chart, stats, export unaffected; user can toggle it back on).

| Type | Paths | Summary |
|---|---|---|
| modified | `src/Htxb.TempUniformity/Resources/polyfill.js` | New Part C `installLargeDataFix()`: overrides `updateMetaSummary` / `renderReferenceTable` / `renderSummaryTable` with iterative `__ttiMinMax` (en-dash subtitle reproduced via `\u2013`); data table auto-hidden for >5,000-row files. `EXE_VERSION` → `V6.09.192`. |
| modified | `src/Htxb.TempUniformity/Htxb.TempUniformity.csproj` | Version `6.9.191.0` → `6.9.192.0`; second TempUniformity update on 2026-09-19. |
| modified | `.data2/manifest.json` | `temp-uniformity` version → `6.9.192.0`. Other six EXEs untouched. |
| release | `dist/` | Targeted republish of `Htxb.TempUniformity` only (single-file, framework-dependent); `TTI_TempUniformity.exe` FileVersion `6.9.192.0`, fix markers verified embedded, smoke-tested alive with correct window title; `dist\data2\manifest.json` synced. |

---

## 2026-09-19 — Temp Uniformity: export data interval selector (10s–1min) — V6.09.193

1-second logging makes huge workbooks (49,892 rows × 10 channels). Export now offers a
data-interval selector next to the Export button: every row / 10 sec / 15 sec / 30 sec /
1 min. Files over 5,000 rows auto-default to 10s (stored preference wins once the user
picks); the export swaps in time-downsampled rows under a suffixed name (`file_10s.xlsx`,
…) and delegates to the page's own exportXLSX, so bridge/browser paths are reused.
Time-based filtering (first + last rows always kept). Validated in PowerShell against the
real GraphTec file: 10s → 4,991 rows (10%), 15s → 3,328, 30s → 1,665, 1min → 833 (1.7%).

| Type | Paths | Summary |
|---|---|---|
| modified | `src/Htxb.TempUniformity/Resources/polyfill.js` | New Part D: `EXPORT_STEPS`, `ensureExportUI()` selector injection, `autoExportDefault()`, `downsampleRows()`; export wrapper downsamples + suffixes when interval > 1s. `EXE_VERSION` → `V6.09.193`. |
| modified | `src/Htxb.TempUniformity/Htxb.TempUniformity.csproj` | Version `6.9.192.0` → `6.9.193.0`; third TempUniformity update on 2026-09-19. |
| modified | `.data2/manifest.json` | `temp-uniformity` version → `6.9.193.0`. Other six EXEs untouched. |
| release | `dist/` | Targeted republish of `Htxb.TempUniformity` only; `TTI_TempUniformity.exe` FileVersion `6.9.193.0`, markers verified embedded, smoke-tested alive; `dist\data2\manifest.json` synced. |

---

## 2026-09-19 — Temp Uniformity: GraphTec binary (.gbd) input — V6.09.194

Same-session binary twin of the CSV (`NewGraphTec GBD\260918-210955_UG.GBD`, GL220, 1 s,
49,892 records, Trigger 2026-09-18 21:09:58). Decoded layout: 8,192-byte ASCII header
(`Counts`/`Sample`/`Order`/`Trigger`) + records of big-endian int16 per Order token
(CH1–CH10 + Alarm words); TEMP = raw/10, raw ≥ 32000 = open sensor (CH10 = 32765,
49,884× = CSV `BURNOUT`). Cross-validated every record against the CSV: 0 timestamp
mismatches, 0 value mismatches (max abs diff 0), CH10 sentinel match 49,884/49,884.
Timestamps synthesised as Trigger + i·Sample; rows flow through the shared
`finishParsedRows` pipeline (CSV tail refactored into it), so the large-file min/max
fix, data-table default, and export-interval selector all apply to GBD unchanged.
Upload text + Browse accept filter extended to `.gbd`; drop/`handleFile` route
`.gbd` → ArrayBuffer parser; Export/Print basenames strip `.gbd`.

| Type | Paths | Summary |
|---|---|---|
| modified | `src/Htxb.TempUniformity/Resources/polyfill.js` | New Part E `parseGbdFile`/`handleGbdFile` + shared `finishParsedRows` (CSV tail refactored to call it); routing/export/upload-text `.gbd` coverage. `EXE_VERSION` → `V6.09.194`. |
| modified | `src/Htxb.TempUniformity/Htxb.TempUniformity.csproj` | Version `6.9.193.0` → `6.9.194.0`; fourth TempUniformity update on 2026-09-19. |
| modified | `.data2/manifest.json` | `temp-uniformity` version → `6.9.194.0`. Other six EXEs untouched. |
| release | `dist/` | Targeted republish of `Htxb.TempUniformity` only; `TTI_TempUniformity.exe` FileVersion `6.9.194.0`, markers verified embedded, smoke-tested alive; `dist\data2\manifest.json` synced. |

---

## 2026-09-19 — Temp Uniformity: instrument gallery below upload card — V6.09.195

Upload screen now shows the supported loggers' pictures side by side under the
drag-drop area (white cards with captions, hidden with the upload screen once a
file loads). Images already in `dist\data2\assets\instrument_pic\` were staged
into the `.data2` seed so full `build.ps1` runs keep them; serving needs no host
change (`WebHost` unescapes `%20` and maps `/assets/*` → `data2\assets\*`,
`.png` → `image/png` — same path as the DUT-package images).

| Type | Paths | Summary |
|---|---|---|
| created | `.data2/assets/instrument_pic/GRAPHTEC GL220.png`, `.data2/assets/instrument_pic/Yokogawa FX1000.png` | Staged from `dist\` into the repo seed (53,359 + 193,022 bytes; 252×183 and 470×470 px verified). |
| modified | `src/Htxb.TempUniformity/Resources/polyfill.js` | New Part F `injectInstrumentGallery()` (idempotent, `onerror` hides card if asset missing). `EXE_VERSION` → `V6.09.195`. |
| modified | `src/Htxb.TempUniformity/Htxb.TempUniformity.csproj` | Version `6.9.194.0` → `6.9.195.0`; fifth TempUniformity update on 2026-09-19. |
| modified | `.data2/manifest.json` | `temp-uniformity` version → `6.9.195.0`. Other six EXEs untouched. |
| release | `dist/` | Targeted republish of `Htxb.TempUniformity` only; `TTI_TempUniformity.exe` FileVersion `6.9.195.0`, markers verified embedded, smoke-tested alive; `dist\data2\manifest.json` synced (`instrument_pic` already present in `dist\data2\assets\`). |

---

## 2026-09-20 — BiDF Analysis: stop `dut_package` images being wiped on republish — V6.09.201

`TTI_BiDFAnalysis.exe` was reported as auto-deleting custom DUT package images from
`data2\assets\dut_package\`. Traced and cleared the EXE: `NativeBridge.cs` has no
delete/remove API at all, `WebHost.cs`/`Data2Loader.cs` are read-only, the page only
reads `images/DUT Packages/${pkg}.png` (line 1808) and `polyfill.js` only patches
XLSX export + `(NEW)` columns — nothing in the runtime writes or deletes there.
The real deleter is the deploy flow: `build.ps1` wipes `dist\` clean on every run and
re-copies the repo `.data2` seed, so `HiP247-4.png` (16,856 bytes, added only at the
deployment site `D:\COBIS-II-632\TTI_DataProcess\_TTI_Utilities\...`) was lost on the
next publish + copy. Same lesson as the 09-19 `instrument_pic` staging.

| Type | Paths | Summary |
|---|---|---|
| created | `.data2/assets/dut_package/HiP247-4.png` | Staged the site-added package image into the repo seed (SHA256-verified identical to the deployed copy). |
| modified | `src/Htxb.BiDFAnalysis/Htxb.BiDFAnalysis.csproj` | Version `6.8.282.0` → `6.9.201.0` (`<Version>` + `<FileVersion>`); first BiDF update on 2026-09-20 (new package illustration is user-visible). |
| modified | `.data2/manifest.json` | `bidf-analysis` version → `6.9.201.0`; `generated` → 2026-09-20. Other six EXEs untouched. |
| modified | `build.ps1` | `dist\data2\assets\dut_package\` is now backed up before the `dist\` wipe and restored after the fresh `.data2` copy (repo files win; only files missing from the seed are restored), so site-added images survive a republish. Build step also changed from `dotnet build TTI_HtxbSuite.slnx` to per-project `dotnet build` — the `.slnx` format is unreadable by the .NET 8 SDK (8.0.425) on this machine (needs SDK 10); per-project build works on both. Build-script-only; no EXE bump. |
| modified | `AGENTS.md` | New DUT package image rule under Architecture conventions: `.data2\assets\dut_package\` is the canonical source, `build.ps1` preserves site-added files, no EXE writes/deletes there. Doc-only; no version bump. |
| release | `dist/` | Full `build.ps1` run on SDK 8.0.425: all 7 EXEs < 5 MB; `TTI_BiDFAnalysis.exe` FileVersion `6.9.201.0`; `dist\data2\assets\dut_package\` holds both `HiP247 Long Leads.png` + `HiP247-4.png`. |

---

## 2026-09-28 — Launcher: Barcode Generator bottom card (Code 128 → Downloads) — V6.09.281

New bottom item on the Tools & Utilities cover page for scanner data entry: a
"Barcode Generator" card opens an overlay where the user types the barcode string
(e.g. `P_TM_BRF9_B_05_4_32`), sees a live Code 128 preview (black bars on white),
and exports `<content>.png` straight to the Windows Downloads folder. Input is
capped at 50 characters; empty / over-long / illegal characters (non-printable
ASCII, filename-illegal `<>:"/\|?*`, leading/trailing space-or-dot) are reported
with the offending characters named. The PNG fit is enforced at ≤ 50 KB (module
width auto-shrinks 3→1; typical output ≈ 5 KB). Canonical HTML untouched — all UI
is injected by the launcher polyfill; JsBarcode v3.11.6 is served from shared
`data2\lib` like the other libs.

| Type | Paths | Summary |
|---|---|---|
| created | `.data2/lib/jsbarcode.all.min.js` | JsBarcode v3.11.6 (60,817 bytes, MIT) downloaded from jsdelivr for the launcher's Code 128 rendering. |
| modified | `src/Htxb.Host/NativeBridge.cs` | New additive `GetDownloadsFolder()` (user profile `Downloads\`, created if missing, `""` on failure); existing tools' behaviour unchanged. |
| modified | `src/Htxb.Launcher/Resources/polyfill.js` | Bottom "Barcode Generator" card (own `tool-meta` version line, synced from manifest) + overlay: live preview, `bcValidate` (50-char cap + illegal-char report), `bcRender` (black-on-white Code 128, 50 KB cap), export via `GetDownloadsFolder`/`JoinPath`/`WriteFileBase64` with saved-path confirmation; Enter=export, Esc/backdrop=close. |
| modified | `src/Htxb.Launcher/Htxb.Launcher.csproj` | Version `6.9.91.0` → `6.9.281.0` (`<Version>` + `<FileVersion>`); first launcher update on 2026-09-28. |
| modified | `.data2/manifest.json` | `launcher` version → `6.9.281.0`; `generated` → 2026-09-28; new `libs` entry `jsbarcode.all.min.js` 3.11.6 usedBy launcher. Other six EXEs untouched. |
| release | `dist/` | Full `build.ps1` run: all 7 EXEs < 5 MB; `TTI_Utils_Launcher.exe` FileVersion `6.9.281.0`, barcode/`GetDownloadsFolder` markers verified embedded, smoke-tested alive with correct window title. Feature verified end-to-end in headless Chromium against the real polyfill+JsBarcode: card/version/open/close, example string → 752×118 canvas → valid PNG 4.7 KB saved as `P_TM_BRF9_B_05_4_32.png`, illegal/over-long/50-char-boundary/clear all behave. |

---

## 2026-09-28 — Launcher: barcode text 2× larger — V6.09.282

| Type | Paths | Summary |
|---|---|---|
| modified | `src/Htxb.Launcher/Resources/polyfill.js` | JsBarcode `fontSize` 16 → 32 so the human-readable text below the bars renders at double size (bars unchanged). |
| modified | `src/Htxb.Launcher/Htxb.Launcher.csproj` | Version `6.9.281.0` → `6.9.282.0`; second launcher update on 2026-09-28. |
| modified | `.data2/manifest.json` | `launcher` version → `6.9.282.0`. Other six EXEs untouched. |
| release | `dist/` | Full `build.ps1` run: all 7 EXEs < 5 MB; `TTI_Utils_Launcher.exe` FileVersion `6.9.282.0`, smoke-tested alive. Re-verified in headless Chromium: canvas 752×134 (text area doubled), valid PNG 8.2 KB (still ≪ 50 KB), all validation paths unchanged. |

---

## 2026-09-28 — Launcher: Windows-style back-to-top button — V6.09.283

| Type | Paths | Summary |
|---|---|---|
| modified | `src/Htxb.Launcher/Resources/polyfill.js` | New `initBackToTop()`: fixed 40×40 Windows-style button (white, 4 px radius, border, shadow, chevron-up SVG, accent border on hover) pinned bottom-right; hidden until scrolled past 200 px, smooth-scrolls to top on click (fallback instant scroll). |
| modified | `src/Htxb.Launcher/Htxb.Launcher.csproj` | Version `6.9.282.0` → `6.9.283.0`; third launcher update on 2026-09-28. |
| modified | `.data2/manifest.json` | `launcher` version → `6.9.283.0`. Other six EXEs untouched. |
| release | `dist/` | Full `build.ps1` run: all 7 EXEs < 5 MB; `TTI_Utils_Launcher.exe` FileVersion `6.9.283.0`, marker verified embedded, smoke-tested alive. Verified in headless Chromium (460×780): hidden at top, appears after scrolling, click issues scroll-to-top, hides again at top. |

---

## 2026-09-28 — New tool: standalone Barcode Generator EXE — V6.09.281

Same generator as the launcher's bottom card, now also a dedicated window
(`TTI_BarcodeGenerator.exe`): type the string, live black-on-white Code 128
preview (2× text), Export writes `<content>.png` (≤ 50 KB) to Downloads, same
50-char cap + illegal-character reporting, header version badge synced from
manifest. New repo-owned page (`Resources/index.html` + `polyfill.js`, following
the wrapper shape; JsBarcode served from shared `data2\lib`, no new lib). The
launcher's in-place overlay is kept as-is.

| Type | Paths | Summary |
|---|---|---|
| created | `src/Htxb.BarcodeGenerator/` | New wrapper WinExe (`net8.0-windows`): `Htxb.BarcodeGenerator.csproj` v6.9.281.0, `Program.cs` (STA), `MainForm.cs` (title "TTI Barcode Generator", tool-id `barcode-generator`, 660×640), `Resources/index.html` (page markup), `Resources/polyfill.js` (validate/render/export/version-sync), `Resources/Favicon.ico`. |
| modified | `build.ps1`, `TTI_HtxbSuite.slnx` | Registered `Htxb.BarcodeGenerator → TTI_BarcodeGenerator.exe`. |
| modified | `.data2/manifest.json` | New `barcode-generator` entry (v6.9.281.0); `generated` stays 2026-09-28. Other tools untouched. |
| modified | `configuration/tools.ini` | New empty `[barcode-generator]` section (tool uses no dialogs; Downloads target is automatic). |
| release | `dist/` | Full `build.ps1` run: now 8 EXEs, all < 5 MB (`TTI_BarcodeGenerator.exe` 1.69 MB, FileVersion `6.9.281.0`), smoke-tested alive with correct title. Page verified in headless Chromium: live preview, valid PNG export (8.2 KB), all validation paths, version badge `V6.09.281`. |

---

## 2026-10-08 — Launcher: Auto-Hide / Edge-Docking after load — V6.10.081

The launcher window now slides off-screen to the left edge once the initial
page load completes (first `NavigationCompleted`, i.e. after the loading
progress), leaving a 10 px visible strip; touching that strip with the mouse
slides the window back out. Poll-based (150 ms): hides after the mouse stays
outside the window for 900 ms (2.5 s grace after load so the window is seen
first); shows when the cursor enters the strip hotspot or the window takes
focus. Sliding animates at ~48 px / 12 ms tick; dragging the window away from
the edge suspends hiding until it is docked back. Opt-in via the shared host
so no other tool is affected.

| Type | Paths | Summary |
|---|---|---|
| modified | `src/Htxb.Host/HostOptions.cs` | New opt-in `EnableEdgeAutoHide` flag (launcher-style windows only). |
| modified | `src/Htxb.Host/HostForm.cs` | New edge auto-hide controller (`ArmEdgeAutoHide` on first `NavigationCompleted`; `EdgePoll`/`StartEdgeSlide`/`EdgeSlideTick`/`StopEdgeSlide`; timers released in `OnFormClosed`). Shared-code change but only the launcher opts in, so only the launcher is bumped. |
| modified | `src/Htxb.Launcher/MainForm.cs` | Sets `EnableEdgeAutoHide = true`. |
| modified | `src/Htxb.Launcher/Htxb.Launcher.csproj` | Version `6.9.283.0` → `6.10.81.0` (`<Version>` + `<FileVersion>`); first launcher update on 2026-10-08. |
| modified | `.data2/manifest.json` | `launcher` version → `6.10.81.0`; `generated` → 2026-10-08. Other seven EXEs untouched. |
| release | `dist/` | Full `build.ps1` run: all 8 EXEs < 5 MB (`TTI_Utils_Launcher.exe` 2.03 MB, FileVersion `6.10.81.0`; other seven FileVersions verified unchanged), smoke-tested alive with correct title. |

---

## 2026-10-11 — Launcher auto-hide reworked: corner tab instead of left-wall strip — V6.10.111

Per user choice (small corner tab over a horizontal strip): the launcher no longer
slides off the left wall. Once loading completes it collapses into a 170×72
`» TTI Tools` tab pinned at the top-left corner after the same triggers (2.5 s
grace + 0.9 s mouse-out dwell, unfocused); hovering/clicking the tab or focusing
the window expands it back to its remembered size/location. WebView hides while
collapsed (no squeezed-page reflow); `NativeBridge.GetOwnerWidth` reports the
remembered full width while collapsed so launched tools never land under the
re-expanded launcher. Moving the window away from the corner suspends hiding.

| Type | Paths | Summary |
|---|---|---|
| modified | `src/Htxb.Host/HostForm.cs` | Replaced slide-off machinery with collapse/expand: `CollapseEdgeTab` (remembers size/location/min-size, lowers minimum, hides WebView, shows tab at working-area corner), `ExpandEdgeTab` (restores all), `EnsureEdgeTab` (bordered `» TTI Tools` panel, click-to-expand), `LaunchReserveWidth`; poll logic retargeted to the corner + tab hotspot. A temporary `TTI_HTXB_NOFOCUS` test hook verified the real paths end-to-end then was fully removed (zero matches). |
| modified | `src/Htxb.Host/NativeBridge.cs` | `GetOwnerWidth` prefers `HostForm.LaunchReserveWidth` when the owner is a `HostForm`. |
| modified | `src/Htxb.Host/HostOptions.cs` | `EnableEdgeAutoHide` doc updated to the corner-tab behavior. |
| modified | `src/Htxb.Launcher/Htxb.Launcher.csproj` | Version `6.10.82.0` → `6.10.111.0`; first launcher update on 2026-10-11 (sole opt-in, only EXE bumped). |
| modified | `.data2/manifest.json` | `launcher` → `6.10.111.0`; `generated` → 2026-10-11. Other seven untouched. |
| modified | `AGENTS.md` | New launcher auto-hide rule note (corner tab, triggers, shared-code ownership). Doc-only. |
| release | `dist/` | Full `build.ps1` run: all 8 EXEs < 5 MB; launcher FileVersion `6.10.111.0` (others verified unchanged). Verified live via cursor-driving + `GetWindowRect`: dwell → `L=0 T=0 W=170 H=72`; mouse onto tab → `L=0 T=0 W=460 H=780`, alive (focus path untestable in this session — foreground cannot be stolen here — but shares the same `ExpandEdgeTab`). Final smoke: alive with correct title. |

---

## 2026-10-11 — Launcher tab auto-hide never fired while focused — V6.10.112

Reported: the corner tab "holds the same state, never changes". Root cause in
`HostForm.EdgePoll`: collapse required `!ContainsFocus`, but the launcher commonly
stays the foreground window while the mouse is elsewhere (e.g. mouse moved off
without clicking another window) — so the dwell never started. Fix: focus is no
longer consulted for hiding — mouse-out dwell alone collapses. To avoid the two
regressions that dropping focus would cause: (1) collapse is suppressed while the
page reports busy (new `window.tools.SetBusy`, wired to the barcode overlay
open/close) or a native modal dialog is up (`NativeBridge.RunDialog` now bumps
`HostForm.AutoHideSuppress`); (2) expand on explicit activation (`Activated`
event) plus a post-expand grace restart, so Alt+Tab/taskbar returns don't
instantly re-collapse, and no collapse↔expand loop (collapsed window keeps
whatever focus it had, but focus alone no longer expands).

| Type | Paths | Summary |
|---|---|---|
| modified | `src/Htxb.Host/HostForm.cs` | `EdgePoll` drops `ContainsFocus` (hide = mouse-out dwell + not blocked); new `EdgeActivated` handler (expand if collapsed, else restart grace); `ExpandEdgeTab` restarts grace; new `PageBusy` / `AutoHideSuppress` / `AutoHideBlocked`; `PageBusy` reset on `NavigationCompleted`. |
| modified | `src/Htxb.Host/NativeBridge.cs` | New `SetBusy(bool)` bridge method; `RunDialog` suppresses auto-hide while any native dialog is open (Interlocked counter, balanced in `finally`). |
| modified | `src/Htxb.Launcher/Resources/polyfill.js` | Barcode overlay `open()`/`close()` report `tools.SetBusy(true/false)` (guarded). |
| modified | `src/Htxb.Host/HostOptions.cs`, `AGENTS.md` | Doc wording corrected (focus not required; busy/dialog suppression). Doc-only. |
| modified | `src/Htxb.Launcher/Htxb.Launcher.csproj` | Version `6.10.111.0` → `6.10.112.0`; second launcher update on 2026-10-11 (sole opt-in, only EXE bumped). |
| modified | `.data2/manifest.json` | `launcher` → `6.10.112.0`. Other seven untouched. |
| release | `dist/` | Full `build.ps1` run: all 8 EXEs < 5 MB; other seven FileVersions verified unchanged. Verified live with the production binary in the exact reported scenario (launcher keeps focus the whole time): mouse away → `L=0 T=0 W=170 H=72`; mouse onto tab → `L=0 T=0 W=460 H=780`, alive. |

---

## 2026-10-11 — Launcher expands to full screen height — V6.10.113

Expanding the corner tab now stretches the launcher to the full working-area
height at the top-left corner (width remembered, e.g. 460) instead of restoring
the remembered short size — the tab never comes back as a short window.

| Type | Paths | Summary |
|---|---|---|
| modified | `src/Htxb.Host/HostForm.cs` | `ExpandEdgeTab` sizes to `(remembered width × working-area height)` at the working-area corner (exact-remember fallback kept); sole opt-in is the launcher. |
| modified | `src/Htxb.Launcher/Htxb.Launcher.csproj` | Version `6.10.112.0` → `6.10.113.0`; third launcher update on 2026-10-11 (only EXE bumped). |
| modified | `.data2/manifest.json` | `launcher` → `6.10.113.0`. Other seven untouched. |
| modified | `AGENTS.md` | Auto-hide note updated (expand = full working-area height). Doc-only. |
| release | `dist/` | Full `build.ps1` run: all 8 EXEs < 5 MB. Verified live (working area 1920×1040): dwell → `0,0 170×72`; hotspot → `0,0 460×1040`, alive. |

---

## 2026-10-15 — Recipe Translator: TestTime(H) → TestTime(Min) — V6.10.151

New `.zadz` recipes (`VSProj_Recipe 261014`: BRF9/BRI9/BRJ7) carry `TestTime(Min)`
(minutes, e.g. `'2'`); the page hardcodes `TestTime(H)` in its header array, table
builder and xlsx export, so new files showed/emitted an empty TestTime column.
Polyfill-side fix (canonical HTML untouched): relabel `headerColumns[2]` to
`TestTime(Min)` (dashboard + export follow, both reference the same array) and wrap
`buildTableData` to backfill `ap['TestTime(H)']` from `TestTime(Min)` when H is
missing. Only the legacy key is ever backfilled, never the reverse — a later zadz
download (xlsx-mode only) therefore serializes exactly what was loaded, with no
bogus key a machine could misread as hours. Values pass through untouched: no
silent hour↔minute scaling (legacy hour-vintage files keep original numbers under
the new label). Old H-only files, empty-string and numeric-zero edges verified.

| Type | Paths | Summary |
|---|---|---|
| modified | `src/Htxb.RecipeTranslator/Resources/polyfill.js` | New TestTime shim section (header relabel + `buildTableData` wrapper + fill helper, lazy-installed pre-bridge); `EXE_VERSION` `V6.10.091` → `V6.10.151`. |
| modified | `src/Htxb.RecipeTranslator/Htxb.RecipeTranslator.csproj` | Version `6.10.91.0` → `6.10.151.0`; first RecipeTranslator update on 2026-10-15 (only EXE bumped). |
| modified | `.data2/manifest.json` | `recipe-translator` → `6.10.151.0`; `generated` → 2026-10-15. Other seven untouched. |
| release | `dist/` | Full `build.ps1` run: all 8 EXEs < 5 MB; `TTI_RecipeTranslator.exe` FileVersion `6.10.151.0`, smoke-tested alive with correct title. Verified in headless Chromium against the real polyfill: header `TestTime(Min)`, badge `V6.10.151`, new-vintage Min→H backfill, old-vintage untouched with no Min invented, numeric-zero and empty-string edges behave. |

---

## 2026-10-12 — Launcher hides its taskbar button; minimize docks to tab — V6.10.121

The resident launcher no longer occupies a taskbar button: new opt-in
`HostOptions.HideTaskbarIcon` (launcher only) sets `ShowInTaskbar = false`
before handle creation (no handle recreation, WebView2-safe; Alt+Tab still
lists it). Since a button-less window stranded by minimize has no way back
except Alt+Tab, minimizing now docks it to the corner tab instead (`OnResize`
intercept → restore + `CollapseEdgeTab`).

| Type | Paths | Summary |
|---|---|---|
| modified | `src/Htxb.Host/HostOptions.cs` | New `HideTaskbarIcon` flag. |
| modified | `src/Htxb.Host/HostForm.cs` | `ShowInTaskbar = !options.HideTaskbarIcon` in ctor; `OnResize` minimize→tab intercept; `ExpandEdgeTab` hardened to set `Size` before raising `MinimumSize` (raising first would clamp the tab to the minimum if the `Size` set ever failed to follow — no observable change, same end state). |
| modified | `src/Htxb.Launcher/MainForm.cs` | Sets `HideTaskbarIcon = true`. |
| modified | `src/Htxb.Launcher/Htxb.Launcher.csproj` | Version `6.10.114.0` → `6.10.121.0`; first launcher update on 2026-10-12 (sole opt-in, only EXE bumped; later reorder rebuild kept the version — no functional change). |
| modified | `.data2/manifest.json` | `launcher` → `6.10.121.0`; `generated` → 2026-10-12. Other seven untouched. |
| release | `dist/` | Full `build.ps1` run: all 8 EXEs < 5 MB. Verified live via PID-enumerated handles (note: `Process.MainWindowHandle` is always 0 for a button-less window, so all checks use `EnumWindows`+`GetWindowRect`): no `WS_EX_APPWINDOW` bit on the launcher (LotExtractor control still has it); dwell → `170×72` tab; minimize → tab (not stranded); hotspot → `460×1040` full height. One transient `380×300` sample during a chaotic zombie-cleanup run never reproduced across three clean runs incl. per-second trajectory (stable `460×1040` → `170×72`); the `Size`-before-`MinimumSize` reorder above removes even the theoretical path to it. Test note: `CloseMainWindow` cannot work on this window (needs a taskbar handle); close via its X button. |

---

## 2026-10-11 — Launcher starts at full screen height — V6.10.114

The launcher window now opens at full working-area height on first start
(`MainForm` sizes from `Screen.PrimaryScreen.WorkingArea`, fallback 780),
matching the full-height expand. Also fixed the stale slide-off comment.

| Type | Paths | Summary |
|---|---|---|
| modified | `src/Htxb.Launcher/MainForm.cs` | `WindowSize` height from new `FullWorkingAreaHeight()` (fallbacks keep 780); stale comment corrected. |
| modified | `src/Htxb.Launcher/Htxb.Launcher.csproj` | Version `6.10.113.0` → `6.10.114.0`; fourth launcher update on 2026-10-11 (only EXE bumped). |
| modified | `.data2/manifest.json` | `launcher` → `6.10.114.0`. Other seven untouched. |
| modified | `AGENTS.md` | Auto-hide note covers full-height first start. Doc-only. |
| release | `dist/` | Full `build.ps1` run: all 8 EXEs < 5 MB. Verified live at startup (mouse kept on window): `0,0 460×1040`, alive. |

---

## 2026-10-09 — Print/PDF header date standardized to Long Form — V6.10.091

Verified every sub EXE's print-to-PDF path: only Temp Uniformity (`printGraph`)
and BiDF Analysis (`printPdf`) print at all — both just call `window.print()`,
and neither page stamps the printout with the print day, so the only header date
on the PDF was the browser's locale-dependent short date. Lot Extractor, Recipe
Translator, Rcp Std Table, P2 Data Analysis and Barcode Generator have no print
function (nothing to standardize). Both polyfills now inject a print-only header
line (`#tti-print-date`, first row of the printed dashboard, hidden on screen)
rendering the print day in deterministic Long Form via explicit month names
(e.g. `October 9, 2026`), re-stamped on every `beforeprint` so long-open
windows print the current day. Canonical HTML untouched. Also fixed BiDF's
stale `V6.07.122` `.logo-tag` badge (now synced like the other tools).

| Type | Paths | Summary |
|---|---|---|
| modified | `src/Htxb.TempUniformity/Resources/polyfill.js` | New print-date header section (`ttiLongFormToday` + `ensurePrintDateHeader('graph-screen')`, wired into `init()` and the `load` handler); `EXE_VERSION` `V6.09.195` → `V6.10.091`. |
| modified | `src/Htxb.BiDFAnalysis/Resources/polyfill.js` | Same print-date header (`ensurePrintDateHeader('dashboard-screen')`) plus new `.logo-tag` → `V6.10.091` badge sync, both placed before the `window.tools` guard (LotExtractor pattern) since neither needs the bridge. |
| modified | `src/Htxb.TempUniformity/Htxb.TempUniformity.csproj` | Version `6.9.195.0` → `6.10.91.0`; first TempUniformity update on 2026-10-09. |
| modified | `src/Htxb.BiDFAnalysis/Htxb.BiDFAnalysis.csproj` | Version `6.9.201.0` → `6.10.91.0`; first BiDF update on 2026-10-09. |
| modified | `.data2/manifest.json` | `temp-uniformity` + `bidf-analysis` → `6.10.91.0`; `generated` → 2026-10-09. Other six EXEs untouched. |
| release | `dist/` | Full `build.ps1` run: all 8 EXEs < 5 MB; both FileVersions `6.10.91.0` (other six verified unchanged), both smoke-tested alive with correct titles. Verified in headless Chromium against the real polyfills: header present as first dashboard row with text `October 9, 2026` (Long-Form regex ✓), hidden on screen, print-only CSS rule present, badges synced, `beforeprint` re-stamp hooked; real PDFs rendered from the same DOM without errors. |

---

## 2026-10-09 — Launch time cut ~6× via offline-first WebView2 environment (all EXEs)

The loading splash was visible ~2.4 s per launch. Measured with temporary timing
marks (removed afterwards): WebView2 init was fast (~0.3 s), but every launch stalled
~2.0 s between `NavigationStarting` and `ContentLoading` — Chromium doing DNS
resolution timeouts for the fake `app` hostname on the standard `https` scheme
(proxy flags alone changed nothing; the stall persisted bit-for-bit). Fix in the
shared host: `HostForm` now creates the WebView2 environment explicitly with
offline-first flags (`--no-proxy-server --disable-background-networking
--disable-component-update --disable-sync --host-resolver-rules="MAP app
127.0.0.1"`), so no DNS/proxy/background traffic is ever attempted — every page
is served locally. Re-measured: navigate → content-loading 2015 ms → 30 ms,
process-start → splash-gone 2453 ms → ~390 ms, comfortably inside 3 s. Applies
to all 8 EXEs (shared host), so all 8 bumped per the version rule; badge sync
added to the three polyfills that lacked it (Recipe/Rcp/P2 showed stale
`V6.06.19x`/`V1.0`), Lot badge `V6.09.091` → current, Temp/BiDF badges → new.

| Type | Paths | Summary |
|---|---|---|
| modified | `src/Htxb.Host/HostForm.cs` | New `InitializeWebViewAsync` (explicit `CoreWebView2Environment.CreateAsync` with offline-first flags, error dialog + close on failure); ctor uses it instead of `CreationProperties` + paramless `EnsureCoreWebView2Async`. Temporary timing marks added for diagnosis then fully removed (verified zero `TmpMark` matches). |
| modified | `src/Htxb.{Launcher,LotExtractor,TempUniformity,BiDFAnalysis,RecipeTranslator,RcpStdTable,P2DataAnalysis,BarcodeGenerator}/*.csproj` | All 8 bumped: launcher `6.10.81.0` → `V6.10.082`; temp/bidf `6.10.91.0` → `V6.10.092`; other five first change today → `V6.10.091`. |
| modified | `.data2/manifest.json` | All 8 versions synced; `generated` stays 2026-10-09. |
| modified | `src/Htxb.{LotExtractor,TempUniformity,BiDFAnalysis}/Resources/polyfill.js` | Badge consts → new versions (`V6.10.091` / `V6.10.092` / `V6.10.092`). |
| modified | `src/Htxb.{RecipeTranslator,RcpStdTable,P2DataAnalysis}/Resources/polyfill.js` | New pre-guard `.logo-tag` → `V6.10.091` badge sync (these showed stale hardcoded versions with no sync). |
| modified | `AGENTS.md` | Architecture note: explicit offline-first WebView2 environment flags, why the resolver rule exists (~2 s DNS stall), do-not-remove + re-measure guidance. Doc-only. |
| release | `dist/` | Full `build.ps1` run: all 8 EXEs < 5 MB, FileVersions verified per-EXE as above; all 8 smoke-tested alive with correct titles. |

---

## 2026-10-09 — Docs: print/PDF header date rule for all (incl. future) tools

| Type | Paths | Summary |
|---|---|---|
| modified | `AGENTS.md` | New standing rule under Architecture conventions: every tool with a print-to-PDF function (existing or new) must show the print day in Long Form (`October 9, 2026`), implemented polyfill-side as a print-only `#tti-print-date` first row with explicit month names + `beforeprint` re-stamp (Temp Uniformity / BiDF polyfills are the reference); adding print output counts as a functionality change requiring a version bump. Doc-only; no EXE bump. |

---

## 2026-10-16 — New tool: P2 Data Analysis – Time (preset vs actual test time across log folders) — V6.10.161

ST Microelectronics' TO247 qualification thread ("ST Qual Lot Questions TO247", the reference
mail in `VSProj_Log Data Analysis\Reference email`) turned on one question: the stress / test
time measured from the run did not match the preset (their example: 3.85 min measured against a
5 min specification, "could we have the time stamp for start & end?"). TTI answered with a
document called **"Preset Time VS Actual Time Data Analysis"** — a per-test-sequence table of
Start / End / Preset Duration / Actual Duration plus a bar graph. That analysis is now a tool.

New EXE **`TTI_P2DataAnalysisTime.exe`** (tool id `p2-analysis-time`, UI name "P2 Data Analysis –
Time", the P2 Data Analysis family). Two folder slots — A (reference) and B (candidate) — each
scanned recursively for `*.bidf.gz` / `*.bidf` (BiDF = gzipped SQLite). Files are read through the
native bridge, gunzipped in-page (`DecompressionStream`), loaded with the shared `sql.js`, and
`tbl_run` + `tbl_testflow` are parsed. Reported per run: Σ preset (`exec_set`), Σ actual
(`exec_time`), Δ and Δ %, Σ timestamp delta (end − start), inter-step gap, wall clock (`tbl_run`
start → end, test-step span as fallback), step/pass counts, parts/good. Comparison pairs runs by
BIB position (slot-to-slot) or by order, plus a lot-to-lot roll-up — **slot/lot totals only, no
step matching**, because step names/test ids do not line up across revisions. Charts: preset vs
actual per step, Σactual per slot A vs B, and deviation-from-preset trend. Exports: styled XLSX
(Summary / A vs B / Lot to lot / Run identity / one sheet per run) and print-to-PDF with the
mandated Long-Form print date. Tolerances ±2 % per step and ±1 % per run, editable, re-evaluated
without re-reading the logs. Optional extras (behind a checkbox / secondary panels): run-identity
naming-rule compliance against the BiDF file-name rule (this is the defect ST rejected earlier
files for — "missing the 2 digits of the second"), missing `exec_set` in rev-1.4 logs, and
minute-resolution timestamps. Verified headless against both reference logs: slot 10 /
`NuTesting2` (rev 1.5) = 13 647.6 s preset vs 13 961.0 s actual (+2.30 %), wall 16 140 s; slot 7 /
`10110900133201CA` (rev 1.4, no `exec_set`) = 15 004.1 s actual, wall 15 869 s — both matching an
independent Python/SQLite calculation exactly.

| Type | Paths | Summary |
|---|---|---|
| created | `src/Htxb.P2DataAnalysisTime/Htxb.P2DataAnalysisTime.csproj` | New WinExe `net8.0-windows` v`6.10.161.0` (first release → `V6.10.161`); references `Htxb.Host`; embeds `Resources/index.html` + `Resources/polyfill.js`. |
| created | `src/Htxb.P2DataAnalysisTime/Program.cs`, `MainForm.cs` | STA entry point; `HostForm` subclass, tool id `p2-analysis-time`, title "TTI HTXB P2 Data Analysis - Time", favicon rewrite. |
| created | `src/Htxb.P2DataAnalysisTime/Resources/index.html` | The tool page (own page in the repo, like the Barcode Generator — the canonical P2 HTML is **not** touched): two folder pickers via `tools.SelectDirectory` + drag-drop fallback, recursive `ListDirectory` scan, gzip + `sql.js` parsing with revision-tolerant column mapping (`exec_set` / `"exec_set (NEW)"` / absent), run + step metrics, pairing, 3 Chart.js charts, XLSX + print/PDF export. |
| created | `src/Htxb.P2DataAnalysisTime/Resources/polyfill.js` | Version badge sync (`V6.10.161`), offline `XLSX.writeFile` shim via `SaveFile` + `WriteFileBase64`, and the standardized Long-Form print-date header. |
| created | `src/Htxb.P2DataAnalysisTime/Resources/Favicon.ico` | EXE icon (copied from the P2 Data Analysis wrapper). |
| modified | `build.ps1` | `$tools` += `Htxb.P2DataAnalysisTime` → `TTI_P2DataAnalysisTime.exe`. |
| modified | `.data2/manifest.json` | New `p2-analysis-time` entry (`6.10.161.0`); `launcher` → `6.10.161.0`; `p2-analysis-time` added to the `usedBy` lists of `xlsx-js-style`, `chart.umd`, `sql-wasm.js`/`.wasm`; `generated` → 2026-10-16. `p2-analysis` deliberately unchanged (its functionality did not change). |
| modified | `configuration/tools.ini` | New `[p2-analysis-time]` section (`LastDir=`) so its folder dialogs reopen where the user last was. |
| modified | `src/Htxb.Launcher/Resources/polyfill.js` | Injects the "P2 Data Analysis – Time" card directly under the existing P2 Data Analysis card (same approach as the Barcode card) and launches `TTI_P2DataAnalysisTime.exe`; version synced from the manifest. The canonical cover page stays unedited. |
| modified | `src/Htxb.Launcher/Htxb.Launcher.csproj` | `6.10.121.0` → `6.10.161.0` (launcher UI gained a card → its own functionality changed). |
| release | `dist/` | Full `build.ps1` run: 9 EXEs published, all < 5 MB; verified headless in Chromium against the real reference logs. |
| modified | `AGENTS.md` | Architecture note documenting the "EXE-only tool" pattern (page kept at `src/<project>/Resources/index.html`, launcher card injected by the launcher polyfill, version read from `data2\manifest.json`) and what the new tool does. Doc-only. |

---

## 2026-10-16 — P2 Data Analysis – Time follow-up: slimmer window, both-folders gate, window-wide drag & drop — V6.10.162

Same-day follow-up on the new tool. Three fixes from the first live look:

- **20 % slimmer window** (`1400×940` → `1120×900`) with tightened page padding/gaps and a denser
  KPI grid, so the dashboard fits the narrower window without horizontal scrolling (verified at
  1120 px: no overflow, 2 × 533 px chart columns, 6 KPI columns).
- **A alone no longer starts the analysis.** The comparison now waits until BOTH folders are loaded
  and the pick row states what is still missing (`Folder A: 1 run(s) loaded — now select or drop
  folder B; the comparison starts once B is loaded too.`).
- **Drag & drop reworked** so a folder can simply be dropped on the window: the whole window is a
  drop target (WebView2 would otherwise navigate to the dropped file), the destination folder is
  resolved from where the drop landed with the first empty folder as fallback, two folders dropped
  together fill A then B, and `DataTransferItem` entries are captured synchronously before the first
  `await`. The (now compact) pick row stays visible above the dashboard so either folder can be
  swapped or re-dropped, and loads are queued so a two-folder drop cannot interleave progress.

| Type | Paths | Summary |
|---|---|---|
| modified | `src/Htxb.P2DataAnalysisTime/MainForm.cs` | Window size `1400×940` → `1120×900` (20 % slimmer). |
| modified | `src/Htxb.P2DataAnalysisTime/Resources/index.html` | Tighter `.main` padding/gaps + denser `.meta-grid`; compact persistent pick row (`#pick-row.compact`) with a "waiting for folder X" status line (`#pick-status`); dashboard gated on `runs.A.length && runs.B.length`; window-level drag & drop (drop-position slot routing, multi-folder drop, synchronous entry capture, `queueLoad` serialisation); print CSS hides the pick screen; `resetApp` clears the new state. |
| modified | `src/Htxb.P2DataAnalysisTime/Htxb.P2DataAnalysisTime.csproj`, `Resources/polyfill.js` | `6.10.161.0` → `6.10.162.0`; badge `V6.10.161` → `V6.10.162` (second change to this EXE today). |
| modified | `.data2/manifest.json` | `p2-analysis-time` → `6.10.162.0`. |
| modified | `src/Htxb.Launcher/Resources/polyfill.js` | Card fallback label → `V6.10.162`. Launcher **not** bumped: the card's shown version is read from the manifest, so no user-visible launcher behaviour changed. |
| release | `dist/` | Full `build.ps1` run: 9 EXEs, all < 5 MB; new EXE smoke-tested alive with FileVersion `6.10.162.0`; behaviour re-verified headless (A-only stays on the pick screen, a dropped folder B starts the comparison, XLSX/PDF output unchanged). |

---

## 2026-10-25 - Launcher: P2 Data Analysis - Time card moved to 3rd position - V6.10.251

Launcher-only layout change (no tool code touched). The "P2 Data Analysis - Time"
card used to be injected directly under the "P2 Data Analysis" card, which put it
7th of 8. It now occupies the third slot from the top, immediately under Temp
Uniformity Graph and BiDF File Analysis.

Implementation stays polyfill-side (canonical cover page untouched): the card is
inserted before the 3rd `a.tool-card` of `.tools-grid` (`insertBefore(card, cards[2])`,
append fallback when fewer than three anchors exist). Only anchor cards are counted,
so the runtime-injected div cards (P2 Time itself, Barcode Generator) can never shift
the target index. Old anchor-to-P2 logic removed.

Verified in headless Chromium against the real polyfill and the real cover-page card
markup: final order is Temp Uniformity, BiDF File Analysis, **P2 Data Analysis - Time**,
Lot Extractor, Recipe Translator, Recipe-to-Std Table, P2 Data Analysis, Barcode
Generator (`p2timeIndex: 2`, card parent is `.tools-grid`).

| Type | Paths | Summary |
|---|---|---|
| modified | `src/Htxb.Launcher/Resources/polyfill.js` | `buildP2TimeCard` now inserts at grid index 2 (3rd card) instead of after the P2 Data Analysis card; anchor-based lookup with the old comment removed. |
| modified | `src/Htxb.Launcher/Htxb.Launcher.csproj` | Version `6.10.161.0` to `6.10.251.0` (first launcher change on 2026-10-25). Launcher is the only EXE bumped - card placement is launcher behaviour, and `p2-analysis-time` itself is unchanged. |
| modified | `.data2/manifest.json` | `launcher` to `6.10.251.0`; `generated` to 2026-10-25. All other tool versions untouched. |
| release | `dist/` | Full `build.ps1` run: 9 EXEs, all < 5 MB. `TTI_Utils_Launcher.exe` FileVersion `6.10.251.0`; new polyfill confirmed embedded in the published binary (new comment present, old gone); live smoke test shows the window alive, titled "TTI HTXB Tools & Utilities", collapsed to the 170x72 corner tab as designed. |

---

## 2026-10-30 - BiDF Analysis: open *.bidf.gz directly - V6.10.301

The tool could already *produce* gzip archives (Batch Compress) but not *read* them: the
file picker filtered to `accept=".bidf"`, `handleFile()` rejected any other name
(`!file.name.endsWith('.bidf')`), and the raw bytes went straight into
`new SQL.Database(...)`. Opening a `.bidf.gz` therefore showed "Please upload a SQLite
*.bidf file." even though the payload is a perfectly normal BiDF SQLite database.

Reference file used for verification: `VSProj_Log Data Analysis\BiDF 260903\
998G20260903152142__COBISIIP2_TTI0001__10__P_TM_BRI9_B_06_5_32__NuTesting2.bidf.gz`
(26.2 KB gzip -> 90 KB SQLite, BiDF rev 1.5, `integrity_check: ok`).

Fix is polyfill-only (canonical page untouched): a new "Direct *.bidf.gz open" section
wraps `window.handleFile` - a page-level function declaration, so it lives on `window`
and every caller (picker, drop zone, later code) resolves through it - and hands the
original function a synthetic `*.bidf` `File` carrying the decompressed bytes.

- **Detection:** gzip by extension (`\.gz$`) **or** magic bytes (`1F 8B`), so a renamed
  file still opens.
- **Decompression:** `DecompressionStream('gzip')` -> `blob.stream()` -> `ArrayBuffer`
  (same API the P2 Data Analysis - Time tool already uses); payload must carry the
  `SQLite format 3\0` magic before it reaches sql.js.
- **Plain `*.bidf` untouched:** passes straight through to the original function.
- **Header keeps the real file:** after load, `#loaded-filename` shows the `.gz` name and
  `#loaded-filesize` the compressed size (the operator sees what they actually opened).
- **Exports unchanged:** `currentFile` is the synthetic `x.bidf`, so the Excel name stays
  `x.xlsx` (verified: `998G...NuTesting2.xlsx`).
- **Clear failures** instead of the generic parse error: not-gzip-payload, gzip of a
  non-SQLite file, and no `DecompressionStream` support each show their own toast.
- Picker `accept` widened to `.bidf,.gz,.bidf.gz,application/gzip`; upload hint changed
  to "Drop your \*.bidf or \*.bidf.gz file here" (DOM edit, HTML untouched).

Verified headless against the real page + real polyfill + real `sql.js`/`xlsx`: the
reference `.gz` opens with lot `NuTesting2`, device `SCT040HU120G3AG`, package
`40HU12AG`, 03/09/2026 15:21 -> 19:50, duration `4h 29m`, BIN map drawn, header showing
the `.gz` name and 26.2 KB, success toast; a plain `.bidf` still loads identically; a
`.gz` renamed to `.bin` loads via magic detection; broken gzip and gzip-of-text each show
their own error toast and leave the upload screen in place.

**Housekeeping:** `D:\_TTI-Dev\proj01 Main Page` had been moved into
`D:\_TTI-Dev\bakup old files\` earlier today (30-10, 11:27), which broke the
`EmbeddedResource` path shared by all 7 HTML-wrapped projects; the folder was moved back
to its canonical location before building (no csproj or AGENTS.md changes needed).

| Type | Paths | Summary |
|---|---|---|
| modified | `src/Htxb.BiDFAnalysis/Resources/polyfill.js` | New "Direct *.bidf.gz open" section (`ttiIsGzName`/`ttiHasGzMagic`/`ttiGunzip`/`ttiLooksLikeSqlite`/`ttiInnerName`/`ttiShowOpenedGz`/`ttiWrapHandleFile`) installed on DOMContentLoaded with a bounded retry, plus picker `accept` and upload-hint updates; `EXE_VERSION` `V6.10.092` -> `V6.10.301`. |
| modified | `src/Htxb.BiDFAnalysis/Htxb.BiDFAnalysis.csproj` | Version `6.10.92.0` -> `6.10.301.0` (first BiDF Analysis change on 2026-10-30). Only EXE bumped. |
| modified | `.data2/manifest.json` | `bidf-analysis` -> `6.10.301.0`; `generated` -> 2026-10-30. All other tool versions untouched. |
| release | `dist/` | Full `build.ps1` run: 9 EXEs, all < 5 MB. `TTI_BiDFAnalysis.exe` FileVersion `6.10.301.0`, new shim + badge confirmed embedded, smoke-tested alive with the correct window title. |



---

## 2026-10-30 - P2 Data Analysis - Time: EDA preset estimate + editable Preset column - V6.10.302

EDA logs are **BiDF rev 1.4**: their `tbl_testflow` has no `exec_set` column, so the
Test step detail showed an empty Preset column and the whole preset-vs-actual comparison
was dead for those runs (`BiDF 261014 EDA`, 32 steps). COBIS logs (rev 1.5) carry
`exec_set` and were already fine.

Requirement from the user: **this applies to the EDA machine only - the COBIS system does
not have this requirement.** So the feature is gated on EDA and every COBIS code path is
left exactly as it was.

**EDA detection** uses structural fields only, never the free-text lot name:
`programname` ends with `.plnx` (EDA loader) **or** `ovenname` contains `EDA` as a whole
token (`EDA-8409-CHM2`). `BiDF 261015` (`P_TM_BRF9_B_05_4_32`, `TTSZ2104`,
`platform=COBIS-HTXB-P2`) is therefore never treated as EDA.

**Estimate** - for an EDA run with no `exec_set`, the preset column is filled from another
loaded run that has real presets: same normalised recipe in the opposite folder first
(`p_tm_brf9_b_05_4_32.plnx` -> `P_TM_BRF9_B_05_4_32`), otherwise any run with `exec_set`.
Every borrowed value is marked `~`, tinted amber and carries a tooltip naming the source
step. Matching ladder: **name -> segment:test_id -> sequence index + ref offset -> unique
test_id**. `test_id` alone was deliberately placed last: EDA and COBIS number their steps
differently, so the raw id collision put EDA id=4 (`VDS=690_VGS=0`, 372 s) on COBIS id=4
`TEST CLOSED` (120 s); with the index pass the reference offset actually drives the whole
series (verified: `methods: {"index": 32}`).

**Reference alignment** - the first build of this feature (V6.10.301, superseded the same
day) started the index match at offset 0, which put every EDA row one step behind its real
counterpart: EDA step 4 (`VDS=20_VGS=10`, 6 min) took the preset of the *previous* COBIS
step, and the whole column read "one row to the next". Cause: COBIS carries transition
rows (`TEST CLOSED`, `TEST_CLOSED2/3`, `FINAL TEST CLOSED`) that the EDA flow does not, so
the two lists are shifted from step 4 onwards. The estimate now **auto-aligns** the
reference: `offsetScore()` evaluates every candidate offset 0...24 and `bestOffset()` keeps
the best one (median relative error -> count within 25 % -> mean relative error), requiring
at least half the steps to map so a short tail cannot win by luck. The resolved offset
lands in the spinner (`(auto offset +1)`) and can still be overridden by hand. For these
two files it picks **+1**, which is the correct series alignment:
A9 41.3 min -> B10 `HTGB_RAMP` 40 (+3.2 %), A12 21.1 -> B13 `HTRB1` 20 (+5.7 %),
A24 26.3 -> B25 `HTRB6` 25 (+5.0 %).

**Editing** - the Preset cell is a number input **in decimal minutes** (blank = no preset)
for EDA runs only: `~` amber = estimated, blue = entered manually, plain = `exec_set` from
the file. Enter commits and moves to the next row, blur commits; Δ, Δ %, row status,
Σ preset, run deviation, KPI cards, run/lot tables and all three charts update in the same
render (`applyPreset()` re-derives `summary.setTotal` / `runDelta` / `runDeltaPct`, which
used to be frozen at parse time). `exec_set = 0` stays a valid zero.

**Toolbar** (EDA runs only, hidden entirely for COBIS): reference `<select>` with
**ref offset 0...24**, `Apply estimate`, `Clear estimates`, a bulk minutes box with
`Fill empty` / `Set all`, `Reset to file`, `Clear saved`, plus a live provenance report
("preset: 0 from file - 32 estimated from B1 (auto offset +1) - matched by index x 32") -
the offset spinner accepts a manual override, which is then shown as `(offset +N)`.

**Persistence** - manual values are stored per recipe in `localStorage`
(`tti.p2time.presets.v1`, key = normalised program name), restored on load as `manual`
and matched by `segment`+`test_id` first, then by index. `Clear saved` drops the recipe.

**Exports** - XLSX gains a `Preset src` column (`file` / `estimated B1` / `manual`) and an
`EDA preset - <run>` summary line, both only when an EDA run is in the workbook; the PDF
keeps the `~` marker plus a "not measured" legend. While there, the XLSX per-run TOTAL row
(which had been one column to the left of its own header since the sheet was written) is
now aligned.

Verified headless against the real page + real polyfill + real `sql.js`/`xlsx`, driving the
real folders through a `window.tools` bridge stub:

- `BiDF 261014 EDA` (A) + `BiDF 261015` (B): 32 presets estimated from `B1`, 32 tilde cells,
  `estOffset: 1` / `estAuto: true` (spinner shows `1`), report
  `0 from file - 32 estimated from B1 (auto offset +1) - index x 32`, note "~ 32 estimated
  presets - not measured", Σ preset `03:12:00` vs actual `04:10:04`, run deviation `+30.24 %`.
  Row-to-row alignment checked: A9 41.3 min -> `HTGB_RAMP` 40, A12 21.1 -> `HTRB1` 20,
  A24 26.3 -> `HTRB6` 25 (no more "one row to the next").
- overridden offset `3` -> `estAuto: false`, values/report change (`(offset +3)`); edit row 4
  to `7.5` -> Δ % `-20.04 %`, FAIL, Σ preset `03:17:30`, cell styled `preset-manual`;
  `Set all 6` -> 32 manual values, Σ preset `03:12:00`; `Reset to file` -> all 32 cells
  empty, Σ preset `-`.
- EDA in slot B instead of A: still auto-references `A1`; EDA in both slots: no reference
  offered (select disabled), 32 empty cells, editing still works.
- Persistence: edit -> `{"recipes":{"ptmbrf9b05432":[...]}}`, `Load New Folders` + reload
  restores `9.25 min` as `manual` (match `saved`), `Clear saved` -> `{"recipes":{}}`.
- **COBIS regression**: `BiDF 261015` in both slots -> `.preset-bar` `display:none`, 0
  preset inputs, plain `00:02:00` cells, Σ preset `03:18:00`, deviation `+0.31 %`, KPI
  subtext still "per-run deviation tolerated: +/-1%", XLSX unchanged (no `Preset src`
  column, no EDA summary line), 37 steps + TOTAL row.

| Type | Paths | Summary |
|---|---|---|
| modified | `src/Htxb.P2DataAnalysisTime/Resources/index.html` | New "EDA preset support (EDA ONLY)" section: EDA detection, effective-preset model (`setFile`/`set`/`setSource`/`setMatch`), `applyPreset`, hybrid estimate with auto-aligned ref offset (`offsetScore`/`bestOffset`, manual override), per-recipe `localStorage` persistence, editable Preset inputs with `~`/amber/blue provenance, preset toolbar, provenance in the detail note and KPI card, `Preset src` column + aligned TOTAL row in XLSX; CSS for the toolbar/cells including print rules. COBIS runs skip every one of these paths. |
| modified | `src/Htxb.P2DataAnalysisTime/Resources/polyfill.js` | `EXE_VERSION` `V6.10.162` -> `V6.10.302` (header badge follows the EXE version). |
| modified | `src/Htxb.P2DataAnalysisTime/Htxb.P2DataAnalysisTime.csproj` | Version `6.10.162.0` -> `6.10.302.0` (running number 1 = feature, 2 = same-day auto-alignment follow-up). Only this EXE bumped. |
| modified | `.data2/manifest.json` | `p2-analysis-time` -> `6.10.302.0`. All other tool versions untouched. |
| release | `dist/` | Full `build.ps1` run: 9 EXEs, all < 5 MB (`TTI_P2DataAnalysisTime.exe` 1.79 MB). FileVersion `6.10.302.0`, preset engine + auto-alignment + toolbar + badge confirmed embedded, smoke-tested alive with the correct window title; `dist/data2/manifest.json` carries `6.10.302.0`. |

---

## 2026-10-30 - BiDF File Analysis: XLSX export for rev-1.4 (EDA) logs - V6.10.302

Reported from the live tool: **"Failed to compile Excel file."** on
`VSProj_Log Data Analysis\BiDF 261014 EDA BRF9\998G...p_tm_brf9_b_05_4_32.plnx.bidf.gz`
(EDA machine, BiDF rev 1.4). COBIS logs (rev 1.5) exported fine.

**Root cause** - `exportToExcel()` walks a fixed `sheetMapping` and reads each table with
`database.exec('PRAGMA table_info(<table>)')[0].values`. A rev-1.4 EDA log has **no
`tbl_addinfo` table** (verified against the file: 12 tables, `tbl_addinfo` absent), so
`exec` returns an empty result, `[0]` is `undefined`, and the export dies before the
workbook is written. Reproduced headlessly against the real page + real polyfill + real
`sql.js`/`xlsx` (`TypeError: Cannot read properties of undefined (reading 'values')` at
the sheet loop, toast `Failed to compile Excel file.`).

**Second defect found while verifying** - a rev-1.4 log also has no `hwbin_desc` column in
`tbl_result`. The page reads it with `SELECT site, "hwbin_desc (NEW)" FROM tbl_result ...`
inside a try/catch that falls back to `tbl_summary`. SQLite's legacy double-quoted-string
behaviour answers an unknown quoted identifier with the literal text instead of raising, so
the fallback never ran and every BIN Map cell on screen *and* in Excel read
`Dxx site n hwbin_desc` (grey). Now the query is redirected to `tbl_summary` when
`tbl_result` lacks the column - the EDA BIN Map shows the real `Good`/`Fail`.

**Fix (polyfill only, canonical HTML untouched)**
- Missing-table guard: wraps `exportToExcel()` (a page-level function declaration, so
  `window.exportToExcel`), swaps every table that does not exist for a placeholder sheet
  that states `Table "<name>" is not present in this BiDF revision`, runs the original
  export, then restores `sheetMapping` in a `finally`. Tables that do exist are exported by
  the page's own code, unchanged.
- BIN Map source: `database.exec` (already wrapped for the `(NEW)` column adaptation) now
  redirects the page's `tbl_result` + `hwbin_desc` query to `tbl_summary` **only** when
  `tbl_result` really lacks that column; rev-1.5 logs keep querying `tbl_result`.

**Self-evaluation, both machine file types** (real page/polyfill/libs, real logs, headless):

| check | COBIS rev 1.5 (`BiDF 261012 2103 BRF9`, `...TTSZ2103...`) | EDA rev 1.4 (`BiDF 261014 EDA BRF9`, `...EDA-8409-CHM2...`) |
|---|---|---|
| open `.bidf.gz` | loads, header shows the `.gz` name | loads, header shows the `.gz` name |
| dashboard / sidebar | fine, 12 tables listed | fine, 12 tables listed (no AddInfo) |
| BIN Map data source | `tbl_result.hwbin_desc` (untouched) | redirected to `tbl_summary.hwbin_desc` |
| BIN Map result | 75 Good / 0 Fail | 73 Good / 2 Fail (DB: 73 P / 2 F) |
| XLSX export | success | success (was `Failed to compile Excel file.`) |
| XLSX sheets | BIN Map + 13 mapped sheets | identical sheet list |
| workbook bytes | 760185 - **identical to the workbook already exported in that folder** | 876915 |
| headers / row counts | all 12 tables match the DB exactly | all 11 present tables match the DB exactly |
| missing tables | none | AddInfo -> placeholder sheet |
| repeated export | success | success |

Spot values compared against the raw SQLite tables (first two and last row of every sheet);
the only difference reported is `''` vs an empty cell in `Parametric.NOTE` for rev 1.4,
which is how SheetJS writes an empty string (pre-existing, both types).

Generalisation test: an EDA copy with `tbl_atr` **and** `tbl_files` additionally dropped
exports successfully with three placeholder sheets (`Files`, `Atr`, `AddInfo`), so the guard
covers any missing table, not just `tbl_addinfo`.

**Known cosmetic issue, not introduced and not fixed:** the Excel BIN Map cells are filled
grey for every status because the page tests `val.includes('GOOD')` against `Good`/`Fail`
(case mismatch); the on-screen grid upper-cases before comparing and colours correctly.
Applies to COBIS exports as well.

| Type | Paths | Summary |
|---|---|---|
| modified | `src/Htxb.BiDFAnalysis/Resources/polyfill.js` | New "Excel export: tolerate tables missing from the log" section (`ttiTableExists`, `ttiWrapExcelExport` with placeholder sheets + `sheetMapping` restore) and BIN Map source redirect (`ttiFixBinQuery` inside the existing `database.exec` wrapper); `EXE_VERSION` `V6.10.301` -> `V6.10.302`. |
| modified | `src/Htxb.BiDFAnalysis/Htxb.BiDFAnalysis.csproj` | Version `6.10.301.0` -> `6.10.302.0` (second BiDF Analysis change on 2026-10-30). Only this EXE bumped. |
| modified | `.data2/manifest.json` | `bidf-analysis` -> `6.10.302.0`. `p2-analysis-time` stays `6.10.302.0`; all other tools untouched. |
| release | `dist/` | Full `build.ps1` run: 9 EXEs, all < 5 MB (`TTI_BiDFAnalysis.exe` 1.8 MB). FileVersion `6.10.302.0`, guards + badge confirmed embedded, smoke-tested alive with the correct window title; `dist/data2/manifest.json` carries `6.10.302.0`. |

---

## 2026-10-31 - New tool: recipe audit, customer workbook vs `Std_*.xlsx` - V6.10.301

The STM `261028` delivery is 17 workbooks (6 x 1200 V, 4 x 650 V, 1 x 900 V, 6 x SQ).
The question the customer asks - "will this recipe produce a correct BiDF with the tables
we ship?" - had no answer short of running a lot. Worse, it failed silently in the
expensive direction: a recipe with no row in `CustomerProfileSelection.csv` is converted
with the fallback profile, which is `report_only`, so the watcher writes a report and no
BiDF at all. Nothing in either toolchain said so before the lot ran.

`Htxb.RecipeCore` + `Htxb.RecipeTool` read the workbook the customer actually sends and
judge it against the table the profile selects.

**Measured result on the delivery (17 workbooks): `0/17 FITS`.**

| verdict | count | recipes |
|---|---|---|
| `MISMATCH` | 4 | BRI9 -> `Std_02_ST_TO247`, BRJ7 -> `Std_02_ST_TO247`, BRJJ -> `Std_07_ST_1200V`, BRF9 -> `Std_02b_ST_BRF9` |
| `NO_PROFILE` | 13 | the other 13 have no profile row: report-only today, no BiDF |

Every one of the four profiled recipes matches 24 of 32 step names exactly and 25 of 32
after normalisation, and all four miss **the same seven names**:

    workbook            std table
    HTGB             -> HTGB1
    HTRB6_R          -> HTRB6        (also HTGB_HT6_R -> HTGB_HT6, and the 7/8 pair)

The eighth difference, `IGSS_20V_1` vs `IGSS_+20V_1`, is bridged by normalisation and is
reported as a name difference, not a missing step. That split matters: seven names do not
normalise, so the engine looks them up, misses, and treats the step as unmatched.

Other measured differences the audit surfaced, all real:

- `Std_02` stores `Units` as `nA` where the workbook says `µA` on every resistance step.
- BRF9 has three genuinely different bin codes: `IDSS_650V_1` `0x2C3` vs `0x2C2`,
  `IDSS_690V_2` `0x2E4` vs `0x2DB` (`F_IDSS_650V_2`), `IDSX_690V_2` `0x218` vs `0x219`.
  The workbook says `690V` where the table says `650V`.
- `P_TM_BRFC_B_05_4_32.xlsx` declares a different recipe in **both** its plan sheet and
  its Reference Table: `P_TM_BRFC_B_04_5_32`.
- `Std_02` has 16 columns, `Std_07` has 17 - the customer conversion page omits
  `Test Sect ID Relative`. The audit reports the missing column instead of ignoring it.

**Two parser defects found by disagreeing with an independent openpyxl measurement**, both
merge-related and both silent:

1. A whole-row merge (`A81:XFD81`) holding a change-log paragraph was expanded into the
   step-name column, producing a 200-character "step".
2. A vertical merge in the name column (`D10:D11`, `E69:F70`) emitted the step twice.

After the fix the C# reader and openpyxl agree on all 17 workbooks: 11 x 32 steps / 25
ignored rows, 5 SQ x 16 / 12, and `P_TM_SQGW` 15 / 6. (Corrected later the same day: the SQ
plans are 15 / 13 - that 16th row is a `RAMP DOWN` with no limit and no bin, which the
ingester already ignored. See the entry below.)

**Layout facts pinned, because position-based parsing looked right on the first family and
was wrong on the rest:** columns are resolved by header text; the profile tab is
`Reference table` in `P_TM_SQGW` (lower-case `t`); that workbook puts the name in column
`D` with its header on row 8 and stores `time` as text (`15sec`, `5s`, `10 min`, `-`); the
`BI recipe name` header sits in `AD` on the 1200 V/650 V sheets, `Y` on SQ and `Q` on
`SQGW`, which is why the first `AD2`-only survey wrongly reported "SQ has no recipe name".

`SW BIN Priority` is read as a **hex** code on both sides. The three spellings all resolve
to one number (`196'=F_PRE_ON`, `'101 = F_PRE_ON`, `196 = F_PRE_ON`), and comparing the
number rather than the string is what let the three BRF9 bin differences above show up.

**Normalisation is one rule implemented twice**, so it is now pinned by a shared vector
file: `docs/test-vectors/normalise.json` in the BidfGen2 checkout, generated from the
Python implementation and asserted by both `bidfgen/tests/test_normalise_vectors.py` and
`Htxb.RecipeCore.Tests`. The generator also proves the `SW BIN` round trip
workbook -> written table cell -> parsed back, so the two parsers cannot drift apart
without a test failing.

**Gate:** `build-recipetool.ps1` publishes `TTI_RecipeTool.exe` into `dist-recipetool\`
(deliberately outside `dist\`, which `build.ps1` wipes, and outside `manifest.json` - this
is an audit tool with no launcher card). `test-recipetool.ps1` runs the checks and is
called by the BidfGen2 `.\dev-check.ps1` as a hard gate.

Note on the gate's shape: it pins the measured matrix, it does not run
`Htxb.RecipeTool fit --strict`. `--strict` means "every recipe fits", and the delivery
genuinely does not fit yet, so gating on it would fail every run. `--strict` stays the
release/onboarding switch; the dev gate asserts that the tool still reproduces today's
measured verdicts, so a parser or fit regression fails the build while the customer's real
mismatches do not.

| Type | Paths | Summary |
|---|---|---|
| added | `src/Htxb.RecipeCore/` | `Xlsx/` read-only OOXML reader (ZipArchive + XLinq, cached formula values), `Model/` (`TestName`, `PlanStep`, `Profile`, `Recipe`, `StdTest`), `Adapters/` (`PlanSheetAdapter`, `ReferenceTableAdapter`, `StdTableAdapter`, `ProfileCsvAdapter`), `Fit/` (`FitChecker` LCS alignment + 8 compared dimensions, `FitResult`). Package-free, `6.10.301.0`. |
| added | `src/Htxb.RecipeTool/` | Console CLI: `version`, `recipes`, `steps`, `fit [--json] [--strict]`. `TTI_RecipeTool.exe` published at 0.27 MB, FileVersion `6.10.301.0`. |
| added | `tests/Htxb.RecipeCore.Tests/` | 26 package-free checks: shared normalise/SW BIN vectors, duration and voltage parsing, cell references, LCS alignment, and the real delivery matrix (17 workbooks, 32/16/15 steps, 0/17 FITS, the seven missing `_R` names). |
| added | `build-recipetool.ps1`, `test-recipetool.ps1` | Build/publish to `dist-recipetool\`, and the check runner the BidfGen2 gate calls. |
| modified | `.gitignore` | `dist-recipetool/`. |

Verified: `test-recipetool.ps1` -> `26 passed, 0 failed, 0 skipped`; BidfGen2
`.\dev-check.ps1` -> `Python : 25/25`, `C# : 14/14`, `Recipe : 26/26`,
`ALL CHECKS PASSED`. The gate was proved to fail (a corrupted vector -> `CHECKS FAILED`,
exit 1) and to skip visibly rather than pass when the checkout is absent.


---

## 2026-10-31 - A fallback recipe must not borrow another family's table - V6.10.301

Customer instruction, verbatim: **"SQ should not use BRF9."** It was not only a display
complaint. The audit tool's `table` column showed `Std_02b_ST_BRF9.xlsx` for all six SQ
recipes, and the rule behind it lives in the shipped engine, where it decides what limits a
real lot is converted with.

`engine.choose_std_table()` - and its mirror in `Htxb.RecipeTool` - took the table whose
normalised test names overlapped the program most, with **no floor at all**: one shared name
was enough to replace the profile's table. Every limits table contains the generic pre-test,
ramp and clamp steps, so "most names in common" is not "same product". Measured against the
tracked tables:

| recipes | closest table | coverage |
|---|---|---|
| the eleven 1200V/650V/900V programs | `Std_07_ST_1200V` / `Std_02b_ST_BRF9` | 22-23 of 29-30 = 76-77% |
| the six SQ programs | `Std_02b_ST_BRF9` | 9 of 14 = 64% |

So an SQ lot with no profile row was converted against a 650V SiC part's limits, drift specs
and bin codes. On the site folder the winner was `Std_02_ST_168Hr` instead - same defect, a
different nearest neighbour.

**Fix: a table must cover every step of the program, or it is not that program's table.** No
shipped table covers any of the 17 programs (64-77%), so an unlisted recipe now gets the
configured `Std_00_Default.xlsx` - the three pre-test steps - and the watcher logs *"configured
default - no limits table covers every step of this program"* rather than a match count. The
audit stops offering a table for those recipes and says why, naming the closest with its
coverage so the gap is visible to the customer.

This narrows the feature to the case it was built for: a program that is a *subset* of a
standard table (a Short-BI lot), where normalisation makes coverage complete. Pinning a table
in `[fallback_profile] std_table` still overrides the choice entirely.

**A second defect surfaced while testing it.** `PlanSheetAdapter` counted the SQ plans as 16
steps; the ingester's `read_plan` said 15. The missing row is `RAMP DOWN`, the last row of
every variant-B (SQ) plan, carrying no limit, no bin and no `V_ds`/`V_gs`. `std_ingest.SKIP`
already ignored it; the adapter's `NotAStep` list did not, because it was derived from variant
A, which has no such row. That mattered twice: the fit reported a phantom missing step, and
under the new rule it demanded a name no generated table can contain, which would have
disabled the auto-table choice permanently for SQ. `NotAStep` now carries the same four names
as `std_ingest.SKIP`.

The reader now agrees with `read_plan` on all 17 workbooks: 11 x 32 steps / 25 ignored, 6 SQ x
15 / 13 (`SQGW` 15 / 6, its own layout), 14 distinct names each. This corrects the "5 SQ x 16 /
12" in the entry above.

| Type | Paths | Summary |
|---|---|---|
| modified | `bidfgen/engine.py` | `choose_std_table` requires full coverage; falls back to `cfg.fallback_table` with 0 hits otherwise. Docstring carries the measured numbers. |
| modified | `bidfgen/watcher.py` | The fallback line distinguishes a match from the configured default. |
| modified | `bidfgen/tests/test_onboard.py` | Five checks: a covering table is chosen, a 32-of-34 partial is refused, the default is not that table, and the real SQGW program is assigned no shipped table (and not BRF9). |
| modified | `src/Htxb.RecipeCore/Fit/FitChecker.cs` | `SuggestTable` moved out of the CLI so it is testable; requires full coverage; reports the closest and its coverage. `NoProfile` takes a `TableSuggestion`. |
| modified | `src/Htxb.RecipeCore/Fit/FitResult.cs` | `TableSuggestion` record. |
| modified | `src/Htxb.RecipeCore/Adapters/PlanSheetAdapter.cs` | `NotAStep` gains `rampdown`, matching `std_ingest.SKIP`. |
| modified | `src/Htxb.RecipeTool/Commands/FitCommand.cs` | Loads the folder once and calls `FitChecker.SuggestTable`. |
| modified | `tests/Htxb.RecipeCore.Tests/RealDataTests.cs` | 28 checks; two new ones: no recipe is offered a table that misses a step, and SQ is offered none (14 steps, closest BRF9 at 9 of 14). SQ counts corrected to 15 / 13. |

Verified: `test-recipetool.ps1` -> `28 passed, 0 failed, 0 skipped`; `test_onboard.py` ->
`ALL ONBOARD CHECKS PASSED`; `Htxb.RecipeTool fit` prints `-` in the table column for every
unlisted recipe and the note *"no table in the folder covers all 14 step(s) - the closest is
Std_02b_ST_BRF9.xlsx at 9 of 14, so the watcher uses its configured fallback table, not that
one"*. The change report for this behaviour still has to be written with the next build,
because the version number comes from build.ps1.

## 2026-10-31 - ecipe-diff ported to C#

| Type | Paths | Summary |
|---|---|---|
| created | `src/Htxb.RecipeCore/Model/ZadzStep.cs` | One `AgingProcess` entry: name, Vds, Vgs, and every field verbatim. |
| created | `src/Htxb.RecipeCore/Model/ZadzRecipe.cs` | The machine recipe (PartName / PartLibrary / Options + steps); `Names` is what the CSV will log. |
| created | `src/Htxb.RecipeCore/Adapters/ZadzAdapter.cs` | Reads the `.zadz` JSON (UTF-8/BOM, 22 fields with spaces and parentheses, unknown fields kept, a step without a name skipped). |
| created | `src/Htxb.RecipeCore/Diff/DiffResult.cs` | `DiffPair` and `DiffResult`; `Aligned` is strict - a rename is a difference. |
| created | `src/Htxb.RecipeCore/Diff/RecipeAligner.cs` | LCS pairing over `TestName.Normalise` keys, alias map applied only to unmatched steps. |
| created | `src/Htxb.RecipeCore/Diff/RecipeDiffer.cs` | Workbook via `PlanSheetAdapter` vs `.zadz`; compares names and Vds/Vgs only (limits are different quantities on the two sides). |
| created | `src/Htxb.RecipeCore/Diff/DiffReport.cs` | Text for the operator, JSON as the parity contract with the Python tool. |
| created | `src/Htxb.RecipeTool/Commands/DiffCommand.cs` | `diff <workbook.xlsx> <recipe.zadz> [--json] [--strict]`. |
| modified | `src/Htxb.RecipeTool/Program.cs` | Usage and dispatch for `diff`. |
| created | `tests/Htxb.RecipeCore.Tests/DiffTests.cs` | 6 rule checks + the 16-pair delivery baseline, asserted per recipe. |
| modified | `tests/Htxb.RecipeCore.Tests/Program.cs`, `TestPaths.cs` | `--zadz`, live-then-fixtures resolution, shared alias-file path. |
| modified | `test-recipetool.ps1` | Builds the tool as well, passes `--zadz`, and adds the parity phase against the Python tool. |
| created | `docs/recipe-diff-port.md` | The plan this implements. |

Verified on 2026-10-31: `test-recipetool.ps1` -> `50 passed, 0 failed, 0 skipped` (was 28);
parity vs the Python tool on the same 16 pairs -> `16 same, 0 different`; BidfGen2
`dev-check.ps1` -> `25/25` Python, `14/14` C#, `50/50` recipe. Baseline: BR 32 paired
(25 exact, 7 alias), the five BR recipes carrying `IGSS_-10V_1` / `IGSS_-10V_3` machine-only;
SQ 15 paired (14 / 1) with `RAMP_DOWN` machine-only. The Python `recipe-diff` stays as the
reference until a second green parity run, then it is deleted.
### 2026-10-31 - retired the Python ecipe-diff

| Type | Paths | Summary |
|---|---|---|
| modified | `test-recipetool.ps1` | Parity phase removed after two green runs (`16 same, 0 different`); the C# baseline checks are the proof now. The tool is still built by the script. |
| modified | `docs/recipe-diff-port.md` | Status complete; step 7 done. |

The Python command itself was deleted in BidfGen2 (`bidfgen/tools/recipe_diff.py`, its
`cli.py` dispatch, its checks in `test_onboard.py`). One implementation again.
`docs/std-ingest.md` there points at `Htxb.RecipeTool diff`.
## 2026-11-01 - std-ingest, std-align and validate-lot ported to C#

BidfGen2 `AGENTS.md` rule 14 now says new code is C#; `bidfgen\` stays the frozen
Python product. The three hand-run table tools moved here, and their Python CLIs
were deleted.

| Type | Paths | Summary |
|---|---|---|
| created | `src/Htxb.RecipeCore/Xlsx/XlsxWriter.cs` | Package-free xlsx writer: zip of the minimum OpenXML parts, inline strings, numbers stay numbers (`15sec` stays text). |
| created | `src/Htxb.RecipeCore/Bidf/BidfReader.cs` | Read-only BiDF access through the in-box `winsqlite3.dll`. Strings cross as `IntPtr` - a `char*` declared as `byte[]` corrupts the heap on the first call. |
| created | `src/Htxb.RecipeCore/Ingest/StdTableBuilder.cs` | `BuildFromWorkbook`, `BuildAligned`, `BuildWithoutMachine`, `ApplyLoggedNames`, `NormaliseLimits`; machine-only rows defined here (`IGSS_-10V_1/_3`, ±80 nA, bins `15F`/`173`). |
| created | `src/Htxb.RecipeCore/Ingest/StdTableWriter.cs` | The `std_table` sheet, 16 or 17 columns. |
| created | `src/Htxb.RecipeCore/Ingest/SlotCsvReader.cs` | Test names from a slot log's `Test Data` block; quoted fields and line breaks handled. |
| created | `src/Htxb.RecipeCore/Model/StdLimits.cs` | `lsl`/`usl` exactly as `bidfgen.stdtable._derive_limits` derives them. |
| created | `src/Htxb.RecipeCore/Validate/LotValidator.cs` | Per-slot validation: names from `tbl_testflow.note` (raw and normalised), limits, bins, counts, drift, DUT/package, slot-summary legend, slot-log names. |
| created | `src/Htxb.RecipeTool/Commands/{StdIngest,StdAlign,ValidateLot}Command.cs` | `std-ingest`, `std-align`, `validate-lot`. |
| created | `tests/Htxb.RecipeCore.Tests/ToolTests.cs` | 5 writer/limit checks, 17 parity checks, 10 validate-lot checks. |
| modified | `Model/TestName.cs` | `Base()` - the re-issue guard (`HTGB1` == `HTGB` == `HTGB_R`). |
| modified | `Adapters/PlanSheetAdapter.cs` | `Remarks` also accepts `remark` (the SQ variant). |
| modified | `RecipeTool/ArgReader.cs`, `Program.cs`, `ToolPaths.cs` | Single-dash options (`-o`), the three commands, the installed tables folder. |

Measured on 2026-11-01:

- **std-align parity.** All 17 shipped tables regenerated from the same workbook and
  `.zadz`: 7536 cells compared, **43 differing**, every one of them `V_gs` - and all
  of them the same case: Python wrote `str(cell("vgs") or "").strip()`, and `0` is
  falsy in Python, so a step applying exactly 0 V lost its value. The C# tool keeps
  `"0"`. Name, limits, units, HW/SW bins, timing, exclusion and remarks match cell
  for cell, including the variant-B SQGW table.
- **validate-lot.** The 9 lots of `2108 post log files`: 8 × 292 checks and 1
  single-slot lot × 16 checks, **0 failed**. The same run on `2108 log files 1`
  fails the four lots it processed - the slot summary still advertises the
  `Std_02b` bins (`706/731/537/756`, `102/132/122`) and the DUT is the old
  `SCT40W120G34AG` - and exits 2 for the five it never touched.
- **Checks.** `test-recipetool.ps1` -> `83 passed, 0 failed, 0 skipped` (was 51).
  BidfGen2 `dev-check.ps1` -> `25/25` Python suites, `14/14` C#, `83/83` recipe.

Three defects found in the Python tools while porting, all fixed by the port:
the single-`.bidf` read (a lot has 24), the name taken through `test_id` instead of
`note` (which hides a `_1`/`_3` swap), and the per-bin count that grouped by
`(hwbin, hwbin_desc, swbin)` and kept one row per bin.
### 2026-11-01 - names read once, not once per measurement

| Type | Paths | Summary |
|---|---|---|
| modified | `src/Htxb.RecipeCore/Ingest/SlotCsvReader.cs` | `ReadStepNames`: distinct, first-seen order, non-step rows dropped - the plan, not the measurements. `bidfgen.tools.std_ingest.logged_step_list` de-duplicated; the port did not. |
| modified | `src/Htxb.RecipeCore/Validate/LotValidator.cs`, `src/Htxb.RecipeTool/Commands/StdIngestCommand.cs` | Both now take step names, not measurement rows. |
| modified | `tests/Htxb.RecipeCore.Tests/ToolTests.cs` | 3 checks: a BRJJ slot log yields 32 distinct names from 66 rows; the pairing guard refuses a short list and a wrong step; the re-issue decorations are the only thing `Base()` ignores. |

Found by running `std-ingest --names-from` against a real lot: every pairing was
refused with *"the log has 66 steps, the workbook 32"*, because the `Test Data`
block has one row per measurement and a step measured twice appears twice. The
older test run never exercised that path - `validate-lot` normalises and
de-duplicates before comparing, so it masked the same mistake.

Re-measured: `Htxb.RecipeTool std-ingest P_TM_BRJJ_B_09_4_32.xlsx --names-from
<10110906873901KA_20261028170220_CSV>` -> `tests 32, renamed 8, without bin 0`
(the eight are `IGSS_20V_1`, `HTGB`, `HTRB6_R`, `HTGB_HT6_R`, `HTRB7_R`,
`HTGB_HT7_R`, `HTRB8_R`, `HTGB_HT8_R`); the same command with the BRF9 workbook
against a BRF9 log is still refused (34 vs 32). `test-recipetool.ps1` ->
`86 passed, 0 failed, 0 skipped`.
## 2026-11-01 - history report: one lot per process, one CSV per lot

Stage 1 of the "recover lots whose recipe was not in the profile list" sub-project.
Reads the site's two folders and writes one report file per lot; it changes nothing.

| Type | Paths | Summary |
|---|---|---|
| created | `src/Htxb.RecipeCore/History/LotScanner.cs` | Resolves a lot from either side of the pair, matches `<14 digits><2 letters>_<14 digits>` (+ `_CSV`, + duplicate " 2"), reads the slot headers, counts slots and statuses, resolves the recipe against the profile CSV, and decides `state`. |
| created | `src/Htxb.RecipeCore/History/LotReportWriter.cs` | The one-row CSV, `<stamp>_<site>_<lot>.csv`, quoting only when needed. |
| modified | `src/Htxb.RecipeCore/Ingest/SlotCsvReader.cs` | `ReadSlotHeader` - the tester's `System Information` row (machine, position, lot, start, status). |
| created | `src/Htxb.RecipeTool/Commands/HistoryCommand.cs` | `history report --lot <folder> [--out DIR]`; exit 0 written, 1 unreadable lot, 2 usage. |
| modified | `src/Htxb.RecipeTool/{Program,ToolPaths}.cs` | Dispatch and the site folder defaults. |
| created | `tests/Htxb.RecipeCore.Tests/HistoryTests.cs` | 11 checks: the folder pattern, all four states, the duplicate folder, the six-hour stale tolerance, the writer, a refused non-lot, and the real 22 archive folders. |

Two things the first run taught:

* the site id in the file name must be the one **our own report wrote** (read from a slot
  RPT), not `[plant] machine_id`: the installed config now says `TTI-0001` while every RPT
  says `TTSZ-2101`, and the config value also drives the vendor `SystemConfig1.xml`, so it
  was not changed here. The disagreement is written into the row's `note`;
* a copy of the whole tree gives the two sides creation times seconds apart in either
  order, so "stale" needs a tolerance - six hours - or every lot looks stale.

Measured on the site: 22 archive folders -> 21 report files (one duplicate folder), **9 full,
9 report-only, 3 not processed**. The nine report-only lots are the recovery list: BRJJ x6 and
SQFJ x3, every one Completed, whose recipes are rows 15 and 17 of the current CSV.
`test-recipetool.ps1` -> 98 passed, 0 failed, 0 skipped (was 87).