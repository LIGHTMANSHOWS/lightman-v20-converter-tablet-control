using System.Text;
using System.Xml.Linq;

namespace LightmanZapravka3D;

internal static class XSchedulePlaylistSynchronizerSelfTest
{
    private static readonly List<string> Results = [];

    internal static void Run()
    {
        Results.Clear();
        string root = Path.Combine(Path.GetTempPath(), "lightman-xschedule-sync-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            OfflineRuntimeNeverTouchesSchedule(root);
            IdleAppendIsBytePreservingAndIdempotent(root);
            IncompleteAndOutsideFoldersAreRejected(root);
            BusyRuntimeNeverTouchesSchedule(root, "Playing");
            BusyRuntimeNeverTouchesSchedule(root, "Paused");
            UnknownOutputStateNeverTouchesSchedule(root);
            IdleRuntimeSavesReloadsVerifiesAndPreservesOutput(root);
            IdleRuntimeReloadsPlaylistAlreadyOnDisk(root);
            ConcurrentInstancesNeverOverwriteEachOther(root);
            DuplicateManualPlaylistIsNeverOverwritten(root);
            ReloadFailureKeepsBackupAndRequestsRestart(root);
            File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "SELFTEST-XSCHEDULE-SYNC.txt"), Results);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    private static void OfflineRuntimeNeverTouchesSchedule(string root)
    {
        string test = NewCase(root, "offline");
        string schedule = WriteSchedule(test);
        byte[] original = File.ReadAllBytes(schedule);
        var candidate = CreateCandidate(test, "14_NUEVO_SHOW", "14 · NUEVO SHOW");
        using var offline = FakeRuntime.Offline();

        var report = XSchedulePlaylistSynchronizer.Synchronize(test, schedule, [candidate], offline);
        Check(report.WaitingForXSchedule && report.RestartRequired && !report.FileChanged &&
              string.IsNullOrEmpty(report.BackupPath) && File.ReadAllBytes(schedule).SequenceEqual(original),
            "API inaccesible difiere la importación y no toca xlights.xschedule");
    }

    private static void IdleAppendIsBytePreservingAndIdempotent(string root)
    {
        string test = NewCase(root, "idle-byte-preserving");
        string schedule = WriteSchedule(test);
        string original = File.ReadAllText(schedule);
        string manualBlock = ManualBlock();
        var candidate = CreateCandidate(test, "14_NUEVO_SHOW", "14 · NUEVO SHOW");
        using var runtime = new FakeRuntime("Idle", outputToLights: true);

        var first = XSchedulePlaylistSynchronizer.Synchronize(test, schedule, [candidate], runtime);
        Check(first.FileChanged && first.RuntimeReloaded && !first.RestartRequired &&
              first.AddedPlaylists.SequenceEqual(["14 · NUEVO SHOW"]),
            "Idle agrega una playlist válida y confirma su recarga en xSchedule");
        Check(File.Exists(first.BackupPath) && File.ReadAllText(first.BackupPath) == original,
            "el respaldo automático es una copia exacta del schedule previo");
        string updated = File.ReadAllText(schedule);
        Check(updated.Contains(manualBlock, StringComparison.Ordinal),
            "el bloque de playlist manual se conserva byte por byte");
        var document = XDocument.Load(schedule);
        var playlists = document.Root!.Elements("PlayList").ToArray();
        Check(playlists.Length == 2 && playlists[1].Attribute("Name")?.Value == "14 · NUEVO SHOW" &&
              playlists[1].Descendants("PLIFSEQ").Single().Attribute("FSEQFile")?.Value == candidate.Sequence,
            "el nodo nuevo usa el formato PLIFSEQ y las rutas esperadas");

        byte[] beforeSecondRun = File.ReadAllBytes(schedule);
        var second = XSchedulePlaylistSynchronizer.Synchronize(test, schedule, [candidate], runtime);
        Check(!second.FileChanged && second.ExistingPlaylists.SequenceEqual(["14 · NUEVO SHOW"]) &&
              File.ReadAllBytes(schedule).SequenceEqual(beforeSecondRun),
            "una segunda ejecución es idempotente y no vuelve a escribir el XML");
    }

