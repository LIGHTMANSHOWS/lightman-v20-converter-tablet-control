using System.Net;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace LightmanZapravka3D;

internal sealed record XSchedulePlaylistCandidate(
    string Playlist,
    string StepName,
    string Sequence,
    string Audio,
    string SourceFolder);

internal sealed record XScheduleSyncIssue(string Playlist, string Reason);

internal sealed class XScheduleSyncReport
{
    public string SchedulePath { get; init; } = "";
    public bool RuntimeConnected { get; init; }
    public string RuntimeStatus { get; init; } = "Unavailable";
    public bool FileChanged { get; init; }
    public bool RuntimeReloaded { get; init; }
    public bool RestartRequired { get; init; }
    public bool SkippedBusy { get; init; }
    public bool WaitingForXSchedule { get; init; }
    public bool OutputToLightsPreserved { get; init; } = true;
    public string BackupPath { get; init; } = "";
    public IReadOnlyList<string> AddedPlaylists { get; init; } = [];
    public IReadOnlyList<string> ExistingPlaylists { get; init; } = [];
    public IReadOnlyList<XScheduleSyncIssue> InvalidCandidates { get; init; } = [];
    public IReadOnlyList<string> Errors { get; init; } = [];
    public string Message { get; init; } = "Sin comprobación de xSchedule.";

    public static XScheduleSyncReport NotRun(string schedulePath = "") => new()
    {
        SchedulePath = schedulePath,
        Message = "La sincronización automática de xSchedule no se ejecutó.",
    };
}

internal sealed record XScheduleSyncRuntimeState(
    bool Connected,
    string Status,
    bool? OutputToLights,
    IReadOnlySet<string> Playlists,
    string Error);

internal readonly record struct XScheduleSyncRuntimeAction(bool Ok, string Error)
{
    public static XScheduleSyncRuntimeAction Success() => new(true, "");
    public static XScheduleSyncRuntimeAction Fail(string error) => new(false, error);
}

internal interface IXScheduleSyncRuntime : IDisposable
{
    XScheduleSyncRuntimeState Probe();
    XScheduleSyncRuntimeAction SaveSchedule();
    XScheduleSyncRuntimeAction ReloadShowFolder(string showFolder);
    XScheduleSyncRuntimeAction RestoreOutputToLights(bool expected);
}

/// <summary>
/// Cliente restringido a localhost para las operaciones documentadas por xSchedule.
/// No inicia ni cierra procesos. La recarga se hace con "Change show folder".
/// </summary>
internal sealed class XScheduleHttpSyncRuntime : IXScheduleSyncRuntime
{
    private readonly HttpClient _http;

