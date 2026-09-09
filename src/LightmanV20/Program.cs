namespace LightmanZapravka3D;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
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
}
