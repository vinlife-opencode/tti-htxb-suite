// TTI Barcode Generator — standalone page logic (Code 128 -> Downloads).
// The page markup lives in the embedded index.html; this script validates the
// input (max 50 chars, illegal characters reported), renders a live black-
// on-white preview via JsBarcode (data2\lib), and exports "<content>.png"
// (capped at 50 KB) straight into the user's Downloads folder.
(function () {
  'use strict';

  var tools = window.tools;
  if (!tools) return;

  var BC_MAX_LEN = 50;
  var BC_MAX_BYTES = 50 * 1024;
  var BC_BAD_FILE = '<>:"/\\|?*';

  function bcValidate(value) {
    if (!value) return 'Please enter a barcode string.';
    if (value.length > BC_MAX_LEN)
      return 'Too long: ' + value.length + ' characters (max ' + BC_MAX_LEN + ').';
    var bad = [];
    for (var i = 0; i < value.length; i++) {
      var ch = value[i];
      var code = value.charCodeAt(i);
      if (code < 32 || code > 126 || BC_BAD_FILE.indexOf(ch) >= 0) {
        if (bad.indexOf(ch) < 0) bad.push(ch);
      }
    }
    if (bad.length) {
      return 'Invalid character(s): ' + bad.map(function (c) {
        var code = c.charCodeAt(0);
        return '"' + c + '" (U+' + ('000' + code.toString(16).toUpperCase()).slice(-4) + ')';
      }).join(', ') + '. Allowed: letters, digits, space and _ - . etc. (no ' + BC_BAD_FILE + ').';
    }
    if (/^[ .]/.test(value) || /[ .]$/.test(value))
      return 'Must not start/end with a space or dot (used as the file name).';
    return '';
  }

  // Render black-on-white Code 128 onto the canvas. Shrinks the module width
  // (3 -> 1) until the PNG fits the 50 KB cap. Returns byte size, or -1.
  function bcRender(canvas, value) {
    if (typeof JsBarcode === 'undefined') return -1;
    var width = 3, size = -1;
    while (width >= 1) {
      try {
        JsBarcode(canvas, value, {
          format: 'CODE128',
          lineColor: '#000000',
          background: '#ffffff',
          displayValue: true,
          fontSize: 32,
          height: 80,
          width: width,
          margin: 10
        });
      } catch (e) { return -1; }
      var url = canvas.toDataURL('image/png');
      size = Math.floor((url.length - url.indexOf(',') - 1) * 3 / 4);
      if (size <= BC_MAX_BYTES) break;
      width--;
    }
    return size <= BC_MAX_BYTES ? size : -1;
  }

  function fmt(v) {
    var p = String(v).split('.');
    if (p.length >= 3 && parseInt(p[2], 10) > 0) {
      var minor = p[1].length < 2 ? '0' + p[1] : p[1];
      var ddr = String(p[2]);
      while (ddr.length < 3) ddr = '0' + ddr;
      return 'V' + parseInt(p[0], 10) + '.' + minor + '.' + ddr;
    }
    return 'V' + parseInt(p[0], 10) + '.' + parseInt(p[1], 10);
  }

  document.addEventListener('DOMContentLoaded', function () {
    var input = document.getElementById('bc-input');
    var err = document.getElementById('bc-err');
    var msg = document.getElementById('bc-msg');
    var canvas = document.getElementById('bc-canvas');
    var btn = document.getElementById('bc-export');
    if (!input || !canvas || !btn) return;

    function preview() {
      msg.textContent = '';
      var v = input.value;
      if (!v) {
        err.textContent = '';
        canvas.width = 0; canvas.height = 0;
        return -1;
      }
      var problem = bcValidate(v);
      if (problem) {
        err.textContent = problem;
        canvas.width = 0; canvas.height = 0;
        return -1;
      }
      err.textContent = '';
      if (typeof JsBarcode === 'undefined') {
        err.textContent = 'Barcode library still loading — try again in a moment.';
        return -1;
      }
      var size = bcRender(canvas, v);
      if (size < 0) err.textContent = 'Cannot encode this string as Code 128 within 50 KB.';
      return size;
    }

    function doExport() {
      msg.textContent = '';
      var v = input.value;
      var problem = bcValidate(v);
      if (problem) { err.textContent = problem; return; }
      err.textContent = '';
      var size = bcRender(canvas, v);
      if (size < 0) {
        err.textContent = (typeof JsBarcode === 'undefined')
          ? 'Barcode library still loading — try again in a moment.'
          : 'Cannot encode this string as Code 128 within 50 KB.';
        return;
      }
      var dl = tools.GetDownloadsFolder();
      if (!dl) { err.textContent = 'Could not resolve the Downloads folder.'; return; }
      var dest = tools.JoinPath(dl, v + '.png');
      var url = canvas.toDataURL('image/png');
      tools.WriteFileBase64(dest, url.slice(url.indexOf(',') + 1));
      msg.textContent = 'Saved ' + v + '.png (' + (size / 1024).toFixed(1) + ' KB) → ' + dl;
    }

    input.addEventListener('input', preview);
    input.addEventListener('keydown', function (ev) {
      if (ev.key === 'Enter') { ev.preventDefault(); doExport(); }
    });
    btn.addEventListener('click', doExport);

    // Header version badge, synced from the host-served manifest.
    fetch('/manifest.json')
      .then(function (r) { return r.ok ? r.json() : null; })
      .then(function (manifest) {
        var self = manifest && manifest.tools && manifest.tools['barcode-generator'];
        var tag = document.querySelector('.logo-tag');
        if (self && self.version && tag) tag.textContent = fmt(self.version);
      })
      .catch(function () { /* keep blank */ });
  });
})();
