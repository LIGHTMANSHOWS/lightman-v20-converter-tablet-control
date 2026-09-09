using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LightmanZapravka3D;

internal sealed record ArtNetFrame(
    byte[] Colors,
    long PacketCount,
    DateTime LastPacketUtc,
    int ReceivedExpectedUniverses,
    IReadOnlyCollection<string> Sources);

internal readonly record struct ArtNetChannelAddress(int Universe, int Channel);

internal sealed record ArtNetSnapshot(Dictionary<int, byte[]> Universes, long PacketCount,
    DateTime LastPacketUtc, IReadOnlyCollection<string> Sources);

internal readonly record struct ArtNetPixelAddress(
    ArtNetChannelAddress Red,
    ArtNetChannelAddress Green,
    ArtNetChannelAddress Blue);

internal sealed class ArtNetReceiver : IDisposable
{
    public const int Port = 6454;
    private const int ChannelsPerUniverse = 512;
    private const int PixelsPerUniverse = 170;

    private sealed class PatchFile
    {
        [JsonPropertyName("configuration")]
        public PatchConfiguration Configuration { get; set; } = new();

        [JsonPropertyName("outputs")]
        public List<PatchOutput> Outputs { get; set; } = [];

        [JsonPropertyName("elements")]
        public List<PatchElement> Elements { get; set; } = [];
    }

    private sealed class PatchConfiguration
    {
        [JsonPropertyName("visible_pixels")]
        public int VisiblePixels { get; set; }

        [JsonPropertyName("used_universes")]
        public int UsedUniverses { get; set; }

        [JsonPropertyName("transport")]
        public string Transport { get; set; } = "";

        [JsonPropertyName("active_pixels")]
        public int ActivePixels { get; set; }
    }

    private sealed class PatchOutput
    {
        [JsonPropertyName("universe_start")]
        public int UniverseStart { get; set; }

        [JsonPropertyName("used_universe_end")]
        public int UsedUniverseEnd { get; set; }
    }

    private sealed class PatchElement
    {
        [JsonPropertyName("element_id")]
        public string ElementId { get; set; } = "";

        [JsonPropertyName("controller")]
        public JsonElement Controller { get; set; }

        [JsonPropertyName("output")]
        public int Output { get; set; }

        [JsonPropertyName("pixel_start")]
        public int PixelStart { get; set; }

        [JsonPropertyName("pixel_count")]
        public int PixelCount { get; set; }

        [JsonPropertyName("direction")]
        public string Direction { get; set; } = "bottom-top";

        [JsonPropertyName("base_universe")]
        public int BaseUniverse { get; set; }

        [JsonPropertyName("active")]
        public bool Active { get; set; } = true;

        [JsonPropertyName("kind")]
        public string Kind { get; set; } = "";
    }

    private static readonly PatchFile Patch = LoadPatch();
    private static readonly ArtNetPixelAddress[] PixelAddresses = BuildPixelAddresses(Patch);
    private static readonly HashSet<int> ExpectedUniverseIds = BuildExpectedUniverseIds(Patch);

    public static int ExpectedUniverses => ExpectedUniverseIds.Count;
    public static int TotalVisiblePixels => PixelAddresses.Length;

