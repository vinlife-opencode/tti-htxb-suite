// TTI Recipe-to-Std Table — offline save shim
// The page saves its standardized XLSX through the File System Access API
// (window.showSaveFilePicker + createWritable), which WebView2 does not expose.
// This polyfill re-routes that flow through the native bridge:
//   showSaveFilePicker({suggestedName}) -> SaveFile dialog (cancel => AbortError)
//   writable.write(blob) buffers chunks; writable.close() writes via WriteFileBase64.
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

  function makeWritable(path) {
    var chunks = [];
    return {
      write: function (chunk) {
        if (chunk instanceof Blob) {
          return chunk.arrayBuffer().then(function (ab) {
            chunks.push(new Uint8Array(ab));
          });
        }
        if (ArrayBuffer.isView(chunk)) {
          chunks.push(new Uint8Array(chunk.buffer, chunk.byteOffset, chunk.byteLength));
          return Promise.resolve();
        }
        if (chunk instanceof ArrayBuffer) {
          chunks.push(new Uint8Array(chunk));
          return Promise.resolve();
        }
        // plain text
        chunks.push(new TextEncoder().encode(String(chunk)));
        return Promise.resolve();
      },
      close: function () {
        var total = 0;
        chunks.forEach(function (c) { total += c.length; });
        var out = new Uint8Array(total);
        var off = 0;
        chunks.forEach(function (c) { out.set(c, off); off += c.length; });
        tools.WriteFileBase64(path, bytesToBase64(out));
      },
      abort: function () { chunks = []; },
    };
  }

  window.showSaveFilePicker = function (options) {
    try {
      var suggested = (options && options.suggestedName) || 'export.xlsx';
      var path = tools.SaveFile(suggested);
      if (!path) {
        return Promise.reject(new DOMException('The user aborted a request.', 'AbortError'));
      }
      return Promise.resolve({ createWritable: function () { return Promise.resolve(makeWritable(path)); } });
    } catch (e) {
      return Promise.reject(e);
    }
  };
})();