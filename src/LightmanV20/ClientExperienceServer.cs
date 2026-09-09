using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace LightmanZapravka3D;

/// <summary>
/// Superficie HTTP publica y minima para la experiencia del cliente.
///
/// El objeto devuelto por <paramref name="experiences"/> puede ser una lista o un
/// objeto con una propiedad "experiences". Antes de publicarlo, este
/// servidor reconstruye cada entrada usando exclusivamente id, publicTitle,
/// categoria y tagline. De ese modo, aunque por error se entregue un
/// ShowLaunchDefinition completo, nunca se serializan playlists, rutas, IPs ni
/// otros datos operativos.
/// </summary>
internal sealed class ClientExperienceServer : IDisposable
{
    private const int MaxHeaderBytes = 16_384;
    private const int MaxBodyBytes = 4_096;
    private const string GenericFailure = "La experiencia no esta disponible en este momento.";

    private readonly string _webFolder;
    private readonly int _port;
    private readonly Func<object> _experiences;
    private readonly Func<string, TabletActionResult> _playExperience;
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _handlerSlots = new(8, 8);
    private readonly SemaphoreSlim _playSlot = new(1, 1);
    private TcpListener? _listener;
    private Task? _worker;

    public ClientExperienceServer(string webFolder, int port, Func<object> experiences,
        Func<string, TabletActionResult> playExperience)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(webFolder);
        ArgumentNullException.ThrowIfNull(experiences);
        ArgumentNullException.ThrowIfNull(playExperience);
        if (port is < 0 or > 65_535) throw new ArgumentOutOfRangeException(nameof(port));

        _webFolder = webFolder;
        _port = port;
        _experiences = experiences;
        _playExperience = playExperience;
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
                if (!_handlerSlots.Wait(0))
                {
                    client.Dispose();
                    continue;
                }

