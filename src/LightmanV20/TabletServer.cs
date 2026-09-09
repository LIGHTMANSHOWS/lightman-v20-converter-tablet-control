using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace LightmanZapravka3D;

internal readonly record struct TabletActionResult(bool Ok, string Error)
{
    public static TabletActionResult Success() => new(true, "");
    public static TabletActionResult Fail(string error) => new(false, error);
}

internal sealed class TabletServer : IDisposable
{
    private readonly string _webFolder;
    private readonly int _port;
    private readonly Func<object> _state;
    private readonly Func<string, bool> _selectSource;
    private readonly Func<bool, bool> _setTest;
    private readonly Func<string, TabletActionResult> _playShow;
    private readonly Func<TabletActionResult> _pauseShow;
    private readonly Func<TabletActionResult> _stopShow;
    private readonly Func<TrackingControlSnapshot>? _trackingControl;
    private readonly Func<string, TrackingModeSelectionResult>? _selectTrackingMode;
    private readonly Func<TrackingStatusUpdate, TrackingStatusResult>? _reportTrackingStatus;
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _handlerSlots = new(12, 12);
    private TcpListener? _listener;
    private Task? _worker;

    public TabletServer(string webFolder, int port, Func<object> state,
        Func<string, bool> selectSource, Func<bool, bool> setTest)
        : this(webFolder, port, state, selectSource, setTest,
            _ => TabletActionResult.Fail("Control de shows no configurado."),
            () => TabletActionResult.Fail("Control de shows no configurado."),
            () => TabletActionResult.Fail("Control de shows no configurado."))
    { }

    public TabletServer(string webFolder, int port, Func<object> state,
        Func<string, bool> selectSource, Func<bool, bool> setTest,
        Func<string, TabletActionResult> playShow, Func<TabletActionResult> pauseShow,
        Func<TabletActionResult> stopShow)
        : this(webFolder, port, state, selectSource, setTest, playShow, pauseShow, stopShow,
            null, null, null)
    { }

    public TabletServer(string webFolder, int port, Func<object> state,
        Func<string, bool> selectSource, Func<bool, bool> setTest,
        Func<string, TabletActionResult> playShow, Func<TabletActionResult> pauseShow,
        Func<TabletActionResult> stopShow,
        Func<TrackingControlSnapshot>? trackingControl,
        Func<string, TrackingModeSelectionResult>? selectTrackingMode,
        Func<TrackingStatusUpdate, TrackingStatusResult>? reportTrackingStatus)
    {
        _webFolder = webFolder;
        _port = port;
        _state = state;
        _selectSource = selectSource;
        _setTest = setTest;
        _playShow = playShow;
        _pauseShow = pauseShow;
        _stopShow = stopShow;
        _trackingControl = trackingControl;
        _selectTrackingMode = selectTrackingMode;
        _reportTrackingStatus = reportTrackingStatus;
    }

    public int ListeningPort => (_listener?.LocalEndpoint as IPEndPoint)?.Port ?? _port;

    public void Start()
    {
        if (_listener is not null) return;
        _listener = new TcpListener(IPAddress.Any, _port);
        _listener.Start(16);
        _worker = Task.Run(() => AcceptLoopAsync(_stop.Token));
    }

