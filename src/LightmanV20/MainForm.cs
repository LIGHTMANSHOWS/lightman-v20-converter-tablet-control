using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;

namespace LightmanZapravka3D;

internal sealed class MainForm : Form
{
    private const int TabletPort = 8780;
    private const int ClientPort = 8781;
    private readonly ArtNetReceiver _artNet = new();
    private readonly LiveEngine _live;
    private readonly XScheduleController _scheduler;
    private readonly TrackingModeController _tracking;
    private readonly ShowPlaybackFallback _showFallback = new();
    private readonly CancellationTokenSource _scheduleMonitorStop = new();
    private Task? _scheduleMonitorTask;
    private readonly WebView2 _viewer = new() { Dock = DockStyle.Fill, BackColor = Color.FromArgb(4, 7, 12) };
    private readonly System.Windows.Forms.Timer _frameTimer = new() { Interval = 33 };
    private readonly Label _status = new() { AutoSize = false, Width = 270, Dock = DockStyle.Right,
        TextAlign = ContentAlignment.MiddleRight, ForeColor = Color.FromArgb(167, 184, 200) };
    private readonly Label _tabletUrl = new() { AutoSize = false, Width = 410, Dock = DockStyle.Right,
        TextAlign = ContentAlignment.MiddleRight, ForeColor = Color.FromArgb(96, 224, 190),
        Font = new Font("Segoe UI", 8.5f) };
    private readonly Dictionary<string, Button> _sourceButtons = new(StringComparer.OrdinalIgnoreCase);
    private readonly Button _testButton = MakeButton("TEST GENERAL");
    private TabletServer? _tablet;
    private ClientExperienceServer? _client;
    private bool _webReady;
    private string _source = "resolume";
    private bool _generalTest;
    private string _receiverError = "";
    private string _tabletError = "";
    private string _clientError = "";
    private long _previousPackets;
    private DateTime _previousRateUtc = DateTime.UtcNow;
    private double _packetRate;
    private DateTime _lastPacketUtc = DateTime.MinValue;
    private long _rx;
    private long _tx;
    private string _activeSender = "";
    private string _liveError = "";
    private string _schedulerActionError = "";
    private readonly object _stateGate = new();
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lightman", "V20Minimal", "source.json");
    private sealed record MinimalSettings(string Source);

    public MainForm(bool previewOnly = false)
    {
        Text = "LIGHTMAN V20 MINIMAL · VISUALIZADOR + PUENTE";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1500, 900);
        MinimumSize = new Size(960, 620);
        BackColor = Color.FromArgb(4, 7, 12);
        _live = new LiveEngine(_artNet, null, automaticOutput: !previewOnly);
        _scheduler = XScheduleController.Load(
            Path.Combine(AppContext.BaseDirectory, "ShowControl", "shows.json"), runDiscovery: !previewOnly);
        _tracking = TrackingModeController.Load(
            Path.Combine(AppContext.BaseDirectory, "TrackingControl", "modes.json"));
        LoadSettings();

