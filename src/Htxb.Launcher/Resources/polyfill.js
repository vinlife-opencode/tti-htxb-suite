// TBI HTXB Tools & Utilities (launcher) — native-shell shim
// 1) Tool cards normally link to sibling HTML pages; here we intercept clicks
//    and launch the corresponding sibling EXE via window.tools.LaunchExe.
// 2) Version labels are synced from /manifest.json (served by the host from
//    data2\manifest.json), so they stay correct without editing the HTML.
(function () {
  'use strict';

  var tools = window.tools;
  if (!tools) return;

  var HREF_TO_EXE = {
    'TTI Temp_Uniformity_Graph.html': 'TTI_TempUniformity.exe',
    'TTI COBIS-II HTXB BiDF Analysis.html': 'TTI_BiDFAnalysis.exe',
    'TTI COBIS-II HTXB Lot Extractor.html': 'TTI_LotExtractor.exe',
    'TTI COBIS-II HTXB Recipe Translator.html': 'TTI_RecipeTranslator.exe',
    'TTI HTXB Rcp_Std_Table.html': 'TTI_RcpStdTable.exe',
    'TTI HTXB_P2 Data Analysis.html': 'TTI_P2DataAnalysis.exe',
  };

  var HREF_TO_TOOLID = {
    'TTI Temp_Uniformity_Graph.html': 'temp-uniformity',
    'TTI COBIS-II HTXB BiDF Analysis.html': 'bidf-analysis',
    'TTI COBIS-II HTXB Lot Extractor.html': 'lot-extractor',
    'TTI COBIS-II HTXB Recipe Translator.html': 'recipe-translator',
    'TTI HTXB Rcp_Std_Table.html': 'rcp-std-table',
    'TTI HTXB_P2 Data Analysis.html': 'p2-analysis',
  };

  function leaf(href) {
    if (!href) return '';
    var i = href.lastIndexOf('/');
    return i >= 0 ? href.slice(i + 1) : href;
  }

  // ---- Restricted tools: compressed by default, unlocked with a passphrase --
  // These three cards start as a one-line "compressed" bar; clicking the bar
  // asks for the admin passphrase before the tool is allowed to open. Only the
  // SHA-256 digest of the passphrase is embedded (lower-cased + trimmed), so
  // the passphrase itself never sits in the repo, the served page or the EXE.
  // The unlock lasts for this launcher session (a reload re-compresses them).
  // This is an operator speed bump, not a security control: anyone can read
  // this file out of the bundle.
  var RESTRICTED = {
    'TTI COBIS-II HTXB Lot Extractor.html': true,
    'TTI COBIS-II HTXB Recipe Translator.html': true,
    'TTI HTXB Rcp_Std_Table.html': true,
    'TTI HTXB_P2 Data Analysis.html': true,
  };
  var ADMIN_SHA256 = '163e49567eef0c05e154db2074383d0dcff8e04cefdfb3114df9a417627783e5';
  var _unlocked = {};

  function isLocked(a) {
    var href = leaf(a.getAttribute('href') || '');
    return !!RESTRICTED[href] && !_unlocked[href];
  }

  // Collapse a restricted card into its compressed bar. Done with CSS classes
  // on the anchor so the canonical markup is never edited.
  function restrictedStyles() {
    if (document.getElementById('tti-lock-css')) return;
    var css = document.createElement('style');
    css.id = 'tti-lock-css';
    css.textContent =
      '.tti-lock,.tti-lock-badge{display:none}' +
      'a.tool-card.tti-restricted{padding:7px 14px;gap:10px;min-height:0;' +
      'border-style:dashed;background:#fafafa;box-shadow:none;}' +
      'a.tool-card.tti-restricted:hover{background:#f4f8fc;box-shadow:none;transform:none;}' +
      'a.tool-card.tti-restricted .tool-icon,' +
      'a.tool-card.tti-restricted .tool-desc,' +
      'a.tool-card.tti-restricted .tool-arrow,' +
      'a.tool-card.tti-restricted .tool-version,' +
      'a.tool-card.tti-restricted .tool-badge{display:none!important;}' +
      'a.tool-card.tti-restricted .tool-body{display:flex;align-items:center;gap:8px;min-width:0;}' +
      'a.tool-card.tti-restricted .tool-name{font-size:12.5px;font-weight:500;' +
      'color:#5f5f5f;margin:0;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;}' +
      'a.tool-card.tti-restricted .tti-lock{display:inline-flex;flex-shrink:0;color:#8a8a8a;}' +
      'a.tool-card.tti-restricted .tti-lock-badge{display:inline-block;flex-shrink:0;' +
      'font-size:10px;font-weight:600;padding:1px 7px;border-radius:4px;' +
      'background:#fff0c2;color:#7a5b00;margin-left:auto;}';
    document.head.appendChild(css);
  }

  function lockGlyphs(a) {
    if (a.querySelector('.tti-lock')) return;
    var lock = document.createElement('span');
    lock.className = 'tti-lock';
    lock.setAttribute('aria-hidden', 'true');
    lock.innerHTML =
      '<svg xmlns="http://www.w3.org/2000/svg" width="14" height="14" fill="none" ' +
      'viewBox="0 0 24 24" stroke="currentColor" stroke-width="2">' +
      '<rect x="4.5" y="10.5" width="15" height="10" rx="2"/>' +
      '<path stroke-linecap="round" d="M8.25 10.5V7.5a3.75 3.75 0 0 1 7.5 0v3"/></svg>';
    var body = a.querySelector('.tool-body') || a;
    body.insertBefore(lock, body.firstChild);

    var badge = document.createElement('span');
    badge.className = 'tti-lock-badge';
    badge.textContent = 'Restricted';
    (a.querySelector('.tool-body') || a).appendChild(badge);
  }

  function compress(a) {
    restrictedStyles();
    lockGlyphs(a);
    a.classList.add('tti-restricted');
    a.title = 'Restricted — click to enter the admin passphrase';
  }

  function expand(a) {
    a.classList.remove('tti-restricted');
    a.removeAttribute('title');
  }

  function hex(bytes) {
    var s = '';
    for (var i = 0; i < bytes.length; i++) s += ('0' + bytes[i].toString(16)).slice(-2);
    return s;
  }

  // Resolve once: SHA-256 of the typed passphrase, compared to ADMIN_SHA256.
  function checkPass(typed, ok, fail) {
    var value = String(typed == null ? '' : typed).trim().toLowerCase();
    if (!value) { fail('Enter the admin passphrase.'); return; }
    if (!(window.crypto && window.crypto.subtle && window.TextEncoder)) {
      fail('Cannot verify the passphrase on this machine.');
      return;
    }
    window.crypto.subtle.digest('SHA-256', new TextEncoder().encode(value))
      .then(function (buf) {
        if (hex(new Uint8Array(buf)) === ADMIN_SHA256) ok();
        else fail('Incorrect passphrase.');
      })
      .catch(function () { fail('Cannot verify the passphrase on this machine.'); });
  }

  // Passphrase dialog: built once, re-wired for whichever card was clicked.
  var _lockOverlay = null;

  function busy(on) {
    if (tools && typeof tools.SetBusy === 'function') { try { tools.SetBusy(on); } catch (e) {} }
  }

  function buildLockDialog() {
    if (_lockOverlay) return _lockOverlay;

    var overlay = document.createElement('div');
    overlay.id = 'tti-lock-overlay';
    overlay.style.cssText = 'display:none;position:fixed;inset:0;background:rgba(0,0,0,.45);' +
      'z-index:10000;align-items:center;justify-content:center;padding:16px;';
    overlay.innerHTML =
      '<div style="background:#fff;border-radius:10px;max-width:360px;width:100%;' +
      'padding:20px 22px;box-shadow:0 8px 32px rgba(0,0,0,.25);">' +
      '<div style="display:flex;align-items:center;gap:8px;font-size:15px;font-weight:600;">' +
      '<svg xmlns="http://www.w3.org/2000/svg" width="18" height="18" fill="none" ' +
      'viewBox="0 0 24 24" stroke="#7a5b00" stroke-width="2">' +
      '<rect x="4.5" y="10.5" width="15" height="10" rx="2"/>' +
      '<path stroke-linecap="round" d="M8.25 10.5V7.5a3.75 3.75 0 0 1 7.5 0v3"/></svg>' +
      'Admin passphrase</div>' +
      '<div id="tti-lock-name" style="font-size:12px;color:#5f5f5f;margin:2px 0 10px;"></div>' +
      '<input id="tti-lock-pass" type="password" autocomplete="off" ' +
      'placeholder="Passphrase" style="width:100%;font-size:14px;padding:8px 10px;' +
      'border:1px solid #c8c8c8;border-radius:6px;"/>' +
      '<div id="tti-lock-err" style="color:#a80000;font-size:12px;min-height:18px;' +
      'margin:4px 0;"></div>' +
      '<div style="display:flex;gap:8px;margin-top:8px;">' +
      '<button id="tti-lock-ok" style="flex:1;background:#0067c0;color:#fff;border:none;' +
      'border-radius:6px;padding:9px 0;font-size:13px;font-weight:600;cursor:pointer;">Unlock</button>' +
      '<button id="tti-lock-cancel" style="background:#f3f3f3;color:#202020;' +
      'border:1px solid #d2d2d2;border-radius:6px;padding:9px 16px;font-size:13px;' +
      'cursor:pointer;">Cancel</button>' +
      '</div></div>';
    document.body.appendChild(overlay);

    overlay.addEventListener('click', function (ev) {
      if (ev.target === overlay && overlay.__hide) overlay.__hide();
    });
    document.addEventListener('keydown', function (ev) {
      if (ev.key === 'Escape' && overlay.style.display === 'flex' && overlay.__hide) overlay.__hide();
    });

    _lockOverlay = overlay;
    return overlay;
  }

  function askPassphrase(a) {
    var overlay = buildLockDialog();
    var input = overlay.querySelector('#tti-lock-pass');
    var err = overlay.querySelector('#tti-lock-err');
    var nameEl = overlay.querySelector('#tti-lock-name');
    var href = a.getAttribute('href') || '';
    var nameNode = a.querySelector('.tool-name');

    nameEl.textContent = nameNode
      ? (nameNode.textContent || '').replace('Restricted', '').trim()
      : '';

    function hide() {
      overlay.style.display = 'none';
      input.value = '';
      err.textContent = '';
      busy(false);
    }

    function submit() {
      checkPass(input.value, function () {
        _unlocked[href] = true;
        expand(a);
        hide();
        var exe = HREF_TO_EXE[leaf(href)];
        if (!exe) return;
        var res = tools.LaunchExe(exe);
        if (res !== 'ok' && res !== '') console.warn('LaunchExe failed: ' + exe + ' -> ' + res);
      }, function (msg) {
        err.textContent = msg;
        input.value = '';
        input.focus();
      });
    }

    overlay.__submit = submit;
    overlay.__hide = hide;
    overlay.querySelector('#tti-lock-ok').onclick = submit;
    overlay.querySelector('#tti-lock-cancel').onclick = hide;
    input.onkeydown = function (ev) {
      if (ev.key === 'Enter') { ev.preventDefault(); submit(); }
    };

    input.value = '';
    err.textContent = '';
    overlay.style.display = 'flex';
    busy(true);
    setTimeout(function () { input.focus(); }, 0);
  }

  // Compress on load, and re-apply after the version labels land.
  document.addEventListener('DOMContentLoaded', function () {
    document.querySelectorAll('a.tool-card').forEach(function (a) {
      if (RESTRICTED[leaf(a.getAttribute('href') || '')]) compress(a);
    });
  });

  // Intercept card clicks before the default navigation happens.
  document.addEventListener('click', function (ev) {
    var a = ev.target && ev.target.closest ? ev.target.closest('a.tool-card') : null;
    if (!a) return;
    if (isLocked(a)) {
      ev.preventDefault();
      ev.stopPropagation();
      askPassphrase(a);
      return;
    }
    var exe = HREF_TO_EXE[leaf(a.getAttribute('href') || '')];
    if (!exe) return;
    ev.preventDefault();
    ev.stopPropagation();
    var res = tools.LaunchExe(exe);
    if (res !== 'ok' && res !== '') {
      console.warn('LaunchExe failed: ' + exe + ' -> ' + res);
    }
  }, true);

  // Move each card's version + status badge onto their own line below the
  // tool name (spans keep their original classes, so fonts are unchanged).
  document.addEventListener('DOMContentLoaded', function () {
    document.querySelectorAll('.tool-name').forEach(function (nameEl) {
      if (nameEl.querySelector('.tool-meta')) return; // already restructured
      var ver = nameEl.querySelector('.tool-version');
      var badge = nameEl.querySelector('.tool-badge');
      if (!ver && !badge) return;
      var meta = document.createElement('div');
      meta.className = 'tool-meta';
      meta.style.marginTop = '2px';
      if (ver) meta.appendChild(ver);     // version first, then ready status
      if (badge) meta.appendChild(badge);
      nameEl.appendChild(meta);
    });
  });

  // Sync version labels from the host-served manifest.
  fetch('/manifest.json')
    .then(function (r) { return r.ok ? r.json() : null; })
    .then(function (manifest) {
      if (!manifest || !manifest.tools) return;

      function fmt(v) {
        var p = String(v).split('.');
        if (p.length >= 3 && parseInt(p[2], 10) > 0) {
          var minor = p[1].length < 2 ? '0' + p[1] : p[1];
          var ddr = String(p[2]);
          while (ddr.length < 3) ddr = '0' + ddr; // pad DDR to 3 digits (e.g. 81 -> 081)
          return 'V' + parseInt(p[0], 10) + '.' + minor + '.' + ddr;
        }
        return 'V' + parseInt(p[0], 10) + '.' + parseInt(p[1], 10);
      }

      document.querySelectorAll('a.tool-card').forEach(function (a) {
        var id = HREF_TO_TOOLID[leaf(a.getAttribute('href') || '')];
        var entry = id && manifest.tools[id];
        if (!entry || !entry.version) return;
        var span = a.querySelector('.tool-version');
        if (span) span.textContent = fmt(entry.version);
      });

      var self = manifest.tools['launcher'];
      var tag = document.querySelector('.logo-tag');
      if (self && self.version && tag) tag.textContent = fmt(self.version);
    })
    .catch(function () { /* keep hardcoded labels */ });

  // ---- Barcode Generator (bottom card + overlay) -------------------------
  // Code 128 barcode PNG for scanner data entry. Input is capped at 50
  // characters; illegal characters are reported, not silently dropped.
  // Export is black bars on white background, capped at 50 KB, saved as
  // "<content>.png" straight into the user's Downloads folder (no dialog).
  var BC_MAX_LEN = 50;
  var BC_MAX_BYTES = 50 * 1024;
  var BC_BAD_FILE = '<>:"/\\|?*';
  var BC_EXE_VERSION = null; // filled from manifest below

  // Load JsBarcode (served by the host from data2\lib) once, up front.
  if (typeof JsBarcode === 'undefined') {
    var bcScript = document.createElement('script');
    bcScript.src = '/libs/jsbarcode.all.min.js';
    document.head.appendChild(bcScript);
  }

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

  function bcBuildUI() {
    if (document.getElementById('tti-barcode-card')) return;

    var grid = document.querySelector('.tools-grid');
    if (!grid) return;

    var card = document.createElement('div');
    card.className = 'tool-card';
    card.id = 'tti-barcode-card';
    card.setAttribute('role', 'button');
    card.setAttribute('tabindex', '0');
    card.innerHTML =
      '<div class="tool-icon">' +
      '<svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" fill="none" viewBox="0 0 24 24" stroke="currentColor">' +
      '<path stroke-linecap="round" stroke-linejoin="round" stroke-width="1.8" d="M4 5v14M7.5 5v14M11 5v14M12.5 5v14M16 5v14M19.5 5v14"/>' +
      '</svg></div>' +
      '<div class="tool-body">' +
      '<div class="tool-name">Barcode Generator' +
      '<div class="tool-meta" style="margin-top:2px"><span class="tool-version">' +
      (BC_EXE_VERSION || '') + '</span><span class="tool-badge badge-ready">Ready</span></div></div>' +
      '<div class="tool-desc">Code 128 barcode PNG for scanner data entry. Saved to Downloads.</div>' +
      '</div>' +
      '<div class="tool-arrow">' +
      '<svg xmlns="http://www.w3.org/2000/svg" width="18" height="18" fill="none" viewBox="0 0 24 24" stroke="currentColor">' +
      '<path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="m8.25 4.5 7.5 7.5-7.5 7.5"/></svg>' +
      '</div>';
    grid.appendChild(card);

    var overlay = document.createElement('div');
    overlay.id = 'tti-barcode-overlay';
    overlay.style.cssText = 'display:none;position:fixed;inset:0;background:rgba(0,0,0,.45);' +
      'z-index:9999;align-items:center;justify-content:center;padding:16px;';
    overlay.innerHTML =
      '<div style="background:#fff;border-radius:10px;max-width:430px;width:100%;' +
      'padding:20px 22px;box-shadow:0 8px 32px rgba(0,0,0,.25);">' +
      '<div style="font-size:15px;font-weight:600;">Barcode Generator</div>' +
      '<div style="font-size:12px;color:#5f5f5f;margin:2px 0 10px;">Code 128 · max 50 characters · ' +
      'black on white · PNG ≤ 50 KB → Downloads</div>' +
      '<input id="tti-bc-input" maxlength="50" placeholder="e.g. P_TM_BRF9_B_05_4_32" ' +
      'style="width:100%;font-size:14px;padding:8px 10px;border:1px solid #c8c8c8;border-radius:6px;"/>' +
      '<div id="tti-bc-err" style="color:#a80000;font-size:12px;min-height:18px;margin:4px 0;"></div>' +
      '<div style="border:1px solid #e5e5e5;border-radius:8px;padding:10px;' +
      'display:flex;justify-content:center;background:#fff;overflow-x:auto;">' +
      '<canvas id="tti-bc-canvas" style="max-width:100%;"></canvas></div>' +
      '<div id="tti-bc-msg" style="font-size:12px;color:#006100;min-height:18px;margin:4px 0;"></div>' +
      '<div style="display:flex;gap:8px;margin-top:8px;">' +
      '<button id="tti-bc-export" style="flex:1;background:#0067c0;color:#fff;border:none;' +
      'border-radius:6px;padding:9px 0;font-size:13px;font-weight:600;cursor:pointer;">Export PNG</button>' +
      '<button id="tti-bc-back" style="background:#f3f3f3;color:#202020;border:1px solid #d2d2d2;' +
      'border-radius:6px;padding:9px 16px;font-size:13px;cursor:pointer;">Back</button>' +
      '</div></div>';
    document.body.appendChild(overlay);

    var input = overlay.querySelector('#tti-bc-input');
    var err = overlay.querySelector('#tti-bc-err');
    var msg = overlay.querySelector('#tti-bc-msg');
    var canvas = overlay.querySelector('#tti-bc-canvas');

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

    function open() {
      overlay.style.display = 'flex';
      if (tools && typeof tools.SetBusy === 'function') { try { tools.SetBusy(true); } catch (e) {} }
      setTimeout(function () { input.focus(); }, 0);
    }
    function close() {
      overlay.style.display = 'none';
      if (tools && typeof tools.SetBusy === 'function') { try { tools.SetBusy(false); } catch (e) {} }
    }

    card.addEventListener('click', open);
    card.addEventListener('keydown', function (ev) {
      if (ev.key === 'Enter' || ev.key === ' ') { ev.preventDefault(); open(); }
    });
    overlay.querySelector('#tti-bc-back').addEventListener('click', close);
    overlay.addEventListener('click', function (ev) { if (ev.target === overlay) close(); });
    document.addEventListener('keydown', function (ev) {
      if (ev.key === 'Escape' && overlay.style.display === 'flex') close();
    });
    input.addEventListener('input', preview);
    input.addEventListener('keydown', function (ev) {
      if (ev.key === 'Enter') { ev.preventDefault(); doExport(); }
    });
    overlay.querySelector('#tti-bc-export').addEventListener('click', doExport);
  }

  document.addEventListener('DOMContentLoaded', bcBuildUI);

  // Keep the barcode card's version in step with the launcher version.
  var bcOrigFetch = fetch;
  document.addEventListener('DOMContentLoaded', function () {
    bcOrigFetch.call(window, '/manifest.json')
      .then(function (r) { return r.ok ? r.json() : null; })
      .then(function (manifest) {
        var self = manifest && manifest.tools && manifest.tools['launcher'];
        if (!self || !self.version) return;
        var span = document.querySelector('#tti-barcode-card .tool-version');
        if (span) span.textContent = fmtLauncherVersion(self.version);
      })
      .catch(function () { /* keep blank */ });
  });

  // ---- P2 Data Analysis - Time (card injected next to P2 Data Analysis) ----
  // The canonical cover page has no card for this tool, so build it here (same
  // approach as the Barcode Generator card) and keep the served HTML untouched.
  // The tool pair two BiDF log folders and reports preset vs actual test time.
  var P2T_EXE = 'TTI_P2DataAnalysisTime.exe';
  var P2T_TOOLID = 'p2-analysis-time';
  var P2T_VERSION = 'V6.10.162';

  function buildP2TimeCard() {
    if (document.getElementById('tti-p2time-card')) return;
    var grid = document.querySelector('.tools-grid');
    if (!grid) return;

    var card = document.createElement('div');
    card.className = 'tool-card';
    card.id = 'tti-p2time-card';
    card.setAttribute('role', 'button');
    card.setAttribute('tabindex', '0');
    card.innerHTML =
      '<div class="tool-icon">' +
      '<svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" fill="none" viewBox="0 0 24 24" stroke="currentColor">' +
      '<path stroke-linecap="round" stroke-linejoin="round" stroke-width="1.8" d="M12 6v6h4.5m4.5 0a9 9 0 1 1-18 0 9 9 0 0 1 18 0Z"/></svg>' +
      '</div>' +
      '<div class="tool-body">' +
      '<div class="tool-name">P2 Data Analysis &ndash; Time' +
      '<div class="tool-meta" style="margin-top:2px"><span class="tool-version">' + P2T_VERSION + '</span>' +
      '<span class="tool-badge badge-ready">Ready</span></div></div>' +
      '<div class="tool-desc">Preset vs actual test time from BiDF logs. Compares two folders slot-to-slot and lot-to-lot.</div>' +
      '</div>' +
      '<div class="tool-arrow">' +
      '<svg xmlns="http://www.w3.org/2000/svg" width="18" height="18" fill="none" viewBox="0 0 24 24" stroke="currentColor">' +
      '<path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="m8.25 4.5 7.5 7.5-7.5 7.5"/></svg>' +
      '</div>';

    function launch() {
      var res = tools.LaunchExe(P2T_EXE);
      if (res !== 'ok' && res !== '') console.warn('LaunchExe failed: ' + P2T_EXE + ' -> ' + res);
    }
    card.addEventListener('click', launch);
    card.addEventListener('keydown', function (ev) {
      if (ev.key === 'Enter' || ev.key === ' ') { ev.preventDefault(); launch(); }
    });

    // Third slot from the top (2026-10-25): sits directly under Temp
    // Uniformity + BiDF Analysis. Deliberately NOT anchored to the
    // "P2 Data Analysis" card any more — the P2 pair is no longer adjacent.
    // Only anchor links are counted, so this div card and the Barcode card
    // are ignored wherever they happen to land.
    var anchorCards = grid.querySelectorAll('a.tool-card');
    if (anchorCards.length >= 3) grid.insertBefore(card, anchorCards[2]);
    else grid.appendChild(card);
  }

  document.addEventListener('DOMContentLoaded', buildP2TimeCard);

  // Final card order. The two injected cards used to land by luck: P2 Data
  // Analysis - Time 3rd (anchored to the anchor list) and the Barcode card last.
  // The Barcode Generator belongs 4th, right under the P2 pair, so place both
  // explicitly here - this listener is registered after bcBuildUI and
  // buildP2TimeCard, so both cards already exist and the result does not depend
  // on handler order.
  document.addEventListener('DOMContentLoaded', function () {
    var grid = document.querySelector('.tools-grid');
    if (!grid) return;
    var bc = document.getElementById('tti-barcode-card');
    if (!bc) return;
    var kids = grid.children;
    if (kids.length > 3) grid.insertBefore(bc, kids[3]);
    else grid.appendChild(bc);
  });

  document.addEventListener('DOMContentLoaded', function () {
    fetch('/manifest.json')
      .then(function (r) { return r.ok ? r.json() : null; })
      .then(function (manifest) {
        var entry = manifest && manifest.tools && manifest.tools[P2T_TOOLID];
        var span = document.querySelector('#tti-p2time-card .tool-version');
        if (entry && entry.version && span) span.textContent = fmtLauncherVersion(entry.version);
      })
      .catch(function () { /* keep the hardcoded label */ });
  });

  // ---- Back-to-top button (Windows-style, bottom-right corner) ------------
  // Appears once the cover page is scrolled down; click scrolls smoothly up.
  (function initBackToTop() {
    function build() {
      if (document.getElementById('tti-back-top')) return;
      var btn = document.createElement('button');
      btn.id = 'tti-back-top';
      btn.type = 'button';
      btn.setAttribute('aria-label', 'Back to top');
      btn.title = 'Back to top';
      btn.style.cssText = 'display:none;position:fixed;right:16px;bottom:16px;' +
        'width:40px;height:40px;background:#ffffff;border:1px solid #d2d2d2;' +
        'border-radius:4px;box-shadow:0 2px 8px rgba(0,0,0,.15);cursor:pointer;' +
        'align-items:center;justify-content:center;z-index:9998;padding:0;';
      btn.innerHTML =
        '<svg xmlns="http://www.w3.org/2000/svg" width="18" height="18" fill="none" ' +
        'viewBox="0 0 24 24" stroke="#202020" stroke-width="2">' +
        '<path stroke-linecap="round" stroke-linejoin="round" d="m4.5 15.75 7.5-7.5 7.5 7.5"/></svg>';
      btn.addEventListener('mouseenter', function () { btn.style.borderColor = '#0067c0'; });
      btn.addEventListener('mouseleave', function () { btn.style.borderColor = '#d2d2d2'; });
      btn.addEventListener('click', function () {
        try {
          window.scrollTo({ top: 0, behavior: 'smooth' });
        } catch (e) { window.scrollTo(0, 0); }
      });
      document.body.appendChild(btn);

      window.addEventListener('scroll', function () {
        var y = window.pageYOffset || document.documentElement.scrollTop ||
          document.body.scrollTop || 0;
        btn.style.display = y > 200 ? 'flex' : 'none';
      }, { passive: true });
    }
    if (document.body) build();
    else document.addEventListener('DOMContentLoaded', build);
  })();

  function fmtLauncherVersion(v) {
    var p = String(v).split('.');
    if (p.length >= 3 && parseInt(p[2], 10) > 0) {
      var minor = p[1].length < 2 ? '0' + p[1] : p[1];
      var ddr = String(p[2]);
      while (ddr.length < 3) ddr = '0' + ddr;
      return 'V' + parseInt(p[0], 10) + '.' + minor + '.' + ddr;
    }
    return 'V' + parseInt(p[0], 10) + '.' + parseInt(p[1], 10);
  }
})();