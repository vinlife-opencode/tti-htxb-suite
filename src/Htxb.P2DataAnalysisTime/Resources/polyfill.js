// TTI P2 Data Analysis - Time  (EXE polyfill, V6.11.012)
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
// Part D: charts print at the full width of the page. See snapshotChartsForPrint.
(function () {
  'use strict';

  var EXE_VERSION = 'V6.11.012';

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
  // empty. Chart.js cannot be told the print width either - the print layout is
  // not built until after `beforeprint` - so each chart is redrawn at the width
  // of a printed page, snapshotted to an <img> that the print stylesheet stretches
  // to width:100%, and the live canvas is put back on `afterprint`.
  //
  // 720 CSS px is a printed A4/Letter portrait body (~190 mm with 10 mm margins),
  // so the labels print at their on-screen size instead of being scaled down;
  // rendering at 3x device pixels keeps the bitmap ~300 dpi on paper.
  //
  // `stop()` matters: Chart.resize() while an animation is running only stores
  // the size for the next frame, so the snapshot would capture the old, narrow
  // chart. Measured in the app's bundled Chart.js 4.4.1: a 457 px wide canvas
  // snapshots at 2160x960 this way and comes back to 457x320 afterwards.
  var PRINT_CHART_WIDTH = 720;
  var PRINT_CHART_DPR = 3;
  var _printChartState = [];

  function printChartStyle() {
    if (document.getElementById('tti-print-chart-style')) return;
    var st = document.createElement('style');
    st.id = 'tti-print-chart-style';
    st.textContent =
      '.tti-print-chart{display:none;}'
      + '@media print{'
      // the snapshot replaces the canvas and fills whatever width the page has
      + '.tti-print-chart{display:block !important;width:100% !important;height:auto !important;}'
      + '.tti-print-chart-original{display:none !important;}'
      // let the image define the height (the on-screen box is a fixed 320px)
      + '.chart-box{height:auto !important;padding:0 !important;}'
      + '}';
    document.head.appendChild(st);
  }

  function snapshotChartsForPrint() {
    printChartStyle();
    restoreChartsAfterPrint();                 // never stack two snapshots
    var live = window.charts;
    if (!live) return;
    for (var k in live) {
      if (!Object.prototype.hasOwnProperty.call(live, k)) continue;
      var ch = live[k];
      if (!ch || !ch.canvas || typeof ch.toBase64Image !== 'function') continue;
      var canvas = ch.canvas;
      var screenW = canvas.clientWidth;
      var screenH = canvas.clientHeight;
      if (!screenW || !screenH) continue;      // chart not drawn yet
      var src = null;
      try {
        ch.stop();
        ch.options.devicePixelRatio = PRINT_CHART_DPR;
        ch.resize(PRINT_CHART_WIDTH, screenH);
        ch.update('none');                     // draw now, no animation frame
        src = ch.toBase64Image();
      } catch (e) {
        src = null;
      } finally {
        ch.options.devicePixelRatio = undefined;
        ch.stop();
        ch.resize(screenW, screenH);           // back to the on-screen size
      }
      if (!src) continue;
      try {
        var img = document.createElement('img');
        img.className = 'tti-print-chart';
        img.alt = '';
        img.src = src;
        canvas.parentNode.insertBefore(img, canvas.nextSibling);
        canvas.classList.add('tti-print-chart-original');
        _printChartState.push({ chart: ch, canvas: canvas, img: img });
      } catch (e) { /* printing still works, just not full width */ }
    }
  }

  function restoreChartsAfterPrint() {
    for (var i = 0; i < _printChartState.length; i++) {
      var s = _printChartState[i];
      try {
        if (s.img.parentNode) s.img.parentNode.removeChild(s.img);
        s.canvas.classList.remove('tti-print-chart-original');
        s.chart.stop();
        s.chart.resize();                      // let Chart.js re-measure its box
      } catch (e) { /* keep going */ }
    }
    _printChartState = [];
  }

  function hookPrintCharts() {
    if (window.__ttiPrintChartsHooked) return;
    window.__ttiPrintChartsHooked = true;
    printChartStyle();
    window.addEventListener('beforeprint', snapshotChartsForPrint);
    window.addEventListener('afterprint', restoreChartsAfterPrint);
    // WebView2's print preview also reaches the page as a media change, and
    // Ctrl+P in some hosts skips `beforeprint` - cover both paths.
    if (window.matchMedia) {
      var mq = window.matchMedia('print');
      var onChange = function (m) {
        if (m.matches) snapshotChartsForPrint();
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
