using System.Text.Json;

namespace LightmanZapravka3D;

internal static class TrackingModeControllerSelfTest
{
    internal static void Run(Action<bool, string> check)
    {
        ArgumentNullException.ThrowIfNull(check);
        DateTimeOffset now = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
        var modes = new[]
        {
            new TrackingModeDefinition("silhouette", "SILUETA", "Contorno del cuerpo", true),
            new TrackingModeDefinition("particles", "PARTÍCULAS", "Partículas reactivas", true),
        };
        var controller = new TrackingModeController(modes, TimeSpan.FromSeconds(2), () => now);

        TrackingControlSnapshot initial = controller.GetControlSnapshot();
        check(initial.ApiVersion == 1 && initial.Revision == 0 && !initial.Selected && initial.DesiredMode == "",
            "tracking inicia sin selección y publica contrato API V1");

        TrackingModeSelectionResult unsafeSelection = controller.SelectMode("../particles");
        TrackingModeSelectionResult unknownSelection = controller.SelectMode("unknown");
        check(!unsafeSelection.Ok && !unknownSelection.Ok && controller.GetControlSnapshot().Revision == 0,
            "tracking rechaza IDs fuera de la lista permitida sin publicar revisión");

        TrackingModeSelectionResult first = controller.SelectMode("silhouette");
        TrackingModeSelectionResult repeated = controller.SelectMode("silhouette");
        check(first.Ok && repeated.Ok && first.Revision == 1 && repeated.Revision == 1 &&
              controller.GetControlSnapshot() == new TrackingControlSnapshot(1, 1, true, "silhouette"),
            "seleccionar dos veces el mismo modo es idempotente");

        TrackingStatusResult stale = controller.ReportStatus(
            new TrackingStatusUpdate("tracker-main", 0, "silhouette", "running", ""));
        TrackingStatusResult mismatched = controller.ReportStatus(
            new TrackingStatusUpdate("tracker-main", 1, "particles", "running", ""));
        TrackingStateSnapshot beforeAck = controller.GetStateSnapshot();
        check(!stale.Ok && stale.Revision == 1 && !mismatched.Ok &&
              beforeAck.ActiveMode == "" && !beforeAck.Connected && beforeAck.Status == "offline",
            "ACK obsoleto o de otro modo no cambia el estado confirmado");

        TrackingStatusResult accepted = controller.ReportStatus(
            new TrackingStatusUpdate("tracker-main", 1, "silhouette", "running", ""));
        TrackingStateSnapshot running = controller.GetStateSnapshot();
        check(accepted.Ok && accepted.Revision == 1 && running.Configured && running.Connected &&
              running.Status == "running" && running.DesiredMode == "silhouette" &&
              running.ActiveMode == "silhouette" && running.LastSeenUtc == now && running.Error == "" &&
              running.Modes.Select(mode => mode.Id).SequenceEqual(new[] { "silhouette", "particles" }),
            "ACK vigente confirma el modo y alimenta el estado completo para la tablet");

        now = now.AddMilliseconds(1999);
        check(controller.GetStateSnapshot().Connected,
            "heartbeat conserva conectado al tracker antes del vencimiento");
        now = now.AddMilliseconds(2);
        TrackingStateSnapshot offline = controller.GetStateSnapshot();
        check(!offline.Connected && offline.Status == "offline" && offline.ActiveMode == "silhouette" &&
              offline.LastSeenUtc.HasValue,
            "heartbeat vencido publica offline sin inventar ni borrar el último ACK");

        TrackingModeSelectionResult second = controller.SelectMode("particles");
        check(second.Ok && second.Revision == 2 && controller.GetControlSnapshot().DesiredMode == "particles",
            "un modo distinto publica exactamente una nueva revisión");
        TrackingStatusResult secondAck = controller.ReportStatus(
            new TrackingStatusUpdate("tracker-main", 2, "particles", "degraded", "Cámara sin profundidad"));
        TrackingStateSnapshot degraded = controller.GetStateSnapshot();
        check(secondAck.Ok && degraded.Connected && degraded.Status == "degraded" &&
              degraded.ActiveMode == "particles" && degraded.Error == "Cámara sin profundidad",
            "status y error válidos se asocian al ACK de la revisión vigente");

        string controlJson = JsonSerializer.Serialize(controller.GetControlSnapshot(), LiveEngine.Json);
        using var controlDocument = JsonDocument.Parse(controlJson);
        JsonElement root = controlDocument.RootElement;
        check(root.GetProperty("apiVersion").GetInt32() == 1 &&
              root.GetProperty("revision").GetInt64() == 2 &&
              root.GetProperty("selected").GetBoolean() &&
              root.GetProperty("desiredMode").GetString() == "particles",
            "snapshot de control serializa exactamente los campos acordados en camelCase");

        var concurrent = new TrackingModeController(modes);
        Parallel.For(0, 64, _ => concurrent.SelectMode("particles"));
        check(concurrent.GetControlSnapshot().Revision == 1,
            "selección concurrente del mismo modo mantiene una sola revisión");

        bool duplicateRejected = ThrowsInvalidData(() => new TrackingModeController(
        [
            new TrackingModeDefinition("particles", "UNO", "", true),
            new TrackingModeDefinition("particles", "DOS", "", true),
        ]));
        bool unsafeCatalogRejected = ThrowsInvalidData(() => new TrackingModeController(
        [new TrackingModeDefinition("../../cmd", "NO", "", true)]));
        check(duplicateRejected && unsafeCatalogRejected,
            "catálogo rechaza IDs duplicados y valores que podrían convertirse en rutas o comandos");
    }

    private static bool ThrowsInvalidData(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (InvalidDataException)
        {
            return true;
        }
    }
}
