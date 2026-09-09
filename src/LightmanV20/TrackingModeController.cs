using System.Text.Json;
using System.Text.RegularExpressions;

namespace LightmanZapravka3D;

internal sealed record TrackingModeDefinition(string Id, string Title, string Description, bool Enabled);

internal sealed class TrackingModeCatalog
{
    public int SchemaVersion { get; init; }
    public TrackingModeDefinition[] Modes { get; init; } = [];
}

internal sealed record TrackingControlSnapshot(
    int ApiVersion,
    long Revision,
    bool Selected,
    string DesiredMode);

internal sealed record TrackingStateSnapshot(
    bool Configured,
    bool Connected,
    string Status,
    string DesiredMode,
    string ActiveMode,
    long Revision,
    DateTimeOffset? LastSeenUtc,
    string Error,
    IReadOnlyList<TrackingModeDefinition> Modes);

internal sealed record TrackingStatusUpdate(
    string ClientId,
    long Revision,
    string ActiveMode,
    string Status,
    string Error);

internal readonly record struct TrackingModeSelectionResult(
    bool Ok,
    string Id,
    long Revision,
    string Error)
{
    public static TrackingModeSelectionResult Success(string id, long revision) =>
        new(true, id, revision, "");

    public static TrackingModeSelectionResult Fail(string error, long revision) =>
        new(false, "", revision, error);
}

internal readonly record struct TrackingStatusResult(bool Ok, long Revision, string Error)
{
    public static TrackingStatusResult Success(long revision) => new(true, revision, "");
    public static TrackingStatusResult Fail(string error, long revision) => new(false, revision, error);
}

internal sealed class TrackingModeController
{
    internal static readonly TimeSpan DefaultHeartbeatTimeout = TimeSpan.FromSeconds(2);

    private const int ApiVersion = 1;
    private const int MaximumCatalogBytes = 64 * 1024;
    private const int MaximumModes = 64;
    private const int MaximumTitleLength = 80;
    private const int MaximumDescriptionLength = 240;
    private const int MaximumErrorLength = 512;
    private static readonly Regex ModeIdPattern = new(
        "^[a-z0-9](?:[a-z0-9-]{0,62}[a-z0-9])?$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex ClientIdPattern = new(
        "^[a-z0-9](?:[a-z0-9._-]{0,62}[a-z0-9])?$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly HashSet<string> AllowedStatuses = new(
        ["starting", "running", "degraded", "error"], StringComparer.Ordinal);

    private readonly object _gate = new();
    private readonly TrackingModeDefinition[] _modes;
    private readonly IReadOnlyList<TrackingModeDefinition> _publicModes;
    private readonly Dictionary<string, TrackingModeDefinition> _modesById;
    private readonly TimeSpan _heartbeatTimeout;
    private readonly Func<DateTimeOffset> _utcNow;
    private long _revision;
    private string _desiredMode = "";
    private string _activeMode = "";
    private string _status = "offline";
    private string _error = "";
    private DateTimeOffset? _lastSeenUtc;

