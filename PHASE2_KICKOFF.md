# Phase 2 Kick-off — HTML-to-EXE Transformation Build Plan

- **Project:** TTI HTXB Tools & Utilities — EXE Transformation
- **Phase:** 2 of 2 (Phase 1 = history/reference: `proj01 Main Page\PROJECT_HISTORY.md`)
- **Phase 2 root:** `D:\_TTI-Dev\_VSProj\VSProj_00_Main` (moved from old-PC `E:\...` on 2026-09-19)
- **Source (HTML):** `D:\_TTI-Dev\proj01 Main Page`
- **Stack:** C# (.NET 8) + WinForms + Microsoft.Web.WebView2, built with VS Code / `dotnet` CLI
- **Status:** Kick-off

---

## 1. Objective

Wrap every HTML tool under `proj01 Main Page` into a native Windows `.exe` so that:

1. **Protection** — the original HTML/CSS/JS are no longer readable/editable by end users
   (HTML is embedded inside the EXE and served through a WebView2 virtual host).
2. **Function parity** — every tool behaves exactly like the browser version, including
   file/folder selection, CSV/Excel/BiDF parsing, chart rendering, and export-to-Excel.
3. **Small EXE footprint** — large libraries (`.js`, `.wasm`, `.dll`, `.node`, `.pyd`, `.so`,
   images) are NOT embedded in each EXE. They are externalised into a centralised **`data2`**
   folder in the same root directory as the `.exe` and loaded at runtime.

---

## 2. Scope

| # | Tool | HTML source | HTML version | Priority |
|---|---|---|---|---|
| 0 | Tools & Utilities (Launcher) | `TTI HTXB Tools & Utilities.html` | V6.07.121 | High |
| 1 | Lot Extractor | `TTI COBIS-II HTXB Lot Extractor.html` | V6.08.191 | **First (template)** |
| 2 | Temp Uniformity Graph | `TTI Temp_Uniformity_Graph.html` | V6.07.123 | High |
| 3 | BiDF File Analysis | `TTI COBIS-II HTXB BiDF Analysis.html` | V6.07.122 | High |
| 4 | Recipe Translator | `TTI COBIS-II HTXB Recipe Translator.html` | V6.06.193 | Medium |
| 5 | Recipe-to-Std Table | `TTI HTXB Rcp_Std_Table.html` | V6.06.192 | Medium |
| 6 | P2 Data Analysis | `TTI HTXB_P2 Data Analysis.html` | V1.0 (WIP) | Low (defer if needed) |

Lot Extractor is the reference template first: it has NO CDN library (only the file-system API), so
it isolates the WebView2 host work from library-externalisation work. Temp Uniformity Graph (the
Proj7 pilot) is the fallback template if deeper library handling is needed early.

---

## 3. Target Distribution Layout (the `data2` model)

```
<ROOT>   (same root directory that holds the .exe files)
|- TTI_LotExtractor.exe            # thin wrapper, a few MB
|- TTI_TempUniformity.exe
|- TTI_BiDFAnalysis.exe
|- TTI_RecipeTranslator.exe
|- TTI_RcpStdTable.exe
|- TTI_P2DataAnalysis.exe
|- TTI_Utils_Launcher.exe          # replaces the Tools & Utilities page
'- data2/
    |- manifest.json               # version manifest (replaces update-versions.ps1 + fetch-sync)
    |- lib/
    |   |- xlsx.full.min.js        # v0.18.5  (Temp Uniformity)
    |   |- xlsx.min.js             # xlsx-js-style v1.2.0 (BiDF / Recipe / STD / P2)
    |   |- chart.umd.min.js        # Chart.js v4.4.x (BiDF, Temp Uniformity)
    |   |- sql-wasm.js             # sql.js v1.8.0 (BiDF)
    |   |- sql-wasm.wasm           # sql.js worker/wasm (BiDF)
    |- assets/
    |   |- favicon.ico
    |   |- Trio-Tech Logo.png
    |   |- Customer.png
    |   |- HiP247 Long Leads.png
    '- runtime/                    # optional native deps (.dll/.node/.pyd/.so) if any tool needs them
```

Rules:

- `data2` is created ONCE and shared by all tools; each EXE resolves paths relative to
  `AppContext.BaseDirectory` (the folder containing the EXE, i.e. `<ROOT>`).
- The HTML files stay embedded in the EXEs (they are the protected surface). External files live
  ONLY under `data2`.
- `data2\manifest.json` holds `<tool-id> -> <version>` so the launcher and update scripts no longer
  `fetch()` HTML files.

---

## 4. Solution Structure (VS Code / CLI)

