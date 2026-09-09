using System.Text;
using System.Text.Json;

namespace LightmanZapravka3D;

internal static class ShowFolderDiscoverySelfTest
{
    internal static void Run(Action<bool, string> check)
    {
        string temporaryRoot = Path.Combine(Path.GetTempPath(), "lightman-v20-discovery-" + Guid.NewGuid().ToString("N"));
        string showsRoot = Path.Combine(temporaryRoot, "shows xlights");
        string configurationDirectory = Path.Combine(temporaryRoot, "app", "ShowControl");
        string configurationPath = Path.Combine(configurationDirectory, "shows.json");
        try
        {
            Directory.CreateDirectory(configurationDirectory);
            string manualFolder = Path.Combine(showsRoot, "01_MANUAL");
            string discoveredFolder = Path.Combine(showsRoot, "14_NEW_SHOW_CORTA_XATW_2026_LIGHTMAN_V20 ok");
            string incompleteFolder = Path.Combine(showsRoot, "15_INCOMPLETE");
            string invalidFseqFolder = Path.Combine(showsRoot, "16_INVALID_FSEQ");
            string ambiguousFolder = Path.Combine(showsRoot, "17_AMBIGUOUS");
            string unnumberedFolder = Path.Combine(showsRoot, "SECOND_SHOW_LIGHTMAN");
            Directory.CreateDirectory(Path.Combine(manualFolder, "Audio"));
            Directory.CreateDirectory(Path.Combine(discoveredFolder, "Audio"));
            Directory.CreateDirectory(Path.Combine(discoveredFolder, "Fuente_original"));
            Directory.CreateDirectory(Path.Combine(discoveredFolder, "Validacion"));
            Directory.CreateDirectory(incompleteFolder);
            Directory.CreateDirectory(Path.Combine(invalidFseqFolder, "Audio"));
            Directory.CreateDirectory(Path.Combine(ambiguousFolder, "Audio"));
            Directory.CreateDirectory(Path.Combine(unnumberedFolder, "Audio"));

            string manualSequence = Path.Combine(manualFolder, "manual.fseq");
            string manualAudio = Path.Combine(manualFolder, "Audio", "manual.mp3");
            WriteValidFseq(manualSequence);
            File.WriteAllBytes(manualAudio, [2]);

            string selectedSequence = Path.Combine(discoveredFolder, "14_NEW_SHOW.fseq");
            string olderRootSequence = Path.Combine(discoveredFolder, "old.fseq");
            string ignoredSequence = Path.Combine(discoveredFolder, "Validacion", "newest-but-invalid.fseq");
            WriteValidFseq(selectedSequence);
            WriteValidFseq(olderRootSequence);
            WriteValidFseq(ignoredSequence);
            File.SetLastWriteTimeUtc(olderRootSequence, DateTime.UtcNow.AddMinutes(-2));
            File.SetLastWriteTimeUtc(selectedSequence, DateTime.UtcNow.AddMinutes(-1));
            File.SetLastWriteTimeUtc(ignoredSequence, DateTime.UtcNow);

            string referencedAudio = Path.Combine(discoveredFolder, "Audio", "right.mp3");
            string wrongAudio = Path.Combine(discoveredFolder, "Audio", "wrong-but-newer.wav");
            File.WriteAllBytes(referencedAudio, [6]);
            File.WriteAllBytes(wrongAudio, [7]);
            File.SetLastWriteTimeUtc(referencedAudio, DateTime.UtcNow.AddMinutes(-3));
            File.SetLastWriteTimeUtc(wrongAudio, DateTime.UtcNow);
            File.WriteAllText(Path.Combine(discoveredFolder, "14_NEW_SHOW.xsq"),
                "<?xml version=\"1.0\"?><xsequence><head><mediaFile>Audio/right.mp3</mediaFile></head></xsequence>",
                new UTF8Encoding(false));
            WriteValidFseq(Path.Combine(incompleteFolder, "15_INCOMPLETE.fseq"));
            byte[] invalidFseq = new byte[64];
            Encoding.ASCII.GetBytes("NOPE").CopyTo(invalidFseq, 0);
            invalidFseq[4] = 32;
            File.WriteAllBytes(Path.Combine(invalidFseqFolder, "16_INVALID_FSEQ.fseq"), invalidFseq);
            File.WriteAllBytes(Path.Combine(invalidFseqFolder, "Audio", "invalid.mp3"), [9]);
            File.WriteAllText(Path.Combine(invalidFseqFolder, "16_INVALID_FSEQ.xsq"),
                "<?xml version=\"1.0\"?><xsequence><head><mediaFile>Audio/invalid.mp3</mediaFile></head></xsequence>",
                new UTF8Encoding(false));

            WriteValidFseq(Path.Combine(unnumberedFolder, "SECOND_SHOW.fseq"));
            File.WriteAllBytes(Path.Combine(unnumberedFolder, "Audio", "second.mp3"), [10]);

            WriteValidFseq(Path.Combine(ambiguousFolder, "chosen.fseq"));
            File.WriteAllBytes(Path.Combine(ambiguousFolder, "Audio", "a.mp3"), [11]);
            File.WriteAllBytes(Path.Combine(ambiguousFolder, "Audio", "b.mp3"), [12]);
            File.WriteAllText(Path.Combine(ambiguousFolder, "a.xsq"),
                "<?xml version=\"1.0\"?><xsequence><head><mediaFile>Audio/a.mp3</mediaFile></head></xsequence>",
                new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(ambiguousFolder, "b.xsq"),
                "<?xml version=\"1.0\"?><xsequence><head><mediaFile>Audio/b.mp3</mediaFile></head></xsequence>",
                new UTF8Encoding(false));

            var settings = new XScheduleSettings
            {
                AutoDiscoverShows = true,
                ShowFolderRoot = showsRoot,
                Shows =
                [
                    new ShowLaunchDefinition
                    {
                        Id = "manual",
                        Title = "MANUAL ORIGINAL",
                        ClientVisible = false,
                        Playlist = "01 · MANUAL",
                        Sequence = manualSequence,
                        Audio = manualAudio,
                    },
                    new ShowLaunchDefinition
                    {
                        Id = "manual-13",
                        Title = "MANUAL 13",
                        ClientVisible = false,
                        Playlist = "13 · MANUAL",
                        Sequence = manualSequence,
                        Audio = manualAudio,
                    },
                ],
            };
            var json = new JsonSerializerOptions(LiveEngine.Json) { WriteIndented = true };
            File.WriteAllText(configurationPath, JsonSerializer.Serialize(settings, json), new UTF8Encoding(false));

            using (var first = XScheduleController.Load(configurationPath))
            {
                var auto = first.Shows.Single(show => show.AutoDiscovered && show.Title == "NEW SHOW");
                var secondAuto = first.Shows.Single(show => show.AutoDiscovered && show.Title == "SECOND SHOW");
                check(first.Shows.Count == 4 && first.Shows.Single(show => show.Id == "manual").Title == "MANUAL ORIGINAL",
                    "autodescubrimiento conserva intacto el catálogo manual y añade sólo carpetas listas");
                check(first.DiscoveryReport.ScannedFolders == 6 && first.DiscoveryReport.Added == 2 &&
                      first.DiscoveryReport.Existing == 1 && first.DiscoveryReport.Incomplete == 3 &&
                      first.DiscoveryReport.Errors == 0 && first.DiscoveryReport.Persisted,
                    "autodescubrimiento informa carpetas nuevas, existentes e incompletas");
                check(auto.Id == "auto-new-show" && auto.Playlist == "14 · NEW SHOW" && auto.Enabled &&
                      !auto.ClientVisible && !auto.ClientEnabled && auto.SourceFolder.Length > 0,
                    "show automático queda habilitado únicamente para pruebas internas con ID estable");
                check(secondAuto.Playlist == "15 · SECOND SHOW",
                    "una carpeta sin número recibe el siguiente número libre sin duplicar el 14");
                check(Path.GetFullPath(Environment.ExpandEnvironmentVariables(auto.Sequence).Replace('/', Path.DirectorySeparatorChar)) ==
                          Path.GetFullPath(selectedSequence) &&
                      Path.GetFullPath(Environment.ExpandEnvironmentVariables(auto.Audio).Replace('/', Path.DirectorySeparatorChar)) ==
                          Path.GetFullPath(referencedAudio),
                    "autoimportación prioriza FSEQ raíz y el mediaFile declarado por el XSQ raíz");
                check(first.ExperiencesForClient().Length == 0 &&
                      first.DiscoveryReport.Incomplete == 3,
                    "show descubierto automáticamente no se publica en el servidor cliente");
            }

            string sidecar = Path.Combine(configurationDirectory, "shows.autodiscovered.json");
            check(File.Exists(sidecar), "catálogo automático persiste en archivo lateral sin reescribir shows.json");
            string manualBeforeSecondLoad = File.ReadAllText(configurationPath);
            string sidecarBeforeSecondLoad = File.ReadAllText(sidecar);
            DateTime sidecarWriteBeforeSecondLoad = File.GetLastWriteTimeUtc(sidecar);
            using (var second = XScheduleController.Load(configurationPath))
            {
                check(second.Shows.Count == 4 && second.DiscoveryReport.Added == 0 &&
                      second.DiscoveryReport.Existing == 3 && second.DiscoveryReport.Incomplete == 3,
                    "segundo inicio es idempotente y no duplica shows ni playlists");
            }
            check(File.ReadAllText(configurationPath) == manualBeforeSecondLoad,
                "descubrimiento nunca modifica el archivo manual shows.json");
            check(File.ReadAllText(sidecar) == sidecarBeforeSecondLoad &&
                  File.GetLastWriteTimeUtc(sidecar) == sidecarWriteBeforeSecondLoad,
                "inicio idempotente tampoco reescribe el catálogo lateral cuando no hay cambios");

            settings.Shows.Add(new ShowLaunchDefinition
            {
                Id = "new-show-public",
                Title = "NEW SHOW",
                PublicTitle = "Nueva experiencia",
                Category = "Experiencia visual",
                Tagline = "Show promovido manualmente.",
                ClientVisible = true,
                Playlist = "14 · NEW SHOW",
                Sequence = selectedSequence,
                Audio = referencedAudio,
                Enabled = true,
                ClientEnabled = true,
            });
            File.WriteAllText(configurationPath, JsonSerializer.Serialize(settings, json), new UTF8Encoding(false));

            using (var promotedLoad = XScheduleController.Load(configurationPath))
            {
                var promoted = promotedLoad.Shows.Single(show => show.Id == "new-show-public");
                check(promotedLoad.Shows.Count(show => show.Title == "NEW SHOW") == 1 &&
                      !promoted.AutoDiscovered && promoted.ClientVisible && promoted.ClientEnabled &&
                      promotedLoad.ExperiencesForClient().Length == 1,
                    "definición manual promueve el show al cliente sin duplicar la entrada automática");
            }

            var sidecarAfterPromotion = JsonSerializer.Deserialize<AutoDiscoveredShowCatalog>(
                File.ReadAllText(sidecar), LiveEngine.Json);
            check(sidecarAfterPromotion is not null && sidecarAfterPromotion.Shows.Count == 1 &&
                  sidecarAfterPromotion.Shows.Single().Title == "SECOND SHOW" &&
                  sidecarAfterPromotion.Shows.All(show => show.Id != "auto-new-show"),
                "promoción manual retira del catálogo lateral la copia automática reemplazada");
        }
        finally
        {
            try { if (Directory.Exists(temporaryRoot)) Directory.Delete(temporaryRoot, true); }
            catch { }
        }
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
}