                _ = Task.Run(async () =>
                {
                    try { await HandleAsync(client, token).ConfigureAwait(false); }
                    finally { _handlerSlots.Release(); }
                }, CancellationToken.None);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (SocketException) when (token.IsCancellationRequested) { }
    }

    private async Task HandleAsync(TcpClient client, CancellationToken serverToken)
    {
        using (client)
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(serverToken))
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(7));
            var token = timeout.Token;

            try
            {
                client.NoDelay = true;
                NetworkStream stream = client.GetStream();
                var request = await ReadRequestAsync(stream, token).ConfigureAwait(false);
                if (request is null) return;

                var (method, path, body, headers) = request.Value;

                // No CORS: los comandos solo deben salir de la pagina servida aqui.
                if (method == "OPTIONS")
                {
                    await JsonAsync(stream, 403, new { ok = false, error = "Origen no autorizado." }, token).ConfigureAwait(false);
                    return;
                }

                if (method == "GET" && path == "/health")
                {
                    await JsonAsync(stream, 200, new { ok = true, service = "client-experience" }, token).ConfigureAwait(false);
                    return;
                }

                if (method == "GET" && path == "/api/experiences")
                {
                    PublicCatalog catalog;
                    try { catalog = CapturePublicCatalog(); }
                    catch
                    {
                        await JsonAsync(stream, 503, new { ok = false, error = GenericFailure }, token).ConfigureAwait(false);
                        return;
                    }

                    await JsonAsync(stream, 200, new
                    {
                        ok = true,
                        experiences = catalog.Experiences,
                        activeExperienceId = catalog.ActiveExperienceId,
                    }, token).ConfigureAwait(false);
                    return;
                }

                if (method == "POST" && path == "/api/experience/play")
                {
                    if (!ValidJsonRequest(headers, out int status, out string error))
                    {
                        await JsonAsync(stream, status, new { ok = false, error }, token).ConfigureAwait(false);
                        return;
                    }

                    string id;
                    try
                    {
                        using var json = JsonDocument.Parse(body);
                        if (json.RootElement.ValueKind != JsonValueKind.Object ||
                            !json.RootElement.TryGetProperty("id", out var idElement) ||
                            idElement.ValueKind != JsonValueKind.String)
                            throw new JsonException();
                        id = idElement.GetString() ?? "";
                    }
                    catch (JsonException)
                    {
                        await JsonAsync(stream, 400, new { ok = false, error = "Solicitud invalida." }, token).ConfigureAwait(false);
                        return;
                    }

                    PublicCatalog catalog;
                    try { catalog = CapturePublicCatalog(); }
                    catch
                    {
                        await JsonAsync(stream, 503, new { ok = false, error = GenericFailure }, token).ConfigureAwait(false);
                        return;
                    }

                    // La llamada nunca recibe nombres de playlist ni texto arbitrario:
                    // solo el ID canonico que ya aparecia en el catalogo publico.
                    var experience = catalog.Experiences.FirstOrDefault(item =>
                        item.Id.Equals(id, StringComparison.Ordinal));
                    if (experience is null || !experience.Enabled)
                    {
                        await JsonAsync(stream, 400, new { ok = false, error = "Experiencia no disponible." }, token).ConfigureAwait(false);
                        return;
                    }

                    if (!_playSlot.Wait(0))
                    {
                        await JsonAsync(stream, 409, new { ok = false, error = "Hay una seleccion en curso." }, token).ConfigureAwait(false);
                        return;
                    }

                    TabletActionResult result;
                    try { result = _playExperience(experience.Id); }
                    catch { result = TabletActionResult.Fail(GenericFailure); }
                    finally { _playSlot.Release(); }

                    // TabletActionResult.Error es deliberadamente privado: puede
                    // contener informacion de xSchedule o de la maquina operadora.
                    await JsonAsync(stream, result.Ok ? 200 : 503,
                        result.Ok
                            ? new { ok = true, id = experience.Id }
                            : new { ok = false, error = GenericFailure }, token).ConfigureAwait(false);
                    return;
                }

                if (method == "GET")
                {
                    string file = path switch
                    {
                        "/" or "/client.html" => "client.html",
                        "/client.css" => "client.css",
                        "/client.js" => "client.js",
                        _ => "",
                    };

                    if (file.Length > 0)
                    {
                        string fullPath = Path.Combine(_webFolder, file);
                        if (File.Exists(fullPath))
                        {
                            string contentType = Path.GetExtension(file) switch
                            {
                                ".css" => "text/css; charset=utf-8",
                                ".js" => "text/javascript; charset=utf-8",
                                _ => "text/html; charset=utf-8",
                            };
                            byte[] content = await File.ReadAllBytesAsync(fullPath, token).ConfigureAwait(false);
                            await RespondAsync(stream, 200, contentType, content, token).ConfigureAwait(false);
                            return;
                        }
                    }
                }

                await JsonAsync(stream, 404, new { ok = false, error = "No encontrado." }, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch
            {
                try
                {
                    await JsonAsync(client.GetStream(), 400,
                        new { ok = false, error = "Solicitud invalida." }, CancellationToken.None).ConfigureAwait(false);
                }
                catch { }
            }
        }
    }

    /// <summary>
    /// Convierte un catalogo potencialmente rico en un DTO publico con lista
    /// blanca. Los nombres se buscan sin distinguir mayusculas para admitir tanto
    /// records anonimos camelCase como los tipos C# del controlador.
    /// </summary>
    private PublicCatalog CapturePublicCatalog()
    {
        object source = _experiences() ?? throw new InvalidDataException("Catalogo vacio.");
        JsonElement root = JsonSerializer.SerializeToElement(source, LiveEngine.Json);

        JsonElement items;
        JsonElement container = root;
        if (root.ValueKind == JsonValueKind.Array)
        {
            items = root;
        }
        else if (root.ValueKind == JsonValueKind.Object &&
                 TryProperty(root, "experiences", out items) &&
                 items.ValueKind == JsonValueKind.Array)
        {
            // El contenedor puede aportar solo un estado de presentacion seguro.
        }
        else
        {
            throw new InvalidDataException("Catalogo invalido.");
        }

        var publicItems = new List<PublicExperience>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            if (TryBoolean(item, "publicEnabled", out bool publicEnabled) && !publicEnabled) continue;
            bool enabled = !TryBoolean(item, "enabled", out bool configuredEnabled) || configuredEnabled;

            string id = StringProperty(item, "id");
            // "Title" pertenece al control interno. Solo PublicTitle cruza el
            // limite publico, incluso si ambos valores hoy fueran iguales.
            string publicTitle = StringProperty(item, "publicTitle");
            if (!ValidId(id) || string.IsNullOrWhiteSpace(publicTitle) || !ids.Add(id)) continue;

            publicItems.Add(new PublicExperience(
                id,
                LimitText(publicTitle, 80),
                LimitText(StringProperty(item, "category"), 40),
                LimitText(StringProperty(item, "tagline"), 160),
                enabled));
        }

        string? activeId = null;
        if (container.ValueKind == JsonValueKind.Object)
        {
            string candidate = FirstNonEmpty(
                StringProperty(container, "activeExperienceId"),
                StringProperty(container, "activeId"));
            if (ids.Contains(candidate)) activeId = candidate;
        }

        return new PublicCatalog(publicItems, activeId);
    }

    private static bool ValidJsonRequest(Dictionary<string, string> headers, out int status, out string error)
    {
        status = 415;
        error = "Se requiere Content-Type application/json.";
        if (!headers.TryGetValue("Content-Type", out string? contentType) ||
            !contentType.Split(';', 2)[0].Trim().Equals("application/json", StringComparison.OrdinalIgnoreCase)) return false;

        if (headers.TryGetValue("Origin", out string? origin))
        {
            status = 403;
            error = "Origen no autorizado.";
            if (!headers.TryGetValue("Host", out string? host) ||
                !Uri.TryCreate(origin, UriKind.Absolute, out var uri) ||
                !uri.Authority.Equals(host, StringComparison.OrdinalIgnoreCase)) return false;
        }

        status = 200;
        error = "";
        return true;
    }

    private static async Task<(string Method, string Path, byte[] Body,
        Dictionary<string, string> Headers)?> ReadRequestAsync(NetworkStream stream, CancellationToken token)
    {
        var data = new List<byte>(2048);
        var buffer = new byte[1024];
        int headerEnd = -1;
        while (data.Count < MaxHeaderBytes && headerEnd < 0)
        {
            int read = await stream.ReadAsync(buffer, token).ConfigureAwait(false);
            if (read <= 0) return null;
            data.AddRange(buffer.AsSpan(0, read).ToArray());
            headerEnd = FindHeaderEnd(data);
        }
        if (headerEnd < 0) throw new InvalidDataException();

        string header = Encoding.ASCII.GetString(data.Take(headerEnd).ToArray());
        string[] lines = header.Split("\r\n", StringSplitOptions.None);
        string[] requestLine = lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (requestLine.Length != 3 ||
            requestLine[0].Length > 8 ||
            requestLine[1].Length > 2048 ||
            requestLine[2] is not ("HTTP/1.0" or "HTTP/1.1"))
            throw new InvalidDataException();

        int contentLength = 0;
        bool foundContentLength = false;
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in lines.Skip(1))
        {
            int colon = line.IndexOf(':');
            if (colon <= 0) throw new InvalidDataException();
            string name = line[..colon].Trim();
            string value = line[(colon + 1)..].Trim();
            if (name.Length == 0 || headers.ContainsKey(name)) throw new InvalidDataException();
            headers[name] = value;

            if (name.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException();
            if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
            {
                if (foundContentLength || !int.TryParse(value, out contentLength) ||
                    contentLength is < 0 or > MaxBodyBytes) throw new InvalidDataException();
                foundContentLength = true;
            }
        }

        int bodyStart = headerEnd + 4;
        while (data.Count - bodyStart < contentLength)
        {
            int read = await stream.ReadAsync(buffer, token).ConfigureAwait(false);
            if (read <= 0) throw new EndOfStreamException();
            data.AddRange(buffer.AsSpan(0, read).ToArray());
        }

        string rawPath = requestLine[1].Split('?', 2)[0];
        string path = Uri.UnescapeDataString(rawPath);
        return (requestLine[0].ToUpperInvariant(), path,
            data.Skip(bodyStart).Take(contentLength).ToArray(), headers);
    }

    private static int FindHeaderEnd(List<byte> data)
    {
        for (int i = 3; i < data.Count; i++)
            if (data[i - 3] == 13 && data[i - 2] == 10 && data[i - 1] == 13 && data[i] == 10)
                return i - 3;
        return -1;
    }

    private static bool TryProperty(JsonElement value, string name, out JsonElement property)
    {
        foreach (JsonProperty candidate in value.EnumerateObject())
        {
            if (candidate.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                property = candidate.Value;
                return true;
            }
        }
        property = default;
        return false;
    }

    private static string StringProperty(JsonElement value, string name) =>
        TryProperty(value, name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()?.Trim() ?? ""
            : "";

    private static bool TryBoolean(JsonElement value, string name, out bool result)
    {
        if (TryProperty(value, name, out var property) && property.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            result = property.GetBoolean();
            return true;
        }
        result = false;
        return false;
    }

    private static string FirstNonEmpty(string first, string second) =>
        string.IsNullOrWhiteSpace(first) ? second : first;

    private static string LimitText(string value, int length) =>
        value.Length <= length ? value : value[..length];

    private static bool ValidId(string id)
    {
        if (id.Length is < 1 or > 64) return false;
        foreach (char character in id)
            if (!(character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_'))
                return false;
        return true;
    }

    private static Task JsonAsync(NetworkStream stream, int status, object value, CancellationToken token) =>
        RespondAsync(stream, status, "application/json; charset=utf-8",
            JsonSerializer.SerializeToUtf8Bytes(value, LiveEngine.Json), token);

    private static async Task RespondAsync(NetworkStream stream, int status, string contentType,
        byte[] body, CancellationToken token)
    {
        string reason = status switch
        {
            200 => "OK",
            400 => "Bad Request",
            403 => "Forbidden",
            404 => "Not Found",
            409 => "Conflict",
            415 => "Unsupported Media Type",
            503 => "Service Unavailable",
            _ => "Error",
        };
        string headers =
            $"HTTP/1.1 {status} {reason}\r\n" +
            $"Content-Type: {contentType}\r\n" +
            $"Content-Length: {body.Length}\r\n" +
            "Cache-Control: no-store\r\n" +
            "Content-Security-Policy: default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'; form-action 'none'\r\n" +
            "Permissions-Policy: camera=(), microphone=(), geolocation=()\r\n" +
            "Referrer-Policy: no-referrer\r\n" +
            "X-Content-Type-Options: nosniff\r\n" +
            "X-Frame-Options: DENY\r\n" +
            "Connection: close\r\n\r\n";
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

    private sealed record PublicExperience(string Id, string PublicTitle, string Category, string Tagline, bool Enabled);
    private sealed record PublicCatalog(IReadOnlyList<PublicExperience> Experiences, string? ActiveExperienceId);
}