    internal TrackingModeController(
        IEnumerable<TrackingModeDefinition> modes,
        TimeSpan? heartbeatTimeout = null,
        Func<DateTimeOffset>? utcNow = null)
    {
        ArgumentNullException.ThrowIfNull(modes);
        _modes = ValidateAndCopyModes(modes);
        _publicModes = Array.AsReadOnly(_modes);
        _modesById = _modes.ToDictionary(mode => mode.Id, StringComparer.Ordinal);
        _heartbeatTimeout = heartbeatTimeout ?? DefaultHeartbeatTimeout;
        if (_heartbeatTimeout <= TimeSpan.Zero || _heartbeatTimeout > TimeSpan.FromMinutes(5))
            throw new ArgumentOutOfRangeException(nameof(heartbeatTimeout),
                "El vencimiento del heartbeat debe estar entre 1 tick y 5 minutos.");
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    internal static TrackingModeController Load(
        string catalogPath,
        TimeSpan? heartbeatTimeout = null,
        Func<DateTimeOffset>? utcNow = null)
    {
        if (string.IsNullOrWhiteSpace(catalogPath))
            throw new ArgumentException("Falta la ruta del catálogo de tracking.", nameof(catalogPath));

        var file = new FileInfo(catalogPath);
        if (!file.Exists) throw new FileNotFoundException("No existe el catálogo de tracking.", file.FullName);
        if (file.Length is <= 0 or > MaximumCatalogBytes)
            throw new InvalidDataException("El catálogo de tracking está vacío o supera 64 KiB.");

        TrackingModeCatalog catalog;
        try
        {
            using var stream = File.OpenRead(file.FullName);
            catalog = JsonSerializer.Deserialize<TrackingModeCatalog>(stream, LiveEngine.Json)
                ?? throw new InvalidDataException("El catálogo de tracking está vacío.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("El catálogo de tracking no contiene JSON válido.", ex);
        }

        if (catalog.SchemaVersion != ApiVersion)
            throw new InvalidDataException($"Versión de catálogo no compatible: {catalog.SchemaVersion}.");
        return new TrackingModeController(catalog.Modes, heartbeatTimeout, utcNow);
    }

    internal TrackingModeSelectionResult SelectMode(string? id)
    {
        if (!TryNormalizeModeId(id, out string normalized) ||
            !_modesById.TryGetValue(normalized, out var mode) || !mode.Enabled)
        {
            lock (_gate)
                return TrackingModeSelectionResult.Fail("Modo de tracking inválido o deshabilitado.", _revision);
        }

        lock (_gate)
        {
            if (string.Equals(_desiredMode, normalized, StringComparison.Ordinal))
                return TrackingModeSelectionResult.Success(normalized, _revision);

            checked { _revision++; }
            _desiredMode = normalized;
            _status = "starting";
            _error = "";
            return TrackingModeSelectionResult.Success(normalized, _revision);
        }
    }

    internal TrackingControlSnapshot GetControlSnapshot()
    {
        lock (_gate)
            return new TrackingControlSnapshot(ApiVersion, _revision, _desiredMode.Length > 0, _desiredMode);
    }

    internal TrackingStateSnapshot GetStateSnapshot()
    {
        lock (_gate)
        {
            DateTimeOffset now = UtcNow();
            bool connected = _lastSeenUtc.HasValue && now - _lastSeenUtc.Value <= _heartbeatTimeout;
            return new TrackingStateSnapshot(
                Configured: true,
                Connected: connected,
                Status: connected ? _status : "offline",
                DesiredMode: _desiredMode,
                ActiveMode: _activeMode,
                Revision: _revision,
                LastSeenUtc: _lastSeenUtc,
                Error: _error,
                Modes: _publicModes);
        }
    }

    internal TrackingStatusResult ReportStatus(TrackingStatusUpdate? update)
    {
        if (update is null)
        {
            lock (_gate) return TrackingStatusResult.Fail("Falta el estado de tracking.", _revision);
        }

        if (!IsValidClientId(update.ClientId))
        {
            lock (_gate) return TrackingStatusResult.Fail("clientId inválido.", _revision);
        }
        if (!AllowedStatuses.Contains(update.Status))
        {
            lock (_gate) return TrackingStatusResult.Fail("Estado de tracking inválido.", _revision);
        }
        if (update.Error is null || update.Error.Length > MaximumErrorLength ||
            update.Error.Any(character => char.IsControl(character) && character is not '\t'))
        {
            lock (_gate) return TrackingStatusResult.Fail("Detalle de error inválido.", _revision);
        }
        if (!TryNormalizeModeId(update.ActiveMode, out string activeMode) ||
            !_modesById.TryGetValue(activeMode, out var mode) || !mode.Enabled)
        {
            lock (_gate) return TrackingStatusResult.Fail("Modo activo inválido o deshabilitado.", _revision);
        }

        lock (_gate)
        {
            if (_desiredMode.Length == 0)
                return TrackingStatusResult.Fail("Todavía no hay un modo solicitado.", _revision);
            if (update.Revision != _revision)
                return TrackingStatusResult.Fail("La revisión confirmada no coincide con la revisión vigente.", _revision);
            if (!string.Equals(activeMode, _desiredMode, StringComparison.Ordinal))
                return TrackingStatusResult.Fail("El modo activo no coincide con el modo solicitado.", _revision);

            _activeMode = activeMode;
            _status = update.Status;
            _error = update.Error;
            _lastSeenUtc = UtcNow();
            return TrackingStatusResult.Success(_revision);
        }
    }

    private DateTimeOffset UtcNow() => _utcNow().ToUniversalTime();

    private static TrackingModeDefinition[] ValidateAndCopyModes(IEnumerable<TrackingModeDefinition> modes)
    {
        var copy = modes.ToArray();
        if (copy.Length is <= 0 or > MaximumModes)
            throw new InvalidDataException($"El catálogo debe contener entre 1 y {MaximumModes} modos.");

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < copy.Length; index++)
        {
            TrackingModeDefinition? mode = copy[index];
            if (mode is null)
                throw new InvalidDataException($"El modo {index + 1} es nulo.");
            if (!TryNormalizeModeId(mode.Id, out string normalized) ||
                !string.Equals(mode.Id, normalized, StringComparison.Ordinal))
                throw new InvalidDataException($"El ID del modo {index + 1} no es seguro ni estable.");
            if (!seen.Add(normalized))
                throw new InvalidDataException($"El ID de tracking '{normalized}' está duplicado.");
            if (string.IsNullOrWhiteSpace(mode.Title) || mode.Title.Length > MaximumTitleLength ||
                ContainsUnsafeTextControl(mode.Title))
                throw new InvalidDataException($"El título del modo '{normalized}' es inválido.");
            if (mode.Description is null || mode.Description.Length > MaximumDescriptionLength ||
                ContainsUnsafeTextControl(mode.Description))
                throw new InvalidDataException($"La descripción del modo '{normalized}' es inválida.");

            copy[index] = new TrackingModeDefinition(
                normalized,
                mode.Title.Trim(),
                mode.Description.Trim(),
                mode.Enabled);
        }
        return copy;
    }

    private static bool TryNormalizeModeId(string? id, out string normalized)
    {
        normalized = id?.Trim() ?? "";
        return normalized.Length > 0 && ModeIdPattern.IsMatch(normalized);
    }

    private static bool IsValidClientId(string? clientId) =>
        clientId is not null && ClientIdPattern.IsMatch(clientId);

    private static bool ContainsUnsafeTextControl(string value) =>
        value.Any(character => char.IsControl(character) && character is not '\t');
}