    public XScheduleHttpSyncRuntime(string baseUrl, int timeoutMs)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttp ||
            !IPAddress.TryParse(uri.Host, out var address) || !IPAddress.IsLoopback(address))
            throw new InvalidDataException("La API de xSchedule para sincronización debe usar localhost.");
        _http = new HttpClient
        {
            BaseAddress = new Uri(uri.AbsoluteUri.EndsWith('/') ? uri.AbsoluteUri : uri.AbsoluteUri + "/"),
            Timeout = TimeSpan.FromMilliseconds(Math.Clamp(timeoutMs, 300, 5000)),
        };
    }

    public XScheduleSyncRuntimeState Probe()
    {
        try
        {
            using var statusJson = GetJson("xScheduleQuery", "Query", "GetPlayingStatus", "Parameters", "");
            string status = FindString(statusJson.RootElement, "status", "Status") ?? "Unknown";
            bool? outputToLights = ParseBoolean(FindString(statusJson.RootElement, "outputtolights", "OutputToLights"));
            var playlists = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string playlistError = "";
            try
            {
                using var playlistsJson = GetJson("xScheduleQuery", "Query", "GetPlayLists", "Parameters", "");
                FindPlaylistNames(playlistsJson.RootElement, playlists);
            }
            catch (Exception ex)
            {
                playlistError = "No se pudo consultar GetPlayLists: " + FriendlyError(ex);
            }
            return new(true, status, outputToLights, playlists, playlistError);
        }
        catch (Exception ex)
        {
            return new(false, "Unavailable", null,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase), FriendlyError(ex));
        }
    }

    public XScheduleSyncRuntimeAction SaveSchedule() => Command("Save schedule", "");

    public XScheduleSyncRuntimeAction ReloadShowFolder(string showFolder) =>
        Command("Change show folder", showFolder);

    public XScheduleSyncRuntimeAction RestoreOutputToLights(bool expected)
    {
        var before = Probe();
        if (!before.Connected) return XScheduleSyncRuntimeAction.Fail(before.Error);
        if (before.OutputToLights is null)
            return XScheduleSyncRuntimeAction.Fail("xSchedule no informó el estado de Output to Lights.");
        if (before.OutputToLights.Value == expected)
            return XScheduleSyncRuntimeAction.Success();
        var toggled = Command("Toggle output to lights", "");
        if (!toggled.Ok) return toggled;
        var after = Probe();
        return after.Connected && after.OutputToLights == expected
            ? XScheduleSyncRuntimeAction.Success()
            : XScheduleSyncRuntimeAction.Fail("xSchedule no confirmó la restauración de Output to Lights.");
    }

    private XScheduleSyncRuntimeAction Command(string command, string parameters)
    {
        try
        {
            using var json = GetJson("xScheduleCommand", "Command", command, "Parameters", parameters);
            string? result = FindString(json.RootElement, "result", "Result", "status", "Status");
            string? message = FindString(json.RootElement, "message", "Message", "error", "Error");
            if (result is not null && (result.Contains("fail", StringComparison.OrdinalIgnoreCase) ||
                result.Contains("error", StringComparison.OrdinalIgnoreCase)))
                return XScheduleSyncRuntimeAction.Fail(message ?? $"xSchedule rechazó {command}.");
            return XScheduleSyncRuntimeAction.Success();
        }
        catch (Exception ex)
        {
            return XScheduleSyncRuntimeAction.Fail(FriendlyError(ex));
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

    private static void FindPlaylistNames(JsonElement value, ISet<string> names)
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
                else FindPlaylistNames(property.Value, names);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray()) FindPlaylistNames(item, names);
        }
    }

    private static string? FindString(JsonElement root, params string[] names)
    {
        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in root.EnumerateObject())
                if (names.Any(name => name.Equals(property.Name, StringComparison.OrdinalIgnoreCase)))
                    return property.Value.ValueKind == JsonValueKind.String
                        ? property.Value.GetString()
                        : property.Value.ToString();
            foreach (var property in root.EnumerateObject())
            {
                string? nested = FindString(property.Value, names);
                if (nested is not null) return nested;
            }
        }
        else if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
            {
                string? nested = FindString(item, names);
                if (nested is not null) return nested;
            }
        }
        return null;
    }

    private static bool? ParseBoolean(string? value)
    {
        if (bool.TryParse(value, out bool parsed)) return parsed;
        return value switch { "1" => true, "0" => false, _ => null };
    }

    private static string FriendlyError(Exception ex) => ex is TaskCanceledException
        ? "xSchedule no respondió a tiempo."
        : ex.GetBaseException().Message;

    public void Dispose() => _http.Dispose();
}

/// <summary>
/// Añade playlists nuevas sin reserializar los nodos ya existentes. Cada escritura
/// crea un respaldo exacto y usa un reemplazo atómico en el mismo volumen.
/// </summary>
internal static class XSchedulePlaylistSynchronizer
{
    private static readonly HashSet<string> AudioExtensions =
        new([".mp3", ".wav", ".ogg", ".flac", ".m4a", ".aac", ".wma"], StringComparer.OrdinalIgnoreCase);

    public static XScheduleSyncReport Synchronize(
        string showFolderRoot,
        string schedulePath,
        IEnumerable<XSchedulePlaylistCandidate> candidates,
        string baseUrl,
        int timeoutMs = 1200)
    {
        using var runtime = new XScheduleHttpSyncRuntime(baseUrl, timeoutMs);
        return Synchronize(showFolderRoot, schedulePath, candidates, runtime);
    }