    private static void IncompleteAndOutsideFoldersAreRejected(string root)
    {
        string test = NewCase(root, "invalid");
        string schedule = WriteSchedule(test);
        byte[] original = File.ReadAllBytes(schedule);
        string missingFolder = Path.Combine(test, "15_INCOMPLETO");
        Directory.CreateDirectory(missingFolder);
        string sequence = Path.Combine(missingFolder, "show.fseq");
        WriteValidFseq(sequence);
        var missingAudio = new XSchedulePlaylistCandidate("15 · INCOMPLETO", "INCOMPLETO", sequence,
            Path.Combine(missingFolder, "audio.mp3"), missingFolder);

        string nestedParent = Path.Combine(test, "contenedor");
        string nestedFolder = Path.Combine(nestedParent, "16_FUERA");
        Directory.CreateDirectory(nestedFolder);
        string nestedSequence = Path.Combine(nestedFolder, "show.fseq");
        string nestedAudio = Path.Combine(nestedFolder, "audio.mp3");
        File.WriteAllBytes(nestedSequence, [1]);
        File.WriteAllBytes(nestedAudio, [2]);
        var outside = new XSchedulePlaylistCandidate("16 · FUERA", "FUERA", nestedSequence, nestedAudio, nestedFolder);

        string corruptFolder = Path.Combine(test, "17_CORRUPTO");
        Directory.CreateDirectory(corruptFolder);
        string corruptSequence = Path.Combine(corruptFolder, "show.fseq");
        string corruptAudio = Path.Combine(corruptFolder, "audio.wav");
        File.WriteAllBytes(corruptSequence, Encoding.ASCII.GetBytes("PSEQ\x20\0\x02\x02"));
        File.WriteAllBytes(corruptAudio, [1]);
        var corrupt = new XSchedulePlaylistCandidate(
            "17 · CORRUPTO", "CORRUPTO", corruptSequence, corruptAudio, corruptFolder);
        using var offline = FakeRuntime.Offline();

        var report = XSchedulePlaylistSynchronizer.Synchronize(test, schedule, [missingAudio, outside, corrupt], offline);
        Check(!report.FileChanged && report.InvalidCandidates.Count == 3 &&
              File.ReadAllBytes(schedule).SequenceEqual(original),
            "FSEQ sin audio, carpeta fuera de raíz y FSEQ PSEQ truncado se omiten sin tocar el schedule");
    }

    private static void BusyRuntimeNeverTouchesSchedule(string root, string status)
    {
        string test = NewCase(root, "busy-" + status);
        string schedule = WriteSchedule(test);
        byte[] original = File.ReadAllBytes(schedule);
        var candidate = CreateCandidate(test, "17_BUSY", "17 · BUSY");
        using var runtime = new FakeRuntime(status, outputToLights: true);

        var report = XSchedulePlaylistSynchronizer.Synchronize(test, schedule, [candidate], runtime);
        Check(report.SkippedBusy && !report.FileChanged && runtime.SaveCalls == 0 && runtime.ReloadCalls == 0 &&
              File.ReadAllBytes(schedule).SequenceEqual(original),
            $"estado {status} no guarda, recarga ni modifica xlights.xschedule");
    }

    private static void UnknownOutputStateNeverTouchesSchedule(string root)
    {
        string test = NewCase(root, "unknown-output-state");
        string schedule = WriteSchedule(test);
        byte[] original = File.ReadAllBytes(schedule);
        var candidate = CreateCandidate(test, "18_UNKNOWN_OUTPUT", "18 · UNKNOWN OUTPUT");
        using var runtime = new FakeRuntime("Idle", outputToLights: null);

        var report = XSchedulePlaylistSynchronizer.Synchronize(test, schedule, [candidate], runtime);
        Check(!report.FileChanged && !report.OutputToLightsPreserved && report.Errors.Count == 1 &&
              runtime.SaveCalls == 0 && runtime.ReloadCalls == 0 &&
              File.ReadAllBytes(schedule).SequenceEqual(original),
            "Output to Lights desconocido cancela la importación sin tocar el schedule");
    }

    private static void IdleRuntimeSavesReloadsVerifiesAndPreservesOutput(string root)
    {
        string test = NewCase(root, "idle");
        string schedule = WriteSchedule(test);
        var candidate = CreateCandidate(test, "18_IDLE", "18 · IDLE");
        using var runtime = new FakeRuntime("Idle", outputToLights: true);

        var report = XSchedulePlaylistSynchronizer.Synchronize(test, schedule, [candidate], runtime);
        Check(report.FileChanged && report.RuntimeReloaded && !report.RestartRequired &&
              runtime.SaveCalls == 1 && runtime.ReloadCalls == 1 && runtime.LastReloadFolder == test,
            "runtime idle guarda, añade, recarga la carpeta correcta y verifica la playlist");
        Check(runtime.RestoreCalls == 1 && runtime.LastExpectedOutputToLights && report.OutputToLightsPreserved,
            "la recarga conserva exactamente el estado previo de Output to Lights");
    }