        var toolbar = BuildToolbar();
        Controls.Add(_viewer);
        Controls.Add(toolbar);
        toolbar.BringToFront();
        _frameTimer.Tick += (_, _) => PublishFrame();
        FormClosed += (_, _) => Shutdown();
        Load += async (_, _) => await InitializeAsync(previewOnly);
    }

    private static Button MakeButton(string text) => new()
    {
        Text = text,
        AutoSize = false,
        Width = 126,
        Height = 42,
        Margin = new Padding(5, 9, 5, 9),
        FlatStyle = FlatStyle.Flat,
        BackColor = Color.FromArgb(15, 25, 37),
        ForeColor = Color.FromArgb(220, 231, 240),
        Font = new Font("Segoe UI Semibold", 9.5f),
        Cursor = Cursors.Hand,
    };

    private Control BuildToolbar()
    {
        var bar = new Panel { Dock = DockStyle.Top, Height = 62, Padding = new Padding(12, 0, 12, 0),
            BackColor = Color.FromArgb(7, 12, 20) };
        var flow = new FlowLayoutPanel { Dock = DockStyle.Left, AutoSize = true, WrapContents = false,
            BackColor = Color.Transparent };
        var title = new Label { Text = "LIGHTMAN  V20", AutoSize = false, Width = 125, Height = 60,
            TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 11f) };
        flow.Controls.Add(title);
        foreach (var (id, text) in new[] { ("resolume", "RESOLUME"), ("xlights", "XLIGHTS"), ("tracking", "TRACKING") })
        {
            var button = MakeButton(text);
            button.Tag = id;
            button.Click += (_, _) => SelectSource(id);
            _sourceButtons[id] = button;
            flow.Controls.Add(button);
        }
        _testButton.Width = 145;
        _testButton.Click += (_, _) => SetGeneralTest(!_generalTest);
        flow.Controls.Add(_testButton);
        bar.Controls.Add(flow);
        bar.Controls.Add(_status);
        bar.Controls.Add(_tabletUrl);
        RefreshControls();
        return bar;
    }

    private async Task InitializeAsync(bool previewOnly)
    {
        try
        {
            bool receiverReady = previewOnly;
            if (!previewOnly)
            {
                try { _artNet.Start(); receiverReady = true; }
                catch (Exception ex) { _receiverError = ex.Message; }
            }
            _live.SelectInput(_source, "");

            string webFolder = Path.Combine(AppContext.BaseDirectory, "Web");
            string startPage = Path.Combine(webFolder, "index.html");
            if (!File.Exists(startPage)) throw new FileNotFoundException("Falta el visualizador minimalista.", startPage);
            await _viewer.EnsureCoreWebView2Async();
            if (_viewer.CoreWebView2 is null) throw new InvalidOperationException("WebView2 no pudo inicializarse.");
            _viewer.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            _viewer.CoreWebView2.Settings.IsStatusBarEnabled = false;
            _viewer.CoreWebView2.Settings.AreDevToolsEnabled = false;
            _viewer.CoreWebView2.Settings.IsZoomControlEnabled = true;
            _viewer.CoreWebView2.SetVirtualHostNameToFolderMapping("minimal-v20.lightman", webFolder,
                CoreWebView2HostResourceAccessKind.Allow);
            _viewer.CoreWebView2.WebMessageReceived += (_, args) =>
            {
                try
                {
                    using var json = JsonDocument.Parse(args.WebMessageAsJson);
                    if (json.RootElement.TryGetProperty("type", out var type) && type.GetString() == "ready")
                    { _webReady = true; PublishFrame(true); }
                }
                catch { }
            };
            _viewer.Source = new Uri("https://minimal-v20.lightman/index.html?minimal=1&revision=portable-v4");

            if (!previewOnly)
            {
                try
                {
                    _tablet = new TabletServer(webFolder, TabletPort, TabletState, SelectSource, SetGeneralTest,
                        PlayShow, PauseShow, StopShow, TrackingControlState, SelectTrackingMode,
                        _tracking.ReportStatus);
                    _tablet.Start();
                }
                catch (Exception ex)
                {
                    _tablet?.Dispose();
                    _tablet = null;
                    _tabletError = ex.Message;
                }

                try
                {
                    _client = new ClientExperienceServer(webFolder, ClientPort, ClientExperienceState, PlayShow,
                        SelectTrackingMode);
                    _client.Start();
                }
                catch (Exception ex)
                {
                    _client?.Dispose();
                    _client = null;
                    _clientError = ex.Message;
                }

                string internalAddress = _tablet is null ? "INTERNO NO DISPONIBLE" : "INTERNO  " + ServerAddress(TabletPort);
                string clientAddress = _client is null ? "CLIENTE NO DISPONIBLE" : "CLIENTE  " + ServerAddress(ClientPort);
                _tabletUrl.Text = internalAddress + Environment.NewLine + clientAddress;
            }
            else _tabletUrl.Text = "VISTA SIN RED";
            if (!previewOnly) StartScheduleMonitor();
            // Start physical output only after the monitor itself is ready.
            // A missing WebView/runtime asset therefore fails safely in black.
            if (receiverReady) _live.Start();
            _frameTimer.Start();
        }
        catch (Exception ex)
        {
            lock (_stateGate) _liveError = ex.Message;
            _status.Text = "ERROR · " + ex.Message;
        }
    }

    private bool SelectSource(string source) => SelectSource(source, pauseLeavingXlights: true);

    private bool SelectSource(string source, bool pauseLeavingXlights)
    {
        if (InvokeRequired)
        {
            try { return (bool)Invoke(new Func<string, bool, bool>(SelectSource), source, pauseLeavingXlights); }
            catch { return false; }
        }
        if (source is not ("resolume" or "xlights" or "tracking")) return false;
        string previous;
        lock (_stateGate) previous = _source;
        if (source != "xlights") _showFallback.Disarm();
        if (pauseLeavingXlights && previous == "xlights" && source != "xlights" && _scheduler.PauseWhenLeavingXlights)
        {
            var paused = _scheduler.PauseIfPlaying();
            lock (_stateGate) _schedulerActionError = paused.Ok ? "" : paused.Error;
            if (!paused.Ok) return false;
        }
        _live.SetGeneralTest(false);
        _artNet.ResetAutoSource(source);
        _live.SelectInput(source, "");
        lock (_stateGate) { _source = source; _generalTest = false; }
        SaveSettings();
        ResetRate();
        RefreshControls();
        PublishFrame(true);
        return true;
    }

    private bool SetGeneralTest(bool enabled)
    {
        if (InvokeRequired)
        {
            try { return (bool)Invoke(new Func<bool, bool>(SetGeneralTest), enabled); }
            catch { return false; }
        }
        if (enabled && _receiverError.Length > 0) return false;
        if (enabled && _source == "xlights" && _scheduler.PauseWhenLeavingXlights)
        {
            var paused = _scheduler.PauseIfPlaying();
            lock (_stateGate) _schedulerActionError = paused.Ok ? "" : paused.Error;
            if (!paused.Ok) return false;
        }
        _live.SetGeneralTest(enabled);
        lock (_stateGate) _generalTest = enabled;
        RefreshControls();
        PublishFrame(true);
        return true;
    }

    private TabletActionResult PlayShow(string id)
    {
        if (InvokeRequired)
        {
            try { return (TabletActionResult)Invoke(new Func<string, TabletActionResult>(PlayShow), id); }
            catch { return TabletActionResult.Fail("V20 no pudo ejecutar el show."); }
        }
        var result = _scheduler.Play(id);
        if (!result.Ok)
        {
            lock (_stateGate) _schedulerActionError = result.Error;
            return TabletActionResult.Fail(result.Error);
        }
        lock (_stateGate) _schedulerActionError = "";
        if (!SelectSource("xlights")) return TabletActionResult.Fail("No se pudo seleccionar xLights.");
        _showFallback.Arm(DateTime.UtcNow);
        return TabletActionResult.Success();
    }

    private TabletActionResult PauseShow()
    {
        var result = _scheduler.TogglePause();
        lock (_stateGate) _schedulerActionError = result.Ok ? "" : result.Error;
        return result.Ok ? TabletActionResult.Success() : TabletActionResult.Fail(result.Error);
    }

    private TabletActionResult StopShow()
    {
        var result = _scheduler.Stop();
        lock (_stateGate) _schedulerActionError = result.Ok ? "" : result.Error;
        if (!result.Ok) return TabletActionResult.Fail(result.Error);
        _showFallback.Disarm();
        return SelectSource("resolume", pauseLeavingXlights: false)
            ? TabletActionResult.Success()
            : TabletActionResult.Fail("El show se detuvo, pero V20 no pudo regresar a Resolume.");
    }

    private TrackingModeSelectionResult SelectTrackingMode(string id)
    {
        if (InvokeRequired)
        {
            try { return (TrackingModeSelectionResult)Invoke(new Func<string, TrackingModeSelectionResult>(SelectTrackingMode), id); }
            catch { return TrackingModeSelectionResult.Fail("V20 no pudo seleccionar el modo de tracking.", 0); }
        }

        string previousSource;
        lock (_stateGate) previousSource = _source;
        var trackingState = _tracking.GetStateSnapshot();
        bool requireFreshAck = previousSource != "tracking" || !trackingState.Connected ||
            trackingState.Status == "error";
        var result = _tracking.SelectMode(id, requireFreshAck);
        if (!result.Ok) return result;
        if (SelectSource("tracking")) return result;
        return TrackingModeSelectionResult.Fail("V20 no pudo seleccionar Motion Tracking.", result.Revision);
    }

    private TrackingControlSnapshot TrackingControlState()
    {
        var state = _tracking.GetControlSnapshot();
        string source;
        lock (_stateGate) source = _source;
        return state with { Selected = source == "tracking" };
    }

    private void StartScheduleMonitor()
    {
        if (_scheduleMonitorTask is not null) return;
        _scheduleMonitorTask = Task.Run(async () =>
        {
            var token = _scheduleMonitorStop.Token;
            try
            {
                using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(750));
                while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
                {
                    if (!_showFallback.Armed) continue;
                    bool xlightsSelected;
                    bool xlightsSignalActive;
                    DateTime now = DateTime.UtcNow;
                    lock (_stateGate)
                    {
                        xlightsSelected = _source == "xlights";
                        xlightsSignalActive = xlightsSelected && _lastPacketUtc != DateTime.MinValue &&
                            now - _lastPacketUtc < TimeSpan.FromSeconds(2);
                    }
                    var scheduler = _scheduler.GetStatus(force: true);
                    if (!_showFallback.Observe(now, scheduler, xlightsSelected, xlightsSignalActive,
                            out long fallbackGeneration)) continue;
                    if (IsDisposed || Disposing) return;
                    try
                    {
                        BeginInvoke(new Action(() =>
                        {
                            string source;
                            lock (_stateGate) source = _source;
                            if (source == "xlights" && _showFallback.IsCurrent(fallbackGeneration))
                                SelectSource("resolume", pauseLeavingXlights: false);
                        }));
                    }
                    catch (InvalidOperationException) { return; }
                }
            }
            catch (OperationCanceledException) { }
        });
    }

    private void ResetRate()
    {
        lock (_stateGate)
        {
            _previousPackets = 0;
            _packetRate = 0;
            _previousRateUtc = DateTime.UtcNow;
            _lastPacketUtc = DateTime.MinValue;
            _rx = 0;
            _activeSender = "";
        }
    }

    private void PublishFrame(bool force = false)
    {
        var snapshot = _artNet.CaptureUniverses(_source, "");
        var now = DateTime.UtcNow;
        var output = _live.MonitorSnapshot;
        bool signal;
        lock (_stateGate)
        {
            _rx = snapshot.PacketCount;
            _lastPacketUtc = snapshot.LastPacketUtc;
            _activeSender = snapshot.Sources.FirstOrDefault() ?? "";
            if ((now - _previousRateUtc).TotalSeconds >= 1)
            {
                _packetRate = Math.Max(0, snapshot.PacketCount - _previousPackets) / (now - _previousRateUtc).TotalSeconds;
                _previousPackets = snapshot.PacketCount;
                _previousRateUtc = now;
            }
            _tx = output.Tx;
            _liveError = output.Error;
            signal = (_generalTest || (_lastPacketUtc != DateTime.MinValue && now - _lastPacketUtc < TimeSpan.FromSeconds(2))) &&
                _receiverError.Length == 0 && _liveError.Length == 0;
            _status.Text = _receiverError.Length > 0 ? "PUERTO OCUPADO" : _liveError.Length > 0 ? "ERROR DE SALIDA" :
                _generalTest ? "TEST GENERAL · 25%" : signal ? $"EN VIVO · {_packetRate:0} pkt/s" : "ESPERANDO SEÑAL";
        }
        RefreshControls();

        if (!_webReady || _viewer.CoreWebView2 is null) return;
        var frames = output.Frames.ToDictionary(p => p.Key, p => Convert.ToBase64String(p.Value));
        _viewer.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new
        {
            type = "outputFrame",
            routes = frames,
            source = _source,
            sourceLabel = SourceLabel(_source),
            generalTest = _generalTest,
            signal,
            packetRate = Math.Round(_packetRate, 1),
            rx = _rx,
            tx = _tx,
            sender = _activeSender,
            error = _receiverError.Length > 0 ? _receiverError : _liveError,
        }, LiveEngine.Json));
    }

    private object TabletState()
    {
        var scheduler = _scheduler.GetStatus();
        var shows = _scheduler.ShowsForTablet();
        lock (_stateGate)
        {
            var now = DateTime.UtcNow;
            bool signal = _generalTest || (_lastPacketUtc != DateTime.MinValue && now - _lastPacketUtc < TimeSpan.FromSeconds(2));
            return new
            {
                source = _source,
                sourceLabel = SourceLabel(_source),
                generalTest = _generalTest,
                signal,
                packetRate = Math.Round(_packetRate, 1),
                rx = _rx,
                tx = _tx,
                sender = _activeSender,
                error = _receiverError.Length > 0 ? _receiverError : _liveError,
                tabletError = _tabletError,
                clientError = _clientError,
                scheduler = new
                {
                    configured = scheduler.Configured,
                    connected = scheduler.Connected,
                    status = scheduler.Status,
                    playlist = scheduler.Playlist,
                    step = scheduler.Step,
                    position = scheduler.Position,
                    length = scheduler.Length,
                    error = _schedulerActionError.Length > 0 ? _schedulerActionError : scheduler.Error,
                },
                tracking = TrackingStateForPresentation(includeLastSeen: true),
                autoImport = _scheduler.DiscoveryReport,
                scheduleSync = _scheduler.ScheduleSyncReport,
                shows,
                endpoints = new { resolume = "127.0.0.2:6454", xlights = "127.0.0.3:6454",
                    tracking = "IP LAN de V20:6454" },
            };
        }
    }

    private object ClientExperienceState()
    {
        var scheduler = _scheduler.GetStatus();
        string source;
        lock (_stateGate) source = _source;
        bool showActive = source == "xlights" && scheduler.Connected &&
            (scheduler.Status.Equals("Playing", StringComparison.OrdinalIgnoreCase) ||
             scheduler.Status.Equals("Paused", StringComparison.OrdinalIgnoreCase));
        return new
        {
            experiences = _scheduler.ExperiencesForClient(),
            activeExperienceId = showActive ? _scheduler.ExperienceIdForPlaylist(scheduler.Playlist) : "",
            tracking = TrackingStateForPresentation(includeLastSeen: false),
        };
    }

    private object TrackingStateForPresentation(bool includeLastSeen)
    {
        var tracking = _tracking.GetStateSnapshot();
        string source;
        lock (_stateGate) source = _source;
        bool selected = source == "tracking";
        bool activeConfirmed = selected && tracking.Connected &&
            tracking.ActiveMode.Equals(tracking.DesiredMode, StringComparison.Ordinal) &&
            tracking.Status is "running" or "degraded";
        return new
        {
            configured = tracking.Configured,
            connected = tracking.Connected,
            selected,
            status = selected ? tracking.Status : "standby",
            desiredMode = selected ? tracking.DesiredMode : "",
            activeMode = activeConfirmed ? tracking.ActiveMode : "",
            revision = tracking.Revision,
            lastSeenUtc = includeLastSeen ? tracking.LastSeenUtc : null,
            error = includeLastSeen ? tracking.Error : "",
            modes = tracking.Modes,
        };
    }

    private void RefreshControls()
    {
        foreach (var pair in _sourceButtons)
        {
            bool active = pair.Key == _source && !_generalTest;
            pair.Value.BackColor = active ? Color.FromArgb(13, 105, 86) : Color.FromArgb(15, 25, 37);
            pair.Value.FlatAppearance.BorderColor = active ? Color.FromArgb(79, 237, 195) : Color.FromArgb(44, 60, 76);
        }
        _testButton.BackColor = _generalTest ? Color.FromArgb(124, 47, 44) : Color.FromArgb(15, 25, 37);
        _testButton.FlatAppearance.BorderColor = _generalTest ? Color.FromArgb(255, 123, 108) : Color.FromArgb(44, 60, 76);
        _testButton.Text = _generalTest ? "DETENER TEST" : "TEST GENERAL";
        _testButton.Enabled = _receiverError.Length == 0;
    }

    private static string SourceLabel(string source) => source switch
    {
        "xlights" => "xLights",
        "tracking" => "Motion Tracking",
        _ => "Resolume",
    };

    private static string ServerAddress(int port)
    {
        var addresses = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Where(a => a.DuplicateAddressDetectionState == DuplicateAddressDetectionState.Preferred)
            .Select(a => a.Address)
            .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a) &&
                !a.ToString().StartsWith("169.254.", StringComparison.Ordinal))
            .Distinct()
            .ToArray();
        var address = addresses.FirstOrDefault(a => a.ToString().StartsWith("192.168.1.", StringComparison.Ordinal))
            ?? addresses.FirstOrDefault(a =>
            {
                var b = a.GetAddressBytes();
                return b[0] == 10 || b[0] == 172 && b[1] is >= 16 and <= 31 || b[0] == 192 && b[1] == 168;
            })
            ?? addresses.FirstOrDefault();
        return address is null ? $"http://IP-DE-ESTA-PC:{port}" : $"http://{address}:{port}";
    }

    private void LoadSettings()
    {
        // Una fuente restaurada no puede reconstruir de forma segura la sesión
        // de xSchedule ni el ACK del tracker. Cada arranque comienza en Resolume.
        _source = "resolume";
    }

    private void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new MinimalSettings(_source), LiveEngine.Json));
        }
        catch { }
    }

    private void Shutdown()
    {
        _frameTimer.Stop();
        _scheduleMonitorStop.Cancel();
        try { _scheduleMonitorTask?.Wait(TimeSpan.FromSeconds(2)); } catch { }
        _tablet?.Dispose();
        _client?.Dispose();
        _scheduler.Dispose();
        _live.Dispose();
        _artNet.Dispose();
        _viewer.Dispose();
        _scheduleMonitorStop.Dispose();
    }
}
