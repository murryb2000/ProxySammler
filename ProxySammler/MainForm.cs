using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using ProxySammler.Core;
using ProxySammler.Persistence;

namespace ProxySammler.App;

public sealed class MainForm : Form
{
    private readonly CheckBox _http = new() { Text = "HTTP", Checked = true, AutoSize = true };
    private readonly CheckBox _socks4 = new() { Text = "SOCKS4", Checked = true, AutoSize = true };
    private readonly CheckBox _socks5 = new() { Text = "SOCKS5", Checked = true, AutoSize = true };
    private readonly CheckBox _showErrors = new() { Text = "Fehler anzeigen", AutoSize = true };
    private readonly CheckBox _turbo = new() { Text = "Turbo-Modus", Checked = true, AutoSize = true };
    private readonly CheckBox _negativeCache = new() { Text = "Tote 6–72 Std. überspringen", Checked = true, AutoSize = true };
    private readonly CheckBox _hourlySearch = new() { Text = "Stündlich automatisch neue Proxys suchen", AutoSize = true };
    private readonly System.Windows.Forms.Timer _hourlyTimer = new() { Interval = 15_000 };
    private DateTimeOffset? _nextHourlySearch;
    private readonly ComboBox _profile = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 190 };
    private readonly NumericUpDown _concurrency = new() { Minimum = 1, Maximum = 100, Value = 50, Width = 62 };
    private readonly NumericUpDown _timeout = new() { Minimum = 2, Maximum = 30, Value = 8, Width = 62 };
    private readonly Button _start = new() { Text = "Sammeln und prüfen", AutoSize = true, Height = 34 };
    private readonly Button _newOnly = new() { Text = "Neue Proxys suchen", AutoSize = true, Height = 34 };
    private readonly Button _retestLists = new() { Text = "Listen erneut prüfen", AutoSize = true, Height = 34 };
    private readonly Button _stop = new() { Text = "Stopp", AutoSize = true, Height = 34, Enabled = false };
    private readonly Button _clipboard = new() { Text = "IP:Port kopieren", AutoSize = true, Enabled = false };
    private readonly Button _sources = new() { Text = "Quellen bearbeiten", AutoSize = true };
    private readonly Button _discover = new() { Text = "Neue Quellen finden", AutoSize = true };
    private readonly Button _folder = new() { Text = "Ergebnisordner", AutoSize = true };
    private readonly System.Windows.Forms.Timer _discoveryTimer = new() { Interval = 15_000 };
    private readonly DiscoveryStore _discoveryStore = new(AppContext.BaseDirectory);
    private readonly SafeSourceDownloader _discoveryDownloader = new();
    private readonly SourceDiscoveryService _discoveryService;
    private ProxyCheckProfile _discoveryProfile = ProxyCheckProfile.SecureGeneric;
    private readonly DiscoverySettings _discoverySettings;
    private readonly List<SourceCandidate> _sourceCandidates;
    private CancellationTokenSource? _discoveryCancellation;
    private SourceDiscoveryForm? _discoveryForm;
    private readonly DataGridView _grid = new();
    private readonly BindingList<RowModel> _rows = new();
    private readonly ProgressBar _progress = new() { Dock = DockStyle.Fill, Style = ProgressBarStyle.Continuous };
    private readonly ToolStripStatusLabel _status = new() { Text = "Bereit" };
    private readonly ToolStripStatusLabel _counts = new() { Spring = true, TextAlign = ContentAlignment.MiddleRight };
    private readonly Label _phase = new() { Text = "Bereit", AutoEllipsis = true, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    private CancellationTokenSource? _cancellation;
    private IReadOnlyList<ProxyTestResult> _allResults = Array.Empty<ProxyTestResult>();

    private static string AppDirectory => AppContext.BaseDirectory;
    private static string SourcesPath => Path.Combine(AppDirectory, "sources.txt");
    private static string ResultsDirectory => Path.Combine(AppDirectory, "Ergebnisse");
    private static string HistoryPath => Path.Combine(AppDirectory, "proxy_history.json");
    private static string WindowSettingsPath => Path.Combine(AppDirectory, "window_settings.json");
    private static string LegacyPoolDirectory => Path.Combine(AppDirectory, "Gefundene_Proxys");
    private ProxyCheckProfile SelectedProfile => _profile.SelectedIndex == 1
        ? ProxyCheckProfile.MacAttackCompatible : ProxyCheckProfile.SecureGeneric;

    public MainForm()
    {
        Text = "ProxySammler 1.8.4";
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = new Size(780, 550);
        Size = new Size(1335, 733);
        Font = new Font("Segoe UI", 9F);
        BackColor = Color.FromArgb(245, 247, 250);

        Controls.Add(BuildLayout());
        ConfigureGrid();
        EnsureSourcesFile();
        _discoverySettings = _discoveryStore.LoadSettings();
        _sourceCandidates = _discoveryStore.LoadCandidates();
        _discoveryService = new SourceDiscoveryService(new GitHubSourceProvider(), _discoveryDownloader,
            (sample, bots, timeout, ct) => new ProxyChecker(profile: _discoveryProfile)
                .CheckAllAsync(sample, bots, timeout, null, null, ct));
        Directory.CreateDirectory(ResultsDirectory);
        var initialStore = ProxyListStore.Load(ResultsDirectory, LegacyPoolDirectory);
        initialStore.Save(ResultsDirectory);

        _profile.Items.AddRange(new object[] { "Standard (HTTPS)", "MacAttack-kompatibel" });
        _profile.SelectedIndex = 0;
        _start.Click += async (_, _) => await StartAsync(false);
        _newOnly.Click += async (_, _) => await StartAsync(true);
        _retestLists.Click += async (_, _) => await RetestListsAsync();
        _stop.Click += (_, _) => _cancellation?.Cancel();
        _clipboard.Click += (_, _) => CopyToClipboard();
        _sources.Click += (_, _) => OpenFile(SourcesPath);
        _discover.Click += (_, _) => OpenDiscovery();
        _folder.Click += (_, _) => OpenFolder(ResultsDirectory);
        _showErrors.CheckedChanged += (_, _) => RenderResults();
        _profile.SelectedIndexChanged += (_, _) => ApplyProfile();
        _hourlySearch.CheckedChanged += (_, _) =>
        {
            _nextHourlySearch = _hourlySearch.Checked ? DateTimeOffset.UtcNow.AddHours(1) : null;
            _hourlyTimer.Enabled = _hourlySearch.Checked;
            if (_hourlySearch.Checked)
                _status.Text = "Automatische Suche aktiviert – erster Lauf in einer Stunde";
            else if (_cancellation is null)
                _status.Text = "Automatische Suche deaktiviert";
        };
        _hourlyTimer.Tick += async (_, _) =>
        {
            if (!_hourlySearch.Checked || _nextHourlySearch is null ||
                DateTimeOffset.UtcNow < _nextHourlySearch || _cancellation is not null ||
                _discoveryCancellation is not null) return;
            // Vor dem await verschieben: Timer-Ticks können keinen zweiten Lauf starten.
            _nextHourlySearch = DateTimeOffset.UtcNow.AddHours(1);
            await StartAsync(true, automatic: true);
        };
        _discoveryTimer.Tick += async (_, _) =>
        {
            if (_discoverySettings.AutomaticSearch && _discoveryCancellation is null &&
                DateTimeOffset.UtcNow >= (_discoverySettings.NextAllowedUtc ?? DateTimeOffset.MinValue) &&
                (_discoverySettings.LastSearchUtc is null ||
                 DateTimeOffset.UtcNow >= _discoverySettings.NextSearchUtc(DateTimeOffset.UtcNow)))
                await DiscoverAsync(true);
        };
        _discoveryTimer.Start();
        Load += (_, _) => RestoreWindowGeometry();
        FormClosing += (_, e) =>
        {
            if (_cancellation is { IsCancellationRequested: false })
            {
                var answer = MessageBox.Show("Die Prüfung läuft noch. Wirklich beenden?", "ProxySammler",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (answer != DialogResult.Yes) e.Cancel = true;
                else _cancellation.Cancel();
            }
            if (!e.Cancel)
            {
                SaveWindowGeometry();
                _hourlyTimer.Stop();
                _discoveryTimer.Stop();
                _discoveryCancellation?.Cancel();
            }
        };
    }

    private void RestoreWindowGeometry()
    {
        WindowGeometry? saved = null;
        try
        {
            if (File.Exists(WindowSettingsPath))
                saved = JsonSerializer.Deserialize<WindowGeometry>(File.ReadAllText(WindowSettingsPath));
        }
        catch (Exception) { /* Damaged or inaccessible settings use the default size. */ }

        var wanted = saved is { Width: >= 780, Height: >= 550 }
            ? new Rectangle(saved.X, saved.Y, saved.Width, saved.Height)
            : new Rectangle(0, 0, 1335, 733);
        var screen = saved is null
            ? Screen.PrimaryScreen
            : Screen.AllScreens.FirstOrDefault(item => item.WorkingArea.IntersectsWith(wanted))
              ?? Screen.PrimaryScreen;
        var area = screen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        var width = Math.Min(wanted.Width, area.Width);
        var height = Math.Min(wanted.Height, area.Height);
        var x = saved is null ? area.Left + (area.Width - width) / 2
            : Math.Clamp(wanted.X, area.Left, area.Right - width);
        var y = saved is null ? area.Top + (area.Height - height) / 2
            : Math.Clamp(wanted.Y, area.Top, area.Bottom - height);
        StartPosition = FormStartPosition.Manual;
        Bounds = new Rectangle(x, y, width, height);
        if (saved?.Maximized == true) WindowState = FormWindowState.Maximized;
    }

    private void SaveWindowGeometry()
    {
        try
        {
            var bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            if (bounds.Width < MinimumSize.Width || bounds.Height < MinimumSize.Height) return;
            var settings = new WindowGeometry
            {
                X = bounds.X, Y = bounds.Y, Width = bounds.Width, Height = bounds.Height,
                Maximized = WindowState == FormWindowState.Maximized
            };
            var temporary = WindowSettingsPath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings));
            File.Move(temporary, WindowSettingsPath, overwrite: true);
        }
        catch (Exception) { /* Settings must never prevent the application from closing. */ }
    }

    private sealed class WindowGeometry
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public bool Maximized { get; set; }
    }

    private Control BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), RowCount = 5, ColumnCount = 1 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 110));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var title = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1 };
        title.Controls.Add(new Label { Text = "ProxySammler", Font = new Font("Segoe UI Semibold", 20F), AutoSize = true, ForeColor = Color.FromArgb(30, 41, 59) });
        title.Controls.Add(new Label
        {
            Text = "Freie Proxys sammeln, prüfen und in drei aktuellen Listen speichern",
            AutoSize = true, ForeColor = Color.FromArgb(71, 85, 105), Margin = new Padding(2, 0, 0, 10)
        });
        root.Controls.Add(title, 0, 0);

        // Fixed rows at the reference width. Whole option groups move to the
        // next line only when the available width becomes too small.
        var toolbar = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            Margin = new Padding(0, 0, 0, 10)
        };
        var optionGroups = new Control[][]
        {
            [_http, _socks4, _socks5],
            [Spacer(8), LabelFor("Prüfprofil:"), _profile],
            [Spacer(8), LabelFor("Prüf-Bots:"), _concurrency],
            [LabelFor("Timeout (s):"), _timeout],
            [_turbo],
            [_negativeCache]
        };
        var actionGroups = new Control[][]
        {
            [_start], [_newOnly], [_retestLists], [_stop],
            [Spacer(8), _showErrors], [_clipboard], [_sources], [_discover], [_folder]
        };
        var automationGroups = new Control[][] { [_hourlySearch] };
        toolbar.Controls.AddRange(optionGroups.Concat(actionGroups).Concat(automationGroups)
            .SelectMany(group => group).ToArray());

        int ArrangeRow(Control[][] groups, int top, int availableWidth)
        {
            var left = 10;
            var right = Math.Max(40, availableWidth - 10);
            var lineHeight = 0;
            foreach (var group in groups)
            {
                var groupWidth = group.Sum(control => control.Width + control.Margin.Horizontal);
                if (left > 10 && left + groupWidth > right)
                {
                    top += lineHeight + 4;
                    left = 10;
                    lineHeight = 0;
                }
                foreach (var control in group)
                {
                    control.Location = new Point(left + control.Margin.Left, top + control.Margin.Top);
                    left += control.Width + control.Margin.Horizontal;
                    lineHeight = Math.Max(lineHeight, control.Height + control.Margin.Vertical);
                }
            }
            return top + lineHeight;
        }

        void ArrangeToolbar()
        {
            if (toolbar.ClientSize.Width <= 0) return;
            var bottom = ArrangeRow(optionGroups, 10, toolbar.ClientSize.Width);
            bottom = ArrangeRow(actionGroups, bottom + 6, toolbar.ClientSize.Width);
            bottom = ArrangeRow(automationGroups, bottom + 6, toolbar.ClientSize.Width);
            var rowHeight = bottom + 10 + toolbar.Margin.Vertical;
            if (Math.Abs(root.RowStyles[1].Height - rowHeight) > 0.5f)
                root.RowStyles[1].Height = rowHeight;
        }
        toolbar.SizeChanged += (_, _) => ArrangeToolbar();
        root.Controls.Add(toolbar, 0, 1);
        root.Controls.Add(_grid, 0, 2);

        var progressPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        progressPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        progressPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        progressPanel.Controls.Add(_progress, 0, 0);
        progressPanel.Controls.Add(_phase, 1, 0);
        root.Controls.Add(progressPanel, 0, 3);

        var strip = new StatusStrip { SizingGrip = false, BackColor = Color.White };
        strip.Items.Add(_status);
        strip.Items.Add(_counts);
        root.Controls.Add(strip, 0, 4);
        return root;
    }

    private void ConfigureGrid()
    {
        _grid.Dock = DockStyle.Fill;
        _grid.BackgroundColor = Color.White;
        _grid.BorderStyle = BorderStyle.FixedSingle;
        _grid.AutoGenerateColumns = false;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = true;
        _grid.DataSource = _rows;
        _grid.Columns.Add(Column(nameof(RowModel.Protocol), "Protokoll", 85));
        _grid.Columns.Add(Column(nameof(RowModel.Proxy), "Proxy", 190));
        _grid.Columns.Add(Column(nameof(RowModel.Status), "Status", 80));
        _grid.Columns.Add(Column(nameof(RowModel.ResponseTime), "Antwortzeit", 100));
        _grid.Columns.Add(Column(nameof(RowModel.ExitIp), "Ausgangs-IP", 150));
        var error = Column(nameof(RowModel.Error), "Fehler", 360);
        error.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        _grid.Columns.Add(error);
        _grid.CellFormatting += (_, e) =>
        {
            if (e.RowIndex < 0) return;
            var row = (RowModel)_grid.Rows[e.RowIndex].DataBoundItem;
            _grid.Rows[e.RowIndex].DefaultCellStyle.ForeColor = row.IsWorking
                ? Color.FromArgb(22, 101, 52) : Color.FromArgb(153, 27, 27);
        };
    }

    private async Task StartAsync(bool onlyNew, bool automatic = false)
    {
        if (_cancellation is not null || _discoveryCancellation is not null) return;
        var protocols = SelectedProtocols(showWarning: !automatic);
        if (automatic && protocols.Count == 0)
            _status.Text = "Automatische Suche: kein Proxytyp ausgewählt";
        if (protocols.Count == 0) return;
        BeginRun(onlyNew ? "Suche nach neuen Proxys" : "Sammeln und prüfen");
        try
        {
            var collector = new ProxyCollector();
            var sources = ProxyCollector.ReadSources(SourcesPath);
            var collectionEta = new RemainingTimeEstimator();
            var sourceProgress = new Progress<(int Done, int Total)>(value =>
            {
                _progress.Maximum = Math.Max(1, value.Total);
                _progress.Value = Math.Min(value.Done, _progress.Maximum);
                _phase.Text = $"Quellen laden: {value.Done:N0} von {value.Total:N0} · " +
                    collectionEta.Update("Quellen laden", value.Done, value.Total);
            });
            var collection = await collector.CollectAsync(sources, protocols,
                new Progress<string>(text => _status.Text = text), _cancellation!.Token, sourceProgress);
            var failedSources = collection.Sources.Count(s => s.Error is not null);
            if (!onlyNew && collection.Proxies.Count == 0)
                throw new InvalidOperationException("Es wurden keine Kandidaten geladen. Die vorhandenen Listen wurden nicht verändert.");
            var history = ProxyHistory.Load(HistoryPath);
            int skipped;
            IReadOnlyList<ProxyEndpoint> candidates = onlyNew
                ? history.SelectNew(collection.Proxies, out skipped, _negativeCache.Checked, TimeSpan.FromHours(6))
                : history.Prepare(collection.Proxies, _negativeCache.Checked, TimeSpan.FromHours(6), out skipped);

            var initialResults = await CheckCandidatesAsync(candidates,
                onlyNew ? "Neue Kandidaten" : "Prüfung");
            history.Update(initialResults);

            var store = ProxyListStore.Load(ResultsDirectory, LegacyPoolDirectory);
            if (onlyNew)
            {
                var added = store.AddWorking(initialResults);
                var fullList = store.Select(protocols);
                _allResults = await CheckCandidatesAsync(fullList, "Gesamtliste erneut prüfen");
                history.Update(_allResults);

                var working = _allResults.Count(r => r.IsWorking);
                if (fullList.Count > 0 && working == 0)
                    throw new InvalidOperationException("Bei der Nachprüfung wurde kein Proxy bestätigt. Die vorhandenen Listen wurden zum Schutz nicht überschrieben. Bitte Internetverbindung und Testziel prüfen.");

                var removed = store.ReplaceSelected(protocols, _allResults);
                store.Save(ResultsDirectory);
                history.Save(HistoryPath);
                CompleteRun($"{working:N0} aktuell funktionierende Proxys", failedSources);
                if (!automatic) MessageBox.Show(
                    $"Suche nach neuen Proxys abgeschlossen.\n\nNeue Treffer angehängt: {added:N0}\n" +
                    $"Gesamtliste geprüft: {fullList.Count:N0}\nTote Proxys entfernt: {removed:N0}\n" +
                    $"Aktuell funktionierend: {working:N0}\nBereits bekannte Kandidaten ausgelassen: {skipped:N0}\n\n" +
                    $"Listen:\n{ResultsDirectory}", "ProxySammler", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                _allResults = initialResults;
                store.ReplaceSelected(protocols, _allResults);
                store.Save(ResultsDirectory);
                history.Save(HistoryPath);
                var working = _allResults.Count(r => r.IsWorking);
                CompleteRun($"{working:N0} funktionierende Proxys", failedSources);
                if (!automatic) MessageBox.Show(
                    $"Prüfung abgeschlossen.\n\nFunktionierend: {working:N0} von {_allResults.Count:N0}\n" +
                    $"Negativcache übersprungen: {skipped:N0}\n\nListen:\n{ResultsDirectory}",
                    "ProxySammler", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            RenderResults();
        }
        catch (OperationCanceledException)
        {
            _status.Text = "Abgebrochen";
            _phase.Text = "Prüfung wurde gestoppt; die dauerhaften Listen wurden nicht verändert.";
        }
        catch (Exception ex)
        {
            _status.Text = "Fehler";
            _phase.Text = ex.Message;
            if (!automatic) MessageBox.Show(ex.Message, "ProxySammler", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { EndRun(); }
    }

    private async Task RetestListsAsync()
    {
        if (_cancellation is not null || _discoveryCancellation is not null) return;
        var protocols = SelectedProtocols();
        if (protocols.Count == 0) return;
        var store = ProxyListStore.Load(ResultsDirectory, LegacyPoolDirectory);
        var candidates = store.Select(protocols);
        if (candidates.Count == 0)
        {
            MessageBox.Show("Für die ausgewählten Proxytypen enthalten die drei Listen noch keine Einträge.",
                "ProxySammler", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (MessageBox.Show($"{candidates.Count:N0} gespeicherte Proxys erneut prüfen?\n\nTote Einträge werden anschließend entfernt.",
                "Listen erneut prüfen", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

        BeginRun("Listen erneut prüfen");
        try
        {
            _allResults = await CheckCandidatesAsync(candidates, "Gespeicherte Listen");
            var working = _allResults.Count(r => r.IsWorking);
            if (candidates.Count > 0 && working == 0)
                throw new InvalidOperationException("Kein Proxy wurde bestätigt. Die vorhandenen Listen wurden zum Schutz nicht überschrieben.");
            var removed = store.ReplaceSelected(protocols, _allResults);
            store.Save(ResultsDirectory);
            var history = ProxyHistory.Load(HistoryPath);
            history.Update(_allResults);
            history.Save(HistoryPath);
            RenderResults();
            CompleteRun($"{working:N0} weiterhin funktionierend – {removed:N0} entfernt", 0);
            MessageBox.Show($"Listenprüfung abgeschlossen.\n\nWeiterhin funktionierend: {working:N0}\n" +
                $"Tote Proxys entfernt: {removed:N0}\n\nListen:\n{ResultsDirectory}",
                "ProxySammler", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (OperationCanceledException)
        {
            _status.Text = "Abgebrochen";
            _phase.Text = "Die Listen wurden nicht verändert.";
        }
        catch (Exception ex)
        {
            _status.Text = "Fehler";
            _phase.Text = ex.Message;
            MessageBox.Show(ex.Message, "ProxySammler", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { EndRun(); }
    }

    private async Task<IReadOnlyList<ProxyTestResult>> CheckCandidatesAsync(IReadOnlyCollection<ProxyEndpoint> candidates, string label)
    {
        if (candidates.Count == 0)
        {
            _phase.Text = $"{label}: keine Kandidaten zu prüfen.";
            return Array.Empty<ProxyTestResult>();
        }
        var eta = new RemainingTimeEstimator();
        _phase.Text = $"{label}: Restzeit wird ermittelt…";
        _rows.Clear();
        _progress.Value = 0;
        var live = new List<ProxyTestResult>();
        var checker = new ProxyChecker(profile: SelectedProfile);
        var resultProgress = new Progress<ProxyTestResult>(result =>
        {
            live.Add(result);
            if (result.IsWorking || _showErrors.Checked) _rows.Add(RowModel.From(result));
            UpdateCounts(live);
        });
        if (_turbo.Checked)
        {
            var phases = new Progress<CheckProgress>(value =>
            {
                _progress.Maximum = Math.Max(1, value.Total);
                _progress.Value = Math.Min(value.Done, _progress.Maximum);
                _phase.Text = $"{label} – {value.Phase}: {value.Done:N0} von {value.Total:N0} · " +
                    eta.Update(value.Phase, value.Done, value.Total);
            });
            return await checker.CheckAllTurboAsync(candidates, (int)_concurrency.Value,
                TimeSpan.FromSeconds((double)_timeout.Value), TimeSpan.FromSeconds(1),
                resultProgress, phases, _cancellation!.Token);
        }

        var counts = new Progress<(int done, int total)>(value =>
        {
            _progress.Maximum = Math.Max(1, value.total);
            _progress.Value = Math.Min(value.done, _progress.Maximum);
            _phase.Text = $"{label}: {value.done:N0} von {value.total:N0} · " +
                eta.Update(label, value.done, value.total);
        });
        return await checker.CheckAllAsync(candidates, (int)_concurrency.Value,
            TimeSpan.FromSeconds((double)_timeout.Value), resultProgress, counts, _cancellation!.Token);
    }

    private HashSet<ProxyProtocol> SelectedProtocols(bool showWarning = true)
    {
        var protocols = new HashSet<ProxyProtocol>();
        if (_http.Checked) protocols.Add(ProxyProtocol.Http);
        if (_socks4.Checked) protocols.Add(ProxyProtocol.Socks4);
        if (_socks5.Checked) protocols.Add(ProxyProtocol.Socks5);
        if (showWarning && protocols.Count == 0)
            MessageBox.Show("Bitte mindestens einen Proxytyp auswählen.", "ProxySammler", MessageBoxButtons.OK, MessageBoxIcon.Information);
        return protocols;
    }

    private void BeginRun(string title)
    {
        _allResults = Array.Empty<ProxyTestResult>();
        _rows.Clear();
        _progress.Value = 0;
        _cancellation = new CancellationTokenSource();
        SetRunning(true);
        _status.Text = title;
        _phase.Text = "Vorbereitung…";
    }

    private void EndRun()
    {
        _cancellation?.Dispose();
        _cancellation = null;
        SetRunning(false);
    }

    private void CompleteRun(string text, int failedSources)
    {
        _status.Text = "Fertig";
        _phase.Text = text;
        _counts.Text = failedSources > 0 ? $"{failedSources} Quelle(n) nicht erreichbar" : _counts.Text;
    }

    private void RenderResults()
    {
        _rows.RaiseListChangedEvents = false;
        _rows.Clear();
        foreach (var result in _allResults.Where(r => r.IsWorking || _showErrors.Checked)) _rows.Add(RowModel.From(result));
        _rows.RaiseListChangedEvents = true;
        _rows.ResetBindings();
        UpdateCounts(_allResults);
    }

    private void UpdateCounts(IEnumerable<ProxyTestResult> results)
    {
        var array = results as ProxyTestResult[] ?? results.ToArray();
        _counts.Text = $"Geprüft: {array.Length:N0}   |   OK: {array.Count(r => r.IsWorking):N0}";
    }

    private void CopyToClipboard()
    {
        var text = ExportService.ToIpPortText(_allResults);
        if (text.Length == 0)
        {
            MessageBox.Show("Es sind keine funktionierenden Proxys zum Kopieren vorhanden.", "ProxySammler", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        try
        {
            Clipboard.SetText(text);
            _status.Text = $"{text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries).Length:N0} Proxys kopiert";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Zwischenablage", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ApplyProfile()
    {
        var macAttack = SelectedProfile == ProxyCheckProfile.MacAttackCompatible;
        if (macAttack)
        {
            _http.Checked = true;
            _socks4.Checked = false;
            _socks5.Checked = false;
            _timeout.Value = 30;
            _turbo.Checked = false;
        }
        else if (_timeout.Value == 30) _timeout.Value = 8;
        _socks4.Enabled = _socks5.Enabled = !macAttack;
        _turbo.Enabled = !macAttack;
    }

    private void OpenDiscovery()
    {
        if (_discoveryForm is { IsDisposed: false }) { _discoveryForm.Activate(); return; }
        _discoveryForm = new SourceDiscoveryForm(this, _sourceCandidates, _discoverySettings);
        _discoveryForm.FormClosed += (_, _) => _discoveryForm = null;
        _discoveryForm.Show(this);
        _discoveryForm.ShowProgress(_discoveryService.IsRunning ? "Quellensuche läuft…" : "Bereit", _discoveryService.IsRunning);
    }

    internal void SetDiscoveryOptions(bool automatic, DiscoveryInterval interval, bool autoAccept)
    {
        _discoverySettings.AutomaticSearch = automatic;
        _discoverySettings.Interval = interval;
        _discoverySettings.AutoAcceptGood = autoAccept;
        _discoveryStore.SaveSettings(_discoverySettings);
    }

    internal void SaveCandidates() => _discoveryStore.SaveCandidates(_sourceCandidates);
    internal void CancelDiscovery() => _discoveryCancellation?.Cancel();
    internal void OpenSources() => OpenFile(SourcesPath);

    internal async Task DiscoverAsync(bool automatic)
    {
        if (_cancellation is not null || _discoveryCancellation is not null || _discoveryService.IsRunning) return;
        if (_discoverySettings.NextAllowedUtc is { } retry && DateTimeOffset.UtcNow < retry)
        {
            _discoveryForm?.ShowProgress($"GitHub begrenzt die Suche. Erneut möglich ab {retry.ToLocalTime():g}.", false);
            return;
        }
        _discoveryCancellation = new CancellationTokenSource();
        SetRunning(true);
        _discoveryProfile = SelectedProfile;
        _discoverySettings.LastSearchUtc = DateTimeOffset.UtcNow;
        _discoveryStore.SaveSettings(_discoverySettings);
        _discoveryForm?.ShowProgress("Quellensuche startet… Restzeit wird ermittelt…", true);
        try
        {
            var discoveryEta = new RemainingTimeEstimator();
            var discoveryMessage = "GitHub-Repositories suchen";
            var discoveryRemaining = "Restzeit wird ermittelt…";
            var progress = new Progress<string>(message =>
            {
                discoveryMessage = message;
                _discoveryForm?.ShowProgress($"{message} · {discoveryRemaining}", true);
                _status.Text = "Quellensuche läuft";
            });
            var candidateProgress = new Progress<(int Done, int Total)>(value =>
            {
                discoveryRemaining = discoveryEta.Update("Quellen prüfen", value.Done, value.Total);
                _discoveryForm?.ShowProgress(
                    $"{discoveryMessage} · {value.Done:N0} von {value.Total:N0} · {discoveryRemaining}", true);
            });
            var knownSources = ProxyCollector.ReadSources(SourcesPath);
            var bots = (int)_concurrency.Value;
            var timeout = TimeSpan.FromSeconds((double)_timeout.Value);
            var autoAccept = _discoverySettings.AutoAcceptGood;
            var summary = await Task.Run(() => _discoveryService.SearchAsync(_sourceCandidates,
                knownSources, autoAccept, bots, timeout, c =>
                {
                    var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    BeginInvoke((Action)(() =>
                    {
                        try { completion.SetResult(AcceptSourceCore(c)); }
                        catch (Exception error) { completion.SetException(error); }
                    }));
                    return completion.Task;
                }, progress, _discoveryCancellation.Token, candidateProgress));
            SaveCandidates();
            _discoveryForm?.RefreshRows(_sourceCandidates);
            if (summary is not null)
            {
                var text = $"Repositories untersucht: {summary.Repositories}\n" +
                    $"Mögliche Quellen: {summary.Found}\nNeue Quellen: {summary.New}\n" +
                    $"Bereits bekannte Quellen: {summary.Known}\nErfolgreich geprüft: {summary.Checked}\n" +
                    $"Übernommen: {summary.Accepted}\nFehlerhaft: {summary.Failed}";
                _discoveryForm?.ShowProgress(text.Replace('\n', ' '), false);
                _status.Text = "Quellensuche beendet";
                if (!automatic) MessageBox.Show(text, "Quellensuche abgeschlossen", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        catch (RateLimitException ex)
        {
            _discoverySettings.NextAllowedUtc = ex.RetryAt ?? DateTimeOffset.UtcNow.AddMinutes(10);
            _discoveryStore.SaveSettings(_discoverySettings);
            _discoveryForm?.ShowProgress(ex.Message, false);
            _status.Text = ex.Message;
            if (!automatic) MessageBox.Show(ex.Message, "GitHub-Ratenbegrenzung", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (OperationCanceledException)
        {
            _discoveryForm?.ShowProgress("Quellensuche abgebrochen.", false);
            _status.Text = "Quellensuche abgebrochen";
        }
        catch (Exception ex)
        {
            _discoveryForm?.ShowProgress("Fehler: " + ex.Message, false);
            _status.Text = "Quellensuche fehlgeschlagen";
            if (!automatic) MessageBox.Show(ex.Message, "Quellensuche", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SaveCandidates();
            _discoveryCancellation.Dispose();
            _discoveryCancellation = null;
            SetRunning(_cancellation is not null);
            _discoveryForm?.ShowProgress(_status.Text ?? "Bereit", false);
        }
    }

    internal async Task CheckSourceAsync(SourceCandidate candidate)
    {
        if (_cancellation is not null || _discoveryCancellation is not null) return;
        _discoveryCancellation = new CancellationTokenSource();
        SetRunning(true);
        _discoveryProfile = SelectedProfile;
        _discoveryForm?.ShowProgress("Ausgewählte Quelle prüfen…", true);
        try
        {
            var hashes = _sourceCandidates.Where(c => !ReferenceEquals(c, candidate) && c.ContentHash.Length > 0)
                .Select(c => c.ContentHash).ToHashSet(StringComparer.Ordinal);
            var bots = (int)_concurrency.Value;
            var timeout = TimeSpan.FromSeconds((double)_timeout.Value);
            await Task.Run(() => _discoveryService.CheckAsync(candidate, hashes, bots,
                timeout, _discoveryCancellation.Token));
            SaveCandidates();
            _discoveryForm?.RefreshRows();
            _discoveryForm?.ShowProgress($"Prüfung beendet: {candidate.WorkingCount} von {candidate.SampleCount} Proxys funktionieren.", false);
        }
        catch (OperationCanceledException) { _discoveryForm?.ShowProgress("Prüfung abgebrochen.", false); }
        finally
        {
            _discoveryCancellation.Dispose();
            _discoveryCancellation = null;
            SetRunning(false);
            _discoveryForm?.ShowProgress(_status.Text ?? "Bereit", false);
        }
    }

    private bool AcceptSourceCore(SourceCandidate candidate)
    {
        if (candidate.Status != CandidateStatus.Geprueft || candidate.ProxyCount == 0) return false;
        if (!SourceRegistry.Add(SourcesPath, candidate)) return false;
        candidate.Status = CandidateStatus.Uebernommen;
        SaveCandidates();
        _discoveryForm?.RefreshRows();
        return true;
    }

    internal void AcceptSource(SourceCandidate candidate)
    {
        if (candidate.Status != CandidateStatus.Geprueft)
        {
            MessageBox.Show("Bitte die Quelle zuerst erfolgreich prüfen.", "Quellensuche", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (!DiscoveryPolicy.Suitable(candidate) && MessageBox.Show(
            "Diese Quelle erfüllt die Grenzwerte für eine automatische Übernahme nicht. Trotzdem übernehmen?",
            "Quellensuche", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        try
        {
            if (AcceptSourceCore(candidate)) OfferProxySearch();
            else MessageBox.Show("Die Quelle ist bereits in sources.txt enthalten.", "Quellensuche", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Quellensuche", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    internal void AcceptAllSources()
    {
        var count = 0;
        try
        {
            foreach (var candidate in _sourceCandidates.Where(DiscoveryPolicy.Suitable).ToArray())
                if (AcceptSourceCore(candidate)) count++;
            MessageBox.Show($"{count} geeignete Quelle(n) übernommen.", "Quellensuche", MessageBoxButtons.OK, MessageBoxIcon.Information);
            if (count > 0) OfferProxySearch();
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Quellensuche", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void OfferProxySearch()
    {
        if (MessageBox.Show("Jetzt mit den aktuellen Programmeinstellungen nach neuen Proxys suchen?",
            "Quellensuche", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            _ = StartAsync(true);
    }

    private void SetRunning(bool running)
    {
        _start.Enabled = _newOnly.Enabled = _retestLists.Enabled = !running;
        _stop.Enabled = _cancellation is not null;
        _discover.Enabled = _cancellation is null;
        _clipboard.Enabled = !running && _allResults.Any(r => r.IsWorking);
        _http.Enabled = !running;
        _socks4.Enabled = _socks5.Enabled = !running && SelectedProfile == ProxyCheckProfile.SecureGeneric;
        _profile.Enabled = !running;
        _concurrency.Enabled = _timeout.Enabled = _negativeCache.Enabled = !running;
        _turbo.Enabled = !running && SelectedProfile == ProxyCheckProfile.SecureGeneric;
        _sources.Enabled = _folder.Enabled = !running;
    }

    private static void EnsureSourcesFile()
    {
        if (!File.Exists(SourcesPath)) File.WriteAllText(SourcesPath, DefaultSources.Text, new System.Text.UTF8Encoding(false));
    }

    private static void OpenFile(string path)
    {
        try { Process.Start(new ProcessStartInfo("notepad.exe", $"\"{path}\"") { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "ProxySammler", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private static void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "ProxySammler", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private static DataGridViewTextBoxColumn Column(string property, string title, int width) => new()
    {
        DataPropertyName = property, HeaderText = title, Width = width, SortMode = DataGridViewColumnSortMode.Automatic
    };

    private static Label LabelFor(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(8, 7, 2, 0) };
    private static Control Spacer(int width) => new Panel { Width = width, Height = 1 };
    private static FlowLayoutPanel FixedToolbarRow() => new()
    {
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        FlowDirection = FlowDirection.LeftToRight,
        WrapContents = false,
        Margin = Padding.Empty,
        Padding = Padding.Empty
    };

    private sealed class RowModel
    {
        public required string Protocol { get; init; }
        public required string Proxy { get; init; }
        public required string Status { get; init; }
        public required string ResponseTime { get; init; }
        public required string ExitIp { get; init; }
        public required string Error { get; init; }
        public bool IsWorking { get; init; }

        public static RowModel From(ProxyTestResult result) => new()
        {
            Protocol = result.Proxy.ProtocolName,
            Proxy = result.Proxy.Address,
            Status = result.IsWorking ? "OK" : "Fehler",
            ResponseTime = result.IsWorking ? $"{result.ResponseTimeMs} ms" : "–",
            ExitIp = result.ExitIp,
            Error = result.Error,
            IsWorking = result.IsWorking
        };
    }
}