    private static void DuplicateManualPlaylistIsNeverOverwritten(string root)
    {
        string test = NewCase(root, "duplicate");
        string schedule = WriteSchedule(test);
        byte[] original = File.ReadAllBytes(schedule);
        var duplicate = CreateCandidate(test, "19_DUPLICATE", "MANUAL · NO TOCAR");
        using var offline = FakeRuntime.Offline();

        var report = XSchedulePlaylistSynchronizer.Synchronize(test, schedule, [duplicate], offline);
        Check(!report.FileChanged && report.ExistingPlaylists.Contains("MANUAL · NO TOCAR") &&
              File.ReadAllBytes(schedule).SequenceEqual(original),
            "un nombre de playlist manual existente nunca se sobrescribe");
    }

    private static void IdleRuntimeReloadsPlaylistAlreadyOnDisk(string root)
    {
        string test = NewCase(root, "reload-existing");
        string schedule = WriteSchedule(test);
        byte[] original = File.ReadAllBytes(schedule);
        var candidate = CreateCandidate(test, "19_RUNTIME_MISSING", "MANUAL · NO TOCAR");
        using var runtime = new FakeRuntime("Idle", outputToLights: false);

        var report = XSchedulePlaylistSynchronizer.Synchronize(test, schedule, [candidate], runtime);
        Check(!report.FileChanged && report.RuntimeReloaded && !report.RestartRequired &&
              runtime.SaveCalls == 1 && runtime.ReloadCalls == 1 &&
              File.ReadAllBytes(schedule).SequenceEqual(original),
            "GetPlayLists detecta una playlist guardada pero ausente del runtime y fuerza recarga sin reescribirla");
    }

