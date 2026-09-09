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
    {
        _webFolder = webFolder;
        _port = port;
        _state = state;
        _selectSource = selectSource;
        _setTest = setTest;
        _playShow = playShow;
        _pauseShow = pauseShow;
        _stopShow = stopShow;
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
