using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace LightmanZapravka3D;

internal sealed record AutoDiscoveryReport(
    string Root,
    int ScannedFolders,
    int Added,
    int Existing,
    int Incomplete,
    int Errors,
    bool Persisted,
    string Message,
    IReadOnlyList<string> AddedShows)
{
    public static AutoDiscoveryReport Disabled(string root) => new(
        root, 0, 0, 0, 0, 0, true,
        "Descubrimiento automático desactivado.", []);
}

internal sealed class AutoDiscoveredShowCatalog
{
    public int SchemaVersion { get; init; } = 1;
    public List<ShowLaunchDefinition> Shows { get; init; } = [];
}

/// <summary>
/// Descubre Show Folders únicamente para el panel interno. El archivo manual
/// shows.json nunca se reescribe: los hallazgos se guardan en un catálogo lateral.
/// </summary>
internal static partial class ShowFolderDiscovery
{
    private const string AutoCatalogFileName = "shows.autodiscovered.json";
    private static readonly HashSet<string> AudioExtensions = new(
        [".mp3", ".wav", ".flac", ".m4a", ".aac", ".ogg", ".wma"],
        StringComparer.OrdinalIgnoreCase);
    private static readonly string[] ArchiveDirectoryNames =
        ["fuente_original", "fuente original", "backup", "backups", "respaldo", "respaldos", "source",
         "validacion", "validación", "_validacion", "_validación"];

    internal static AutoDiscoveryReport Apply(XScheduleSettings settings, string configurationPath)
    {
        string root;
        try { root = ExpandPath(settings.ShowFolderRoot); }
        catch
        {
            return new(settings.ShowFolderRoot, 0, 0, 0, 0, 1, false,
                "La ruta configurada para los shows no es válida.", []);
        }
        if (!settings.AutoDiscoverShows) return AutoDiscoveryReport.Disabled(root);

        string? configurationDirectory = Path.GetDirectoryName(Path.GetFullPath(configurationPath));
        if (configurationDirectory is null)
            return new(root, 0, 0, 0, 0, 1, false,
                "No se pudo resolver la carpeta de configuración.", []);

        string autoCatalogPath = Path.Combine(configurationDirectory, AutoCatalogFileName);
        int errors = 0;
        var addedIds = new List<string>();
        var generated = LoadPersisted(autoCatalogPath, root, ref errors);

        var usedIds = new HashSet<string>(settings.Shows.Select(show => show.Id), StringComparer.OrdinalIgnoreCase);
        var usedPlaylists = new HashSet<string>(settings.Shows.Select(show => show.Playlist), StringComparer.OrdinalIgnoreCase);
        var representedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var show in settings.Shows)
        {
            string? folder = ResolveTopLevelShowFolder(root, show.SourceFolder, show.Sequence);
            if (folder is not null) representedFolders.Add(folder);
        }

        // El catálogo manual siempre gana ante cualquier colisión del lateral.
        var acceptedGenerated = new List<ShowLaunchDefinition>();
        foreach (var storedShow in generated)
        {
            var safe = MakePersistedEntrySafe(storedShow);
            string? folder = ResolveTopLevelShowFolder(root, safe.SourceFolder, safe.Sequence);
            if (folder is null)
            {
                errors++;
                continue;
            }
            // Una definición manual que usa el mismo ID, playlist o carpeta es una
            // promoción deliberada, no un error. Se descarta la copia automática.
            if (usedIds.Contains(safe.Id) || usedPlaylists.Contains(safe.Playlist) || representedFolders.Contains(folder))
                continue;
            usedIds.Add(safe.Id);
            usedPlaylists.Add(safe.Playlist);
            representedFolders.Add(folder);
            acceptedGenerated.Add(safe);
        }
        settings.Shows.AddRange(acceptedGenerated);

        int scanned = 0;
        int existing = 0;
        int incomplete = 0;
        int nextNumber = NextPlaylistNumber(settings.Shows);

        if (!Directory.Exists(root))
        {
            errors++;
            return new(root, 0, 0, acceptedGenerated.Count, 0, errors, false,
                "La carpeta de shows no existe; se conservó el catálogo interno previamente detectado.", []);
        }