    private static void ConcurrentInstancesNeverOverwriteEachOther(string root)
    {
        string test = NewCase(root, "concurrent");
        string schedule = WriteSchedule(test);
        var firstCandidate = CreateCandidate(test, "21_FIRST", "21 · FIRST");
        var secondCandidate = CreateCandidate(test, "22_SECOND", "22 · SECOND");
        using var saveEntered = new ManualResetEventSlim(false);
        using var continueSave = new ManualResetEventSlim(false);
        using var firstRuntime = new FakeRuntime("Idle", outputToLights: true)
        {
            SaveEntered = saveEntered,
            ContinueSave = continueSave,
        };
        using var secondRuntime = new FakeRuntime("Idle", outputToLights: true);

        var firstTask = Task.Run(() => XSchedulePlaylistSynchronizer.Synchronize(
            test, schedule, [firstCandidate], firstRuntime));
        if (!saveEntered.Wait(TimeSpan.FromSeconds(3)))
            throw new InvalidOperationException("la primera sincronización no alcanzó Save schedule");
        var concurrent = XSchedulePlaylistSynchronizer.Synchronize(
            test, schedule, [secondCandidate], secondRuntime);
        continueSave.Set();
        var first = firstTask.GetAwaiter().GetResult();

        Check(first.FileChanged && !concurrent.FileChanged &&
              concurrent.Errors.Any(error => error.Contains("Otra instancia", StringComparison.OrdinalIgnoreCase)),
            "dos V20 simultáneos no pueden reemplazar el mismo schedule");

        using var retryRuntime = new FakeRuntime("Idle", outputToLights: true);
        var retry = XSchedulePlaylistSynchronizer.Synchronize(test, schedule, [secondCandidate], retryRuntime);
        var names = XDocument.Load(schedule).Root!.Elements("PlayList")
            .Select(node => node.Attribute("Name")?.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Check(retry.FileChanged && names.Contains(firstCandidate.Playlist) && names.Contains(secondCandidate.Playlist),
            "la importación aplazada se agrega después sin perder la primera playlist");
    }

    private static void ReloadFailureKeepsBackupAndRequestsRestart(string root)
    {
        string test = NewCase(root, "reload-failure");
        string schedule = WriteSchedule(test);
        var candidate = CreateCandidate(test, "20_RELOAD", "20 · RELOAD");
        using var runtime = new FakeRuntime("Idle", outputToLights: false) { FailReload = true };

        var report = XSchedulePlaylistSynchronizer.Synchronize(test, schedule, [candidate], runtime);
        Check(report.FileChanged && report.RestartRequired && !report.RuntimeReloaded &&
              File.Exists(report.BackupPath) && report.Errors.Any(error => error.Contains("recargar", StringComparison.OrdinalIgnoreCase)),
            "si la API no recarga ni puede verificar, no se reproduce nada y se solicita reinicio manual");
    }

    private static string NewCase(string root, string name)
    {
        string path = Path.Combine(root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static string WriteSchedule(string root)
    {
        string path = Path.Combine(root, "xlights.xschedule");
        File.WriteAllText(path,
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\r\n" +
            "<xSchedule>\r\n" +
            "  <Options WebServerPort=\"80\"><!-- CONFIG MANUAL INTACTA --></Options>\r\n" +
            ManualBlock() + "\r\n" +
            "</xSchedule>\r\n", new UTF8Encoding(false));
        return path;
    }

    private static string ManualBlock() =>
        "  <PlayList Name=\"MANUAL · NO TOCAR\">\r\n" +
        "    <PlayListStep Name=\"Paso manual\">\r\n" +
        "      <PLIText Text=\"Conservar espacios &amp; atributos\" Name=\"Manual\"/>\r\n" +
        "    </PlayListStep>\r\n" +
        "  </PlayList>";

    private static XSchedulePlaylistCandidate CreateCandidate(string root, string folderName, string playlist)
    {
        string folder = Path.Combine(root, folderName);
        Directory.CreateDirectory(folder);
        string sequence = Path.Combine(folder, folderName + ".fseq");
        string audio = Path.Combine(folder, "Audio canción & prueba.mp3");
        WriteValidFseq(sequence);
        File.WriteAllBytes(audio, [4, 5, 6]);
        return new(playlist, folderName.Replace('_', ' '), sequence, audio, folder);
    }

    private static void WriteValidFseq(string path)
    {
        byte[] bytes = new byte[64];
        Encoding.ASCII.GetBytes("PSEQ").CopyTo(bytes, 0);
        bytes[4] = 32;
        bytes[6] = 2;
        bytes[7] = 2;
        File.WriteAllBytes(path, bytes);
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
        Results.Add("PASS · " + description);
    }

    private sealed class FakeRuntime : IXScheduleSyncRuntime
    {
        private readonly HashSet<string> _playlists = new(StringComparer.OrdinalIgnoreCase);
        private readonly string _status;
        private readonly bool? _outputToLights;
        internal int SaveCalls { get; private set; }
        internal int ReloadCalls { get; private set; }
        internal int RestoreCalls { get; private set; }
        internal string LastReloadFolder { get; private set; } = "";
        internal bool LastExpectedOutputToLights { get; private set; }
        internal bool FailReload { get; init; }
        internal ManualResetEventSlim? SaveEntered { get; init; }
        internal ManualResetEventSlim? ContinueSave { get; init; }
        private bool _offline;

        internal FakeRuntime(string status, bool? outputToLights)
        {
            _status = status;
            _outputToLights = outputToLights;
        }

        internal static FakeRuntime Offline() => new("Unavailable", false) { _offline = true };

        public XScheduleSyncRuntimeState Probe() => _offline
            ? new(false, "Unavailable", null, _playlists, "offline")
            : new(true, _status, _outputToLights, _playlists, "");

        public XScheduleSyncRuntimeAction SaveSchedule()
        {
            SaveCalls++;
            SaveEntered?.Set();
            ContinueSave?.Wait(TimeSpan.FromSeconds(5));
            return XScheduleSyncRuntimeAction.Success();
        }

        public XScheduleSyncRuntimeAction ReloadShowFolder(string showFolder)
        {
            ReloadCalls++;
            LastReloadFolder = showFolder;
            if (FailReload) return XScheduleSyncRuntimeAction.Fail("fallo simulado");
            var document = XDocument.Load(Path.Combine(showFolder, "xlights.xschedule"));
            foreach (var playlist in document.Root!.Elements("PlayList"))
            {
                string? name = playlist.Attribute("Name")?.Value;
                if (!string.IsNullOrWhiteSpace(name)) _playlists.Add(name);
            }
            return XScheduleSyncRuntimeAction.Success();
        }

        public XScheduleSyncRuntimeAction RestoreOutputToLights(bool expected)
        {
            RestoreCalls++;
            LastExpectedOutputToLights = expected;
            return XScheduleSyncRuntimeAction.Success();
        }

        public void Dispose() { }
    }
}
