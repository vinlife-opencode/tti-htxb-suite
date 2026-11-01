using System.Globalization;
using Htxb.RecipeCore.History;

namespace Htxb.HistoryUi;

/// <summary>
/// The grid: every lot of the archive, filtered by the folder stamp, coloured by what
/// the watcher did with it, with a tick-box per lot and one Recover button.
///
/// Colours are the operator's shorthand, not decoration: light green = converted,
/// light pink = the run was stopped by hand, light amber = converted without a BiDF
/// (the lots this window exists for).
/// </summary>
internal sealed class HistoryForm : Form
{
    private static readonly Color Converted = Color.FromArgb(0xE6, 0xF4, 0xEA);   // light green
    private static readonly Color Stopped = Color.FromArgb(0xFD, 0xE7, 0xE9);     // light pink
    private static readonly Color ReportOnly = Color.FromArgb(0xFF, 0xF4, 0xCE);  // light amber

    private readonly HistoryOptions _options;
    private readonly string _history;
    private readonly string _process;

    private readonly DateTimePicker _from = new() { Format = DateTimePickerFormat.Short, Width = 110 };
    private readonly DateTimePicker _to = new() { Format = DateTimePickerFormat.Short, Width = 110 };
    private readonly DataGridView _grid = new();
    private readonly Label _summary = new() { AutoSize = true, Padding = new Padding(6, 6, 0, 0) };
    private readonly Button _recover = new() { Text = "Recover selected", AutoSize = true, Enabled = false };

    private List<LotHistory> _lots = [];

    public HistoryForm(HistoryOptions options, string historyDirectory, string processDirectory)
    {
        _options = options;
        _history = historyDirectory;
        _process = processDirectory;

        Text = "Lots not in the profile list - TTI Post Data Processor";
        Width = 1180;
        Height = 560;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(820, 320);

        _from.Value = DateTime.Today.AddDays(-14);
        _to.Value = DateTime.Today.AddDays(1);

        var scan = new Button { Text = "Scan", AutoSize = true };
        scan.Click += (_, _) => Scan();

        var open = new Button { Text = "Open reports folder", AutoSize = true };
        open.Click += (_, _) => OpenFolder(_history);

        _recover.Click += (_, _) => RecoverTicked();

        var top = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(6),
            WrapContents = false,
        };
        top.Controls.AddRange(
        [
            new Label { Text = "From", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, _from,
            new Label { Text = "to", AutoSize = true, Padding = new Padding(8, 6, 0, 0) }, _to,
            scan, _recover, open, _summary,
        ]);

        BuildGrid();
        Controls.Add(_grid);
        Controls.Add(top);

        Shown += (_, _) => Scan();
    }

