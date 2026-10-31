using System.Diagnostics;
using System.Text;
using LibVLCSharp.Shared;

namespace RadioGolha;

/// <summary>
/// Main window - C# port of radio_golha.py (Tkinter + python-vlc).
/// Uses only stock Windows controls (labels, text box, track bar, buttons,
/// progress bar, status strip) in a compact, resizable-free layout.
/// </summary>
internal sealed class RadioApp : Form
{
    // ---------------- constants ----------------
    private const string AppTitle = "Radio Golha";
    private const string DefaultUrl = "mms://www.radiogolha.net/RadioGolha";

    private static readonly string UrlFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".radio_golha_url");

    private static readonly string SettingsFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".radio_golha_settings.json");

    private const double BufferFreshSeconds = 3.0;   // max age of a buffering event to be trusted
    private const double SlowConnectSeconds = 30.0;  // after this, hint that the connection is slow

    private static readonly Color StatusPlaying = Color.FromArgb(0x1B, 0x7F, 0x3B);
    private static readonly Color StatusError = Color.FromArgb(0xC0, 0x39, 0x2B);

    // VLC player states -> (text, color)
    private static readonly (string Text, Color Color)[] StateText =
    {
        ("Ready", SystemColors.ControlText),              // 0 NothingSpecial
        ("Connecting...", SystemColors.ControlText),      // 1 Opening
        ("Connecting...", SystemColors.ControlText),      // 2 Buffering
        ("Playing", StatusPlaying),                       // 3 Playing
        ("Paused", SystemColors.ControlText),             // 4 Paused
        ("Stopped", SystemColors.ControlText),            // 5 Stopped
        ("Ended", SystemColors.ControlText),              // 6 Ended
        ("Connection error!", StatusError),               // 7 Error
    };

    // ---------------- VLC state ----------------
    private LibVLC? _libVlc;
    private MediaPlayer? _player;
    private bool _vlcOk;
    private string _vlcError = string.Empty;

    // playback bookkeeping
    private readonly object _bufferLock = new();
    private float? _bufferPct;                          // latest buffering percent reported by VLC
    private DateTime _lastBufferAt = DateTime.MinValue; // when the last buffering event arrived
    private long _lastTime;                             // last reported playback time (ms)
    private DateTime? _connectSince;                    // when the current connecting phase began
    private DateTime? _lastFlowAt;                      // when data flow was last confirmed
    private DateTime? _playStartedAt;                   // wall-clock time playback was detected
    private long _lastDemuxBytes;                       // last demuxed-bytes counter (media stats)
    private long _lastDecodedAudio;                     // last decoded-audio counter (media stats)

    // ---------------- UI controls ----------------
    private readonly System.Windows.Forms.Timer _pollTimer = new() { Interval = 500 };
    private readonly ToolTip _toolTip = new();
    private TextBox _urlBox = null!;
    private TrackBar _volume = null!;
    private Label _volumeValueLabel = null!;
    private ComboBox _stationCombo = null!;
    private Button _favoriteButton = null!;
    private ToolStripMenuItem _favoritesOnlyItem = null!;
    private ToolStripMenuItem _autoPlaySelectItem = null!;
    private ToolStripMenuItem _autoPlayStartupItem = null!;
    private List<RadioStation> _stations = new();
    private bool _updatingStationUi;

    private Button _playButton = null!;
    private Button _stopButton = null!;
    private TextProgressBar _progress = null!;
    private ToolStripStatusLabel _statusItem = null!;

    public RadioApp()
    {
        InitializeVlc();
        BuildUi();
        _pollTimer.Tick += PollStatus;
        _pollTimer.Start();
    }

    // ---------------- VLC initialisation ----------------
    private void InitializeVlc()
    {
        try
        {
            var vlcDirectory = VlcLocator.FindInstallDirectory();
            if (vlcDirectory is null)
            {
                throw new InvalidOperationException(
                    "libvlc.dll was not found. Is VLC Media Player installed?");
            }

            // Load the native libvlc that ships with the installed VLC player.
            Core.Initialize(vlcDirectory);

            // Audio player: never open a video output window.
            _libVlc = new LibVLC("--no-video");
            _vlcOk = true;
        }
        catch (Exception err)
        {
            _vlcError =
                "VLC Media Player was not found.\n\n" +
                "Please install VLC from https://www.videolan.org and reopen this app.\n" +
                "The app and VLC must be the same bitness (64-bit app -> 64-bit VLC).\n\n" +
                $"Technical details: {err.Message}";
        }
    }

    // ---------------- build the UI ----------------
    private void BuildUi()
    {
        Text = AppTitle;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Font;
        AutoScaleDimensions = new SizeF(7F, 15F);
        Font = new Font("Segoe UI", 9F);
        Icon = SystemIcons.Application;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            Padding = new Padding(16),
            BackColor = SystemColors.Control,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        // --- station picker row ---
        var stationRow = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 3,
            Margin = new Padding(0, 0, 0, 12),
            Padding = new Padding(0),
        };
        stationRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        stationRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        stationRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40F));
        var stationLabel = new Label
        {
            Text = "Station:",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 6, 8, 0),
        };
        stationRow.Controls.Add(stationLabel, 0, 0);
        _stationCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Dock = DockStyle.Fill,
            MaxDropDownItems = 14,
            TabIndex = 0,
            Margin = new Padding(0, 2, 8, 2),
            Font = new Font("Segoe UI", 9F),
        };
        _stationCombo.SelectedIndexChanged += (_, _) => OnStationSelected();
        stationRow.Controls.Add(_stationCombo, 1, 0);
        _favoriteButton = new Button
        {
            Text = "☆",
            Dock = DockStyle.Fill,
            TabIndex = 1,
            Margin = new Padding(0, 2, 0, 2),
            Font = new Font("Segoe UI", 12F),
            FlatStyle = FlatStyle.Standard,
        };
        _favoriteButton.Click += (_, _) => ToggleFavorite();
        stationRow.Controls.Add(_favoriteButton, 2, 0);
        root.Controls.Add(stationRow, 0, 0);

        // --- stream URL row ---
        var urlRow = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            Margin = new Padding(0, 0, 0, 12),
            Padding = new Padding(0),
        };
        urlRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        urlRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        var urlLabel = new Label
        {
            Text = "Stream URL:",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 6, 8, 0),
        };
        urlRow.Controls.Add(urlLabel, 0, 0);
        _urlBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Text = LoadUrl() ?? DefaultUrl,
            TabIndex = 2,
            Margin = new Padding(0, 2, 0, 2),
            Font = new Font("Segoe UI", 9F),
        };
        _urlBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            Play();
        };
        urlRow.Controls.Add(_urlBox, 1, 0);
        root.Controls.Add(urlRow, 0, 1);

        // --- volume row ---
        var volumeRow = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 3,
            Margin = new Padding(0, 0, 0, 12),
            Padding = new Padding(0),
        };
        volumeRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        volumeRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        volumeRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 45F));
        var volumeLabel = new Label
        {
            Text = "Volume:",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 6, 8, 0),
        };
        volumeRow.Controls.Add(volumeLabel, 0, 0);
        _volume = new TrackBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = 80,
            TickStyle = TickStyle.None,
            SmallChange = 1,
            LargeChange = 10,
            AutoSize = false,
            Height = 30,
            Dock = DockStyle.Fill,
            TabIndex = 3,
            Margin = new Padding(0),
        };
        _volume.ValueChanged += OnVolumeChanged;
        volumeRow.Controls.Add(_volume, 1, 0);
        _volumeValueLabel = new Label
        {
            Text = "80%",
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight,
            Margin = new Padding(4, 6, 0, 0),
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
        };
        volumeRow.Controls.Add(_volumeValueLabel, 2, 0);
        root.Controls.Add(volumeRow, 0, 2);

        // --- buttons row ---
        var buttonRow = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 3,
            Margin = new Padding(0, 0, 0, 12),
            Padding = new Padding(0),
        };
        buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34F));
        buttonRow.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
        _playButton = new Button
        {
            Text = "Play",
            Dock = DockStyle.Fill,
            TabIndex = 4,
            Margin = new Padding(0, 0, 4, 0),
            Font = new Font("Segoe UI", 9F),
        };
        _stopButton = new Button
        {
            Text = "Stop",
            Dock = DockStyle.Fill,
            TabIndex = 5,
            Margin = new Padding(4, 0, 4, 0),
            Font = new Font("Segoe UI", 9F),
        };
        var vlcButton = new Button
        {
            Text = "Open in VLC",
            Dock = DockStyle.Fill,
            TabIndex = 6,
            Margin = new Padding(4, 0, 0, 0),
            Font = new Font("Segoe UI", 9F),
        };
        buttonRow.Controls.Add(_playButton, 0, 0);
        buttonRow.Controls.Add(_stopButton, 1, 0);
        buttonRow.Controls.Add(vlcButton, 2, 0);
        _playButton.Click += (_, _) => Play();
        _stopButton.Click += (_, _) => Stop_();
        vlcButton.Click += (_, _) => OpenInVlc();
        root.Controls.Add(buttonRow, 0, 3);

        // --- progress bar ---
        _progress = new TextProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            Style = ProgressBarStyle.Continuous,
            Dock = DockStyle.Top,
            Height = 22,
            Margin = new Padding(0, 0, 0, 8),
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
        };
        root.Controls.Add(_progress, 0, 4);

        // --- status bar ---
        _statusItem = new ToolStripStatusLabel("Ready")
        {
            Spring = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 9F),
        };
        var statusStrip = new StatusStrip
        {
            SizingGrip = false,
            BackColor = SystemColors.Control,
        };
        statusStrip.Items.Add(_statusItem);

        var menu = BuildMenu();

        Controls.Add(root);
        Controls.Add(menu);
        Controls.Add(statusStrip);

        _toolTip.SetToolTip(vlcButton, "Play the stream in the installed VLC Media Player.");

        LoadSettings();
        LoadStations();

        // size the window to fit content
        var wanted = root.GetPreferredSize(new Size(448, 0));
        ClientSize = new Size(448, wanted.Height + menu.Height + statusStrip.Height);

        // disable built-in playback if VLC/library is unavailable
        if (!_vlcOk)
        {
            _playButton.Enabled = false;
            _stopButton.Enabled = false;
            _volume.Enabled = false;
        }
    }

    /// <summary>
    /// Build a Media from the URL the same way python-vlc's media_new() does:
    /// strings with a scheme ("mms://", "http://", ...) are treated as MRLs,
    /// everything else (local files) as a plain path.
    /// </summary>
    private static Media CreateMedia(LibVLC libVlc, string url) =>
        HasStreamScheme(url)
            ? new Media(libVlc, url, FromType.FromLocation)
            : new Media(libVlc, url, FromType.FromPath);

    private static bool HasStreamScheme(string url)
    {
        var colon = url.IndexOf(':');
        if (colon <= 0)
            return false;

        // A Windows drive letter ("C:\music\file.mp3") is a path, not a scheme.
        if (colon == 1 && char.IsLetter(url[0]))
            return false;

        return true;
    }

    // ---------------- save/load last URL ----------------
    private static string? LoadUrl()
    {
        try
        {
            var value = File.ReadAllText(UrlFile, Encoding.UTF8).Trim();
            return value.Length == 0 ? null : value;
        }
        catch
        {
            return null;
        }
    }

    private void SaveUrl()
    {
        try
        {
            File.WriteAllText(UrlFile, _urlBox.Text.Trim(), new UTF8Encoding(false));
        }
        catch
        {
            // best effort - ignoring is fine, exactly like the Python version
        }
    }

    // ---------------- settings (auto-play, ...) ----------------
    private void LoadSettings()
    {
        try
        {
            if (!File.Exists(SettingsFile))
                return;

            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(SettingsFile, Encoding.UTF8));
            var root = document.RootElement;
            if (root.TryGetProperty("AutoPlayOnSelect", out var select) && select.ValueKind == System.Text.Json.JsonValueKind.True)
                _autoPlaySelectItem.Checked = true;
            if (root.TryGetProperty("AutoPlayOnStartup", out var startup) && startup.ValueKind == System.Text.Json.JsonValueKind.True)
                _autoPlayStartupItem.Checked = true;
        }
        catch
        {
            // ignore malformed settings
        }
    }

    private void SaveSettings()
    {
        try
        {
            var json = $"{{ \"AutoPlayOnSelect\": {_autoPlaySelectItem.Checked.ToString().ToLowerInvariant()}, \"AutoPlayOnStartup\": {_autoPlayStartupItem.Checked.ToString().ToLowerInvariant()} }}";
            File.WriteAllText(SettingsFile, json, new UTF8Encoding(false));
        }
        catch
        {
            // best effort
        }
    }

    // ---------------- station list ----------------
    /// <summary>Menu with the station-list commands (add, import, remove, favorites).</summary>
    private MenuStrip BuildMenu()
    {
        var addItem = new ToolStripMenuItem("&Add station...", null, (_, _) => AddStation())
        {
            ShortcutKeys = Keys.Control | Keys.N,
        };
        var importItem = new ToolStripMenuItem("&Import stations...", null, (_, _) => ImportStations())
        {
            ShortcutKeys = Keys.Control | Keys.I,
        };
        var removeItem = new ToolStripMenuItem("&Remove station", null, (_, _) => RemoveStation());

        _favoritesOnlyItem = new ToolStripMenuItem("Show &favorites only", null, (_, _) => RefreshStationList())
        {
            CheckOnClick = true,
        };

        _autoPlaySelectItem = new ToolStripMenuItem("Auto-play on &select", null, (_, _) => SaveSettings())
        {
            CheckOnClick = true,
        };
        _autoPlayStartupItem = new ToolStripMenuItem("Auto-play on &startup", null, (_, _) => SaveSettings())
        {
            CheckOnClick = true,
        };

        var stationsMenu = new ToolStripMenuItem("&Stations");
        stationsMenu.DropDownItems.AddRange(new ToolStripItem[]
        {
            addItem,
            importItem,
            removeItem,
            new ToolStripSeparator(),
            _favoritesOnlyItem,
            new ToolStripSeparator(),
            _autoPlaySelectItem,
            _autoPlayStartupItem,
        });

        var menu = new MenuStrip();
        menu.Items.Add(stationsMenu);
        return menu;
    }

    /// <summary>Fill the station combo from the saved list (seeded from radios.json).</summary>
    private void LoadStations()
    {
        _stations = StationStore.Load();
        RefreshStationList();
        SelectStationForUrl(_urlBox.Text.Trim());
    }

    private RadioStation? SelectedStation => _stationCombo.SelectedItem as RadioStation;

    /// <summary>
    /// Rebuild the combo: favorites first (with a star), or only favorites when
    /// the "Show favorites only" menu item is checked.
    /// </summary>
    private void RefreshStationList()
    {
        var wanted = SelectedStation?.Url ?? _urlBox.Text.Trim();

        _updatingStationUi = true;
        try
        {
            _stationCombo.BeginUpdate();
            _stationCombo.Items.Clear();

            var visible = _favoritesOnlyItem.Checked
                ? _stations.Where(station => station.Favorite)
                : _stations;

            foreach (var station in visible.OrderByDescending(station => station.Favorite))
                _stationCombo.Items.Add(station);

            _stationCombo.EndUpdate();
        }
        finally
        {
            _updatingStationUi = false;
        }

        SelectStationForUrl(wanted);
    }

    private void SelectStationForUrl(string url)
    {
        _updatingStationUi = true;
        try
        {
            var index = -1;
            for (var i = 0; i < _stationCombo.Items.Count; i++)
            {
                if (_stationCombo.Items[i] is RadioStation station
                    && string.Equals(station.Url, url, StringComparison.OrdinalIgnoreCase))
                {
                    index = i;
                    break;
                }
            }

            _stationCombo.SelectedIndex = index;
        }
        finally
        {
            _updatingStationUi = false;
        }

        UpdateFavoriteButton();
    }

    private void OnStationSelected()
    {
        if (_updatingStationUi)
            return;

        if (SelectedStation is { } station)
            _urlBox.Text = station.Url;

        UpdateFavoriteButton();

        if (!_updatingStationUi && _autoPlaySelectItem.Checked && SelectedStation is not null)
            Play();
    }

    private void UpdateFavoriteButton()
    {
        var station = SelectedStation;
        _favoriteButton.Enabled = station is not null;
        _favoriteButton.Text = station?.Favorite == true ? "★" : "☆";
        _toolTip.SetToolTip(_favoriteButton,
            station?.Favorite == true ? "Remove from favorites" : "Add to favorites");
    }

    private void ToggleFavorite()
    {
        if (SelectedStation is not { } station)
            return;

        station.Favorite = !station.Favorite;
        StationStore.Save(_stations);
        RefreshStationList();
    }

    /// <summary>Ask for a name + URL and append the station to the list.</summary>
    private void AddStation()
    {
        using var dialog = new StationDialog(string.Empty, _urlBox.Text.Trim());
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        var url = dialog.StationUrl;
        var existing = _stations.FirstOrDefault(
            station => string.Equals(station.Url, url, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            SelectStationForUrl(existing.Url);
            MessageBox.Show(this, "That stream is already in the station list.", AppTitle,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var added = new RadioStation { Name = dialog.StationName, Url = url };
        _stations.Add(added);
        StationStore.Save(_stations);
        RefreshStationList();
        SelectStationForUrl(added.Url);
        _urlBox.Text = added.Url;
    }

    /// <summary>Merge a station list from a JSON file into the current list.</summary>
    private void ImportStations()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Import stations",
            Filter = "Station lists (*.json)|*.json|All files (*.*)|*.*",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        List<RadioStation> imported;
        try
        {
            imported = StationStore.Parse(File.ReadAllText(dialog.FileName, Encoding.UTF8));
        }
        catch (Exception err)
        {
            MessageBox.Show(this, $"Could not read that file:\n{err.Message}", AppTitle,
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        if (imported.Count == 0)
        {
            MessageBox.Show(this, "No stations were found in that file.", AppTitle,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var count = StationStore.Merge(_stations, imported);
        if (count > 0)
            StationStore.Save(_stations);

        RefreshStationList();
        MessageBox.Show(
            this,
            count == 0
                ? "All stations in that file are already in the list."
                : $"Imported {count} station(s).",
            AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void RemoveStation()
    {
        if (SelectedStation is not { } station)
            return;

        var answer = MessageBox.Show(this, $"Remove \"{station.Name}\" from the list?", AppTitle,
            MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (answer != DialogResult.Yes)
            return;

        _stations.Remove(station);
        StationStore.Save(_stations);
        RefreshStationList();
    }

    // ---------------- actions ----------------
    private void SetStatus(string text, Color color)
    {
        _statusItem.Text = text;
        _statusItem.ForeColor = color;
    }

    /// <summary>Apply volume immediately while the slider moves.</summary>
    private void OnVolumeChanged(object? sender, EventArgs e)
    {
        var level = _volume.Value;
        _volumeValueLabel.Text = $"{level}%";
        if (_player is null) return;
        try
        {
            _player.Volume = level;
        }
        catch
        {
            // player not ready yet
        }
    }

    private void Play()
    {
        if (!_vlcOk || _libVlc is null) return;

        var url = _urlBox.Text.Trim();
        if (url.Length == 0)
        {
            MessageBox.Show(this, "Please enter the stream URL.", AppTitle,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            if (_player is null)
            {
                _player = new MediaPlayer(_libVlc);
                // receive the buffering percent from a VLC event (VLC thread)
                _player.Buffering += OnVlcBuffering;
            }

            _player.Stop();

            using var media = CreateMedia(_libVlc, url);
            _player.Media = media;
            _player.Volume = _volume.Value;

            ResetPlaybackState();
            _progress.Style = ProgressBarStyle.Continuous;
            _progress.Value = 0;
            _progress.Text = string.Empty;

            _player.Play();
            SaveUrl();
            SetStatus("Connecting...", SystemColors.ControlText);
        }
        catch (Exception err)
        {
            MessageBox.Show(this, $"Playback error:\n{err.Message}", AppTitle,
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void Stop_()
    {
        if (_player is null) return;
        try
        {
            _player.Stop();
        }
        catch
        {
            // already stopped
        }
    }

    /// <summary>Open the stream in the external VLC application.</summary>
    private void OpenInVlc()
    {
        var url = _urlBox.Text.Trim();
        if (url.Length == 0)
        {
            MessageBox.Show(this, "Please enter the stream URL.", AppTitle,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var vlcExe = VlcLocator.FindExecutable();
        if (vlcExe is null)
        {
            MessageBox.Show(this,
                "VLC was not found.\nPlease install VLC from https://www.videolan.org first.",
                AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            var startInfo = new ProcessStartInfo(vlcExe)
            {
                UseShellExecute = false,
            };
            startInfo.ArgumentList.Add(url);
            Process.Start(startInfo);
            SetStatus("Opened in VLC", SystemColors.ControlText);
        }
        catch (Exception err)
        {
            MessageBox.Show(this, $"Could not launch VLC:\n{err.Message}", AppTitle,
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ResetPlaybackState()
    {
        lock (_bufferLock)
        {
            _bufferPct = null;
            _lastBufferAt = DateTime.MinValue;
        }
        _lastTime = 0;
        _connectSince = null;
        _lastDemuxBytes = 0;
        _lastDecodedAudio = 0;
        _lastFlowAt = null;
        _playStartedAt = null;
    }

    // ---------------- background polling ----------------
    /// <summary>
    /// True while VLC media statistics show stream data still arriving and
    /// being decoded. For mms live streams this is the most reliable
    /// "is it really playing" signal: Time often stays at 0 and the state
    /// never leaves Opening/Buffering, but the stats keep growing.
    /// </summary>
    private bool StatsFlowing()
    {
        try
        {
            var media = _player?.Media;
            if (media is null) return false;

            var stats = media.Statistics;
            long demux = stats.DemuxReadBytes;
            long decoded = stats.DecodedAudio;

            var flowed = demux > _lastDemuxBytes || decoded > _lastDecodedAudio;
            if (demux > _lastDemuxBytes) _lastDemuxBytes = demux;
            if (decoded > _lastDecodedAudio) _lastDecodedAudio = decoded;
            return flowed;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Read the player state twice a second; refresh status + progress bar.</summary>
    private void PollStatus(object? sender, EventArgs e)
    {
        if (_vlcOk && _player is not null)
        {
            int? state = null;
            try
            {
                state = (int)_player.State;
            }
            catch
            {
                // player disposed / not ready
            }

            if (state is not null)
            {
                // Detect "really playing" while libvlc still reports
                // Opening/Buffering - very common for mms live streams.
                // (a) playback time keeps advancing -> it is playing
                long currentTime = 0;
                try
                {
                    currentTime = _player.Time;
                }
                catch
                {
                    currentTime = 0;
                }
                var timeAdvancing = currentTime > 0 && currentTime != _lastTime;
                _lastTime = currentTime;

                // (b) media statistics keep growing -> data is flowing even
                //     when the playback clock stays at 0 (typical for mms://)
                if (StatsFlowing())
                    _lastFlowAt = DateTime.UtcNow;
                var flowingRecently = _lastFlowAt is not null
                    && (DateTime.UtcNow - _lastFlowAt.Value).TotalSeconds < 2.5;

                if (state is 1 or 2 && (timeAdvancing || flowingRecently))
                    state = 3; // promote: audio is actually playing

                if (state == 3 && _playStartedAt is null)
                    _playStartedAt = DateTime.UtcNow;
                else if (state is 5 or 6 or 7)
                {
                    _playStartedAt = null;
                    _lastFlowAt = null;
                }

                // remember when the connecting phase began (for the slow hint)
                if (state is 1 or 2)
                    _connectSince ??= DateTime.UtcNow;
                else
                    _connectSince = null;

                var (text, color) = state.Value is >= 0 and <= 7
                    ? StateText[state.Value]
                    : ("...", SystemColors.ControlText);

                if (state is 1 or 2 && _connectSince is not null
                    && (DateTime.UtcNow - _connectSince.Value).TotalSeconds > SlowConnectSeconds)
                {
                    text = "Still connecting... (slow stream)";
                }

                SetStatus(text, color);
                UpdateProgress(state.Value);
            }
        }
    }

    /// <summary>Receive the buffering percent from a VLC event (runs in a VLC thread).</summary>
    private void OnVlcBuffering(object? sender, MediaPlayerBufferingEventArgs e)
    {
        try
        {
            lock (_bufferLock)
            {
                _bufferPct = e.Cache;
                _lastBufferAt = DateTime.UtcNow;
            }
        }
        catch
        {
            // ignore malformed events
        }
    }

    /// <summary>
    /// Latest buffering percentage reported by VLC, but only while it is fresh.
    /// VLC reports a real percentage only when the stream is actually opening
    /// (or re-buffering), so a fresh value means "real progress is happening".
    /// </summary>
    private bool TryGetFreshBufferPercent(out int percent)
    {
        float? pct;
        DateTime lastBufferAt;
        lock (_bufferLock)
        {
            pct = _bufferPct;
            lastBufferAt = _lastBufferAt;
        }

        var fresh = pct is > 0
            && (DateTime.UtcNow - lastBufferAt).TotalSeconds < BufferFreshSeconds;

        percent = fresh ? Math.Clamp((int)Math.Round(pct!.Value), 0, 100) : 0;
        return fresh;
    }

    /// <summary>Refresh the progress bar according to the player state.</summary>
    private void UpdateProgress(int state)
    {
        try
        {
            // While VLC reports a real buffering percentage the stream is truly
            // opening, so show that number centered inside the bar. VLC often
            // already reports "Playing" during this phase, so the percentage
            // takes priority over the elapsed-time display until it reaches 100.
            var hasPercent = TryGetFreshBufferPercent(out var percent)
                && state is 1 or 2 or 3
                && percent > 0
                && (state != 3 || percent < 100);

            if (hasPercent)
            {
                SetBarMode(false);
                _progress.Value = percent;
                _progress.Text = $"{percent}%";
            }
            else if (state is 1 or 2)
            {
                // no percentage reported -> smooth indeterminate animation;
                // there is no honest number to show yet
                SetBarMode(true);
                _progress.Text = string.Empty;
            }
            else if (state == 3)
            {
                // playing - full bar + elapsed playback time
                SetBarMode(false);
                _progress.Value = 100;

                long vlcTime = 0;
                long length = 0;
                try
                {
                    vlcTime = _player?.Time ?? 0;
                    length = _player?.Length ?? 0;
                }
                catch
                {
                    vlcTime = 0;
                    length = 0;
                }

                string label;
                if (length > 0 && vlcTime > 0)
                    // finite media - show the real playback position
                    label = TimeText.Format(vlcTime);
                else if (_playStartedAt is not null)
                    // live streams report no duration (and may report a bogus
                    // clock), so show how long the listener has been playing
                    label = TimeText.Format((long)((DateTime.UtcNow - _playStartedAt.Value).TotalMilliseconds));
                else
                    label = "00:00";

                _progress.Text = label;
            }
            else if (state == 4)
            {
                // paused - keep the bar as it is
            }
            else
            {
                // ready / stopped / ended / error - empty the bar
                SetBarMode(false);
                _progress.Value = 0;
                _progress.Text = string.Empty;
            }
        }
        catch
        {
            // never let a UI refresh crash the app
        }
    }

    private void SetBarMode(bool marquee)
    {
        var style = marquee ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous;
        if (_progress.Style != style)
            _progress.Style = style;
    }

    // ---------------- startup / shutdown ----------------
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (_vlcError.Length == 0) 
        {
            if (_autoPlayStartupItem.Checked)
                Play();
            return;
        }

        SetStatus("VLC Media Player not found", StatusError);
        MessageBox.Show(this, _vlcError, AppTitle,
            MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _pollTimer.Stop();
        try
        {
            _player?.Stop();
        }
        catch
        {
            // ignore
        }
        SaveUrl();
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _pollTimer.Dispose();
            _toolTip.Dispose();
            _player?.Dispose();
            _libVlc?.Dispose();
        }
        base.Dispose(disposing);
    }
}
