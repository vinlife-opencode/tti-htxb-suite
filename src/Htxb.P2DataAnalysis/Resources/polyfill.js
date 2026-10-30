// TTI tool — offline Excel export shim
// XLSX.writeFile() relies on browser download support, which WebView2 does not
// expose like Chrome. This polyfill re-routes export through the native bridge:
//   SaveFile(name) -> user picks a path -> WriteFileBase64 writes workbook bytes.
// NOTE: SheetJS assigns an EMPTY global XLSX first and fills it in later, so we
// cannot intercept at assignment time; we patch the method once the library is
// fully loaded (DOMContentLoaded / load fallback).
(function () {
  'use strict';

  // --- Header version badge sync (UI display rule: header == cover page) -----
  var EXE_VERSION = 'V6.10.091';
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