```
_VSProj\VSProj_00_Main\
|- TTI_HtxbSuite.sln              # solution (dotnet new sln)
|- src\
|   |- Htxb.Host\                 # SHARED wrapper project (class library)
|   |   |- WebHost.cs             # virtual host + embedded resource serving
|   |   |- Data2Loader.cs         # resolves/serves data2 files (libs + assets)
|   |   |- NativeBridge.cs        # AddHostObjectToScript bridge (file dialogs, fs IO)
|   |   |- Manifest.cs            # reads data2\manifest.json, version atom
|   |   |- Htxb.Host.csproj
|   |- Htxb.LotExtractor\         # generated wrapper per tool
|   |   |- Program.cs
|   |   |- MainForm.cs
|   |   |- Resources\index.html   # input: the deployed HTML (embedded)
|   |   |- Htxb.LotExtractor.csproj
|   |- Htxb.TempUniformity\ ...   # same shape, one project per tool
|   '- Htxb.Launcher\             # launcher EXE (Process.Start + manifest)
'-.data2\                          # local dev copy of the runtime folder (copied to output root on publish)
'- build.ps1                       # one-command build + publish + data2 copy
```

Recommended commands (run inside `VSProj_00_Main`):

```
dotnet new sln -n TTI_HtxbSuite
dotnet new classlib -o src/Htxb.Host -n Htxb.Host
dotnet new winforms -o src/Htxb.LotExtractor -n Htxb.LotExtractor
dotnet sln add src/Htxb.Host/Htxb.Host.csproj src/Htxb.LotExtractor/Htxb.LotExtractor.csproj
dotnet add src/Htxb.LotExtractor/Htxb.LotExtractor.csproj reference src/Htxb.Host/Htxb.Host.csproj
dotnet add src/Htxb.LotExtractor/Htxb.LotExtractor.csproj package Microsoft.Web.WebView2
```

---

## 5. Shared Host Implementation (copy once, customise per tool)

Base it on `Proj7 ...\TTI_TempUniformity\MainForm.cs` but externalise libraries:

1. `MainForm` (WinForms) hosting a full-dock `WebView2`.
2. On `CoreWebView2InitializationCompleted`:
   - `AddWebResourceRequestedFilter("https://app/*", All)`.
   - `WebResourceRequested` handler routes:
     - `/index.html` (embedded resource) -> serve HTML; rewrite `<script src=CDN...>`
       to `https://app/libs/...` and `images/...` to `https://app/assets/...`.
     - `/libs/*`, `/assets/*` -> serve from disk under `data2` (Data2Loader).
     - `/favicon.ico` -> `data2\assets\favicon.ico`.
   - `AddHostObjectToScript("tools", new NativeBridge(...))` for native capabilities.
   - `Navigate("https://app/index.html")`.
3. `NativeBridge` (COM-visible, `[ClassInterface(ClassInterfaceType.AutoDual)]`):
   - `string SelectDirectory()`, `string SelectFiles(filter)`, `string SaveFile(defaultName)`
   - `byte[] ReadFile(path)` / `WriteFile(path, bytes)` / `CreateDirectory`, `ListDirectory`
   - Each mirrors a browser API the tool used (see section 7).

---

## 6. HTML-to-WebView2 Adaptations

| Browser feature | WebView2 behaviour | Adaptation |
|---|---|---|
| `<input type=file webkitdirectory>` | XML-input works only with `--allow-file-access` quirks; prefer host bridge | Rewrite to call `window.tools.SelectDirectory()` then feed paths to the existing parser |
| `window.showDirectoryPicker()` (Lot Extractor) | **Not available** | Replace with `await window.tools.SelectDirectory()` returning a path; parse via `ReadFile`/`ListDirectory` |
| `window.showSaveFilePicker()` (Rcp Std Table) | **Not available** | Replace with `window.tools.SaveFile(name)` |
| Drag-and-drop | Works natively | Keep as-is |
| `fetch()` (launcher version sync) | Works within virtual host | Drop; use `data2\manifest.json` |
| `window.open` / `target=_blank` (launcher) | Blocks `popups` unless configured | Launcher EXE uses `Process.Start` on sibling EXEs instead |
| CDN `<script src>` | Needs offline | Rewrite to `https://app/libs/<file>` served from `data2` |
| `history`/blob URLs / downloads | `AllowNone` default; downloads need `CoreWebView2.DownloadStarting` | Enable download handling / `SaveFile` bridge for `exportXLSX`, `printGraph` |

For each tool, patch ONLY the small browser-API shims in the embedded HTML; keep the analytic logic
intact (it becomes protected inside the EXE).

---

## 7. Per-Tool Bridge / Library Matrix

| Tool | Embedded HTML | data2 libs | Native bridge needed |
|---|---|---|---|
| Lot Extractor | yes | (assets only) | directory picker, item read, recursive list, write tree |
| Temp Uniformity Graph | yes | xlsx.full, chart.js, assets | folder picker / `webkitdirectory` shim, save-file for export |
| BiDF Analysis | yes | sql.js+wasm, xlsx-js-style, chart.js, assets | folder picker, db read, save-file |
| Recipe Translator | yes | xlsx-js-style, assets | file open, save-file |
| Recipe-to-Std Table | yes | xlsx-js-style, assets | file open, save-file picker |
| P2 Data Analysis | yes | xlsx-js-style, assets | file open (multi), save-file |
| Launcher | yes | assets | process launch + manifest read |