    internal static XScheduleSyncReport Synchronize(
        string showFolderRoot,
        string schedulePath,
        IEnumerable<XSchedulePlaylistCandidate> candidates,
        IXScheduleSyncRuntime runtime)
    {
        string root;
        string schedule;
        try
        {
            root = CanonicalDirectory(showFolderRoot);
            schedule = Path.GetFullPath(Environment.ExpandEnvironmentVariables(schedulePath));
        }
        catch (Exception ex)
        {
            return Failure(schedulePath, "Rutas de sincronización inválidas: " + ex.Message);
        }

        FileStream operationLock;
        try
        {
            operationLock = new FileStream(schedule + ".lightman-auto.lock", FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            return Failure(schedule,
                "Otra instancia de V20 está comprobando xSchedule; esta importación se aplazó.");
        }
        catch (Exception ex)
        {
            return Failure(schedule, "No se pudo reservar la sincronización de xSchedule: " + ex.Message);
        }
        using var heldOperationLock = operationLock;

        var errors = new List<string>();
        var invalid = new List<XScheduleSyncIssue>();
        var existing = new List<string>();
        var valid = ValidateCandidates(root, candidates, invalid);
        if (!File.Exists(schedule))
            return Failure(schedule, "No existe xlights.xschedule; no se crea uno vacío porque se perderían sus opciones manuales.", invalid);

        ScheduleSnapshot snapshot;
        try { snapshot = ScheduleSnapshot.Read(schedule); }
        catch (Exception ex) { return Failure(schedule, "No se pudo validar xlights.xschedule: " + ex.Message, invalid); }

        var pending = Classify(snapshot, valid, existing);
        if (valid.Count == 0)
        {
            return new()
            {
                SchedulePath = schedule,
                ExistingPlaylists = existing,
                InvalidCandidates = invalid,
                Message = invalid.Count == 0
                    ? "No hay playlists nuevas para agregar a xSchedule."
                    : "No se agregó ninguna playlist; revisa las carpetas incompletas.",
            };
        }

        var runtimeState = runtime.Probe();
        bool runtimeNeedsReload = runtimeState.Connected &&
            valid.Any(candidate => !runtimeState.Playlists.Contains(candidate.Playlist));
        if (pending.Count == 0 && (!runtimeState.Connected || !runtimeNeedsReload))
        {
            return new()
            {
                SchedulePath = schedule,
                RuntimeConnected = runtimeState.Connected,
                RuntimeStatus = runtimeState.Status,
                RuntimeReloaded = runtimeState.Connected,
                ExistingPlaylists = existing,
                InvalidCandidates = invalid,
                Errors = string.IsNullOrWhiteSpace(runtimeState.Error) ? [] : [runtimeState.Error],
                Message = runtimeState.Connected
                    ? "xSchedule ya tiene cargadas todas las playlists válidas detectadas."
                    : "El archivo ya contiene todas las playlists válidas; xSchedule las cargará cuando se inicie.",
            };
        }
        if (!runtimeState.Connected)
        {
            return new()
            {
                SchedulePath = schedule,
                RuntimeStatus = runtimeState.Status,
                WaitingForXSchedule = true,
                RestartRequired = true,
                ExistingPlaylists = existing,
                InvalidCandidates = invalid,
                Errors = string.IsNullOrWhiteSpace(runtimeState.Error) ? [] : [runtimeState.Error],
                Message = "xSchedule no respondió; no se tocó su archivo. Ábrelo y vuelve a iniciar V20 para importar.",
            };
        }
        if (runtimeState.Connected && !runtimeState.Status.Equals("Idle", StringComparison.OrdinalIgnoreCase))
        {
            return new()
            {
                SchedulePath = schedule,
                RuntimeConnected = true,
                RuntimeStatus = runtimeState.Status,
                SkippedBusy = true,
                ExistingPlaylists = existing,
                InvalidCandidates = invalid,
                Errors = string.IsNullOrWhiteSpace(runtimeState.Error) ? [] : [runtimeState.Error],
                Message = $"xSchedule está {runtimeState.Status}; no se tocó el archivo. Se intentará en el próximo inicio.",
            };
        }
        if (runtimeState.OutputToLights is null)
        {
            return new()
            {
                SchedulePath = schedule,
                RuntimeConnected = true,
                RuntimeStatus = runtimeState.Status,
                ExistingPlaylists = existing,
                InvalidCandidates = invalid,
                Errors = ["xSchedule no informó el estado de Output to Lights."],
                OutputToLightsPreserved = false,
                Message = "No se tocó xlights.xschedule porque no se pudo comprobar Output to Lights.",
            };
        }

        bool? expectedOutputToLights = runtimeState.OutputToLights;
        if (runtimeState.Connected)
        {
            var saved = runtime.SaveSchedule();
            if (!saved.Ok)
            {
                return new()
                {
                    SchedulePath = schedule,
                    RuntimeConnected = true,
                    RuntimeStatus = runtimeState.Status,
                    ExistingPlaylists = existing,
                    InvalidCandidates = invalid,
                    Errors = ["No se guardó el estado actual de xSchedule: " + saved.Error],
                    Message = "No se tocó xlights.xschedule para proteger cambios manuales abiertos en xSchedule.",
                };
            }

            try { snapshot = ScheduleSnapshot.Read(schedule); }
            catch (Exception ex)
            {
                return Failure(schedule, "xSchedule guardó un archivo que no se pudo validar: " + ex.Message, invalid,
                    runtimeConnected: true, runtimeStatus: runtimeState.Status);
            }
            existing.Clear();
            pending = Classify(snapshot, valid, existing);
            var lastProbe = runtime.Probe();
            if (!lastProbe.Connected || !lastProbe.Status.Equals("Idle", StringComparison.OrdinalIgnoreCase))
            {
                return new()
                {
                    SchedulePath = schedule,
                    RuntimeConnected = lastProbe.Connected,
                    RuntimeStatus = lastProbe.Status,
                    SkippedBusy = lastProbe.Connected,
                    ExistingPlaylists = existing,
                    InvalidCandidates = invalid,
                    Errors = string.IsNullOrWhiteSpace(lastProbe.Error) ? [] : [lastProbe.Error],
                    Message = lastProbe.Connected
                        ? $"xSchedule cambió a {lastProbe.Status}; no se tocó el archivo."
                        : "Se perdió la conexión con xSchedule antes de escribir; no se tocó el archivo.",
                };
            }
            if (pending.Count == 0)
            {
                var reload = runtime.ReloadShowFolder(root);
                bool verified = false;
                if (!reload.Ok) errors.Add("No se pudo recargar xSchedule: " + reload.Error);
                for (int attempt = 0; attempt < 21; attempt++)
                {
                    var after = runtime.Probe();
                    if (after.Connected && valid.All(candidate => after.Playlists.Contains(candidate.Playlist)))
                    {
                        verified = true;
                        break;
                    }
                    if (attempt < 20) Thread.Sleep(250);
                }
                bool reloadOutputPreserved = true;
                if (expectedOutputToLights is bool expected)
                {
                    var restored = runtime.RestoreOutputToLights(expected);
                    reloadOutputPreserved = restored.Ok;
                    if (!restored.Ok) errors.Add("No se pudo conservar Output to Lights: " + restored.Error);
                }
                return new()
                {
                    SchedulePath = schedule,
                    RuntimeConnected = true,
                    RuntimeStatus = runtimeState.Status,
                    RuntimeReloaded = verified,
                    RestartRequired = !verified,
                    OutputToLightsPreserved = reloadOutputPreserved,
                    ExistingPlaylists = existing,
                    InvalidCandidates = invalid,
                    Errors = errors,
                    Message = verified
                        ? "xSchedule recargó las playlists que ya estaban guardadas en el archivo."
                        : "Las playlists están guardadas, pero xSchedule no confirmó la recarga; reinícialo manualmente.",
                };
            }
        }

        string updated;
        try { updated = snapshot.Append(pending); }
        catch (Exception ex) { return Failure(schedule, "No se pudo construir el XML nuevo: " + ex.Message, invalid); }

        string backupPath = "";
        try
        {
            using var guard = new FileStream(schedule, FileMode.Open, FileAccess.Read,
                FileShare.Read | FileShare.Delete);
            if (guard.Length > int.MaxValue) throw new IOException("xlights.xschedule es demasiado grande.");
            byte[] currentBytes = new byte[(int)guard.Length];
            guard.ReadExactly(currentBytes);
            if (!currentBytes.AsSpan().SequenceEqual(snapshot.OriginalBytes))
                throw new IOException("xlights.xschedule cambió mientras se preparaba la actualización; se canceló para no sobrescribirlo.");
            backupPath = AtomicReplaceWithBackup(schedule, updated, snapshot.HasUtf8Bom);
        }
        catch (Exception ex)
        {
            return Failure(schedule, "No se modificó xlights.xschedule: " + ex.Message, invalid,
                runtimeConnected: runtimeState.Connected, runtimeStatus: runtimeState.Status);
        }

        bool runtimeReloaded = false;
        bool restartRequired = !runtimeState.Connected;
        bool outputPreserved = true;
        if (runtimeState.Connected)
        {
            var reload = runtime.ReloadShowFolder(root);
            if (!reload.Ok) errors.Add("No se pudo recargar xSchedule: " + reload.Error);

            for (int attempt = 0; attempt < 21; attempt++)
            {
                var after = runtime.Probe();
                if (after.Connected && pending.All(candidate => after.Playlists.Contains(candidate.Playlist)))
                {
                    runtimeReloaded = true;
                    break;
                }
                if (attempt < 20) Thread.Sleep(250);
            }
            restartRequired = !runtimeReloaded;

            if (expectedOutputToLights is bool expected)
            {
                var restored = runtime.RestoreOutputToLights(expected);
                outputPreserved = restored.Ok;
                if (!restored.Ok) errors.Add("No se pudo conservar Output to Lights: " + restored.Error);
            }
        }

        string message = runtimeReloaded
            ? $"Se agregaron {pending.Count} playlists y xSchedule las recargó correctamente."
            : restartRequired
                ? $"Se agregaron {pending.Count} playlists. Reinicia xSchedule para que aparezcan."
                : $"Se agregaron {pending.Count} playlists.";
        if (invalid.Count > 0) message += $" {invalid.Count} carpetas incompletas se omitieron.";

        return new()
        {
            SchedulePath = schedule,
            RuntimeConnected = runtimeState.Connected,
            RuntimeStatus = runtimeState.Status,
            FileChanged = true,
            RuntimeReloaded = runtimeReloaded,
            RestartRequired = restartRequired,
            OutputToLightsPreserved = outputPreserved,
            BackupPath = backupPath,
            AddedPlaylists = pending.Select(candidate => candidate.Playlist).ToArray(),
            ExistingPlaylists = existing,
            InvalidCandidates = invalid,
            Errors = errors,
            Message = message,
        };
    }

    private static List<ValidatedCandidate> ValidateCandidates(
        string root,
        IEnumerable<XSchedulePlaylistCandidate> candidates,
        List<XScheduleSyncIssue> invalid)
    {
        var valid = new List<ValidatedCandidate>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sequences = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates ?? [])
        {
            string label = string.IsNullOrWhiteSpace(candidate.Playlist) ? "(sin nombre)" : candidate.Playlist.Trim();
            try
            {
                string playlist = VerifyText(label, "playlist");
                string step = VerifyText(string.IsNullOrWhiteSpace(candidate.StepName) ? playlist : candidate.StepName.Trim(), "paso");
                if (playlist.Length > 160 || step.Length > 160)
                    throw new InvalidDataException("el nombre supera 160 caracteres");
                string folder = CanonicalDirectory(candidate.SourceFolder);
                if (!Path.GetDirectoryName(folder)!.Equals(root, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("la carpeta no es hija directa de Show X Live");
                if (File.GetAttributes(folder).HasFlag(FileAttributes.ReparsePoint))
                    throw new InvalidDataException("la carpeta del show no puede ser un punto de redirección");
                string sequence = CanonicalFile(candidate.Sequence);
                string audio = CanonicalFile(candidate.Audio);
                if (!IsInside(sequence, folder) || !IsInside(audio, folder))
                    throw new InvalidDataException("FSEQ y audio deben estar dentro de su carpeta de show");
                if (!Path.GetExtension(sequence).Equals(".fseq", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("la secuencia no es un archivo FSEQ");
                if (!AudioExtensions.Contains(Path.GetExtension(audio)))
                    throw new InvalidDataException("el audio no tiene una extensión admitida");
                if (!File.Exists(sequence)) throw new FileNotFoundException("falta el FSEQ");
                if (!File.Exists(audio)) throw new FileNotFoundException("falta el audio");
                if (File.GetAttributes(sequence).HasFlag(FileAttributes.ReparsePoint) ||
                    File.GetAttributes(audio).HasFlag(FileAttributes.ReparsePoint))
                    throw new InvalidDataException("FSEQ y audio no pueden ser enlaces o puntos de redirección");
                if (!HasPseqHeader(sequence))
                    throw new InvalidDataException("el FSEQ no tiene una cabecera PSEQ válida");
                if (!names.Add(playlist)) throw new InvalidDataException("el lote contiene el nombre de playlist duplicado");
                if (!sequences.Add(sequence)) throw new InvalidDataException("el lote contiene el mismo FSEQ más de una vez");
                valid.Add(new(playlist, step, sequence, audio));
            }
            catch (Exception ex)
            {
                invalid.Add(new(label, ex.Message.TrimEnd('.')));
            }
        }
        return valid;
    }

    private static List<ValidatedCandidate> Classify(
        ScheduleSnapshot snapshot,
        IEnumerable<ValidatedCandidate> candidates,
        List<string> existing)
    {
        var pending = new List<ValidatedCandidate>();
        var pendingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pendingSequences = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            if (snapshot.PlaylistNames.Contains(candidate.Playlist) || snapshot.SequenceFiles.Contains(candidate.Sequence) ||
                !pendingNames.Add(candidate.Playlist) || !pendingSequences.Add(candidate.Sequence))
            {
                if (!existing.Contains(candidate.Playlist, StringComparer.OrdinalIgnoreCase)) existing.Add(candidate.Playlist);
                continue;
            }
            pending.Add(candidate);
        }
        return pending;
    }

    private static string AtomicReplaceWithBackup(string schedulePath, string updated, bool utf8Bom)
    {
        string directory = Path.GetDirectoryName(schedulePath)!;
        string backup = Path.Combine(directory,
            $"xlights.xschedule.backup-auto-{DateTime.Now:yyyyMMdd-HHmmssfff}");
        for (int suffix = 1; File.Exists(backup); suffix++)
            backup = Path.Combine(directory,
                $"xlights.xschedule.backup-auto-{DateTime.Now:yyyyMMdd-HHmmssfff}-{suffix}");
        string temp = Path.Combine(directory, $".xlights.xschedule.auto-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temp, updated, new UTF8Encoding(utf8Bom));
            _ = ScheduleSnapshot.Read(temp);
            File.Replace(temp, schedulePath, backup, ignoreMetadataErrors: true);
            return backup;
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
        }
    }

    private static XScheduleSyncReport Failure(
        string schedule,
        string error,
        IReadOnlyList<XScheduleSyncIssue>? invalid = null,
        bool runtimeConnected = false,
        string runtimeStatus = "Unavailable") => new()
    {
        SchedulePath = schedule,
        RuntimeConnected = runtimeConnected,
        RuntimeStatus = runtimeStatus,
        InvalidCandidates = invalid ?? [],
        Errors = [error],
        Message = error,
    };

    private static string VerifyText(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidDataException($"falta el nombre de {field}");
        try { return XmlConvert.VerifyXmlChars(value); }
        catch (XmlException) { throw new InvalidDataException($"el nombre de {field} contiene caracteres inválidos"); }
    }

    private static string CanonicalDirectory(string path)
    {
        string expanded = Environment.ExpandEnvironmentVariables(path.Replace('/', Path.DirectorySeparatorChar));
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(expanded));
        if (!Directory.Exists(full)) throw new DirectoryNotFoundException($"No existe la carpeta {full}");
        return full;
    }

    private static string CanonicalFile(string path) => Path.GetFullPath(
        Environment.ExpandEnvironmentVariables(path.Replace('/', Path.DirectorySeparatorChar)));

    private static bool HasPseqHeader(string path)
    {
        Span<byte> header = stackalloc byte[8];
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        if (stream.Length < 32 || stream.Read(header) != header.Length ||
            !header[..4].SequenceEqual("PSEQ"u8)) return false;
        int dataOffset = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(header[4..6]);
        return dataOffset >= 8 && dataOffset < stream.Length;
    }

    private static bool IsInside(string path, string directory) => path.StartsWith(
        Path.TrimEndingDirectorySeparator(directory) + Path.DirectorySeparatorChar,
        StringComparison.OrdinalIgnoreCase);

    private sealed record ValidatedCandidate(string Playlist, string StepName, string Sequence, string Audio);

    private sealed class ScheduleSnapshot
    {
        private readonly string _text;
        private readonly string _newline;
        internal byte[] OriginalBytes { get; }
        internal bool HasUtf8Bom { get; }
        internal HashSet<string> PlaylistNames { get; }
        internal HashSet<string> SequenceFiles { get; }

        private ScheduleSnapshot(byte[] bytes, bool bom, string text, string newline,
            HashSet<string> playlistNames, HashSet<string> sequenceFiles)
        {
            OriginalBytes = bytes;
            HasUtf8Bom = bom;
            _text = text;
            _newline = newline;
            PlaylistNames = playlistNames;
            SequenceFiles = sequenceFiles;
        }

        internal static ScheduleSnapshot Read(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            bool bom = bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble);
            ReadOnlySpan<byte> payload = bom ? bytes.AsSpan(Encoding.UTF8.Preamble.Length) : bytes;
            string text = new UTF8Encoding(false, true).GetString(payload);
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            XDocument document;
            using (var reader = XmlReader.Create(new StringReader(text), settings))
                document = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
            if (document.Root is null || !document.Root.Name.LocalName.Equals("xSchedule", StringComparison.Ordinal))
                throw new InvalidDataException("la raíz XML no es xSchedule");

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var sequences = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var playlist in document.Root.Elements().Where(e => e.Name.LocalName == "PlayList"))
            {
                string? name = playlist.Attributes().FirstOrDefault(a => a.Name.LocalName == "Name")?.Value;
                if (!string.IsNullOrWhiteSpace(name)) names.Add(name);
                foreach (var item in playlist.Descendants().Where(e => e.Name.LocalName == "PLIFSEQ"))
                {
                    string? sequence = item.Attributes().FirstOrDefault(a => a.Name.LocalName == "FSEQFile")?.Value;
                    if (string.IsNullOrWhiteSpace(sequence)) continue;
                    try { sequences.Add(CanonicalFile(sequence)); } catch { }
                }
            }
            return new(bytes, bom, text, text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n", names, sequences);
        }

        internal string Append(IReadOnlyList<ValidatedCandidate> candidates)
        {
            int closing = _text.LastIndexOf("</xSchedule>", StringComparison.OrdinalIgnoreCase);
            if (closing < 0) throw new InvalidDataException("falta el cierre de xSchedule");
            string prefix = _text[..closing];
            string suffix = _text[closing..];
            var result = new StringBuilder(_text.Length + candidates.Count * 600);
            result.Append(prefix);
            if (!prefix.EndsWith(_newline, StringComparison.Ordinal)) result.Append(_newline);
            foreach (var candidate in candidates)
            {
                string fragment = BuildPlaylistFragment(candidate, _newline);
                result.Append("  ").Append(fragment.Replace(_newline, _newline + "  ", StringComparison.Ordinal));
                result.Append(_newline);
            }
            result.Append(suffix);
            return result.ToString();
        }

        private static string BuildPlaylistFragment(ValidatedCandidate candidate, string newline)
        {
            var builder = new StringBuilder();
            var settings = new XmlWriterSettings
            {
                OmitXmlDeclaration = true,
                ConformanceLevel = ConformanceLevel.Fragment,
                Indent = true,
                IndentChars = "  ",
                NewLineChars = newline,
                NewLineHandling = NewLineHandling.Replace,
            };
            using (var writer = XmlWriter.Create(builder, settings))
            {
                writer.WriteStartElement("PlayList");
                writer.WriteAttributeString("Name", candidate.Playlist);
                writer.WriteStartElement("PlayListStep");
                writer.WriteAttributeString("Name", candidate.StepName);
                writer.WriteStartElement("PLIFSEQ");
                writer.WriteAttributeString("FSEQFile", candidate.Sequence);
                writer.WriteAttributeString("AudioFile", candidate.Audio);
                writer.WriteAttributeString("AudioDevice", "");
                writer.WriteAttributeString("ApplyMethod", "0");
                writer.WriteAttributeString("StartChannel", "1");
                writer.WriteAttributeString("Channels", "0");
                writer.WriteAttributeString("Delay", "0");
                writer.WriteAttributeString("Name", candidate.StepName);
                writer.WriteEndElement();
                writer.WriteEndElement();
                writer.WriteEndElement();
            }
            return builder.ToString();
        }
    }
}
