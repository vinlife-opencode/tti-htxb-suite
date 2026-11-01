// TTI P2 Data Analysis - Time  (EXE polyfill, V6.11.013)
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
// Part D: charts print at the full width of the page. See prepareChartsForPrint.
(function () {
  'use strict';

  var EXE_VERSION = 'V6.11.013';

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

  // --- Part D: charts print at the full page width -------------------------
  // Both "Preset vs actual" and "Trend - Sigma actual per slot" sit side by side
  // in a .grid-2, which the print stylesheet stacks into one column. A Chart.js
  // canvas cannot follow that: it keeps the pixel size it measured on screen, so
  // the print showed the graph in the left half of the sheet with the right half
  // empty.
  //
  // So the chart is redrawn at the width of a printed page before the print dialog
  // takes its snapshot, and the print stylesheet stretches that canvas to whatever
  // width the sheet really has.
  //
  // Drawing on the canvas itself - and NOT on an <img> copy of it - is deliberate.
  // The first attempt swapped in a data-URL <img> during `beforeprint`; a data URL
  // is still decoding when the print engine snapshots the page, so every chart
  // printed blank. A canvas bitmap is already rendered at that point.
  //
  // 720 CSS px is a printed A4/Letter portrait body (~190 mm with 10 mm margins);
  // rendering at 3x device pixels keeps ~300 dpi on paper.
  //
  // `stop()` matters: Chart.resize() during an animation only queues the size for
  // the next frame, so the old, narrow chart would print (measured on the bundled
  // Chart.js 4.4.1).
  var PRINT_CHART_WIDTH = 720;
  var PRINT_CHART_DPR = 3;
  var _printChartState = [];

  function printChartStyle() {
    if (document.getElementById('tti-print-chart-style')) return;
    var st = document.createElement('style');
    st.id = 'tti-print-chart-style';
    st.textContent =
      '@media print{'
      // the fixed 320px on-screen box must not cut a page-wide chart in half
      + '.chart-box{height:auto !important;padding:0 !important;}'
      // ...and the canvas fills the sheet, keeping its aspect from the bitmap
      + '.chart-box canvas{width:100% !important;height:auto !important;max-width:100% !important;}'
      + '}';
    document.head.appendChild(st);
  }

  function prepareChartsForPrint() {
    printChartStyle();
    if (_printChartState.length) return;       // already page-wide
    var live = window.charts;
    if (!live) return;
    for (var k in live) {
      if (!Object.prototype.hasOwnProperty.call(live, k)) continue;
      var ch = live[k];
      if (!ch || !ch.canvas || !ch.resize || !ch.update) continue;
      var canvas = ch.canvas;
      var screenW = canvas.clientWidth;
      var screenH = canvas.clientHeight;
      if (!screenW || !screenH) continue;      // chart not drawn yet
      try {
        ch.stop();
        ch.options.devicePixelRatio = PRINT_CHART_DPR;
        ch.resize(PRINT_CHART_WIDTH, screenH);
        ch.update('none');                     // draw now, no animation frame
        _printChartState.push({ chart: ch, w: screenW, h: screenH });
      } catch (e) {
        try { ch.options.devicePixelRatio = undefined; } catch (e2) { /* ignore */ }
      }
    }
  }

  function restoreChartsAfterPrint() {
    for (var i = 0; i < _printChartState.length; i++) {
      var s = _printChartState[i];
      try {
        s.chart.stop();
        s.chart.options.devicePixelRatio = undefined;
        s.chart.resize(s.w, s.h);              // back to the on-screen size
      } catch (e) { /* keep going */ }
    }
    _printChartState = [];
  }

  function hookPrintCharts() {
    if (window.__ttiPrintChartsHooked) return;
    window.__ttiPrintChartsHooked = true;
    printChartStyle();
    window.addEventListener('beforeprint', prepareChartsForPrint);
    window.addEventListener('afterprint', restoreChartsAfterPrint);
    // No `focus` fallback here on purpose: Chromium also fires focus while the
    // print preview is open, and restoring the charts at that moment prints them
    // stretched over the sheet (measured: 3 pages, upscaled labels).
    // WebView2's print preview also reaches the page as a media change, and Ctrl+P
    // in some hosts skips `beforeprint` - cover both paths.
    if (window.matchMedia) {
      var mq = window.matchMedia('print');
      var onChange = function (m) {
        if (m.matches) prepareChartsForPrint();
        else restoreChartsAfterPrint();
      };
      if (mq.addEventListener) mq.addEventListener('change', onChange);
      else if (mq.addListener) mq.addListener(onChange);
    }
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
    hookPrintCharts();
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
    hookPrintCharts();
    installXlsxShim();
  });
})();