        IReadOnlyList<string> folders;
        try
        {
            folders = Directory.EnumerateDirectories(root)
                .Where(path => !IsReparsePoint(path))
                .Select(Path.GetFullPath)
                .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch
        {
            errors++;
            return new(root, 0, 0, acceptedGenerated.Count, 0, errors, false,
                "No se pudo leer la carpeta de shows; se conservó el catálogo interno previamente detectado.", []);
        }

        foreach (string folder in folders)
        {
            scanned++;
            string normalizedFolder = NormalizeDirectory(folder);
            if (representedFolders.Contains(normalizedFolder))
            {
                existing++;
                continue;
            }

            try
            {
                var files = EnumerateFilesWithoutFollowingLinks(folder)
                    .Where(path => !HasArchiveDirectory(Path.GetRelativePath(folder, path)))
                    .ToArray();
                string? sequence = SelectSequence(folder, files);
                string? audio = SelectAudio(folder, files, sequence);
                if (sequence is null || audio is null)
                {
                    incomplete++;
                    continue;
                }

                string folderName = Path.GetFileName(folder);
                int requestedNumber = ReadLeadingNumber(folderName);
                while (PlaylistNumberInUse(usedPlaylists, nextNumber)) nextNumber++;
                int playlistNumber = requestedNumber > 0 && !PlaylistNumberInUse(usedPlaylists, requestedNumber)
                    ? requestedNumber
                    : nextNumber++;
                nextNumber = Math.Max(nextNumber, playlistNumber + 1);
                string title = DisplayTitle(folderName);
                string id = UniqueId("auto-" + Slug(title), usedIds);
                string playlist = $"{playlistNumber:00} · {title}";
                while (!usedPlaylists.Add(playlist))
                    playlist = $"{nextNumber++:00} · {title}";

                var show = new ShowLaunchDefinition
                {
                    Id = id,
                    Title = title,
                    PublicTitle = "",
                    Category = "Pruebas internas",
                    Tagline = "",
                    ClientVisible = false,
                    Playlist = playlist,
                    Sequence = CompactPath(sequence),
                    Audio = CompactPath(audio),
                    Enabled = true,
                    ClientEnabled = false,
                    Note = "AUTOIMPORTADO PARA PRUEBA INTERNA: validar sincronía, render y salida física antes de publicar.",
                    AutoDiscovered = true,
                    SourceFolder = CompactPath(folder),
                };
                settings.Shows.Add(show);
                acceptedGenerated.Add(show);
                representedFolders.Add(normalizedFolder);
                addedIds.Add(show.Id);
            }
            catch
            {
                errors++;
            }
        }

        bool persisted = Persist(autoCatalogPath, acceptedGenerated, ref errors);
        string message = addedIds.Count > 0
            ? $"Se agregaron {addedIds.Count} show(s) nuevos sólo al control interno."
            : "No se encontraron shows nuevos listos para importar.";
        if (incomplete > 0) message += $" {incomplete} carpeta(s) aún no tienen FSEQ y audio.";
        if (!persisted) message += " El catálogo quedó sólo en memoria porque no pudo guardarse.";

        return new(root, scanned, addedIds.Count, existing, incomplete, errors, persisted, message, addedIds);
    }

    private static List<ShowLaunchDefinition> LoadPersisted(string path, string root, ref int errors)
    {
        if (!File.Exists(path)) return [];
        try
        {
            var catalog = JsonSerializer.Deserialize<AutoDiscoveredShowCatalog>(File.ReadAllText(path), LiveEngine.Json);
            if (catalog is null || catalog.SchemaVersion != 1) throw new InvalidDataException("Catálogo automático incompatible.");
            var accepted = new List<ShowLaunchDefinition>();
            foreach (var show in catalog.Shows)
            {
                string sequence = ExpandPath(show.Sequence);
                string audio = ExpandPath(show.Audio);
                bool valid = show.AutoDiscovered && !string.IsNullOrWhiteSpace(show.Id) &&
                    !string.IsNullOrWhiteSpace(show.Title) && !string.IsNullOrWhiteSpace(show.Playlist) &&
                    ResolveTopLevelShowFolder(root, show.SourceFolder, show.Sequence) is not null &&
                    HasValidFseqHeader(sequence) && File.Exists(audio) && AudioExtensions.Contains(Path.GetExtension(audio));
                if (valid) accepted.Add(show);
                else errors++;
            }
            return accepted;
        }
        catch
        {
            errors++;
            return [];
        }
    }

