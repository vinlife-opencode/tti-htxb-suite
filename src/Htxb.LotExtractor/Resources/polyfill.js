// TTI Lot Extractor — File System Access API shim
// WebView2 does not implement window.showDirectoryPicker() / FileSystemDirectoryHandle.
// This polyfill re-exposes them on top of the native bridge (window.tools) so the
// existing tool logic runs unchanged. The bridge returns absolute native paths.

(function () {
  'use strict';

  // --- Lot Keyword: manual key-in (default 'VC') -----------------------------
  // The page renders the Lot Keyword filter as a <select> with only VC/All
  // preset options. Replace it with a free-text input (default 'VC') so the
  // user can key in any lot keyword. The page's getFilters() still reads
  // #filter-lot-keyword.value, so no page change is needed. Leaving the field
  // empty (or typing 'All') disables the keyword filter, as before.
  function convertLotKeywordToInput() {
    try {
      var sel = document.getElementById('filter-lot-keyword');
      if (!sel || sel.tagName !== 'SELECT') return;
      var input = document.createElement('input');
      input.type = 'text';
      input.id = 'filter-lot-keyword';
      input.className = sel.className || '';
      input.title = 'Lot keyword (starts-with match). Leave empty or enter All for no filter.';
      input.placeholder = 'VC';
      input.style.minWidth = '120px';
      input.value = sel.value || 'VC'; // default wording starts with 'VC'
      sel.parentNode.replaceChild(input, sel);
    } catch (e) { /* ignore */ }
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', convertLotKeywordToInput);
  } else {
    convertLotKeywordToInput();
  }

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

  window.__ttiLotRoot = null; // input folder path captured by showDirectoryPicker

  var guardNotAllowed = function (p) {
    if (!p) {
      var err = new Error('Operation cancelled');
      err.name = 'AbortError';
      throw err;
    }
    return p;
  };

  function VNode(path, name, kind) {
    this.path = path;
    this.name = name;
    this.kind = kind; // 'file' | 'directory'
  }

  // --- Directory enumeration -------------------------------------------------
  // The page drives its own folder walk via dirHandle.values() after picking a
  // directory. Our showDirectoryPicker shim returns a VNode, so VNode must
  // enumerate real children or the page's readDirHandleRecursive() throws and
  // selectInputFolder() falls back to the file-input dialog ("select folder to
  // upload") and never proceeds. Children are listed natively through the
  // bridge; CSV/date filtering is applied later (native scan / processFolderFiles),
  // so no content is read here.
  //
  // When __ttiLotRoot is set (we're inside the pick flow), the page's
  // readDirHandleRecursive is about to walk the entire tree and build a
  // files[] array that processFolderFiles (our override) discards when it
  // short-circuits to runNativeLotScan. Yield nothing so the walk completes
  // instantly and the native scan starts immediately.

  function readDateBounds() {
    var from = '', to = '';
    var f = null, t = null;
    try { f = document.getElementById('filter-date-from'); } catch (e) {}
    try { t = document.getElementById('filter-date-to'); } catch (e) {}
    if (f && f.value) from = String(f.value).replace(/-/g, '');
    if (t && t.value) to = String(t.value).replace(/-/g, '');
    return { from: from, to: to };
  }

  function filenameDateKey(name) {
    var n = String(name || '').replace(/\.csv$/i, '');
    var m = n.match(/(\d{4})[-_]?(\d{2})[-_]?(\d{2})[-_]?(\d{2})[-_]?(\d{2})[-_]?(\d{2})/);
    if (m) return m[1] + m[2] + m[3] + m[4] + m[5] + m[6];
    var m2 = n.match(/(\d{4})[-_]?(\d{2})[-_]?(\d{2})[-_]?(\d{2})[-_]?(\d{2})/);
    if (m2) return m2[1] + m2[2] + m2[3] + '0000';
    var m3 = n.match(/(\d{4})[-_]?(\d{2})[-_]?(\d{2})/);
    if (m3) return m3[1] + m3[2] + m3[3] + '000000';
    return '';
  }

  function isCsv(name) {
    return /\.csv$/i.test(String(name || ''));
  }

  function csvInDateRange(name) {
    if (!isCsv(name)) return true; // non-CSV is handled by the page
    var raw = filenameDateKey(name).substring(0, 8);
    if (raw.length !== 8) return true; // no date in the name -> keep (page does the same)
    var b = readDateBounds();
    if (b.from && raw < b.from) return false;
    if (b.to && raw > b.to) return false;
    return true;
  }

  VNode.prototype.values = async function* () {
    if (window.__ttiLotRoot) return;
    var items = null;
    try { items = JSON.parse(tools.ListDirectory(this.path)); }
    catch (e) { items = []; }
    for (var i = 0; i < items.length; i++) {
      var it = items[i] || {};
      if (!it.name) continue;
      yield new VNode(it.p, it.name, it.isDir ? 'directory' : 'file');
    }
  };

  VNode.prototype.getDirectoryHandle = async function (name, options) {
    guardNotAllowed(name);
    var p = tools.JoinPath(this.path, name);
    if (options && options.create) tools.CreateDirectory(p);
    return new VNode(p, name, 'directory');
  };

  VNode.prototype.getFileHandle = async function (name, options) {
    guardNotAllowed(name);
    var p = tools.JoinPath(this.path, name);
    var node = new VNode(p, name, 'file');
    node.createWritable = async function () {
      var buffer = '';
      return {
        write: function (data) { buffer = buffer + data; },
        close: async function () { tools.WriteFile(p, buffer); }
      };
    };
    return node;
  };

  VNode.prototype.getFile = async function () {
    // Never materialize file content into the WebView2 heap. Return an (empty)
    // File tagged with the native path; heavy CSV parsing is done natively and
    // export re-reads the file from disk via that path.
    var f;
    try {
      f = new File([], this.name, { type: 'text/plain' });
    } catch (e) {
      f = { name: this.name, size: 0, type: 'text/plain' };
    }
    try { Object.defineProperty(f, '__ttiNativePath', { value: this.path, writable: false }); } catch (e) {}
    return f;
  };

  function pickDirectory(options) {
    var p = tools.SelectDirectory();
    p = guardNotAllowed(p);
    p = tools.NormalizePath ? tools.NormalizePath(p) : p;
    var leaf = tools.LeafName(p) || 'Selected Folder';
    // Show a folder summary (file/CSV/subfolder totals) once the path bar is up.
    setTimeout(function () { showFolderSummaryNative(p); }, 0);
    var mode = (options && options.mode) || '';
    if (mode !== 'readwrite') window.__ttiLotRoot = p; // input folder for the native scan
    return new VNode(p, leaf, 'directory');
  }

  // Always take over the folder picker inside the EXE so no WebView2/native
  // file-system permission dialog can ever be raised.
  try {
    Object.defineProperty(window, 'showDirectoryPicker', { value: pickDirectory, writable: true, configurable: true });
  } catch (e) {
    window.showDirectoryPicker = pickDirectory;
  }

  // --- Folder summary --------------------------------------------------------
  // Renders e.g. "In this folder: 12,707 total files, 6,677 CSV files across
  // 24 slot subfolders" under the selected path, right after the user picks it.

  function getSummaryAnchor() {
    var pathText = document.getElementById('input-path-text');
    if (!pathText) return null;
    var bar = pathText.parentElement;
    return bar && bar.id === 'input-path-display' ? bar : (pathText.parentElement || pathText);
  }

  function renderFolderSummary(text) {
    var anchor = getSummaryAnchor();
    if (!anchor) return;
    var el = document.getElementById('tti-folder-summary');
    if (!el) {
      el = document.createElement('div');
      el.id = 'tti-folder-summary';
      el.style.marginTop = '6px';
      el.style.padding = '6px 12px';
      el.style.background = '#f4f9ff';
      el.style.border = '1px solid #cce5ff';
      el.style.borderRadius = '6px';
      el.style.fontSize = '12px';
      el.style.color = '#1f6feb';
      el.style.fontWeight = '500';
      el.style.textAlign = 'left';
      anchor.parentNode.appendChild(el);
    }
    el.innerText = text;
  }

  function fmt(n) { return String(n).replace(/\B(?=(\d{3})+(?!\d))/g, ','); }

  function showFolderSummaryNative(path) {
    try {
      if (typeof tools.CountFiles !== 'function' || !path) return;
      var s = JSON.parse(tools.CountFiles(path));
      renderFolderSummary(
        'In this folder: ' + fmt(s.totalFiles) + ' total files, '
        + fmt(s.csvFiles) + ' CSV files across '
        + fmt(s.subfolders) + ' slot subfolders.'
      );
    } catch (e) { /* ignore */ }
  }

  function showFolderSummaryFromFiles(fileList) {
    try {
      var list = fileList || [];
      if (list.length === 0) return;
      var total = 0, csv = 0;
      var folders = {};
      for (var i = 0; i < list.length; i++) {
        var f = list[i];
        if (!f || !f.name) continue;
        total++;
        if (/\.csv$/i.test(f.name)) csv++;
        var rp = f.webkitRelativePath || '';
        var first = rp.split('/')[0];
        if (first) folders[first] = true;
      }
      renderFolderSummary(
        'In this folder: ' + fmt(total) + ' total files, '
        + fmt(csv) + ' CSV files across '
        + fmt(Object.keys(folders).length) + ' slot subfolders.'
      );
    } catch (e) { /* ignore */ }
  }

  // Legacy <input type=file webkitdirectory> / drag-drop: derive the summary
  // from the picked File objects (they carry webkitRelativePath).
  document.addEventListener('change', function (e) {
    var t = e.target;
    if (t && t.id === 'folder-input' && t.files && t.files.length > 0) {
      setTimeout(function () { showFolderSummaryFromFiles(Array.prototype.slice.call(t.files)); }, 0);
    }
  });

  // --- Native CSV scan (memory-friendly) -------------------------------------
  // The page's own parse loop reads every CSV into the WebView2 heap (File blobs
  // + full parsed rows), which can exceed 4 GB on a large slot folder. Instead,
  // when a folder is picked through showDirectoryPicker we send the root path to
  // the native bridge, which reads/parses files on a background thread and only
  // returns compact per-file summaries. The page's flow continues unchanged.

  var hasNativeScan = function () {
    return typeof tools.BeginScanLotCsvs === 'function'
        && typeof tools.PollScanLotCsvs === 'function';
  };

  function makeNativeFile(name, path) {
    var f;
    try { f = new File([], name, { type: 'text/plain' }); }
    catch (e) { f = { name: name, size: 0, type: 'text/plain' }; }
    try { Object.defineProperty(f, '__ttiNativePath', { value: path, writable: false }); } catch (e) {}
    return f;
  }

  function runNativeLotScan(root) {
    var overlay = document.getElementById('progress-overlay');
    function setEl(id, txt) { try { var el = document.getElementById(id); if (el) el.innerText = txt; } catch (e) {} }
    function setBar(pct) {
      try {
        var b = document.getElementById('progress-bar');
        var p = document.getElementById('progress-percent');
        if (b) b.style.width = pct + '%';
        if (p) p.innerText = pct + '%';
      } catch (e) {}
    }

    setEl('progress-header', 'Scanning CSV Files');
    setEl('progress-status-msg', 'Reading CSV files (native engine)...');
    setBar(5);
    if (overlay) overlay.style.display = 'flex';

    var bounds = readDateBounds();
    try { tools.BeginScanLotCsvs(root, bounds.from, bounds.to); }
    catch (e) {
      if (overlay) overlay.style.display = 'none';
      try { window.showToast('Scan failed.', 'danger'); } catch (e2) {}
      return;
    }

    var timer = setInterval(function () {
      var st = null;
      try { st = JSON.parse(tools.PollScanLotCsvs()); } catch (e) {}
      if (!st) return;
      var totalN = st.total || 0;
      var workedN = st.processed || 0;
      setBar(5 + Math.round((totalN > 0 ? workedN / totalN : 0) * 55));
      setEl('progress-status-msg', 'Parsing ' + workedN + ' of ' + totalN + ' CSV files...');

      if (st.running) return;
      clearInterval(timer);

      if (st.error) {
        if (overlay) overlay.style.display = 'none';
        try { window.showToast('Scan failed: ' + st.error, 'danger'); } catch (e) {}
        return;
      }

      if (typeof window.finishParsing !== 'function') {
        if (overlay) overlay.style.display = 'none';
        return;
      }

      var summaries = Array.isArray(st.result) ? st.result : [];
      var parsed = [];
      for (var i = 0; i < summaries.length; i++) {
        var s = summaries[i] || {};
        parsed.push({
          id: i,
          name: s.name,
          path: s.path,
          dtCode: s.dtCode,
          dtKey: s.dtKey,
          lot: s.lot,
          slot: s.slot,
          status: s.status,
          rows: { length: s.rowCount },
          file: makeNativeFile(s.name, s.path)
        });
      }

      if (parsed.length === 0) {
        if (overlay) overlay.style.display = 'none';
        var b = readDateBounds();
        var msg = (b.from || b.to)
          ? 'No CSV files found for the selected date range. Try a wider Date From / Date To.'
          : 'No CSV files found in the selected folder.';
        try { window.showToast(msg, 'danger'); } catch (e) {}
        return;
      }
      window.finishParsing(parsed, parsed.length);
    }, 150);
  }

  // Safety net / legacy paths (input + drag-drop): keep CSV-only + date-range
  // pre-filter so the page's sequential parse only sees qualifying files.
  // Modern paths (showDirectoryPicker) are handed straight to the native scan.
  window.addEventListener('load', function () {
    if (typeof window.processFolderFiles !== 'function') return;
    var origProcess = window.processFolderFiles;
    window.processFolderFiles = function (files) {
      var root = window.__ttiLotRoot;
      if (root && hasNativeScan()) {
        window.__ttiLotRoot = null;
        return runNativeLotScan(root);
      }
      window.__ttiLotRoot = null;
      var kept = (files || []).filter(function (f) {
        if (!f || !f.name) return false;
        if (!isCsv(f.name)) return false; // rule 1: CSV only
        return csvInDateRange(f.name);    // rule 2: within date range
      });
      return origProcess.call(this, kept);
    };

    // Export re-reads each file's content to copy it to the output folder.
    // Tagged files (native scan) are read straight from disk; real File objects
    // (drag-drop / legacy) fall back to the page's normal reader.
    if (typeof window.readFileAsText === 'function') {
      var origReadText = window.readFileAsText;
      window.readFileAsText = function (file) {
        if (file && file.__ttiNativePath && typeof tools.ReadFile === 'function') {
          try { return Promise.resolve(tools.ReadFile(file.__ttiNativePath)); } catch (e) { /* fall through */ }
        }
        return origReadText(file);
      };
    }
  });
})();