---

## 8. Build & Publish (size control)

- Target: `net8.0-windows`, `UseWindowsForms=true`, `OutputType=WinExe`.
- Publish mode: **Framework-dependent, NOT self-contained/single-file** (avoids the ~163 MB pilot
  output). Requires .NET 8 Desktop Runtime on target machines, or ship the `Microsoft.WindowsDesktop.App`
  files once inside `data2\runtime`.
- Keep `<PublishSingleFile>` OFF. Expected EXE size target: **< 5 MB each**.
- The WebView2 SDK assemblies themselves are candidates for `data2\runtime` so they are not
  duplicated per EXE; use `WebView2Loader.dll` handling from `data2`.
- `build.ps1` flow:
  1. `dotnet restore` / `dotnet build -c Release`
  2. `dotnet publish src/<Tool> -c Release -o dist/<Tool>`
  3. Copy `_VSProj\VSProj_00_Main\.data2\*` -> `dist\data2\` (merge, no overwrite of newer)
  4. Validate with the size-check step (EXE must be small, `data2` holds the bulk).

---

## 9. Acceptance Criteria

1. Each tool opens as a native window with no address bar; F12/devtools disabled
   (set `AreDevToolsEnabled=false`, `AreBrowserAcceleratorKeysEnabled=false`).
2. Deployed workflow works: folder input -> parse -> export, matching the HTML version.
3. Lot Extractor correctly shows "Completed"/"Pass" status (V6.08.191 logic) and exports
   `<lot>_CSV\SlotNN\...` through the native directory-picker.
4. Excel export produces valid `.xlsx` (xlsx from `data2`), BiDF loads its databases via
   `data2\lib\sql-wasm.js`.
5. EXEs run with `data2` present in the same root; each EXE `File size < 5 MB`.
6. `data2` is shared — a single library update updates all tools (no per-EXE copy).
7. Launcher starts sibling EXEs and shows versions from `data2\manifest.json`.
8. HTML sources embedded in EXEs are NOT readable as plain files on disk.

---

## 10. Milestones

| Milestone | Deliverable | Exit criteria |
|---|---|---|
| M0 | Host template + data2 loader + manifest | Lot Extractor EXE runs, dir-picker works, size < 5 MB |
| M1 | Lot Extractor complete | Full workflow passes; status V6.08.191 correct |
| M2 | Temp Uniformity Graph | Graph + export work from `data2` libs |
| M3 | BiDF Analysis | sql.js loads from `data2`, Excel export ok |
| M4 | Recipe Translator + Std Table | Convert/export flow ok |
| M5 | P2 Data Analysis (if in scope) | Dashboard + export ok |
| M6 | Launcher EXE | Launches all tools, manifest versions shown |
| M7 | Packaging + `build.ps1` + docs | Clean rebuild produces full `dist\` with shared `data2` |

---

## 11. Risks & Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| File System Access API has no WebView2 equivalent | Many tools blocked | Build NativeBridge early (M0) and keep it tool-agnostic |
| sql.js WASM served over custom scheme | WASM may be blocked by origin policy | Use `CoreWebView2.SetVirtualHostNameToFolderMapping` for `data2` (simplest: map `https://data2.local/` -> `data2` folder directly) |
| Chart.js/XLSX from disk need correct MIME types | Scripts fail to load | Set MIME in `CreateWebResourceResponse` or use virtual-host mapping with proper content types |
| Offline machines / no WebView2 runtime | Blank window | Detect runtime, prompt install, or bundle in `data2\runtime` |
| HTML slightly different between ProjX source and deployed copy | Behaviour drift | Always wrap the `proj01 Main Page` copy (canonical) |
| WIP tool (P2) not ready | Scope creep | Make P2 optional / defer |

---

## 12. Immediate First Steps (next session)

1. Open this session root: `D:\_TTI-Dev\_VSProj\VSProj_00_Main`.
2. Scaffold solution + shared host (section 4 commands).
3. Port the Proj7 pilot `MainForm.cs` pattern into `Htxb.Host` and add `Data2Loader` +
   `NativeBridge`.
4. Wire `data2\manifest.json` + `assets` + `lib` with the four CDN libraries downloaded locally.
5. Wrap **Lot Extractor** as M0/M1; demo the directory-picker bridge.
6. Once the template is proven, generate the remaining wrappers from the same shape.

> Reference material: `proj01 Main Page\PROJECT_HISTORY.md` (Phase 1) covers every tool's history,
> dependencies, APIs, and the pilot-problem analysis (163 MB single-file). Keep it open during M0-M7.
