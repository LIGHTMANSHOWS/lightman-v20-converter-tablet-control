using System.Net;
using System.Text.Json;

namespace LightmanZapravka3D;

internal sealed class ShowLaunchDefinition
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string PublicTitle { get; init; } = "";
    public string Category { get; init; } = "Experiencias";
    public string Tagline { get; init; } = "";
    public bool ClientVisible { get; init; } = true;
    public string Playlist { get; init; } = "";
    public string Sequence { get; init; } = "";
    public string Audio { get; init; } = "";
    public bool Enabled { get; init; } = true;
    public bool ClientEnabled { get; init; } = true;
    public string Note { get; init; } = "";
    public bool AutoDiscovered { get; init; }
    public string SourceFolder { get; init; } = "";
}

internal sealed class XScheduleSettings
{
    public string BaseUrl { get; init; } = "http://127.0.0.1:80/";
    public int TimeoutMs { get; init; } = 1200;
    public bool PauseWhenLeavingXlights { get; init; } = true;
    public bool AutoDiscoverShows { get; init; }
    public string ShowFolderRoot { get; init; } = "%USERPROFILE%/Desktop/shows xlights";
    public List<ShowLaunchDefinition> Shows { get; init; } = [];
}

internal readonly record struct XScheduleAction(bool Ok, string Error)
{
    public static XScheduleAction Success() => new(true, "");
    public static XScheduleAction Fail(string error) => new(false, error);
}

internal sealed record XScheduleStatus(
    bool Configured,
    bool Connected,
    string Status,
    string Playlist,
    string Step,
    string Position,
    string Length,
    string Error);

/// <summary>
/// Adapter mínimo a la API HTTP incluida en xSchedule. El catálogo usa IDs locales
/// para que la tablet nunca pueda enviar nombres de playlists o rutas arbitrarias.
/// </summary>
internal sealed class XScheduleController : IDisposable
{
    private readonly object _gate = new();
    private readonly HttpClient _http;
    private readonly XScheduleSettings _settings;
    private readonly bool _configured;
    private XScheduleStatus _cached;
    private DateTime _cachedAtUtc = DateTime.MinValue;
    private readonly HashSet<string> _runtimePlaylists = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _runtimePlaylistsAtUtc = DateTime.MinValue;

    private XScheduleController(XScheduleSettings settings, bool configured, string configurationError,
        AutoDiscoveryReport discoveryReport, XScheduleSyncReport scheduleSyncReport)
    {
        _settings = settings;
        _configured = configured;
        _cached = new(configured, false, "Unavailable", "", "", "", "", configurationError);
        _http = new HttpClient
        {
            BaseAddress = NormalizeBaseAddress(settings.BaseUrl),
            Timeout = TimeSpan.FromMilliseconds(Math.Clamp(settings.TimeoutMs, 300, 5000)),
        };
        DiscoveryReport = discoveryReport;
        ScheduleSyncReport = scheduleSyncReport;
    }

    public IReadOnlyList<ShowLaunchDefinition> Shows => _settings.Shows;
    public bool PauseWhenLeavingXlights => _settings.PauseWhenLeavingXlights;
    public AutoDiscoveryReport DiscoveryReport { get; }
    public XScheduleSyncReport ScheduleSyncReport { get; }

    public static XScheduleController Load(string path, bool runDiscovery = true)
    {
        try
        {
            if (!File.Exists(path))
                return new(new XScheduleSettings(), false, "Falta ShowControl/shows.json.",
                    AutoDiscoveryReport.Disabled(""), XScheduleSyncReport.NotRun());
            var settings = JsonSerializer.Deserialize<XScheduleSettings>(File.ReadAllText(path), LiveEngine.Json)
                ?? throw new InvalidDataException("Configuración vacía.");
            Validate(settings);
            var discovery = runDiscovery
                ? ShowFolderDiscovery.Apply(settings, path)
                : AutoDiscoveryReport.Disabled(ExpandPath(settings.ShowFolderRoot));
            Validate(settings);
            XScheduleSyncReport scheduleSync = XScheduleSyncReport.NotRun();
            if (runDiscovery && settings.AutoDiscoverShows)
            {
                string root = ExpandPath(settings.ShowFolderRoot);
                var candidates = settings.Shows.Where(show => show.AutoDiscovered).Select(show =>
                    new XSchedulePlaylistCandidate(show.Playlist, show.Title, show.Sequence, show.Audio, show.SourceFolder));
                scheduleSync = XSchedulePlaylistSynchronizer.Synchronize(
                    root, Path.Combine(root, "xlights.xschedule"), candidates, settings.BaseUrl, settings.TimeoutMs);
            }
            return new(settings, true, "", discovery, scheduleSync);
        }
        catch (Exception ex)
        {
            return new(new XScheduleSettings(), false, "Configuración de xSchedule: " + ex.Message,
                AutoDiscoveryReport.Disabled(""), XScheduleSyncReport.NotRun());
        }
    }

