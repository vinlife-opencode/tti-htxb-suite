// TTI tool — offline Excel export shim
// XLSX.writeFile() relies on browser download support, which WebView2 does not
// expose like Chrome. This polyfill re-routes export through the native bridge:
//   SaveFile(name) -> user picks a path -> WriteFileBase64 writes workbook bytes.
// NOTE: SheetJS assigns an EMPTY global XLSX first and fills it in later, so we
// cannot intercept at assignment time; we patch the method once the library is
// fully loaded (DOMContentLoaded / load fallback).
(function () {
  'use strict';

  // --- TestTime(H) -> TestTime(Min) (recipe format change, no HTML edit) -----
  // New .zadz recipes carry 'TestTime(Min)' (minutes); old files carry
  // 'TestTime(H)' (hours). The page hardcodes the legacy key in its header
  // array, table builder and xlsx export, so new files would show/emit an
  // empty TestTime column. This shim:
  //   1. relabels headerColumns[2] to 'TestTime(Min)' (dashboard + export),
  //   2. fills ap['TestTime(H)'] from ap['TestTime(Min)'] when H is missing
  //      (in place on the loaded recipeData, so the async xlsx export —
  //      which reads the live global — picks it up too).
  // Only the legacy key is ever backfilled, never the reverse: a later zadz
  // download therefore serializes exactly what was loaded (no bogus key that
  // a machine could misread as hours). Values pass through untouched — no
  // silent hour<->minute scaling; legacy hour-vintage files keep their
  // original numbers.
  var TTI_TT_OLD = 'TestTime(H)';
  var TTI_TT_NEW = 'TestTime(Min)';

  function ttiFillLegacyTestTime(data) {
    try {
      var list = data && data.AgingProcess;
      if (!list) return;
      for (var i = 0; i < list.length; i++) {
        var ap = list[i];
        if (!ap) continue;
        var h = ap[TTI_TT_OLD];
        var m = ap[TTI_TT_NEW];
        var hMissing = (h === undefined || h === null || h === '');
        var mPresent = !(m === undefined || m === null || m === '');
        if (hMissing && mPresent) ap[TTI_TT_OLD] = m;
      }
    } catch (e) { /* never break the tool */ }
  }

  function ttiInstallTestTimeShim() {
    try {
      if (window.headerColumns && window.headerColumns[2] === TTI_TT_OLD) {
        window.headerColumns[2] = TTI_TT_NEW;
      }
      if (typeof window.buildTableData === 'function' && !window.buildTableData.__ttiTT) {
        var origBuild = window.buildTableData;
        var wrappedBuild = function (data) {
          ttiFillLegacyTestTime(data);
          return origBuild.call(this, data);
        };
        wrappedBuild.__ttiTT = true;
        window.buildTableData = wrappedBuild;
      }
    } catch (e) { /* never break the tool */ }
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', ttiInstallTestTimeShim);
  } else {
    ttiInstallTestTimeShim();
  }
  window.addEventListener('load', ttiInstallTestTimeShim);

  // --- Header version badge sync (UI display rule: header == cover page) -----
  var EXE_VERSION = 'V6.10.151';
  function applyExeVersion() {
    try {
      var tags = document.querySelectorAll('.logo-tag');
      for (var i = 0; i < tags.length; i++) tags[i].textContent = EXE_VERSION;
    } catch (e) { /* ignore */ }
  }
  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', applyExeVersion);
  } else {
    applyExeVersion();
  }

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
})();