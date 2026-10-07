using System.ComponentModel;
using System.Diagnostics;
using ProxySammler.Core;

namespace ProxySammler.App;

public sealed class SourceDiscoveryForm : Form
{
    private readonly MainForm _main;
    private readonly BindingList<SourceCandidate> _rows = new();
    private readonly DataGridView _grid = new();
    private readonly Button _search = new() { Text = "Jetzt nach Quellen suchen", AutoSize = true };
    private readonly Button _cancel = new() { Text = "Abbrechen", AutoSize = true };
    private readonly CheckBox _automatic = new() { Text = "Automatisch nach neuen Quellen suchen", AutoSize = true };
    private readonly CheckBox _acceptAutomatically = new() { Text = "Gute Quellen automatisch übernehmen", AutoSize = true };
    private readonly ComboBox _interval = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
    private readonly Label _status = new() { Text = "Bereit", AutoEllipsis = true, Dock = DockStyle.Fill };

    public SourceDiscoveryForm(MainForm main, IReadOnlyCollection<SourceCandidate> candidates, DiscoverySettings settings)
    {
        _main = main;
        Text = "Neue Quellen finden – ProxySammler 1.8.0";
        Size = new Size(1300, 650);
        MinimumSize = new Size(950, 480);
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 9F);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), RowCount = 4, ColumnCount = 1 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var options = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
        _interval.Items.AddRange(["Täglich", "Wöchentlich"]);
        _interval.SelectedIndex = settings.Interval == DiscoveryInterval.Woechentlich ? 1 : 0;
        _automatic.Checked = settings.AutomaticSearch;
        _acceptAutomatically.Checked = settings.AutoAcceptGood;
        options.Controls.AddRange([_search, _cancel, _automatic, new Label { Text = "Intervall:", AutoSize = true, Padding = new Padding(8, 5, 0, 0) }, _interval, _acceptAutomatically]);
        layout.Controls.Add(options, 0, 0);

        _grid.Dock = DockStyle.Fill;
        _grid.AutoGenerateColumns = false;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.DataSource = _rows;
        AddColumn(nameof(SourceCandidate.Url), "URL", 360);
        AddColumn(nameof(SourceCandidate.Provider), "Herkunft", 180);
        AddColumn(nameof(SourceCandidate.Protocol), "Proxytyp", 85);
        AddColumn(nameof(SourceCandidate.DiscoveredUtc), "Entdeckt", 150);
        AddColumn(nameof(SourceCandidate.UpdatedUtc), "Aktualisiert", 150);
        AddColumn(nameof(SourceCandidate.ProxyCount), "Proxys", 70);
        AddColumn(nameof(SourceCandidate.SampleCount), "Geprüft", 70);
        AddColumn(nameof(SourceCandidate.WorkingCount), "OK", 55);
        AddColumn(nameof(SourceCandidate.SuccessRate), "Quote", 70, "P0");
        AddColumn(nameof(SourceCandidate.Quality), "Qualität", 70);
        AddColumn(nameof(SourceCandidate.Status), "Status", 100);
        AddColumn(nameof(SourceCandidate.LastError), "Letzter Fehler", 260);
        layout.Controls.Add(_grid, 0, 1);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
        Button Button(string text, Action action)
        {
            var button = new Button { Text = text, AutoSize = true };
            button.Click += (_, _) => action();
            actions.Controls.Add(button);
            return button;
        }
        Button("Ausgewählte Quelle prüfen", async () => { if (Selected is { } c) await _main.CheckSourceAsync(c); });
        Button("Ausgewählte Quelle übernehmen", () => { if (Selected is { } c) _main.AcceptSource(c); });
        Button("Alle geeigneten Quellen übernehmen", () => _main.AcceptAllSources());
        Button("Quelle ignorieren", () => { if (Selected is { } c) { c.Status = CandidateStatus.Ignoriert; _main.SaveCandidates(); RefreshRows(); } });
        Button("Übernommene Quellen öffnen", () => _main.OpenSources());
        layout.Controls.Add(actions, 0, 2);
        layout.Controls.Add(_status, 0, 3);
        Controls.Add(layout);

        _search.Click += async (_, _) => await _main.DiscoverAsync(false);
        _cancel.Click += (_, _) => _main.CancelDiscovery();
        _automatic.CheckedChanged += (_, _) => SaveOptions();
        _acceptAutomatically.CheckedChanged += (_, _) => SaveOptions();
        _interval.SelectedIndexChanged += (_, _) => SaveOptions();
        RefreshRows(candidates);
    }

    private SourceCandidate? Selected => _grid.CurrentRow?.DataBoundItem as SourceCandidate;

    private void AddColumn(string property, string header, int width, string? format = null)
    {
        var column = new DataGridViewTextBoxColumn { DataPropertyName = property, HeaderText = header, Width = width };
        if (format is not null) column.DefaultCellStyle.Format = format;
        _grid.Columns.Add(column);
    }

    private void SaveOptions() => _main.SetDiscoveryOptions(_automatic.Checked,
        _interval.SelectedIndex == 1 ? DiscoveryInterval.Woechentlich : DiscoveryInterval.Taeglich,
        _acceptAutomatically.Checked);

    public void RefreshRows(IReadOnlyCollection<SourceCandidate>? candidates = null)
    {
        if (candidates is not null)
        {
            _rows.Clear();
            foreach (var candidate in candidates) _rows.Add(candidate);
        }
        else _rows.ResetBindings();
    }

    public void ShowProgress(string text, bool running)
    {
        if (IsDisposed) return;
        _status.Text = text;
        _search.Enabled = !running;
        _cancel.Enabled = running;
        RefreshRows();
    }
}