    private sealed record UniverseData(byte[] Data, DateTime ReceivedUtc);
    private sealed class SourceBuffer
    {
        public Dictionary<int, UniverseData> Universes { get; } = new();
        public long PacketCount;
        public DateTime LastPacketUtc;
    }
    private readonly object _sourceGate = new();
    private readonly Dictionary<string, SourceBuffer> _sources = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _autoSources = new();
    private readonly HashSet<string> _localAddresses = GetLocalAddresses();
    private HashSet<int> _additionalUniverses = [];
    private readonly int _listenPort;
    private static readonly TimeSpan SignalTimeout = TimeSpan.FromSeconds(2);
    private readonly List<UdpClient> _clients = [];
    private CancellationTokenSource? _cancellation;
    private readonly List<Task> _receiveTasks = [];
    public ArtNetReceiver(int listenPort = Port) => _listenPort = listenPort;
    public int ListeningPort => (_clients.FirstOrDefault()?.Client.LocalEndPoint as IPEndPoint)?.Port ?? _listenPort;
    public IReadOnlyCollection<string> LocalAddresses => _localAddresses.Order().ToArray();
    public string[] AvailableSources
    {
        get
        {
            lock (_sourceGate)
                return _sources.Keys.Select(k => k.StartsWith("tracking:", StringComparison.OrdinalIgnoreCase) ? k[9..] : k)
                    .Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray();
        }
    }
    private static HashSet<string> GetLocalAddresses()
    {
        var addresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "127.0.0.1", "127.0.0.2", "127.0.0.3", "127.0.0.4" };
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces().Where(a => a.OperationalStatus == OperationalStatus.Up))
            foreach (var address in adapter.GetIPProperties().UnicastAddresses)
                if (address.Address.AddressFamily == AddressFamily.InterNetwork &&
                    address.DuplicateAddressDetectionState == DuplicateAddressDetectionState.Preferred &&
                    !address.Address.ToString().StartsWith("169.254.", StringComparison.Ordinal))
                    addresses.Add(address.Address.ToString());
        return addresses;
    }
    private bool IsLocal(string ip) => _localAddresses.Contains(ip) ||
        (IPAddress.TryParse(ip, out var address) && IPAddress.IsLoopback(address));
    public void ResetAutoSource(string input)
    {
        input = input switch { "local" => "resolume", "unity" => "tracking", _ => input };
        lock (_sourceGate) _autoSources.Remove(input);
    }

    public bool IsRunning => _clients.Count > 0;

    public void Start()
    {
        if (_clients.Count > 0) return;
        _cancellation = new CancellationTokenSource();
        try
        {
            if (_listenPort == 0)
            {
                AddListener(IPAddress.Loopback, 0, "resolume", _cancellation.Token);
                return;
            }

            // Separate destination addresses make local applications selectable even
            // when Windows reports 127.0.0.1 as the sender for both of them.
            AddListener(IPAddress.Parse("127.0.0.2"), _listenPort, "resolume", _cancellation.Token);
            AddListener(IPAddress.Parse("127.0.0.3"), _listenPort, "xlights", _cancellation.Token);
            AddListener(IPAddress.Parse("127.0.0.4"), _listenPort, "tracking", _cancellation.Token);
            foreach (var address in _localAddresses.Select(IPAddress.Parse)
                         .Where(a => !IPAddress.IsLoopback(a)).Distinct())
                AddListener(address, _listenPort, "tracking", _cancellation.Token);
        }
        catch
        {
            _cancellation.Cancel();
            foreach (var client in _clients) client.Dispose();
            _clients.Clear();
            _receiveTasks.Clear();
            _cancellation.Dispose();
            _cancellation = null;
            throw;
        }
    }

    private void AddListener(IPAddress address, int port, string ingress, CancellationToken cancellationToken)
    {
        var client = new UdpClient(AddressFamily.InterNetwork) { ExclusiveAddressUse = true };
        try
        {
            client.Client.Bind(new IPEndPoint(address, port));
            client.EnableBroadcast = true;
            _clients.Add(client);
            _receiveTasks.Add(ReceiveLoopAsync(client, ingress, cancellationToken));
        }
        catch
        {
            client.Dispose();
            throw new InvalidOperationException($"No se pudo reservar {address}:{port}. Cierra otra versión de V20 y vuelve a abrir esta.");
        }
    }

    public ArtNetFrame CaptureFrame(string input = "local", string? sourceIp = null)
    {
        var snapshot = CaptureUniverses(input, sourceIp);
        return new ArtNetFrame(MapUniverses(snapshot.Universes), snapshot.PacketCount,
            snapshot.LastPacketUtc, ExpectedUniverseIds.Count(snapshot.Universes.ContainsKey), snapshot.Sources);
    }

    public void SetAdditionalUniverses(IEnumerable<int> universes)
    {
        lock (_sourceGate) _additionalUniverses = universes.ToHashSet();
    }

    public ArtNetSnapshot CaptureUniverses(string input = "local", string? sourceIp = null)
    {
        input = input switch { "local" => "resolume", "unity" => "tracking", _ => input };
        if (input is not ("resolume" or "xlights" or "tracking")) throw new ArgumentException("Entrada desconocida.", nameof(input));
        Dictionary<int, byte[]> universes = new();
        long packetCount = 0;
        var lastPacketUtc = DateTime.MinValue;
        string? selected = input is "resolume" or "xlights" ? input :
            string.IsNullOrWhiteSpace(sourceIp) ? null : "tracking:" + sourceIp.Trim();
        var now = DateTime.UtcNow;
        lock (_sourceGate)
        {
            // Auto locks one sender per input. No per-universe merging or
            // automatic failover. Applying Auto again explicitly re-arms it.
            if (selected is null && !_autoSources.TryGetValue(input, out selected))
            {
                selected = _sources.Where(s => s.Key.StartsWith("tracking:", StringComparison.OrdinalIgnoreCase) && now-s.Value.LastPacketUtc <= SignalTimeout)
                    .OrderByDescending(s => s.Value.LastPacketUtc).Select(s => s.Key).FirstOrDefault();
                if (selected is not null) _autoSources[input] = selected;
            }
            if (selected is not null && _sources.TryGetValue(selected, out var source))
            {
                packetCount = source.PacketCount;
                lastPacketUtc = source.LastPacketUtc;
                universes = source.Universes.Where(p => now-p.Value.ReceivedUtc <= SignalTimeout)
                    .ToDictionary(p => p.Key, p => p.Value.Data);
            }
        }
        var display = selected is null ? Array.Empty<string>() : new[] {
            selected switch { "resolume" => "Resolume · 127.0.0.2", "xlights" => "xLights · 127.0.0.3",
                _ when selected.StartsWith("tracking:") => "Tracking · " + selected[9..], _ => selected }
        };
        return new ArtNetSnapshot(universes, packetCount, lastPacketUtc, display);
    }

    internal static byte[] MapUniverses(Dictionary<int, byte[]> universes)
    {
        var colors = new byte[PixelAddresses.Length * 3];
        for (var pixel = 0; pixel < PixelAddresses.Length; pixel++)
        {
            var address = PixelAddresses[pixel];
            var target = pixel * 3;
            colors[target] = ReadChannel(universes, address.Red);
            colors[target + 1] = ReadChannel(universes, address.Green);
            colors[target + 2] = ReadChannel(universes, address.Blue);
        }

        return colors;
    }

    private static byte ReadChannel(Dictionary<int, byte[]> universes, ArtNetChannelAddress address)
    {
        return universes.TryGetValue(address.Universe, out var data) && address.Channel >= 0 && address.Channel < data.Length
            ? data[address.Channel]
            : (byte)0;
    }

    public static bool TryParseArtDmx(ReadOnlySpan<byte> packet, out int universe, out byte[] data)
    {
        universe = 0;
        data = [];
        if (packet.Length < 18) return false;
        ReadOnlySpan<byte> id = "Art-Net\0"u8;
        if (!packet[..8].SequenceEqual(id)) return false;
        if (packet[8] != 0x00 || packet[9] != 0x50) return false;
        universe = packet[14] | (packet[15] << 8);
        var length = (packet[16] << 8) | packet[17];
        if (length < 2 || length > ChannelsPerUniverse || packet.Length < 18 + length) return false;
        data = packet.Slice(18, length).ToArray();
        return true;
    }

    internal void InjectUniverseForTest(int universe, byte[] data, string source = "127.0.0.1", DateTime? utc = null)
    {
        var ingress = source switch
        {
            "127.0.0.3" => "xlights",
            "127.0.0.4" => "tracking",
            _ => IsLocal(source) ? "resolume" : "tracking",
        };
        StoreUniverse(universe, data, source, ingress, utc);
    }

    private void StoreUniverse(int universe, byte[] data, string source, string ingress, DateTime? utc = null)
    {
        lock (_sourceGate)
        {
            if (!ExpectedUniverseIds.Contains(universe) && !_additionalUniverses.Contains(universe)) return;
            var key = ingress == "tracking" ? "tracking:" + source : ingress;
            if (!_sources.TryGetValue(key, out var buffer))
            {
                if (_sources.Count >= 16)
                {
                    var oldest = _sources.OrderBy(p => p.Value.LastPacketUtc).First().Key;
                    _sources.Remove(oldest);
                }
                _sources[key] = buffer = new SourceBuffer();
            }
            var received = utc ?? DateTime.UtcNow;
            buffer.Universes[universe] = new UniverseData(data, received);
            buffer.LastPacketUtc = received;
            buffer.PacketCount++;
        }
    }

    private async Task ReceiveLoopAsync(UdpClient client, string ingress, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var result = await client.ReceiveAsync(cancellationToken).ConfigureAwait(false);
                if (!TryParseArtDmx(result.Buffer, out var universe, out var data)) continue;
                StoreUniverse(universe, data, result.RemoteEndPoint.Address.ToString(), ingress);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static PatchFile LoadPatch()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Patch", "SHOW-V20-MINIMAL.json");
        if (!File.Exists(path))
            throw new FileNotFoundException("No se encontró el mapa ETH01 del stand.", path);
        var patch = JsonSerializer.Deserialize<PatchFile>(File.ReadAllText(path));
        if (patch is null || patch.Elements.Count == 0 || patch.Configuration.VisiblePixels != patch.Elements.Sum(e => e.PixelCount))
            throw new InvalidOperationException("El mapa ETH01 está incompleto o tiene una cantidad de píxeles incoherente.");
        foreach (var output in patch.Elements.Where(e => e.Active && e.Kind != "flag").GroupBy(e => (e.Controller.ToString(), e.Output)))
            if (output.Sum(e => e.PixelCount) > 800)
                throw new InvalidOperationException($"La salida {output.Key} supera 800 píxeles de neón.");
        if (patch.Configuration.Transport == "continuous-rgb-170")
        {
            var next = 0;
            foreach (var element in patch.Elements.Where(e => e.Active).OrderBy(e => e.PixelStart))
            {
                if (element.BaseUniverse != 0 || element.PixelStart != next)
                    throw new InvalidOperationException($"Hueco o solapamiento en el puente: {element.ElementId}.");
                next += element.PixelCount;
            }
            if (next != patch.Configuration.ActivePixels)
                throw new InvalidOperationException("Cantidad de píxeles del puente incoherente.");
        }
        return patch;
    }

    private static HashSet<int> BuildExpectedUniverseIds(PatchFile patch)
    {
        var universes = new HashSet<int>();
        foreach (var element in patch.Elements.Where(e => e.Active))
            for (var pixel = 0; pixel < element.PixelCount; pixel++)
                universes.Add(element.BaseUniverse + (element.PixelStart + pixel) / PixelsPerUniverse);
        if (universes.Count != patch.Configuration.UsedUniverses)
            throw new InvalidOperationException($"Mapa ETH01 inválido: {universes.Count} universos activos, se esperaban {patch.Configuration.UsedUniverses}.");
        return universes;
    }

    private static ArtNetPixelAddress[] BuildPixelAddresses(PatchFile patch)
    {
        var addresses = new List<ArtNetPixelAddress>(patch.Configuration.VisiblePixels);
        foreach (var element in patch.Elements)
        {
            if (element.Output is < 0 or > 3)
                throw new InvalidOperationException($"Salida inválida en {element.ElementId}.");
            for (var visiblePixel = 0; visiblePixel < element.PixelCount; visiblePixel++)
            {
                if (!element.Active)
                {
                    var black = new ArtNetChannelAddress(-1, 0);
                    addresses.Add(new ArtNetPixelAddress(black, black, black));
                    continue;
                }
                var sourcePixel = ResolveSourcePixel(element.Direction, visiblePixel, element.PixelCount);
                var outputPixel = element.PixelStart + sourcePixel;
                // WLED-aligned patches intentionally reserve the unused tail of
                // an universe before the next physical output. Validate against
                // the complete Art-Net address space, not the active-pixel count.
                var limit = patch.Configuration.UsedUniverses * PixelsPerUniverse;
                if (outputPixel < 0 || outputPixel >= limit)
                    throw new InvalidOperationException($"{element.ElementId} excede los {limit} píxeles de su mapa de entrada.");

                // El fixture banderapyton y las líneas del stand trabajan en RGB.
                addresses.Add(new ArtNetPixelAddress(
                    ResolvePixelComponent(element.BaseUniverse, outputPixel, 0),
                    ResolvePixelComponent(element.BaseUniverse, outputPixel, 1),
                    ResolvePixelComponent(element.BaseUniverse, outputPixel, 2)));
            }
        }
        if (addresses.Count != patch.Configuration.VisiblePixels)
            throw new InvalidOperationException($"Patch V20 inválido: {addresses.Count} píxeles visibles, se esperaban {patch.Configuration.VisiblePixels}.");
        return addresses.ToArray();
    }

    private static int ResolveSourcePixel(string direction, int visiblePixel, int pixelCount)
    {
        if (direction == "reverse")
            return pixelCount - 1 - visiblePixel;

        if (direction == "row-snake-100")
            return visiblePixel / 100 * 100 + (visiblePixel / 100 % 2 == 0 ? visiblePixel % 100 : 99 - visiblePixel % 100);

        if (direction == "row-snake-100-right")
            return visiblePixel / 100 * 100 + (visiblePixel / 100 % 2 == 0 ? 99 - visiblePixel % 100 : visiblePixel % 100);

        if (direction == "matrix-snake-14")
        {
            const int width = 60;
            const int blockHeight = 14;
            var x = visiblePixel % width;
            var y = visiblePixel / width;
            var output = y / blockHeight;
            var localY = y % blockHeight;
            var physicalX = localY % 2 == 0 ? x : width - 1 - x;
            return output * width * blockHeight + localY * width + physicalX;
        }

        return visiblePixel;
    }

    private static ArtNetChannelAddress ResolvePixelComponent(int baseUniverse, int outputPixel, int component)
    {
        return new ArtNetChannelAddress(
            baseUniverse + outputPixel / PixelsPerUniverse,
            (outputPixel % PixelsPerUniverse) * 3 + component);
    }

    public void Dispose()
    {
        _cancellation?.Cancel();
        foreach (var client in _clients) client.Dispose();
        _cancellation?.Dispose();
        _clients.Clear();
        _cancellation = null;
        _receiveTasks.Clear();
    }
}