    private static bool Persist(string path, IReadOnlyList<ShowLaunchDefinition> shows, ref int errors)
    {
        string temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var payload = new AutoDiscoveredShowCatalog { Shows = shows.ToList() };
            var options = new JsonSerializerOptions(LiveEngine.Json) { WriteIndented = true };
            string serialized = JsonSerializer.Serialize(payload, options);
            if (File.Exists(path) && File.ReadAllText(path).Equals(serialized, StringComparison.Ordinal))
                return true;
            File.WriteAllText(temporary, serialized, new UTF8Encoding(false));
            File.Move(temporary, path, true);
            return true;
        }
        catch
        {
            errors++;
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
            return false;
        }
    }

    private static ShowLaunchDefinition MakePersistedEntrySafe(ShowLaunchDefinition show) => new()
    {
        Id = show.Id,
        Title = show.Title,
        PublicTitle = "",
        Category = "Pruebas internas",
        Tagline = "",
        ClientVisible = false,
        Playlist = show.Playlist,
        Sequence = show.Sequence,
        Audio = show.Audio,
        Enabled = show.Enabled,
        ClientEnabled = false,
        Note = string.IsNullOrWhiteSpace(show.Note)
            ? "AUTOIMPORTADO PARA PRUEBA INTERNA: validación pendiente."
            : show.Note,
        AutoDiscovered = true,
        SourceFolder = show.SourceFolder,
    };

    private static IEnumerable<string> EnumerateFilesWithoutFollowingLinks(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            string directory = pending.Pop();
            IEnumerable<string> files;
            IEnumerable<string> children;
            try
            {
                files = Directory.EnumerateFiles(directory).ToArray();
                children = Directory.EnumerateDirectories(directory).ToArray();
            }
            catch
            {
                continue;
            }
            foreach (string file in files)
                if (!IsReparsePoint(file)) yield return file;
            foreach (string child in children)
                if (!IsReparsePoint(child) &&
                    !HasArchiveDirectory(Path.GetRelativePath(root, child))) pending.Push(child);
        }
    }

    private static bool IsReparsePoint(string path)
    {
        try { return File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint); }
        catch { return true; }
    }

    private static string? SelectCandidate(IEnumerable<string> paths)
    {
        var candidates = paths.Select(Path.GetFullPath).ToArray();
        if (candidates.Length == 0) return null;
        var active = candidates.Where(path => !HasArchiveDirectory(path)).ToArray();
        if (active.Length == 0) active = candidates;
        return active.OrderByDescending(SafeLastWriteUtc)
            .ThenByDescending(path => path, StringComparer.OrdinalIgnoreCase)
            .First();
    }

    private static string? SelectSequence(string folder, IReadOnlyList<string> files)
    {
        // El FSEQ publicado en la raíz de la Show Folder es la salida renderizada
        // que xLights considera vigente. Sólo se usa un subdirectorio como respaldo.
        string[] rootSequences = files.Where(path =>
                Path.GetDirectoryName(Path.GetFullPath(path))!.Equals(Path.GetFullPath(folder), StringComparison.OrdinalIgnoreCase) &&
                Path.GetExtension(path).Equals(".fseq", StringComparison.OrdinalIgnoreCase) && HasValidFseqHeader(path))
            .ToArray();
        return SelectCandidate(rootSequences.Length > 0
            ? rootSequences
            : files.Where(path => Path.GetExtension(path).Equals(".fseq", StringComparison.OrdinalIgnoreCase) &&
                                  HasValidFseqHeader(path)));
    }

    private static bool HasValidFseqHeader(string path)
    {
        try
        {
            Span<byte> header = stackalloc byte[8];
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            if (stream.Length < 32 || stream.Read(header) != header.Length ||
                !header[..4].SequenceEqual("PSEQ"u8)) return false;
            int dataOffset = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(header[4..6]);
            return dataOffset >= 8 && dataOffset < stream.Length;
        }
        catch { return false; }
    }

    private static string? SelectAudio(string folder, IReadOnlyList<string> files, string? sequence)
    {
        string[] rootXsqs = files.Where(path =>
                Path.GetDirectoryName(Path.GetFullPath(path))!.Equals(Path.GetFullPath(folder), StringComparison.OrdinalIgnoreCase) &&
                Path.GetExtension(path).Equals(".xsq", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (rootXsqs.Length > 0)
        {
            string? matching = sequence is null ? null : rootXsqs.FirstOrDefault(path =>
                Path.GetFileNameWithoutExtension(path).Equals(
                    Path.GetFileNameWithoutExtension(sequence), StringComparison.OrdinalIgnoreCase));
            if (matching is null && rootXsqs.Length != 1) return null;
            string xsq = matching ?? rootXsqs[0];
            return ReadReferencedAudio(xsq, folder);
        }
        // Sin XSQ sólo es seguro inferir el audio cuando existe una única opción.
        string[] audioCandidates = files.Where(path => AudioExtensions.Contains(Path.GetExtension(path))).ToArray();
        return audioCandidates.Length == 1 ? audioCandidates[0] : null;
    }

    private static string? ReadReferencedAudio(string xsq, string folder)
    {
        try
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            using var reader = XmlReader.Create(xsq, settings);
            var document = XDocument.Load(reader, LoadOptions.None);
            string? value = document.Descendants()
                .FirstOrDefault(element => element.Name.LocalName.Equals("mediaFile", StringComparison.OrdinalIgnoreCase))
                ?.Value.Trim();
            if (string.IsNullOrWhiteSpace(value)) return null;
            string expanded = Environment.ExpandEnvironmentVariables(value.Replace('/', Path.DirectorySeparatorChar));
            string resolved = Path.IsPathRooted(expanded)
                ? Path.GetFullPath(expanded)
                : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(xsq)!, expanded));
            return IsInside(NormalizeDirectory(folder), resolved) &&
                   AudioExtensions.Contains(Path.GetExtension(resolved)) && File.Exists(resolved) &&
                   !HasArchiveDirectory(Path.GetRelativePath(folder, resolved))
                ? resolved
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool HasArchiveDirectory(string path)
    {
        var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Any(part => ArchiveDirectoryNames.Contains(part.Trim(), StringComparer.OrdinalIgnoreCase));
    }

    private static DateTime SafeLastWriteUtc(string path)
    {
        try { return File.GetLastWriteTimeUtc(path); }
        catch { return DateTime.MinValue; }
    }

    private static string? ResolveTopLevelShowFolder(string root, string sourceFolder, string sequence)
    {
        string candidate = string.IsNullOrWhiteSpace(sourceFolder) ? ExpandPath(sequence) : ExpandPath(sourceFolder);
        if (string.IsNullOrWhiteSpace(candidate)) return null;
        try
        {
            string fullRoot = NormalizeDirectory(root);
            string fullCandidate = Path.GetFullPath(candidate);
            if (!IsInside(fullRoot, fullCandidate)) return null;
            string relative = Path.GetRelativePath(fullRoot, fullCandidate);
            string? first = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .FirstOrDefault(part => part.Length > 0 && part != ".");
            return first is null ? null : NormalizeDirectory(Path.Combine(fullRoot, first));
        }
        catch { return null; }
    }

    private static bool IsInside(string root, string candidate)
    {
        string prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        return candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeDirectory(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static int NextPlaylistNumber(IEnumerable<ShowLaunchDefinition> shows) =>
        shows.Select(show => ReadLeadingNumber(show.Playlist)).DefaultIfEmpty(0).Max() + 1;

    private static bool PlaylistNumberInUse(IEnumerable<string> playlists, int number) =>
        playlists.Any(playlist => ReadLeadingNumber(playlist) == number);

    private static int ReadLeadingNumber(string value)
    {
        var match = LeadingNumberRegex().Match(value ?? "");
        return match.Success && int.TryParse(match.Groups[1].Value, out int number) ? number : 0;
    }

    private static string DisplayTitle(string folderName)
    {
        string value = LeadingNumberRegex().Replace(folderName, "");
        value = Regex.Replace(value.Replace('_', ' ').Replace('-', ' '), @"\s+", " ").Trim();
        string previous;
        do
        {
            previous = value;
            value = Regex.Replace(value,
                @"(?:^|\s+)(?:LIGHTMAN|V\d+|XATW(?:\s+\d{4})?|CORTA|OK)$", "",
                RegexOptions.IgnoreCase).Trim();
        } while (!value.Equals(previous, StringComparison.Ordinal));
        return string.IsNullOrWhiteSpace(value) ? "SHOW NUEVO" : value.ToUpperInvariant();
    }

    private static string Slug(string value)
    {
        string normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder();
        bool separator = false;
        foreach (char character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(character))
            {
                if (separator && builder.Length > 0) builder.Append('-');
                builder.Append(char.ToLowerInvariant(character));
                separator = false;
            }
            else separator = true;
        }
        return builder.Length == 0 ? "show" : builder.ToString();
    }

    private static string UniqueId(string preferred, ISet<string> used)
    {
        string candidate = preferred;
        int suffix = 2;
        while (!used.Add(candidate)) candidate = preferred + "-" + suffix++;
        return candidate;
    }

    private static string CompactPath(string path)
    {
        string full = Path.GetFullPath(path);
        string profile = Path.TrimEndingDirectorySeparator(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        if (IsInside(profile, full))
            return "%USERPROFILE%/" + Path.GetRelativePath(profile, full).Replace('\\', '/');
        return full.Replace('\\', '/');
    }

    private static string ExpandPath(string path) => Path.GetFullPath(
        Environment.ExpandEnvironmentVariables((path ?? "").Replace('/', Path.DirectorySeparatorChar)));

    [GeneratedRegex(@"^\s*(\d{1,3})(?:\s*[_.\-·]\s*|\s+)")]
    private static partial Regex LeadingNumberRegex();
}
