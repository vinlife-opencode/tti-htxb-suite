// TTI Temperature Uniformity — EXE polyfill (V6.10.092)
// Part A (existing): offline Excel export shim.
//   XLSX.writeFile() relies on browser download support, which WebView2 does not
//   expose like Chrome. Re-routes export through the native bridge:
//     SaveFile(name) -> user picks a path -> WriteFileBase64 writes workbook bytes.
//   NOTE: SheetJS assigns an EMPTY global XLSX first and fills it in later, so we
//   cannot intercept at assignment time; we patch the method once the library is
//   fully loaded (DOMContentLoaded / load fallback).
// Part B (new): GraphTec GL220 CSV support (same analysis feature, no HTML edit).
//   The canonical page handles .dad / _Rpt.xlsx / generic .xlsx but has no CSV
//   branch (its drop filter even rejects .csv). This polyfill adds a .csv path
//   that parses the GRAPHTEC GL220 logger format into the page's own
//   allHeaders/allRows/numericCols shape, then reuses populateAxisSelectors,
//   renderGraph, updateMetaSummary, toggleDataTable and showFileScreen unchanged:
//     - header block: Vendor/Model GL220, Sampling, Start/Stop time, Amp setting
//       (CH1..CH10 TEMP TC_K degC), then a lone "Data" line, a header row
//       (No., Date&Time, ms, CH1..CH10, Alarm1-10, AlarmOut) and a units row.
//     - Timestamp = Date&Time (+ .ms when ms is non-zero); values carry a
//       leading '+' and open sensors read ' BURNOUT' -> stored as '' (gap).
//     - channels with <50% valid readings (e.g. open CH10) stay listed but are
//       deselected by default so uniformity stats/chart are not skewed.
(function () {
  'use strict';

  var EXE_VERSION = 'V6.10.092';

  // --- Header version badge sync (UI display rule: header == cover page) -----
  function applyExeVersion() {
    try {
      var tags = document.querySelectorAll('.version-badge');
      for (var i = 0; i < tags.length; i++) tags[i].textContent = EXE_VERSION;
    } catch (e) { /* ignore */ }
  }

  // --- Upload card wording: advertise the new inputs ---------------------------
  function updateUploadText() {
    try {
      var desc = document.querySelector('.upload-desc');
      if (desc && desc.getAttribute('data-tti-csv') !== '1') {
        desc.textContent = 'Select a temperature log file (.xlsx / .dad / _Rpt.xlsx / GraphTec .csv / .gbd) to generate a uniformity graph.';
        desc.setAttribute('data-tti-csv', '1');
      }
      var fi = document.getElementById('file-input');
      if (fi && fi.getAttribute('accept') !== null && fi.getAttribute('accept').indexOf('.gbd') === -1) {
        fi.setAttribute('accept', fi.getAttribute('accept') + ',.gbd');
      }
    } catch (e) { /* ignore */ }
  }

  // --- Standardized print/PDF header date — Long Form ("October 9, 2026") ----
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

  // --- Part A: offline Excel export shim (needs native bridge) ---------------
  function installXlsxShim() {
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
  }

  // --- Part B: GraphTec CSV (pure JS, works with or without bridge) ----------
  function splitCsvLine(ln) {
    var out = [], cur = '', inQ = false;
    for (var i = 0; i < ln.length; i++) {
      var ch = ln[i];
      if (ch === '"') {
        if (inQ && ln[i + 1] === '"') { cur += '"'; i++; }
        else inQ = !inQ;
      } else if (ch === ',' && !inQ) { out.push(cur); cur = ''; }
      else cur += ch;
    }
    out.push(cur);
    return out;
  }

  function parseGraphTecCsv(text, fileName) {
    var overlay = document.getElementById('progress-overlay');
    function fail(msg) {
      try { if (overlay) overlay.style.display = 'none'; } catch (e) {}
      try { showToast(msg, 'danger'); } catch (e) {}
    }
    try {
      try { setProgress(10, 'Parsing GraphTec CSV...'); } catch (e) {}
      var lines = String(text || '').split(/\r\n|\n|\r/);

      // Locate the lone "Data" marker; header row follows, then units row.
      var dataIdx = -1;
      for (var i = 0; i < lines.length; i++) {
        if (lines[i].trim() === 'Data') { dataIdx = i; break; }
      }
      var tsIdx = 1, msIdx = 2, chIdxs = [], chNames = [], startRow = -1;
      if (dataIdx !== -1 && dataIdx + 2 < lines.length) {
        var header = splitCsvLine(lines[dataIdx + 1]);
        for (var c = 0; c < header.length; c++) {
          var h = (header[c] || '').trim();
          if (/^date.*time$/i.test(h)) tsIdx = c;
          else if (/^ms$/i.test(h)) msIdx = c;
          else if (/^CH\d+$/i.test(h)) { chIdxs.push(c); chNames.push(h.toUpperCase()); }
        }
        startRow = dataIdx + 3; // skip the units row (NO.,Time,ms,degC,...)
      } else {
        // Generic fallback: first non-empty line is the header row.
        for (var g = 0; g < lines.length; g++) {
          if (!lines[g] || !lines[g].trim()) continue;
          var gh = splitCsvLine(lines[g]);
          for (var gc = 0; gc < gh.length; gc++) {
            var ghv = (gh[gc] || '').trim();
            if (/^date.*time$/i.test(ghv)) tsIdx = gc;
            else if (/^ms$/i.test(ghv)) msIdx = gc;
            else if (/^CH\d+$/i.test(ghv)) { chIdxs.push(gc); chNames.push(ghv.toUpperCase()); }
          }
          startRow = g + 1;
          // skip a units row if present (degC/ms/TIME markers)
          if (startRow < lines.length && /degC/i.test(lines[startRow])) startRow++;
          break;
        }
      }
      if (!chIdxs.length) { fail('No CH1..CHn temperature columns found in CSV.'); return; }

      var rows = [];
      var validCounts = [];
      for (var k = 0; k < chNames.length; k++) validCounts.push(0);
      for (var r = startRow; r < lines.length; r++) {
        var ln = lines[r];
        if (!ln || !ln.trim()) continue;
        var p = splitCsvLine(ln);
        if (p.length <= Math.max(tsIdx, msIdx)) continue;
        var ts = (p[tsIdx] || '').trim();
        if (!ts) continue;
        var ms = p.length > msIdx ? (p[msIdx] || '').trim() : '';
        if (ms && ms !== '000' && /^\d{1,3}$/.test(ms)) ts += '.' + ms;
        var row = { 'Timestamp': ts };
        var hasTemp = false;
        for (var ci = 0; ci < chIdxs.length; ci++) {
          var raw = p.length > chIdxs[ci] ? (p[chIdxs[ci]] || '').trim() : '';
          if (!raw || /BURNOUT/i.test(raw)) { row[chNames[ci]] = ''; continue; }
          var v = parseFloat(raw.charAt(0) === '+' ? raw.substring(1) : raw);
          if (isNaN(v)) { row[chNames[ci]] = ''; continue; }
          row[chNames[ci]] = v;
          hasTemp = true;
          validCounts[ci]++;
        }
        if (!hasTemp) continue;
        rows.push(row);
      }
      if (!rows.length) { fail('No valid temperature data found in CSV.'); return; }

      // Hand over to the page's own analysis pipeline (same as parseDAD).
      finishParsedRows(rows, chNames, validCounts, fileName || 'graphtec.csv', 'csv', 'GraphTec CSV');
    } catch (err) {
      fail('Failed to parse CSV file: ' + (err && err.message ? err.message : err));
    }
  }

  // Shared tail: hand parsed rows to the page's own analysis pipeline (same as
  // parseDAD) — selectors, chart, stats, tables, export defaults, file screen.
  function finishParsedRows(rows, chNames, validCounts, fileName, fileType, kindLabel) {
    var overlay = document.getElementById('progress-overlay');
    allHeaders = ['Timestamp'].concat(chNames);
    allRows = rows;
    currentFileType = fileType;
    currentFileName = fileName;
    numericCols = [];
    for (var ni = 1; ni < allHeaders.length; ni++) numericCols.push(ni);

    try {
      document.getElementById('sheet-select').style.display = 'none';
      document.querySelector('.sheet-picker label').style.display = 'none';
    } catch (e) {}
    populateAxisSelectors();
    var xSel = document.getElementById('axis-x');
    if (xSel) xSel.selectedIndex = 0;
    // Pre-select Timestamp as X and every well-populated channel as Y;
    // mostly-open channels (e.g. unconnected CH10) stay listed but off.
    var ySel = document.getElementById('axis-y-series');
    if (ySel) {
      var total = rows.length;
      for (var yi = 0; yi < ySel.options.length; yi++) {
        var idx = parseInt(ySel.options[yi].value, 10);
        if (idx === 0 || isNaN(idx)) { ySel.options[yi].selected = false; continue; }
        ySel.options[yi].selected = (validCounts[idx - 1] / total) >= 0.5;
      }
    }
    try {
      document.getElementById('file-path-display').style.display = 'flex';
      document.getElementById('file-path-text').innerText = currentFileName;
    } catch (e) {}
    try { setProgress(100, 'Done!'); } catch (e) {}
    renderGraph();
    updateMetaSummary();
    // A 50k-row data table costs ~500k DOM cells; hide it by default for
    // large files (user can still toggle it on). Chart + stats are unaffected.
    try {
      if (rows.length > 5000) {
        var tblCheck = document.getElementById('show-table');
        if (tblCheck) tblCheck.checked = false;
      }
    } catch (e) {}
    autoExportDefault();
    toggleDataTable();
    setTimeout(function () {
      try { if (overlay) overlay.style.display = 'none'; } catch (e2) {}
      try { showFileScreen(); } catch (e3) {}
    }, 300);
    var sparse = [];
    for (var s = 0; s < chNames.length; s++) {
      if (validCounts[s] / rows.length < 0.5) sparse.push(chNames[s]);
    }
    var msg = 'Loaded ' + rows.length + ' readings x ' + chNames.length + ' channels from ' + kindLabel + '.';
    if (sparse.length) msg += ' (' + sparse.join(', ') + ' open - deselected.)';
    if (rows.length > 5000) msg += ' Export interval auto-set to every 10 sec (changeable).';
    try { showToast(msg); } catch (e) {}
  }

  // --- Part E: GraphTec GL220 binary (.gbd) support (no HTML edit) ------------
  // Layout (verified against 260918-210955_UG.GBD, same session as the CSV):
  // 8,192-byte ASCII header ($Common HeaderSiz, $$Data Counts/Sample/Order,
  // $$Time Trigger) then Counts records of big-endian int16: the Order-listed
  // channels (CH1..CH10 here) plus trailing Alarm words, 2 bytes each.
  // TEMP values are raw/10 (1 decimal, matches the logger display); raw values
  // >= 32000 are open-sensor sentinels (CH10 reads 32765 = CSV 'BURNOUT').
  // Timestamps are synthesised as Trigger + i * Sample (page format
  // "YYYY-MM-DD HH:MM:SS"), then flow through finishParsedRows like CSV.
  function parseGbdFile(buf, fileName) {
    var overlay = document.getElementById('progress-overlay');
    function fail(msg) {
      try { if (overlay) overlay.style.display = 'none'; } catch (e) {}
      try { showToast(msg, 'danger'); } catch (e2) {}
    }
    try {
      try { setProgress(10, 'Parsing GraphTec GBD...'); } catch (e) {}
      if (!buf || buf.byteLength < 8192 + 24) { fail('Not a valid GBD file (too small).'); return; }
      var dv = new DataView(buf);
      var htext = String.fromCharCode.apply(null, new Uint8Array(buf, 0, 8192));
      function hval(re) { var m = htext.match(re); return m ? m[1] : ''; }

      var counts = parseInt((hval(/Counts\s*=\s*(\d+)/) || '').trim(), 10);
      var orderTxt = (hval(/Order\s*=\s*([^\r\n]+)/) || '').trim();
      var orderToks = orderTxt ? orderTxt.split(',') : [];
      var chNames = [];
      var chPos = [];
      orderToks.forEach(function (tok, ti) {
        var t = (tok || '').trim();
        if (/^CH\d+$/i.test(t)) { chNames.push(t.toUpperCase()); chPos.push(ti * 2); }
      });
      if (!chNames.length) {
        for (var d = 1; d <= 10; d++) { chNames.push('CH' + d); chPos.push((d - 1) * 2); }
        orderToks = chNames.concat(['Alarm1', 'AlarmOut']);
      }
      var recSize = orderToks.length * 2;
      if (recSize < chPos[chPos.length - 1] + 2) recSize = chPos[chPos.length - 1] + 2;

      // Sample interval, e.g. "1s" / "100ms" / "1min".
      var stepMs = 1000;
      var sm = /([\d.]+)\s*(ms|h|min|s|sec)/i.exec(hval(/Sample\s*=\s*([^\r\n]+)/) || '');
      if (sm) {
        var n = parseFloat(sm[1]);
        var u = sm[2].toLowerCase();
        if (!isNaN(n)) {
          if (u.indexOf('ms') === 0) stepMs = n;
          else if (u.charAt(0) === 'h') stepMs = n * 3600000;
          else if (u.charAt(0) === 'm') stepMs = n * 60000;
          else stepMs = n * 1000;
        }
      }

      // Trigger time (first sample); fall back to Start.
      var trigTxt = (hval(/Trigger\s*=\s*(\d{4}-\d{2}-\d{2},\d{2}:\d{2}:\d{2})/) || '').trim() ||
                    (hval(/Start\s*=\s*(\d{4}-\d{2}-\d{2},\d{2}:\d{2}:\d{2})/) || '').trim();
      if (!trigTxt) { fail('GBD header has no valid start time.'); return; }
      var tp = trigTxt.split(',');
      var dp = tp[0].split('-');
      var tm = tp[1].split(':');
      var t0 = new Date(+dp[0], +dp[1] - 1, +dp[2], +tm[0], +tm[1], +(tm[2] || 0)).getTime();
      if (isNaN(t0)) { fail('GBD header has no valid start time.'); return; }

      var avail = Math.floor((dv.byteLength - 8192) / recSize);
      var total = counts && counts > 0 ? Math.min(counts, avail) : avail;
      if (!total) { fail('GBD file contains no data records.'); return; }

      function pad2(x) { return (x < 10 ? '0' : '') + x; }
      var rows = [];
      var validCounts = [];
      for (var k = 0; k < chNames.length; k++) validCounts.push(0);
      for (var i = 0; i < total; i++) {
        var base = 8192 + i * recSize;
        if (base + recSize > dv.byteLength) break;
        var dt = new Date(t0 + i * stepMs);
        var row = { 'Timestamp': dt.getFullYear() + '-' + pad2(dt.getMonth() + 1) + '-' + pad2(dt.getDate()) + ' ' + pad2(dt.getHours()) + ':' + pad2(dt.getMinutes()) + ':' + pad2(dt.getSeconds()) };
        var hasTemp = false;
        for (var ci = 0; ci < chPos.length; ci++) {
          var raw = dv.getInt16(base + chPos[ci], false); // big-endian
          if (raw >= 32000) { row[chNames[ci]] = ''; continue; }
          row[chNames[ci]] = Math.round(raw) / 10;
          hasTemp = true;
          validCounts[ci]++;
        }
        if (!hasTemp) continue;
        rows.push(row);
        if ((i & 8191) === 8191) { try { setProgress(10 + Math.round(80 * i / total), 'Parsing GraphTec GBD...'); } catch (e) {} }
      }
      if (!rows.length) { fail('No valid temperature data found in GBD file.'); return; }

      finishParsedRows(rows, chNames, validCounts, fileName || 'graphtec.gbd', 'gbd', 'GraphTec GBD');
    } catch (err) {
      fail('Failed to parse GBD file: ' + (err && err.message ? err.message : err));
    }
  }

  function handleGbdFile(file) {
    currentFileName = file.name;
    currentFileType = 'gbd';
    var overlay = document.getElementById('progress-overlay');
    try {
      if (overlay) overlay.style.display = 'flex';
      setProgress(5, 'Reading ' + file.name + '...');
    } catch (e) {}
    try {
      document.getElementById('file-path-display').style.display = 'flex';
      document.getElementById('file-path-text').innerText = file.name;
    } catch (e) {}
    try {
      document.getElementById('sheet-select').style.display = 'none';
      document.querySelector('.sheet-picker label').style.display = 'none';
    } catch (e) {}
    var rd = new FileReader();
    rd.onload = function (e) {
      try { parseGbdFile((e.target && e.target.result) || null, file.name); }
      catch (err) {
        try { if (overlay) overlay.style.display = 'none'; } catch (e2) {}
        try { showToast('Failed to parse GBD file: ' + err.message, 'danger'); } catch (e3) {}
      }
    };
    rd.onerror = function () {
      try { if (overlay) overlay.style.display = 'none'; } catch (e) {}
      try { showToast('Failed to read GBD file.', 'danger'); } catch (e2) {}
    };
    rd.readAsArrayBuffer(file);
  }

  function handleCsvFile(file) {
    currentFileName = file.name;
    currentFileType = 'csv';
    var overlay = document.getElementById('progress-overlay');
    try {
      if (overlay) overlay.style.display = 'flex';
      setProgress(5, 'Reading ' + file.name + '...');
    } catch (e) {}
    try {
      document.getElementById('file-path-display').style.display = 'flex';
      document.getElementById('file-path-text').innerText = file.name;
    } catch (e) {}
    try {
      document.getElementById('sheet-select').style.display = 'none';
      document.querySelector('.sheet-picker label').style.display = 'none';
    } catch (e) {}
    var rd = new FileReader();
    rd.onload = function (e) {
      try { parseGraphTecCsv(String((e.target && e.target.result) || ''), file.name); }
      catch (err) {
        try { if (overlay) overlay.style.display = 'none'; } catch (e2) {}
        try { showToast('Failed to parse CSV file: ' + err.message, 'danger'); } catch (e3) {}
      }
    };
    rd.onerror = function () {
      try { if (overlay) overlay.style.display = 'none'; } catch (e) {}
      try { showToast('Failed to read CSV file.', 'danger'); } catch (e2) {}
    };
    rd.readAsText(file);
  }

  // --- Part D: export data interval (downsample 1s logs, shrink XLSX) ---------
  // 1-second logging makes huge workbooks (50k rows x 10 channels). An interval
  // selector (every row / 10s / 15s / 30s / 1min) is injected next to the Export
  // button; export temporarily swaps in the downsampled rows + a suffixed name
  // (e.g. file_10s.xlsx) and delegates to the page's own exportXLSX, so the
  // bridge/browser-download paths are reused unchanged. Time-based (not
  // every-Nth-row) so gaps never shift the grid; first + last rows always kept.
  var EXPORT_STEPS = [
    { sec: 1, label: 'Export: every row', suffix: '' },
    { sec: 10, label: 'Export: every 10 sec', suffix: '_10s' },
    { sec: 15, label: 'Export: every 15 sec', suffix: '_15s' },
    { sec: 30, label: 'Export: every 30 sec', suffix: '_30s' },
    { sec: 60, label: 'Export: every 1 min', suffix: '_1min' }
  ];

  function getExportStepSec() {
    try {
      var sel = document.getElementById('tti-export-interval');
      if (sel) {
        var v = parseInt(sel.value, 10);
        if (!isNaN(v)) return v;
      }
    } catch (e) {}
    return 1;
  }

  function stepSuffix(stepSec) {
    for (var i = 0; i < EXPORT_STEPS.length; i++) {
      if (EXPORT_STEPS[i].sec === stepSec) return EXPORT_STEPS[i].suffix;
    }
    return '_' + stepSec + 's';
  }

  function ensureExportUI() {
    try {
      if (document.getElementById('tti-export-interval')) return;
      var btn = document.getElementById('export-xlsx-btn');
      if (!btn || !btn.parentNode) return;
      var sel = document.createElement('select');
      sel.id = 'tti-export-interval';
      sel.title = 'Export data interval — downsamples 1s logs to shrink the XLSX file';
      sel.style.cssText = 'font-size:12px;padding:6px 8px;border:1px solid #d2d2d2;border-radius:4px;background:#fff;color:#202020;';
      EXPORT_STEPS.forEach(function (s) {
        var o = document.createElement('option');
        o.value = s.sec;
        o.textContent = s.label;
        sel.appendChild(o);
      });
      var initial = '1';
      try {
        var stored = localStorage.getItem('tti-export-step');
        if (stored !== null && stored !== '') initial = stored;
        else if (typeof allRows !== 'undefined' && allRows && allRows.length > 5000) initial = '10';
      } catch (e) {}
      sel.value = initial;
      sel.addEventListener('change', function () {
        try { localStorage.setItem('tti-export-step', sel.value); } catch (e) {}
      });
      btn.parentNode.insertBefore(sel, btn);
    } catch (e) {}
  }

  function autoExportDefault() {
    // After a file loads: large files auto-select 10s unless the user already
    // picked (and stored) a preference.
    try {
      var stored = null;
      try { stored = localStorage.getItem('tti-export-step'); } catch (e) {}
      if (stored !== null && stored !== '') return;
      var sel = document.getElementById('tti-export-interval');
      if (!sel) return;
      if (typeof allRows !== 'undefined' && allRows && allRows.length > 5000) sel.value = '10';
      else sel.value = '1';
    } catch (e) {}
  }

  function downsampleRows(rows, tsKey, stepSec) {
    if (!stepSec || stepSec <= 1 || !rows || rows.length < 3) return rows;
    var out = [];
    var lastMs = null;
    var useTime = (typeof window.parseDateTime === 'function');
    for (var i = 0; i < rows.length; i++) {
      var r = rows[i];
      var ms = null;
      if (useTime) {
        try { ms = parseDateTime(String(r[tsKey])); } catch (e) { ms = null; }
        if (ms !== null && ms !== undefined && isNaN(ms)) ms = null;
      }
      if (ms === null || ms === undefined) {
        // Timestamp not parseable: fall back to every-Nth-row on the block.
        if (out.length === 0 || (i % stepSec) === 0) out.push(r);
        continue;
      }
      if (lastMs === null || (ms - lastMs) >= stepSec * 1000 - 1) { out.push(r); lastMs = ms; }
    }
    // Always close the range with the final row.
    if (out.length && out[out.length - 1] !== rows[rows.length - 1]) out.push(rows[rows.length - 1]);
    return out.length ? out : rows;
  }

  function installCsvHook() {
    // Drop path: the page's own drop filter only accepts .xlsx/.dad, so
    // intercept .csv/.gbd in the capture phase before its bubble listener fires.
    try {
      var dz = document.getElementById('drop-zone-file');
      if (dz && !dz.__ttiCsvDropHooked) {
        dz.__ttiCsvDropHooked = true;
        dz.addEventListener('drop', function (e) {
          try {
            var fs = e.dataTransfer && e.dataTransfer.files;
            if (fs && fs.length > 0 && /\.(csv|gbd)$/i.test(fs[0].name || '')) {
              e.preventDefault();
              // Same-node listeners fire in registration order at target, so
              // stopImmediatePropagation (not just stopPropagation) is needed
              // to keep the page's own .xlsx/.dad-only drop handler from
              // rejecting the file with a toast.
              try { e.stopImmediatePropagation(); } catch (e2) {}
              try { e.stopPropagation(); } catch (e3) {}
              if (/\.gbd$/i.test(fs[0].name || '')) handleGbdFile(fs[0]);
              else handleCsvFile(fs[0]);
            }
          } catch (err) { /* fall through to page handler */ }
        }, true);
      }
    } catch (e) {}
    // Browse path: the file-input handler has no extension filter, so wrapping
    // window.handleFile is enough to route .csv/.gbd into the right parser.
    try {
      if (typeof window.handleFile === 'function' && !window.handleFile.__ttiCsvWrapped) {
        var origHandle = window.handleFile;
        var wrappedHandle = function (file) {
          try {
            if (file && /\.(csv|gbd)$/i.test(file.name || '')) {
              if (/\.gbd$/i.test(file.name || '')) { handleGbdFile(file); return; }
              handleCsvFile(file); return;
            }
          } catch (e) {}
          return origHandle.apply(this, arguments);
        };
        wrappedHandle.__ttiCsvWrapped = true;
        window.handleFile = wrappedHandle;
      }
    } catch (e) {}
    // Export: csv-aware basename + selectable data interval. The interval path
    // temporarily swaps in downsampled rows (suffixed name) and delegates to
    // the page's own exportXLSX, reusing its bridge/browser-download logic.
    ensureExportUI();
    try {
      if (typeof window.exportXLSX === 'function' && !window.exportXLSX.__ttiCsvWrapped) {
        var origExport = window.exportXLSX;
        var wrappedExport = function () {
          var step = getExportStepSec();
          if (step <= 1) {
            var saved = currentFileName;
            try {
              currentFileName = String(saved || '').replace(/\.(csv|gbd)$/i, '');
              return origExport.apply(this, arguments);
            } finally {
              currentFileName = saved;
            }
          }
          var savedRows = allRows;
          var savedName = currentFileName;
          try {
            var xEl = document.getElementById('axis-x');
            var xIdx = xEl ? parseInt(xEl.value, 10) : 0;
            if (isNaN(xIdx)) xIdx = 0;
            var tsKey = allHeaders[xIdx];
            var filtered = downsampleRows(savedRows, tsKey, step);
            allRows = filtered;
            var base = String(savedName || 'export').replace(/\.(dad|xlsx?|csv|gbd)$/i, '');
            currentFileName = base + stepSuffix(step);
            return origExport.apply(this, arguments);
          } finally {
            allRows = savedRows;
            currentFileName = savedName;
          }
        };
        wrappedExport.__ttiCsvWrapped = true;
        window.exportXLSX = wrappedExport;
      }
    } catch (e) {}
    try {
      if (typeof window.printGraph === 'function' && !window.printGraph.__ttiCsvWrapped) {
        var origPrint = window.printGraph;
        var wrappedPrint = function () {
          var saved = currentFileName;
          try {
            currentFileName = String(saved || '').replace(/\.(csv|gbd)$/i, '');
            return origPrint.apply(this, arguments);
          } finally {
            currentFileName = saved;
          }
        };
        wrappedPrint.__ttiCsvWrapped = true;
        window.printGraph = wrappedPrint;
      }
    } catch (e) {}
  }

  // --- Part C: stack-safe min/max for large files (no HTML edit) ---------------
  // The page computes channel/global minima with Math.min/max.apply(null, arr).
  // Past a few thousand elements the .apply() argument list itself overflows the
  // call stack ("Maximum call stack size exceeded") — never seen with small .dad
  // files, but a 49,892-row GraphTec CSV trips updateMetaSummary (~450k values)
  // and renderReferenceTable (~50k values). These overrides are byte-identical
  // to the page functions except for an iterative min/max helper.
  function __ttiMinMax(vals) {
    var lo = Infinity, hi = -Infinity;
    for (var i = 0; i < vals.length; i++) {
      var v = vals[i];
      if (v < lo) lo = v;
      if (v > hi) hi = v;
    }
    return { min: lo, max: hi };
  }

  function installLargeDataFix() {
    if (window.__ttiLargeDataFixed) return;
    if (typeof window.updateMetaSummary !== 'function' ||
        typeof window.renderReferenceTable !== 'function' ||
        typeof window.renderSummaryTable !== 'function') return; // page not ready yet
    window.__ttiLargeDataFixed = true;

    window.updateMetaSummary = function () {
      var points = 0;
      var series = 0;
      var allTemps = [];

      var ySel = document.getElementById('axis-y-series');
      for (var i = 0; i < ySel.options.length; i++) {
        if (ySel.options[i].selected) series++;
      }

      var selectedYIdxs = [];
      for (var i2 = 0; i2 < ySel.options.length; i2++) {
        if (ySel.options[i2].selected) selectedYIdxs.push(parseInt(ySel.options[i2].value));
      }

      selectedYIdxs.forEach(function(yi) {
        allRows.forEach(function(row) {
          var v = row[allHeaders[yi]];
          if (v !== '' && v !== null && v !== undefined) {
            var num = parseFloat(v);
            if (!isNaN(num)) {
              allTemps.push(num);
              points++;
            }
          }
        });
      });

      var mm = __ttiMinMax(allTemps);
      var min = allTemps.length > 0 ? mm.min : null;
      var max = allTemps.length > 0 ? mm.max : null;

      document.getElementById('meta-points-series').innerText = points + ' readings \u00B7 ' + series + ' series';
      document.getElementById('meta-min-max').innerText = (min !== null ? min.toFixed(1) + ' \u00B0C' : '---') + ' \u00B7 ' + (max !== null ? max.toFixed(1) + ' \u00B0C' : '---');
    };

    window.renderReferenceTable = function () {
      var startSel = document.getElementById('range-start');
      var stopSel = document.getElementById('range-stop');
      if (!startSel || !stopSel || startSel.options.length === 0) return;

      var startIdx = parseInt(startSel.value);
      var stopIdx = parseInt(stopSel.value);
      if (isNaN(startIdx) || isNaN(stopIdx)) return;

      var ySel = document.getElementById('axis-y-series');
      var selectedYIdxs = [];
      for (var i = 0; i < ySel.options.length; i++) {
        if (ySel.options[i].selected) selectedYIdxs.push(parseInt(ySel.options[i].value));
      }
      if (selectedYIdxs.length === 0) return;

      var lo = Math.min(startIdx, stopIdx);
      var hi = Math.max(startIdx, stopIdx);

      // Per-channel stats
      var chStats = [];
      selectedYIdxs.forEach(function(yi) {
        var vals = [];
        for (var r = lo; r <= hi; r++) {
          var v = parseFloat(allRows[r][allHeaders[yi]]);
          if (!isNaN(v)) vals.push(v);
        }
        if (vals.length > 0) {
          var mm = __ttiMinMax(vals);
          var cMin = mm.min;
          var cMax = mm.max;
          var cSum = 0;
          for (var k = 0; k < vals.length; k++) cSum += vals[k];
          chStats.push({ name: allHeaders[yi], min: cMin, max: cMax, avg: cSum / vals.length, delta: cMax - cMin });
        }
      });

      var section = document.getElementById('reference-table-section');
      var refCheck = document.getElementById('show-ref-table');
      if (chStats.length === 0 || (refCheck && !refCheck.checked)) { section.style.display = 'none'; return; }
      section.style.display = '';

      document.getElementById('ref-table-subtitle').innerText = 'Records ' + lo + ' \u2013 ' + hi + ' | ' + chStats.length + ' channels';

      // Find global min/max across channels for highlighting
      var globalMin = Infinity, globalMax = -Infinity;
      chStats.forEach(function(s) { if (s.min < globalMin) globalMin = s.min; if (s.max > globalMax) globalMax = s.max; });

      var html = '<table><thead><tr><th>Channel</th><th>Min (&deg;C)</th><th>Max (&deg;C)</th><th>Avg (&deg;C)</th><th>Delta (&deg;C)</th></tr></thead><tbody>';
      chStats.forEach(function(s) {
        var minCls = (s.min === globalMin) ? 'ref-min-val' : '';
        var maxCls = (s.max === globalMax) ? 'ref-max-val' : '';
        html += '<tr><td>' + s.name + '</td><td class="' + minCls + '">' + s.min.toFixed(1) + '</td><td class="' + maxCls + '">' + s.max.toFixed(1) + '</td><td>' + s.avg.toFixed(1) + '</td><td>' + s.delta.toFixed(1) + '</td></tr>';
      });
      html += '</tbody></table>';

      document.getElementById('reference-table-container').innerHTML = html;

      renderSummaryTable(chStats);
    };

    window.renderSummaryTable = function (chStats) {
      var section = document.getElementById('summary-table-section');
      if (!chStats || chStats.length === 0) { section.style.display = 'none'; return; }
      section.style.display = '';

      var ovenSetting = 0;
      chStats.forEach(function(s) { ovenSetting += s.avg; });
      ovenSetting = ovenSetting / chStats.length;

      var chAvgs = chStats.map(function(s) { return s.avg; });
      var mmAvgs = __ttiMinMax(chAvgs);
      var sumDelta = mmAvgs.max - mmAvgs.min;

      var html = '<table><thead><tr><th>Oven Setting (&deg;C)</th>';
      chStats.forEach(function(s) { html += '<th>' + s.name + '</th>'; });
      html += '<th>Delta (&deg;C)</th></tr></thead><tbody>';
      html += '<tr><td><input type="number" class="oven-input" id="oven-setting-val" step="0.1" value="' + ovenSetting.toFixed(1) + '"></td>';
      chStats.forEach(function(s) { html += '<td>' + s.avg.toFixed(1) + '</td>'; });
      html += '<td class="delta-cell">' + sumDelta.toFixed(1) + '</td></tr>';
      html += '</tbody></table>';

      document.getElementById('summary-table-container').innerHTML = html;
    };
  }

  // --- Part F: instrument gallery below the upload card (no HTML edit) --------
  // Shows the supported loggers side by side under the drag-drop area. Images
  // live in data2\assets\instrument_pic and are served as /assets/... by the
  // shared host (same mechanism as the other assets/* files, spaces included).
  var INSTRUMENT_PICS = [
    { file: 'GRAPHTEC GL220.png', caption: 'GRAPHTEC GL220' },
    { file: 'Yokogawa FX1000.png', caption: 'Yokogawa FX1000' }
  ];

  function injectInstrumentGallery() {
    try {
      if (document.getElementById('tti-instrument-gallery')) return;
      var screen = document.getElementById('upload-screen');
      if (!screen) return;
      var gal = document.createElement('div');
      gal.id = 'tti-instrument-gallery';
      gal.style.cssText = 'display:flex;gap:24px;justify-content:center;align-items:flex-start;margin-top:8px;flex-wrap:wrap;';
      INSTRUMENT_PICS.forEach(function (p) {
        var fig = document.createElement('figure');
        fig.style.cssText = 'margin:0;background:#ffffff;border:1px solid #e5e5e5;border-radius:8px;padding:12px 16px;box-shadow:0 2px 6px rgba(0,0,0,0.03);display:flex;flex-direction:column;align-items:center;gap:8px;';
        var img = document.createElement('img');
        img.src = 'assets/instrument_pic/' + p.file;
        img.alt = p.caption;
        img.style.cssText = 'height:150px;width:auto;max-width:320px;object-fit:contain;display:block;';
        img.onerror = (function (f) { return function () { f.style.display = 'none'; }; })(fig);
        var cap = document.createElement('figcaption');
        cap.textContent = p.caption;
        cap.style.cssText = 'font-size:12px;font-weight:600;color:#5f5f5f;';
        fig.appendChild(img);
        fig.appendChild(cap);
        gal.appendChild(fig);
      });
      screen.appendChild(gal);
    } catch (e) { /* ignore */ }
  }

  function init() {
    applyExeVersion();
    updateUploadText();
    injectInstrumentGallery();
    ensurePrintDateHeader('graph-screen');
    installXlsxShim();
    installCsvHook();
    installLargeDataFix();
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init);
  } else {
    init();
  }
  window.addEventListener('load', function () {
    applyExeVersion();
    updateUploadText();
    injectInstrumentGallery();
    ensurePrintDateHeader('graph-screen');
    installXlsxShim();
    installCsvHook();
    installLargeDataFix();
  });
})();