    private void BuildGrid()
    {
        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = false;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = true;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.BackgroundColor = SystemColors.Window;
        _grid.CellValueChanged += (_, e) => { if (e.RowIndex >= 0) UpdateRecoverButton(); };
        _grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_grid.IsCurrentCellDirty) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };

        _grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "tick", HeaderText = "", Width = 30, FillWeight = 3 });
        Column("lot", "Lot", 26);
        Column("created", "Created", 12);
        Column("slots", "Slots", 6);
        Column("status", "Status", 12);
        Column("recipe", "Recipe", 20);
        Column("profile", "In profile", 8);
        Column("outputs", "Output", 12);
        Column("state", "State", 12);
        Column("note", "Note", 40);

        void Column(string name, string header, int weight) =>
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = name,
                HeaderText = header,
                ReadOnly = true,
                FillWeight = weight,
            });
    }

    private void Scan()
    {
        Cursor = Cursors.WaitCursor;
        try
        {
            var lots = new List<LotHistory>();
            if (Directory.Exists(_options.ArchiveDirectory))
            {
                foreach (string folder in Directory.GetDirectories(_options.ArchiveDirectory)
                             .OrderByDescending(p => p, StringComparer.OrdinalIgnoreCase))
                {
                    try
                    {
                        LotHistory lot = LotScanner.Scan(folder, _options);
                        if (InRange(lot)) lots.Add(lot);
                    }
                    catch (Exception)
                    {
                        // A folder the scanner cannot read is not this window's business:
                        // `history report` is where it is named and accounted for.
                    }
                }
            }
            _lots = lots;
            Fill();
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private bool InRange(LotHistory lot)
    {
        if (!DateTime.TryParseExact(lot.Stamp, "yyyyMMddHHmmss", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out DateTime when))
            return true;                    // an unreadable stamp is shown, not hidden
        return when.Date >= _from.Value.Date && when.Date <= _to.Value.Date.AddDays(1);
    }

    private void Fill()
    {
        _grid.Rows.Clear();
        foreach (LotHistory lot in _lots)
        {
            int index = _grid.Rows.Add(
                false,
                lot.Folder,
                DateTime.TryParseExact(lot.Stamp, "yyyyMMddHHmmss", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out DateTime when) ? when.ToString("yyyy-MM-dd HH:mm") : lot.Stamp,
                lot.Slots,
                lot.Status,
                lot.Recipe,
                lot.RecipeInProfile,
                lot.Outputs,
                lot.State,
                lot.Note);

            DataGridViewRow row = _grid.Rows[index];
            row.Tag = lot;
            row.DefaultCellStyle.BackColor =
                lot.Status == "Manual Stop" ? Stopped
                : lot.Outputs == "report-only" ? ReportOnly
                : lot.Outputs == "full" ? Converted
                : Color.Empty;

            // Only a report-only, Completed lot with its raw CSV still present can be
            // recovered; the others keep their tick disabled rather than failing later.
            (bool can, string _) = LotRecovery.CanRecover(lot, _options.ArchiveDirectory, _process);
            DataGridViewCheckBoxCell tick = (DataGridViewCheckBoxCell)row.Cells["tick"];
            tick.Value = false;
            tick.ReadOnly = !can;
            if (!can) row.Cells["tick"].Style.BackColor = SystemColors.Control;
        }

        _summary.Text = $"{_lots.Count} lot(s) in range  -  " +
                        $"{_lots.Count(l => l.Outputs == "report-only")} without a BiDF  -  " +
                        $"{_lots.Count(l => l.State == "not processed")} not processed";
        UpdateRecoverButton();
    }

    private void UpdateRecoverButton() =>
        _recover.Enabled = _grid.Rows.Cast<DataGridViewRow>()
            .Any(r => r.Cells["tick"].Value is true);

    private void RecoverTicked()
    {
        List<LotHistory> ticked = _grid.Rows.Cast<DataGridViewRow>()
            .Where(r => r.Cells["tick"].Value is true && r.Tag is LotHistory)
            .Select(r => (LotHistory)r.Tag!)
            .ToList();

        if (ticked.Count == 0) return;

        string list = string.Join(Environment.NewLine,
            ticked.Select(l => $"  {l.Folder}   ({l.Recipe}, {l.Slots} slots, {l.Status})"));
        DialogResult answer = MessageBox.Show(this,
            $"Recover {ticked.Count} lot(s)?{Environment.NewLine}{Environment.NewLine}{list}" +
            $"{Environment.NewLine}{Environment.NewLine}" +
            "Each lot's current output is moved into the reports folder first, then its raw CSVs" +
            $"{Environment.NewLine}are copied into the process folder and the watcher converts them again." +
            $"{Environment.NewLine}{Environment.NewLine}Nothing is deleted.",
            "Recover lots", MessageBoxButtons.OKCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);

        if (answer != DialogResult.OK) return;

        var done = new List<string>();
        var failed = new List<string>();

        Cursor = Cursors.WaitCursor;
        try
        {
            foreach (LotHistory lot in ticked)
            {
                try
                {
                    RecoveryOutcome outcome = LotRecovery.Recover(lot,
                        _options.FinalDirectory, _options.ArchiveDirectory, _process, _history);
                    done.Add($"{outcome.Lot}: {outcome.Note}");
                }
                catch (Exception error)
                {
                    failed.Add($"{lot.Folder}: {error.Message}");
                }
            }
        }
        finally
        {
            Cursor = Cursors.Default;
        }

        string message = done.Count > 0
            ? $"{done.Count} lot(s) handed back to the watcher:{Environment.NewLine}  " +
              string.Join(Environment.NewLine + "  ", done)
            : "nothing was recovered.";
        if (failed.Count > 0)
            message += $"{Environment.NewLine}{Environment.NewLine}Not recovered:{Environment.NewLine}  " +
                       string.Join(Environment.NewLine + "  ", failed);

        MessageBox.Show(this, message, "Recover lots",
            MessageBoxButtons.OK, failed.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);

        Scan();
    }

    private void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            });
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Open folder", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