    public object[] ShowsForTablet()
    {
        RefreshRuntimePlaylistsIfNeeded();
        return _settings.Shows.Select(show => new
        {
            id = show.Id,
            title = show.Title,
            playlist = show.Playlist,
            enabled = show.Enabled,
            ready = File.Exists(ExpandPath(show.Sequence)) && File.Exists(ExpandPath(show.Audio)),
            scheduleReady = IsAutoPlaylistReady(show),
            note = show.Note,
            autoDiscovered = show.AutoDiscovered,
        }).Cast<object>().ToArray();
    }

    public object[] ExperiencesForClient() => _settings.Shows
        .Where(show => show.ClientVisible && !string.IsNullOrWhiteSpace(show.PublicTitle))
        .Select(show => new
        {
            id = show.Id,
            publicTitle = show.PublicTitle,
            category = string.IsNullOrWhiteSpace(show.Category) ? "Experiencias" : show.Category,
            tagline = show.Tagline,
            enabled = show.ClientEnabled && show.Enabled &&
                File.Exists(ExpandPath(show.Sequence)) && File.Exists(ExpandPath(show.Audio)),
        }).Cast<object>().ToArray();

    public string ExperienceIdForPlaylist(string playlist) => _settings.Shows
        .FirstOrDefault(show => show.ClientVisible && show.Playlist.Equals(playlist, StringComparison.OrdinalIgnoreCase))?.Id ?? "";

    public XScheduleStatus GetStatus(bool force = false)
    {
        lock (_gate)
        {
            if (!force && DateTime.UtcNow - _cachedAtUtc < TimeSpan.FromMilliseconds(750)) return _cached;
            if (!_configured) return _cached;
            try
            {
                using var json = GetJson("xScheduleQuery", "Query", "GetPlayingStatus", "Parameters", "");
                var root = json.RootElement;
                _cached = new(true, true,
                    FindString(root, "Status", "status") ?? "Idle",
                    FindString(root, "PlayList", "playlist") ?? "",
                    FindString(root, "Step", "step") ?? "",
                    FindString(root, "Position", "position") ?? "",
                    FindString(root, "Length", "length") ?? "",
                    "");
            }
            catch (Exception ex)
            {
                _cached = new(true, false, "Unavailable", "", "", "", "", FriendlyError(ex));
            }
            _cachedAtUtc = DateTime.UtcNow;
            return _cached;
        }
    }

    public XScheduleAction Play(string id)
    {
        var show = _settings.Shows.FirstOrDefault(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (show is null) return XScheduleAction.Fail("Show desconocido.");
        if (!show.Enabled) return XScheduleAction.Fail("Este show todavía está deshabilitado.");
        RefreshRuntimePlaylistsIfNeeded(force: true);
        if (!IsAutoPlaylistReady(show))
            return XScheduleAction.Fail("La playlist autoimportada todavía no está disponible en xSchedule.");
        if (!File.Exists(ExpandPath(show.Sequence))) return XScheduleAction.Fail("No se encontró el archivo FSEQ del show.");
        if (!File.Exists(ExpandPath(show.Audio))) return XScheduleAction.Fail("No se encontró el audio del show.");
        return Command("Play specified playlist", show.Playlist);
    }

    private bool IsAutoPlaylistReady(ShowLaunchDefinition show)
    {
        if (!show.AutoDiscovered) return true;
        if (ScheduleSyncReport.InvalidCandidates.Any(issue =>
            issue.Playlist.Equals(show.Playlist, StringComparison.OrdinalIgnoreCase))) return false;
        bool reportBlocks = ScheduleSyncReport.SkippedBusy || ScheduleSyncReport.RestartRequired ||
            ScheduleSyncReport.WaitingForXSchedule || ScheduleSyncReport.Errors.Count > 0;
        return !reportBlocks || _runtimePlaylists.Contains(show.Playlist);
    }

    private void RefreshRuntimePlaylistsIfNeeded(bool force = false)
    {
        if (!_settings.Shows.Any(show => show.AutoDiscovered) ||
            !(ScheduleSyncReport.SkippedBusy || ScheduleSyncReport.RestartRequired ||
              ScheduleSyncReport.WaitingForXSchedule || ScheduleSyncReport.Errors.Count > 0)) return;
        lock (_gate)
        {
            if (!force && DateTime.UtcNow - _runtimePlaylistsAtUtc < TimeSpan.FromSeconds(2)) return;
            _runtimePlaylistsAtUtc = DateTime.UtcNow;
            try
            {
                using var json = GetJson("xScheduleQuery", "Query", "GetPlayLists", "Parameters", "");
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                CollectPlaylistNames(json.RootElement, names);
                _runtimePlaylists.Clear();
                _runtimePlaylists.UnionWith(names);
            }
            catch { }
        }
    }

    public XScheduleAction PauseIfPlaying()
    {
        var state = GetStatus(true);
        if (!state.Connected) return XScheduleAction.Fail(state.Error);
        if (!state.Status.Equals("Playing", StringComparison.OrdinalIgnoreCase)) return XScheduleAction.Success();
        return Command("Pause", "");
    }

    public XScheduleAction TogglePause()
    {
        var state = GetStatus(true);
        if (!state.Connected) return XScheduleAction.Fail(state.Error);
        if (state.Status.Equals("Idle", StringComparison.OrdinalIgnoreCase))
            return XScheduleAction.Fail("No hay un show reproduciéndose.");
        return Command("Pause", "");
    }

    public XScheduleAction Stop() => Command("Stop all now", "");

    private XScheduleAction Command(string command, string parameters)
    {
        lock (_gate)
        {
            if (!_configured) return XScheduleAction.Fail(_cached.Error);
            try
            {
                using var json = GetJson("xScheduleCommand", "Command", command, "Parameters", parameters);
                string? result = FindString(json.RootElement, "result", "Result", "status", "Status");
                string? message = FindString(json.RootElement, "message", "Message", "error", "Error");
                if (result is not null && (result.Contains("fail", StringComparison.OrdinalIgnoreCase) ||
                    result.Contains("error", StringComparison.OrdinalIgnoreCase)))
                    return XScheduleAction.Fail(message ?? "xSchedule rechazó el comando.");
                _cachedAtUtc = DateTime.MinValue;
                return XScheduleAction.Success();
            }
            catch (Exception ex)
            {
                _cachedAtUtc = DateTime.MinValue;
                return XScheduleAction.Fail(FriendlyError(ex));
            }
        }
    }

    private JsonDocument GetJson(string path, string key1, string value1, string key2, string value2)
    {
        string query = $"{path}?{key1}={Uri.EscapeDataString(value1)}&{key2}={Uri.EscapeDataString(value2)}";
        using var response = _http.GetAsync(query).GetAwaiter().GetResult();
        string body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"xSchedule respondió {(int)response.StatusCode}.");
        try { return JsonDocument.Parse(body); }
        catch (JsonException) { throw new InvalidDataException("xSchedule respondió sin JSON válido."); }
    }

