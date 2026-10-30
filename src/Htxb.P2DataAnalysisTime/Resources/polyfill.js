// TTI P2 Data Analysis - Time  (EXE polyfill, V6.10.302)
// Part A: header version badge sync (UI display rule: header == cover page).
// Part B: offline Excel export shim - XLSX.writeFile() relies on browser download
//   support, which WebView2 does not expose like Chrome. Re-route through the
//   native bridge: SaveFile(name) -> user picks a path -> WriteFileBase64 writes
//   the workbook bytes. SheetJS assigns an EMPTY global XLSX first and fills it
//   in later, so the method is patched once the library is fully loaded.
// Part C: standardized print/PDF header date in Long Form ("October 16, 2026").
//   The page itself never stamps the print day, so inject a print-only line as
//   the first row of the printed report. Explicit month names (never
//   toLocaleDateString) keep every machine identical; re-stamped on every
//   `beforeprint` so a long-open window prints the current day.
(function () {
  'use strict';

  var EXE_VERSION = 'V6.10.302';

  // --- Part A: header version badge -----------------------------------------
  function applyExeVersion() {
    try {
      var tags = document.querySelectorAll('.logo-tag');
      for (var i = 0; i < tags.length; i++) tags[i].textContent = EXE_VERSION;
    } catch (e) { /* never break the tool */ }
  }

  // --- Part C: print/PDF header date (Long Form) ----------------------------
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
      if (!window.__ttiPrintDateHooked) {
        window.__ttiPrintDateHooked = true;
        window.addEventListener('beforeprint', function () {
          var el = document.getElementById('tti-print-date');
          if (el) el.textContent = ttiLongFormToday();
        });
      }
    } catch (e) { /* never break the tool */ }
  }

  // --- Part B: offline Excel export shim -----------------------------------
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
        if (!path) return;                                   // user cancelled
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

  function init() {
    applyExeVersion();
    ensurePrintDateHeader('print-head');
    installXlsxShim();
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init);
  } else {
    init();
  }
  window.addEventListener('load', function () {
    applyExeVersion();
    ensurePrintDateHeader('print-head');
    installXlsxShim();
  });
})();
