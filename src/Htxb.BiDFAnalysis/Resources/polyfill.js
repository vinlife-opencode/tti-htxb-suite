// TTI tool — offline Excel export shim
// XLSX.writeFile() relies on browser download support, which WebView2 does not
// expose like Chrome. This polyfill re-routes export through the native bridge:
//   SaveFile(name) -> user picks a path -> WriteFileBase64 writes workbook bytes.
// NOTE: SheetJS assigns an EMPTY global XLSX first and fills it in later, so we
// cannot intercept at assignment time; we patch the method once the library is
// fully loaded (DOMContentLoaded / load fallback).

// PLUS: BIDF `(NEW)` column adaptation.
// Some producers write columns whose names carry a trailing ` (NEW)` / `(NEW)`
// suffix (e.g. `hwbin_desc (NEW)`, `exec_set (NEW)`, `theadid(NEW)`). Other
// producers write the spec-clean name (`hwbin_desc`, `exec_set`, `theadid`).
// To make the tool parse and display either variant identically we:
//   1. On DB open, rename any `(NEW)`-suffixed column to its spec-clean name
//      (recreate-table method, same as BIDF_NEW_Column_Cleanup.md).
//   2. Wrap database.exec so the page's own hard-coded SQL that still uses the
//      quoted names like "exec_set (NEW)" keeps resolving to the clean column.
// `SQL` is declared as `let SQL` in the page (global lexical binding), so we
// must reference it by bare name rather than window.SQL.
(function () {
  'use strict';

  // --- Header version badge sync (UI display rule: header == cover page) -----
  var EXE_VERSION = 'V6.10.302';
  function applyExeVersion() {
    try {
      var tags = document.querySelectorAll('.logo-tag');
      for (var i = 0; i < tags.length; i++) tags[i].textContent = EXE_VERSION;
    } catch (e) { /* ignore */ }
  }

  // --- Standardized print/PDF header date — Long Form ("October 9, 2026") ---
  // The page never stamps the printout with the print day (the only date on
  // the PDF header would otherwise be the browser's locale-dependent short
  // date), so inject a print-only header line in deterministic Long Form.
  // Explicit month names (not toLocaleDateString) keep every machine identical.
  var TTI_PRINT_MONTHS = ['January', 'February', 'March', 'April', 'May', 'June',
    'July', 'August', 'September', 'October', 'November', 'December'];

  function ttiLongFormToday() {
    var d = new Date();
    return TTI_PRINT_MONTHS[d.getMonth()] + ' ' + d.getDate() + ', ' + d.getFullYear();
  }

  function ensurePrintDateHeader(containerId) {
    try {
      if (!document.getElementById('tti-print-date-style')) {
        var st = document.createElement('style');
        st.id = 'tti-print-date-style';
        st.textContent = '#tti-print-date{display:none;}'
          + '@media print{#tti-print-date{display:block !important;width:100%;'
          + 'text-align:right;font-size:11px;color:#333;margin:0 0 8px 0;}}';
        document.head.appendChild(st);
      }
      var host = document.getElementById(containerId);
      if (host && !document.getElementById('tti-print-date')) {
        var div = document.createElement('div');
        div.id = 'tti-print-date';
        div.textContent = ttiLongFormToday();
        host.insertBefore(div, host.firstChild);
      }
      // Re-stamp on every print so long-open windows print the current day.
      if (!window.__ttiPrintDateHooked) {
        window.__ttiPrintDateHooked = true;
        window.addEventListener('beforeprint', function () {
          var el = document.getElementById('tti-print-date');
          if (el) el.textContent = ttiLongFormToday();
        });
      }
    } catch (e) { /* never break the tool */ }
  }

  function initNoBridge() {
    applyExeVersion();
    ensurePrintDateHeader('dashboard-screen');
  }
  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', initNoBridge);
  } else {
    initNoBridge();
  }

  // ---- Direct *.bidf.gz open ------------------------------------------------
  // The page only accepts plain *.bidf: the picker filter hides .gz,
  // handleFile() rejects any other name, and the raw bytes go straight into
  // `new SQL.Database(...)`. Opening a gzip'd BiDF (exactly what this tool's own
  // Batch Compress produces) therefore failed. We wrap handleFile() — a
  // page-level function declaration, so it lives on `window` and every caller
  // (picker, drop zone, later code) resolves through it — and hand the original
  // function a synthetic *.bidf File carrying the decompressed bytes:
  //   * a plain *.bidf passes through untouched (no behaviour change),
  //   * gzip is detected by extension OR magic bytes (1F 8B), so a renamed file
  //     still works,
  //   * after the load the dashboard keeps showing the file the operator really
  //     opened (its .gz name and its compressed size),
  //   * exports are unaffected: currentFile is the synthetic "x.bidf", so the
  //     Excel name is still "x.xlsx".
  var TTI_GZ_TRIES = 0;
  var TTI_SQLITE_MAGIC = 'SQLite format 3\u0000';

  function ttiIsGzName(name) {
    return /\.gz$/i.test(String(name || ''));
  }

  function ttiHasGzMagic(file) {
    return file.slice(0, 2).arrayBuffer().then(function (buf) {
      var b = new Uint8Array(buf);
      return b.length >= 2 && b[0] === 0x1F && b[1] === 0x8B;
    }).catch(function () { return false; });
  }

  function ttiGunzip(blob) {
    if (typeof DecompressionStream !== 'function') {
      return Promise.reject(new Error('DecompressionStream is not available in this runtime.'));
    }
    var stream = blob.stream().pipeThrough(new DecompressionStream('gzip'));
    return new Response(stream).arrayBuffer();
  }

  function ttiLooksLikeSqlite(buf) {
    if (!buf || buf.byteLength < TTI_SQLITE_MAGIC.length) return false;
    var b = new Uint8Array(buf, 0, TTI_SQLITE_MAGIC.length);
    for (var i = 0; i < TTI_SQLITE_MAGIC.length; i++) {
      if (b[i] !== TTI_SQLITE_MAGIC.charCodeAt(i)) return false;
    }
    return true;
  }

  function ttiToast(msg, kind) {
    try {
      if (typeof showToast === 'function') { showToast(msg, kind); return; }
    } catch (e) { /* fall through to console */ }
    console.warn('[polyfill] ' + msg);
  }

  // "x.bidf.gz" -> "x.bidf"; "x.gz" -> "x.bidf"; anything else keeps a .bidf tail
  // so the page's own extension gate still accepts the synthetic file.
  function ttiInnerName(name) {
    var n = String(name || '').replace(/\.gz$/i, '');
    if (!/\.bidf$/i.test(n)) n += '.bidf';
    return n || 'opened.bidf';
  }

  // Restore the header to the file the operator actually opened (the synthetic
  // name/size from the inner load would otherwise be shown).
  function ttiShowOpenedGz(file) {
    try {
      var nameEl = document.getElementById('loaded-filename');
      if (nameEl) nameEl.innerText = file.name;
      var sizeEl = document.getElementById('loaded-filesize');
      if (sizeEl) {
        sizeEl.innerText = (typeof formatBytes === 'function')
          ? formatBytes(file.size) : (file.size + ' B');
      }
    } catch (e) { /* never break the tool */ }
  }

  function ttiWrapHandleFile() {
    if (typeof handleFile !== 'function') return false;
    if (handleFile.__ttiGz) return true;

    var orig = handleFile;
    var wrapped = async function (file) {
      // Let the page handle "engine still loading" with its own message.
      try {
        if (typeof SQL === 'undefined' || !SQL) return orig.apply(this, arguments);
      } catch (e) { /* TDZ or odd binding — just fall through */ }

      var isGz = false;
      try {
        isGz = !!(file && (ttiIsGzName(file.name) || await ttiHasGzMagic(file)));
      } catch (e) {
        isGz = false;
      }
      if (!isGz) return orig.apply(this, arguments); // plain .bidf: unchanged path

      var plain;
      try {
        plain = await ttiGunzip(file);
      } catch (e) {
        console.error('[polyfill] gzip decompress failed:', e);
        ttiToast('Could not decompress "' + file.name + '" — it is not valid gzip data.', 'danger');
        return;
      }
      if (!ttiLooksLikeSqlite(plain)) {
        ttiToast('"' + file.name + '" does not contain a BiDF (SQLite) database.', 'danger');
        return;
      }

      var synthetic = new File([plain], ttiInnerName(file.name),
        { type: 'application/x-sqlite3' });
      var result = await orig.call(window, synthetic);
      ttiShowOpenedGz(file);
      return result;
    };
    wrapped.__ttiGz = true;
    window.handleFile = wrapped;
    return true;
  }

  function ttiUpdateUploadHint() {
    try {
      var zone = document.getElementById('drop-zone');
      if (!zone) return;
      var title = zone.querySelector('.upload-title');
      if (title && !/\.gz/i.test(title.textContent)) {
        title.textContent = 'Drop your *.bidf or *.bidf.gz file here';
      }
      var desc = zone.querySelector('.upload-desc');
      if (desc && !/\.gz/i.test(desc.textContent)) {
        desc.textContent = 'Drag and drop a SQLite format BIDF file (plain *.bidf or '
          + 'compressed *.bidf.gz), or click to browse files. The database will be '
          + 'processed locally on your computer.';
      }
    } catch (e) { /* never break the tool */ }
  }

  function ttiInstallGzSupport() {
    try {
      var input = document.getElementById('file-input');
      if (input) input.setAttribute('accept', '.bidf,.gz,.bidf.gz,application/gzip');
      ttiUpdateUploadHint();
    } catch (e) { /* never break the tool */ }

    if (ttiWrapHandleFile()) return;
    if (TTI_GZ_TRIES++ > 100) return; // ~5 s at 50 ms, then give up quietly
    setTimeout(ttiInstallGzSupport, 50);
  }

  // Our script is injected in <head>, before the page declares handleFile(), so
  // install on DOMContentLoaded (page script has run) with a short retry guard.
  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', ttiInstallGzSupport);
  } else {
    ttiInstallGzSupport();
  }
  window.addEventListener('load', ttiInstallGzSupport);

  // ---- Excel export: tolerate tables missing from the log -------------------
  // exportToExcel() walks a fixed sheetMapping (tbl_run, tbl_presence, ...,
  // tbl_addinfo ...) and does `database.exec('PRAGMA table_info(<table>)')[0]
  // .values`. A BiDF that simply does not contain one of those tables makes
  // `exec` return an EMPTY result, so `[0]` is undefined and the whole export
  // dies with "Cannot read properties of undefined (reading 'values')" - the
  // operator only sees "Failed to compile Excel file."
  // Rev 1.4 EDA logs have no `tbl_addinfo` at all, so their XLSX export always
  // failed. Instead of guessing which tables exist, the wrapper swaps every
  // missing table for a placeholder sheet that says so, runs the original
  // export untouched, then restores the mapping. Present tables are exported
  // exactly as before, so COBIS workbooks are unchanged.
  var TTI_EXPORT_TRIES = 0;

  function ttiTableExists(db, name) {
    try {
      var res = db.exec("SELECT name FROM sqlite_master WHERE type='table' AND name='"
        + String(name).replace(/'/g, "''") + "'");
      return !!(res && res.length && res[0].values && res[0].values.length);
    } catch (e) {
      return true; // cannot tell -> let the original export decide
    }
  }

  function ttiMissingTableHeader(name) {
    return ['Table "' + name + '" is not present in this BiDF revision'];
  }

  function ttiWrapExcelExport() {
    if (typeof exportToExcel !== 'function') return false;
    if (exportToExcel.__ttiExcel) return true;

    var orig = exportToExcel;
    var wrapped = async function () {
      var saved = null;
      try {
        if (typeof database !== 'undefined' && database &&
            typeof sheetMapping !== 'undefined' && Array.isArray(sheetMapping)) {
          saved = sheetMapping.slice();
          for (var i = 0; i < sheetMapping.length; i++) {
            var m = sheetMapping[i];
            if (!m || !m.table) continue;
            if (ttiTableExists(database, m.table)) continue;
            sheetMapping[i] = {
              table: null,
              sheet: m.sheet,
              dummyHeader: ttiMissingTableHeader(m.table)
            };
          }
        }
      } catch (e) {
        console.error('[polyfill] sheet guard failed:', e);
      }
      try {
        return await orig.apply(this, arguments);
      } finally {
        if (saved) {
          for (var k = 0; k < saved.length && k < sheetMapping.length; k++) {
            sheetMapping[k] = saved[k];
          }
        }
      }
    };
    wrapped.__ttiExcel = true;
    window.exportToExcel = wrapped;
    return true;
  }

  // The page declares exportToExcel() in its own script (a function
  // declaration, so it lives on window); install on DOMContentLoaded with a
  // short retry guard, like the .gz wrapper above.
  function ttiInstallExcelGuard() {
    if (ttiWrapExcelExport()) return;
    if (TTI_EXPORT_TRIES++ > 100) return; // ~5 s at 50 ms
    setTimeout(ttiInstallExcelGuard, 50);
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', ttiInstallExcelGuard);
  } else {
    ttiInstallExcelGuard();
  }
  window.addEventListener('load', ttiInstallExcelGuard);

  var tools = window.tools;
  if (!tools) return;
  function bytesToBase64(u8) {
    var bin = '';
    var chunkSize = 0x8000;
    for (var i = 0; i < u8.length; i += chunkSize) {
      bin += String.fromCharCode.apply(null, u8.subarray(i, i + chunkSize));
    }
    return btoa(bin);
  }

  function patch() {
    var XLSX = window.XLSX;
    if (!XLSX || typeof XLSX.write !== 'function' || typeof XLSX.writeFile !== 'function') return false;
    if (XLSX.__ttiPatched) return true;
    XLSX.writeFile = function (wb, name) {
      var path = tools.SaveFile(name || 'export.xlsx');
      if (!path) return; // user cancelled the save dialog
      var buf = XLSX.write(wb, { bookType: 'xlsx', type: 'array' });
      var u8 = (buf instanceof Uint8Array) ? buf : new Uint8Array(buf);
      tools.WriteFileBase64(path, bytesToBase64(u8));
    };
    XLSX.__ttiPatched = true;
    return true;
  }

  if (!patch()) {
    document.addEventListener('DOMContentLoaded', patch);
    window.addEventListener('load', patch);
  }

  // ---- BIDF `(NEW)` column adaptation ------------------------------------
  var NEW_SUFFIXES = [' (NEW)', '(NEW)'];

  // Rewrite double-quoted identifiers in a SQL string so that names carrying a
  // ` (NEW)` / `(NEW)` suffix map to their spec-clean form. The page embeds a
  // few such quoted names (e.g. "exec_set (NEW)", "hwbin_desc (NEW)"), which
  // must keep working after we have stripped the suffix from the schema, and
  // must also work when a file already uses the clean name.
  function rewriteSql(sql) {
    if (sql.indexOf('(NEW)') === -1) return sql;
    return sql.replace(/"([^"]*)"/g, function (whole, ident) {
      var clean = ident;
      for (var i = 0; i < NEW_SUFFIXES.length; i++) {
        clean = clean.split(NEW_SUFFIXES[i]).join('');
      }
      return clean === ident ? whole : '"' + clean + '"';
    });
  }

  // Wrap a freshly opened database so the page's queries that still use the
  // quoted `(NEW)` names continue to resolve.
  function wrapExec(db) {
    if (db.__ttiExecPatched) return;
    var orig = db.exec.bind(db);
    db.exec = function (sql, params, config) {
      if (typeof sql === 'string') {
        sql = rewriteSql(sql);
        sql = ttiFixBinQuery(db, sql);
      }
      return orig(sql, params, config);
    };
    db.__ttiExecPatched = true;
  }

  function ttiHasColumn(db, table, col) {
    try {
      var res = db.exec('PRAGMA table_info("' + table + '")');
      if (!res || !res.length || !res[0].values) return false;
      for (var i = 0; i < res[0].values.length; i++) {
        if (String(res[0].values[i][1]).toLowerCase() === String(col).toLowerCase()) return true;
      }
    } catch (e) { /* treat as absent */ }
    return false;
  }

  // BIN Map source for rev-1.4 logs (EDA).
  // The page reads the bin description from tbl_result and falls back to
  // tbl_summary in a try/catch. In a rev 1.4 log tbl_result has no such column,
  // and SQLite answers an unknown double-quoted identifier with the literal
  // text instead of raising - so the fallback never fires and every cell of the
  // BIN Map (screen and Excel alike) reads "hwbin_desc". Point that query at
  // tbl_summary, where the column really is; rev 1.5 logs keep using
  // tbl_result untouched.
  function ttiFixBinQuery(db, sql) {
    if (!/\btbl_result\b/i.test(sql)) return sql;
    if (!/"hwbin_desc( \(NEW\))?"/i.test(sql)) return sql;
    if (ttiHasColumn(db, 'tbl_result', 'hwbin_desc')) return sql;
    if (!ttiHasColumn(db, 'tbl_summary', 'hwbin_desc')) return sql;
    return sql.replace(/\btbl_result\b/gi, 'tbl_summary');
  }

  // Port of BIDF_NEW_Column_Cleanup.md: for every table, rebuild it so any
  // column whose name contains `(NEW)` is renamed by stripping the suffix.
  // Column data/type/default/NULL/PK are preserved; nothing is dropped.
  function quoteDefault(dflt) {
    if (dflt === null || dflt === undefined) return '';
    return 'DEFAULT ' + dflt;
  }

  function stripNewColumns(db) {
    var res = db.exec("SELECT name FROM sqlite_master WHERE type='table'");
    if (!res || !res.length) return;
    var tables = res[0].values.map(function (r) { return r[0]; });

    for (var ti = 0; ti < tables.length; ti++) {
      var t = tables[ti];
      if (t === 'sqlite_sequence') continue;

      var info = db.exec('PRAGMA table_info("' + t + '")');
      if (!info || !info.length) continue;
      var cols = info[0].values; // [cid, name, type, notnull, dflt_value, pk]

      var newcols = [];
      var changed = false;
      for (var ci = 0; ci < cols.length; ci++) {
        var c = cols[ci];
        var oldName = c[1];
        var clean = oldName;
        for (var s = 0; s < NEW_SUFFIXES.length; s++) {
          clean = clean.split(NEW_SUFFIXES[s]).join('');
        }
        if (clean !== oldName) changed = true;
        newcols.push({ new: clean, type: c[2], notnull: c[3], dflt: c[4], pk: c[5] });
      }
      if (!changed) continue;

      var parts = [];
      var pkcols = [];
      for (var n = 0; n < newcols.length; n++) {
        var col = newcols[n];
        var def = col.type || '';
        if (col.notnull) def += ' NOT NULL';
        var dq = quoteDefault(col.dflt);
        if (dq) def += ' ' + dq;
        parts.push('"' + col.new + '" ' + def);
        if (col.pk > 0) pkcols.push('"' + col.new + '"');
      }
      if (pkcols.length) parts.push('PRIMARY KEY (' + pkcols.join(', ') + ')');

      var tmp = t + '_tmp';
      var colList = [];
      for (var k = 0; k < newcols.length; k++) colList.push('"' + newcols[k].new + '"');
      var sel = colList.join(', ');

      db.run('CREATE TABLE "' + tmp + '" (' + parts.join(', ') + ')');
      db.run('INSERT INTO "' + tmp + '" (' + sel + ') SELECT ' + sel + ' FROM "' + t + '"');
      db.run('DROP TABLE "' + t + '"');
      db.run('ALTER TABLE "' + tmp + '" RENAME TO "' + t + '"');
    }
  }

  // Patch SQL.Database so every DB opened from a file is adapted. Called once
  // the page's sql.js engine is ready (SQL is a global `let`, so bare name).
  function patchDatabaseEngine() {
    if (typeof SQL === 'undefined' || !SQL) return false;
    if (typeof SQL.Database !== 'function') return false;
    if (SQL.__ttiPatched) return true;

    var Orig = SQL.Database;
    function TTI_Database() {
      var data = arguments.length > 0 ? arguments[0] : null;
      var db = Reflect.construct(Orig, Array.prototype.slice.call(arguments),
                                 new.target || TTI_Database);
      if (data instanceof Uint8Array || data instanceof ArrayBuffer) {
        try {
          stripNewColumns(db);
        } catch (e) {
          console.error('[polyfill] strip (NEW) columns failed:', e);
        }
        wrapExec(db);
      }
      return db;
    }
    TTI_Database.prototype = Orig.prototype;
    SQL.Database = TTI_Database;
    SQL.__ttiPatched = true;
    return true;
  }

  // The page assigns SQL asynchronously after its `load` handler runs, so poll
  // until the engine is ready (with a generous ceiling), then install the hook.
  if (!patchDatabaseEngine()) {
    var tries = 0;
    var timer = setInterval(function () {
      tries++;
      if (patchDatabaseEngine() || tries > 400) clearInterval(timer); // ~20s max
    }, 50);
  }
})();
