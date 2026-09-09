namespace LightmanZapravka3D;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Contains("--sync-shows"))
        {
            RunShowSync();
            return;
        }
        if (args.Contains("--self-test-xschedule-sync"))
        {
            try { XSchedulePlaylistSynchronizerSelfTest.Run(); }
            catch (Exception ex)
            {
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "SELFTEST-XSCHEDULE-SYNC.txt"), ex.ToString());
                Environment.ExitCode = 1;
            }
            return;
        }
        if (args.Contains("--self-test-minimal"))
        {
            try { MinimalSelfTest.Run(); }
            catch (Exception ex) { File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"SELFTEST-MINIMAL.txt"),ex.ToString());Environment.ExitCode=1; }
            return;
        }
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm(args.Contains("--preview-house")));
    }

    private static void RunShowSync()
    {
        string reportPath = Path.Combine(AppContext.BaseDirectory, "SYNC-SHOWS.json");
        try
        {
            string configurationPath = Path.Combine(AppContext.BaseDirectory, "ShowControl", "shows.json");
            using var controller = XScheduleController.Load(configurationPath, runDiscovery: true);
            AutoDiscoveryReport discovery = controller.DiscoveryReport;
            XScheduleSyncReport schedule = controller.ScheduleSyncReport;
            bool ok = discovery.Errors == 0 && discovery.Persisted &&
                schedule.Errors.Count == 0 && !schedule.WaitingForXSchedule &&
                !schedule.SkippedBusy && !schedule.RestartRequired &&
                schedule.InvalidCandidates.Count == 0;
            var report = new
            {
                ok,
                generatedUtc = DateTime.UtcNow,
                discovery,
                schedule,
                automaticShows = controller.Shows.Where(show => show.AutoDiscovered).Select(show => new
                {
                    show.Id,
                    show.Title,
                    show.Playlist,
                    show.Sequence,
                    show.Audio,
                }).ToArray(),
            };
            File.WriteAllText(reportPath, System.Text.Json.JsonSerializer.Serialize(report,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            if (!ok) Environment.ExitCode = 1;
        }
        catch (Exception ex)
        {
            var report = new { ok = false, generatedUtc = DateTime.UtcNow, error = ex.Message };
            File.WriteAllText(reportPath, System.Text.Json.JsonSerializer.Serialize(report,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            Environment.ExitCode = 1;
        }
    }
}