    private static string? FindString(JsonElement root, params string[] names)
    {
        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in root.EnumerateObject())
            {
                if (names.Any(n => n.Equals(property.Name, StringComparison.OrdinalIgnoreCase)))
                    return property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : property.Value.ToString();
            }
            foreach (var property in root.EnumerateObject())
            {
                var nested = FindString(property.Value, names);
                if (nested is not null) return nested;
            }
        }
        else if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
            {
                var nested = FindString(item, names);
                if (nested is not null) return nested;
            }
        }
        return null;
    }

    private static void CollectPlaylistNames(JsonElement value, ISet<string> names)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in value.EnumerateObject())
            {
                if (property.Name.Equals("playlists", StringComparison.OrdinalIgnoreCase) &&
                    property.Value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in property.Value.EnumerateArray())
                    {
                        string? name = FindString(item, "name", "Name");
                        if (!string.IsNullOrWhiteSpace(name)) names.Add(name);
                    }
                }
                else CollectPlaylistNames(property.Value, names);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray()) CollectPlaylistNames(item, names);
        }
    }

    private static void Validate(XScheduleSettings settings)
    {
        var uri = NormalizeBaseAddress(settings.BaseUrl);
        if (!IPAddress.TryParse(uri.Host, out var address) || !IPAddress.IsLoopback(address))
            throw new InvalidDataException("xSchedule debe controlarse por localhost.");
        if (settings.Shows.Select(s => s.Id).Any(string.IsNullOrWhiteSpace) ||
            settings.Shows.Select(s => s.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != settings.Shows.Count)
            throw new InvalidDataException("Los IDs de shows deben existir y ser únicos.");
        if (settings.Shows.Any(s => string.IsNullOrWhiteSpace(s.Playlist)))
            throw new InvalidDataException("Cada show necesita el nombre exacto de su playlist.");
        if (settings.Shows.Any(s => s.ClientVisible && string.IsNullOrWhiteSpace(s.PublicTitle)))
            throw new InvalidDataException("Cada experiencia visible al cliente necesita publicTitle.");
    }

    private static Uri NormalizeBaseAddress(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttp)
            throw new InvalidDataException("baseUrl debe ser una URL HTTP válida.");
        return new Uri(uri.AbsoluteUri.EndsWith('/') ? uri.AbsoluteUri : uri.AbsoluteUri + "/");
    }

    private static string ExpandPath(string path) => Environment.ExpandEnvironmentVariables(path.Replace('/', Path.DirectorySeparatorChar));
    private static string FriendlyError(Exception ex) => ex is TaskCanceledException
        ? "xSchedule no respondió a tiempo. Verifica que esté abierto y que su servidor web esté activo."
        : "No hay conexión con xSchedule: " + ex.GetBaseException().Message;

    public void Dispose() => _http.Dispose();
}