    private async Task AcceptLoopAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var client = await _listener!.AcceptTcpClientAsync(token).ConfigureAwait(false);
                if (!_handlerSlots.Wait(0)) { client.Dispose(); continue; }
                _ = Task.Run(async () =>
                {
                    try { await HandleAsync(client, token).ConfigureAwait(false); }
                    finally { _handlerSlots.Release(); }
                });
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
    }

    private async Task HandleAsync(TcpClient client, CancellationToken token)
    {
        using (client)
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
        {
            try
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(7));
                token = timeout.Token;
                client.NoDelay = true;
                var stream = client.GetStream();
                var request = await ReadRequestAsync(stream, token).ConfigureAwait(false);
                if (request is null) return;
                var (method, path, body, headers) = request.Value;
                if (method == "OPTIONS")
                {
                    await JsonAsync(stream, 403, new { ok = false, error = "Control sólo desde la página de V20." }, token).ConfigureAwait(false);
                    return;
                }
                if (method == "GET" && path == "/api/state")
                {
                    await JsonAsync(stream, 200, _state(), token).ConfigureAwait(false);
                    return;
                }
                if (method == "GET" && path == "/api/tracking/control")
                {
                    if (_trackingControl is null)
                    {
                        await JsonAsync(stream, 503,
                            new { ok = false, error = "Control de tracking no configurado." }, token).ConfigureAwait(false);
                        return;
                    }
                    await JsonAsync(stream, 200, _trackingControl(), token).ConfigureAwait(false);
                    return;
                }
                if (method == "GET" && path == "/health")
                {
                    await JsonAsync(stream, 200, new { ok = true, service = "LIGHTMAN V20 MINIMAL" }, token).ConfigureAwait(false);
                    return;
                }
                if (method == "POST" && path == "/api/source")
                {
                    if (!ValidJsonRequest(headers, out var status, out var error))
                    {
                        await JsonAsync(stream, status, new { ok = false, error }, token).ConfigureAwait(false);
                        return;
                    }
                    using var json = JsonDocument.Parse(body);
                    string source = json.RootElement.GetProperty("source").GetString() ?? "";
                    if (source is not ("resolume" or "xlights" or "tracking"))
                    {
                        await JsonAsync(stream, 400, new { ok = false, error = "Fuente inválida." }, token).ConfigureAwait(false);
                        return;
                    }
                    if (!_selectSource(source))
                    {
                        await JsonAsync(stream, 503, new { ok = false, error = "V20 no pudo aplicar la fuente." }, token).ConfigureAwait(false);
                        return;
                    }
                    await JsonAsync(stream, 200, new { ok = true, source }, token).ConfigureAwait(false);
                    return;
                }
                if (method == "POST" && path == "/api/test")
                {
                    if (!ValidJsonRequest(headers, out var status, out var error))
                    {
                        await JsonAsync(stream, status, new { ok = false, error }, token).ConfigureAwait(false);
                        return;
                    }
                    using var json = JsonDocument.Parse(body);
                    bool enabled = json.RootElement.GetProperty("enabled").GetBoolean();
                    if (!_setTest(enabled))
                    {
                        await JsonAsync(stream, 503, new { ok = false, error = "El test está bloqueado porque la entrada Art-Net no está disponible." }, token).ConfigureAwait(false);
                        return;
                    }
                    await JsonAsync(stream, 200, new { ok = true, enabled }, token).ConfigureAwait(false);
                    return;
                }
                if (method == "POST" && path == "/api/tracking/mode")
                {
                    if (!ValidJsonRequest(headers, out var status, out var error))
                    {
                        await JsonAsync(stream, status, new { ok = false, error }, token).ConfigureAwait(false);
                        return;
                    }
                    if (_selectTrackingMode is null)
                    {
                        await JsonAsync(stream, 503,
                            new { ok = false, error = "Control de tracking no configurado." }, token).ConfigureAwait(false);
                        return;
                    }
                    if (!TryParseJsonObject(body, out var json, out error))
                    {
                        await JsonAsync(stream, 400, new { ok = false, error }, token).ConfigureAwait(false);
                        return;
                    }
                    using (json)
                    {
                        if (!TryReadRequiredString(json.RootElement, "id", 64, false, out string id, out error))
                        {
                            await JsonAsync(stream, 400, new { ok = false, error }, token).ConfigureAwait(false);
                            return;
                        }
                        var result = _selectTrackingMode(id);
                        await JsonAsync(stream, result.Ok ? 200 : 400,
                            result.Ok
                                ? new { ok = true, id = result.Id, revision = result.Revision }
                                : new { ok = false, error = result.Error, revision = result.Revision },
                            token).ConfigureAwait(false);
                    }
                    return;
                }
                if (method == "POST" && path == "/api/tracking/status")
                {
                    if (!ValidJsonRequest(headers, out var status, out var error))
                    {
                        await JsonAsync(stream, status, new { ok = false, error }, token).ConfigureAwait(false);
                        return;
                    }
                    if (_reportTrackingStatus is null)
                    {
                        await JsonAsync(stream, 503,
                            new { ok = false, error = "Control de tracking no configurado." }, token).ConfigureAwait(false);
                        return;
                    }
                    if (!TryParseJsonObject(body, out var json, out error))
                    {
                        await JsonAsync(stream, 400, new { ok = false, error }, token).ConfigureAwait(false);
                        return;
                    }
                    using (json)
                    {
                        JsonElement root = json.RootElement;
                        if (!TryReadRequiredString(root, "clientId", 64, false, out string clientId, out error) ||
                            !TryReadRequiredInt64(root, "revision", out long revision, out error) ||
                            !TryReadRequiredString(root, "activeMode", 64, false, out string activeMode, out error) ||
                            !TryReadRequiredString(root, "status", 16, false, out string trackingStatus, out error) ||
                            !TryReadRequiredString(root, "error", 512, true, out string trackingError, out error))
                        {
                            await JsonAsync(stream, 400, new { ok = false, error }, token).ConfigureAwait(false);
                            return;
                        }

                        var update = new TrackingStatusUpdate(clientId, revision, activeMode, trackingStatus, trackingError);
                        var result = _reportTrackingStatus(update);
                        int responseStatus = result.Ok ? 200 : result.Revision != revision ? 409 : 400;
                        await JsonAsync(stream, responseStatus,
                            result.Ok
                                ? new { ok = true, revision = result.Revision, activeMode, status = trackingStatus }
                                : new { ok = false, error = result.Error, revision = result.Revision },
                            token).ConfigureAwait(false);
                    }
                    return;
                }
                if (method == "POST" && path == "/api/show/play")
                {
                    if (!ValidJsonRequest(headers, out var status, out var error))
                    {
                        await JsonAsync(stream, status, new { ok = false, error }, token).ConfigureAwait(false);
                        return;
                    }
                    using var json = JsonDocument.Parse(body);
                    string id = json.RootElement.GetProperty("id").GetString() ?? "";
                    var result = _playShow(id);
                    await JsonAsync(stream, result.Ok ? 200 : 503,
                        result.Ok ? new { ok = true, id } : new { ok = false, error = result.Error }, token).ConfigureAwait(false);
                    return;
                }
                if (method == "POST" && path is "/api/show/pause" or "/api/show/stop")
                {
                    if (!ValidJsonRequest(headers, out var status, out var error))
                    {
                        await JsonAsync(stream, status, new { ok = false, error }, token).ConfigureAwait(false);
                        return;
                    }
                    var result = path.EndsWith("/pause", StringComparison.Ordinal) ? _pauseShow() : _stopShow();
                    await JsonAsync(stream, result.Ok ? 200 : 503,
                        result.Ok ? new { ok = true } : new { ok = false, error = result.Error }, token).ConfigureAwait(false);
                    return;
                }
                if (method == "GET")
                {
                    string file = path switch
                    {
                        "/" or "/index.html" => "tablet.html",
                        "/tablet.css" => "tablet.css",
                        "/tablet.js" => "tablet.js",
                        _ => "",
                    };
                    if (file.Length > 0)
                    {
                        string full = Path.Combine(_webFolder, file);
                        if (File.Exists(full))
                        {
                            string contentType = Path.GetExtension(full) switch
                            { ".css" => "text/css; charset=utf-8", ".js" => "text/javascript; charset=utf-8", _ => "text/html; charset=utf-8" };
                            await RespondAsync(stream, 200, contentType, await File.ReadAllBytesAsync(full, token), token).ConfigureAwait(false);
                            return;
                        }
                    }
                }
                await JsonAsync(stream, 404, new { ok = false, error = "No encontrado." }, token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                try { await JsonAsync(client.GetStream(), 400, new { ok = false, error = ex.Message }, token).ConfigureAwait(false); }
                catch { }
            }
        }
    }

    private static bool ValidJsonRequest(Dictionary<string, string> headers, out int status, out string error)
    {
        status = 415;
        error = "Se requiere Content-Type application/json.";
        if (!headers.TryGetValue("Content-Type", out var contentType) ||
            !contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase)) return false;
        if (headers.TryGetValue("Origin", out var origin))
        {
            status = 403;
            error = "Origen no autorizado.";
            if (!headers.TryGetValue("Host", out var host) || !Uri.TryCreate(origin, UriKind.Absolute, out var uri) ||
                !uri.Authority.Equals(host, StringComparison.OrdinalIgnoreCase)) return false;
        }
        status = 200;
        error = "";
        return true;
    }

    private static bool TryParseJsonObject(byte[] body, out JsonDocument json, out string error)
    {
        try
        {
            json = JsonDocument.Parse(body);
            if (json.RootElement.ValueKind == JsonValueKind.Object)
            {
                error = "";
                return true;
            }
            json.Dispose();
        }
        catch (JsonException) { }

        json = null!;
        error = "Se requiere un objeto JSON válido.";
        return false;
    }

    private static bool TryReadRequiredString(JsonElement root, string name, int maximumLength,
        bool allowEmpty, out string value, out string error)
    {
        value = "";
        error = $"El campo {name} es obligatorio.";
        JsonProperty[] matches = root.EnumerateObject()
            .Where(property => property.NameEquals(name)).Take(2).ToArray();
        if (matches.Length != 1 || matches[0].Value.ValueKind != JsonValueKind.String) return false;
        value = matches[0].Value.GetString() ?? "";
        if (value.Length > maximumLength || (!allowEmpty && value.Length == 0) ||
            value.Any(character => char.IsControl(character) && character is not '\t'))
        {
            value = "";
            error = $"El campo {name} es inválido.";
            return false;
        }
        error = "";
        return true;
    }

    private static bool TryReadRequiredInt64(JsonElement root, string name, out long value, out string error)
    {
        value = 0;
        error = $"El campo {name} es obligatorio.";
        JsonProperty[] matches = root.EnumerateObject()
            .Where(property => property.NameEquals(name)).Take(2).ToArray();
        if (matches.Length != 1 || matches[0].Value.ValueKind != JsonValueKind.Number ||
            !matches[0].Value.TryGetInt64(out value) || value < 0)
        {
            value = 0;
            error = $"El campo {name} es inválido.";
            return false;
        }
        error = "";
        return true;
    }

    private static async Task<(string Method, string Path, byte[] Body, Dictionary<string, string> Headers)?> ReadRequestAsync(NetworkStream stream, CancellationToken token)
    {
        var data = new List<byte>(2048);
        var buffer = new byte[1024];
        int headerEnd = -1;
        while (data.Count < 16_384 && headerEnd < 0)
        {
            int read = await stream.ReadAsync(buffer, token).ConfigureAwait(false);
            if (read <= 0) return null;
            data.AddRange(buffer.AsSpan(0, read).ToArray());
            headerEnd = FindHeaderEnd(data);
        }
        if (headerEnd < 0) throw new InvalidDataException("Cabecera HTTP demasiado grande.");
        string header = Encoding.ASCII.GetString(data.Take(headerEnd).ToArray());
        string[] lines = header.Split("\r\n", StringSplitOptions.None);
        string[] requestLine = lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (requestLine.Length < 2) throw new InvalidDataException("Solicitud HTTP inválida.");
        int contentLength = 0;
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in lines.Skip(1))
        {
            int colon = line.IndexOf(':');
            if (colon <= 0) continue;
            string name = line[..colon].Trim();
            string value = line[(colon + 1)..].Trim();
            headers[name] = value;
            if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)) int.TryParse(value, out contentLength);
        }
        if (contentLength is < 0 or > 4096) throw new InvalidDataException("Cuerpo HTTP demasiado grande.");
        int bodyStart = headerEnd + 4;
        while (data.Count - bodyStart < contentLength)
        {
            int read = await stream.ReadAsync(buffer, token).ConfigureAwait(false);
            if (read <= 0) throw new EndOfStreamException();
            data.AddRange(buffer.AsSpan(0, read).ToArray());
        }
        string rawPath = requestLine[1].Split('?', 2)[0];
        return (requestLine[0].ToUpperInvariant(), Uri.UnescapeDataString(rawPath), data.Skip(bodyStart).Take(contentLength).ToArray(), headers);
    }

    private static int FindHeaderEnd(List<byte> data)
    {
        for (int i = 3; i < data.Count; i++)
            if (data[i - 3] == 13 && data[i - 2] == 10 && data[i - 1] == 13 && data[i] == 10) return i - 3;
        return -1;
    }

    private static Task JsonAsync(NetworkStream stream, int status, object value, CancellationToken token) =>
        RespondAsync(stream, status, "application/json; charset=utf-8",
            JsonSerializer.SerializeToUtf8Bytes(value, LiveEngine.Json), token);

    private static async Task RespondAsync(NetworkStream stream, int status, string contentType, byte[] body, CancellationToken token)
    {
        string reason = status switch { 200 => "OK", 204 => "No Content", 400 => "Bad Request", 403 => "Forbidden",
            409 => "Conflict",
            404 => "Not Found", 415 => "Unsupported Media Type", 503 => "Service Unavailable", _ => "Error" };
        string headers = $"HTTP/1.1 {status} {reason}\r\nContent-Type: {contentType}\r\nContent-Length: {body.Length}\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(headers), token).ConfigureAwait(false);
        if (body.Length > 0) await stream.WriteAsync(body, token).ConfigureAwait(false);
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener?.Stop();
        try { _worker?.Wait(TimeSpan.FromSeconds(1)); } catch { }
        _stop.Dispose();
    }
}
