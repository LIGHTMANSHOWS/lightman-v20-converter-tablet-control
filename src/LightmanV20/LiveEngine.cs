using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;

namespace LightmanZapravka3D;

internal sealed class LiveRoute
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "Ruta";
    public int Pixels { get; set; } = 100;
    public int Start { get; set; }
    public int Channel { get; set; } = 1;
    public string Protocol { get; set; } = "Art-Net";
    public string Ip { get; set; } = "";
    public int Dest { get; set; }
    public int DestChannel { get; set; } = 1;
    public int Ident { get; set; } = 1;
    public bool Enabled { get; set; }
    public string Source { get; set; } = "Entrada";
    public string Pattern { get; set; } = "Color fijo";
    public double Brightness { get; set; } = 100;
    public bool Reverse { get; set; }
}

internal sealed class LiveTreeTest
{
    public bool Enabled { get; set; }
    public string Strip { get; set; } = "A01";
    public string Direction { get; set; } = "forward";
    public string Pattern { get; set; } = "Recorrido";
    public string Color { get; set; } = "#00ff88";
    public double Brightness { get; set; } = 25;
    public double Speed { get; set; } = 25;
}

internal sealed class LiveHouseMark
{
    public string Name { get; set; } = "Marca";
    public int Pixel { get; set; }
}

internal sealed class LiveHouseTest
{
    public bool Enabled { get; set; }
    public int Pixel { get; set; } = 1;
    public string Mode { get; set; } = "Pixel";
    public string Color { get; set; } = "#ffffff";
    public double Brightness { get; set; } = 25;
    public string Ip { get; set; } = "192.168.1.93";
    public int Dest { get; set; } = 7;
    public int DestChannel { get; set; } = 31;
    public bool MaskHidden { get; set; }
    public int HiddenStart { get; set; } = 40;
    public int HiddenEnd { get; set; } = 40;
    public List<LiveHouseMark> Marks { get; set; } = [];
}

internal sealed class LiveConfig
{
    public int Version { get; set; } = 1;
    public List<LiveRoute> Routes { get; set; } = LiveEngine.DefaultRoutes();
    public bool Sending { get; set; }
    public bool Receiving { get; set; } = true;
    public string WifiBind { get; set; } = "0.0.0.0";
    public string ArtnetBind { get; set; } = "0.0.0.0";
    public double Master { get; set; } = 100;
    public bool Blackout { get; set; }
    public bool GeneralTest { get; set; }
    public int Fps { get; set; } = 30;
    public string Color { get; set; } = "#00e5c0";
    public double Speed { get; set; } = 40;
    public LiveTreeTest TreeTest { get; set; } = new();
    public LiveHouseTest HouseTest { get; set; } = new();
}

internal sealed record LivePreview(string Id, string Name, string Status, string Colors);
internal sealed record LiveRender(Dictionary<int, byte[]> VisualUniverses,
    List<byte[]> RouteColors, List<LivePreview> Previews);
internal sealed record LiveMonitorSnapshot(Dictionary<string, byte[]> Frames, long Tx, string Error, bool Retrying);
internal sealed record LiveDatagram(string Bind, string Ip, int Port, int Slot, byte[][] Packets, byte[][] Black)
{
    public string Key => $"{Bind}/{Ip}/{Port}/{Slot}";
}

/// <summary>Live transport and tester. The only UDP receiver belongs to V20.
/// Testing is rendered in memory; UDP output requires the master send gate and an enabled route,
/// or the explicitly enabled isolated house tester.</summary>
internal sealed class LiveEngine : IDisposable
{
    internal static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };
    internal static readonly int[] TreeInputs = [90, 94, 98, 102];
    internal const int HousePixels = 400;
    private static readonly List<LiveRoute> ReferenceRoutes = DefaultRoutes();
    private readonly object _gate = new();
    private readonly ArtNetReceiver _receiver;
    private readonly string? _settingsPath;
    private readonly Action<LiveDatagram,bool>? _testTransmit;
    private readonly bool _automaticOutput;
    private DateTime _retryAfterUtc = DateTime.MinValue;
    private bool _outputRetrying;
    private readonly CancellationTokenSource _stop = new();
    private Task? _worker;
    private LiveConfig _config = new();
    private string _input = "resolume", _sourceIp = "", _error = "";
    private byte[] _colors = new byte[ArtNetReceiver.TotalVisiblePixels * 3];
    private Dictionary<string, byte[]> _outputFrames = new(StringComparer.OrdinalIgnoreCase);
    private List<LivePreview> _previews = [];
    private long _tx, _rx;
    private string[] _sources = [];
    private long _revision;

    public LiveEngine(ArtNetReceiver receiver, string? settingsPath, Action<LiveDatagram,bool>? testTransmit = null, bool automaticOutput = false)
    {
        _receiver = receiver; _settingsPath = settingsPath; _testTransmit = testTransmit; _automaticOutput = automaticOutput;
        try
        {
            var saved = settingsPath is not null && File.Exists(settingsPath)
                ? JsonSerializer.Deserialize<LiveConfig>(File.ReadAllText(settingsPath), Json) ?? new()
                : new LiveConfig();
            if (_automaticOutput) PrepareAutomatic(saved, resetRuntime:true); else MakeSafe(saved);
            Validate(saved); _config = saved;
        }
        catch (Exception ex) { _error = "Preferencias no cargadas: " + ex.Message; }
        _receiver.SetAdditionalUniverses(InputUniverses(_config));
    }

    public void Start() => _worker ??= Task.Run(WorkerAsync);
    public void SelectInput(string input, string sourceIp) { lock (_gate) { _input = input; _sourceIp = sourceIp; } }
    public byte[] Colors { get { lock (_gate) return _colors; } }
    public Dictionary<string, byte[]> OutputFrames
    {
        get { lock (_gate) return _outputFrames.ToDictionary(p => p.Key, p => p.Value.ToArray(), StringComparer.OrdinalIgnoreCase); }
    }
    public LiveMonitorSnapshot MonitorSnapshot
    {
        get
        {
            lock (_gate) return new LiveMonitorSnapshot(
                _outputFrames.ToDictionary(p => p.Key, p => p.Value.ToArray(), StringComparer.OrdinalIgnoreCase),
                _tx, _error, _outputRetrying);
        }
    }
    public LiveConfig Configuration { get { lock (_gate) return Clone(_config); } }
    public object State()
    {
        lock (_gate) return new { config = Clone(_config), revision = _revision, rx = _rx, tx = _tx, error = _error,
            previews = _previews, sources = _sources, input = _input, sourceIp = _sourceIp, generalTest = _config.GeneralTest,
            automaticOutput = _automaticOutput, previewOnly = _testTransmit is not null,
            outputRetrying = _outputRetrying, outputRetrySeconds = Math.Max(0,(_retryAfterUtc-DateTime.UtcNow).TotalSeconds),
            receiver = "0.0.0.0:6454 (compartido con V20)", addresses = AvailableBindAddresses() };
    }

    internal static LiveConfig Clone(LiveConfig s) => JsonSerializer.Deserialize<LiveConfig>(JsonSerializer.Serialize(s, Json), Json)!;
    internal static void MakeSafe(LiveConfig s)
    {
        s.Sending = false; s.GeneralTest = false; s.TreeTest.Enabled = false; s.HouseTest ??= new(); s.HouseTest.Enabled = false; s.Blackout = false;
        foreach (var r in s.Routes) r.Source = "Entrada";
    }

    internal static bool HasPhysicalDestination(string? value) =>
        IPAddress.TryParse(value,out var address) && address.AddressFamily==AddressFamily.InterNetwork &&
        !IPAddress.IsLoopback(address) && address.GetAddressBytes()[0] is >0 and <224 && !address.Equals(IPAddress.Broadcast);

    internal static void PrepareAutomatic(LiveConfig s, bool resetRuntime = false)
    {
        if (s.Routes is null) throw new ArgumentException("Faltan rutas.");
        var old = s.Routes.FirstOrDefault(r=>r.Id=="outline");
        var house = s.Routes.FirstOrDefault(r=>r.Id=="custom-casa-400");
        if (house is null)
        {
            house = new LiveRoute { Id="custom-casa-400",Name="Casa · salida 3 · 400 LED",Pixels=400,
                Start=old?.Start??122,Channel=old?.Channel??1,Protocol="Art-Net",Ip="192.168.1.93",Dest=7,DestChannel=31,
                Brightness=old?.Brightness??100,Source=old?.Source??"Entrada",Pattern=old?.Pattern??"Color fijo" };
            if (old is not null && HasPhysicalDestination(old.Ip))
            {
                house.Ip=old.Ip; house.Dest=old.Dest; house.DestChannel=old.DestChannel;
                house.Reverse=old.Reverse;
            }
            if (old is not null) s.Routes.Insert(s.Routes.IndexOf(old),house); else s.Routes.Add(house);
        }
        // One house route replaces the obsolete 446-pixel closed outline.
        // Existing custom-house addressing/levels take precedence when imported.
        if (old is not null) s.Routes.Remove(old);
        s.Sending=true;
        foreach (var route in s.Routes) route.Enabled=HasPhysicalDestination(route.Ip);
        s.HouseTest ??= new();
        if (resetRuntime)
        {
            s.GeneralTest=false; s.TreeTest ??= new(); s.TreeTest.Enabled=false; s.HouseTest.Enabled=false;
            s.Receiving=true;
            foreach (var route in s.Routes) route.Source="Entrada";
            if (s.HouseTest.HiddenStart==40 && s.HouseTest.HiddenEnd==40)
            {
                s.HouseTest.HiddenStart=47; s.HouseTest.HiddenEnd=48; s.HouseTest.MaskHidden=true;
            }
        }
    }

    public void Apply(LiveConfig incoming)
    {
        var next = Clone(incoming);
        if (_automaticOutput) PrepareAutomatic(next);
        Validate(next);
        // Validate local interface selection before touching the current live state.
        if (next.Sending)
            foreach (var bind in new[] { next.WifiBind, next.ArtnetBind })
                if (!AvailableBindAddresses().Contains(bind)) throw new ArgumentException("La IP local " + bind + " no pertenece a este equipo. Usa 0.0.0.0 (automático).");
        lock (_gate)
        {
            // Leaving the isolated house test never resumes other live routes
            // merely because the global send gate used to be enabled.
            if (!_automaticOutput && _config.HouseTest.Enabled && !next.HouseTest.Enabled) next.Sending = false;
            if (_settingsPath is not null)
            {
                var safe = Clone(next);
                if (_automaticOutput) PrepareAutomatic(safe,resetRuntime:true); else MakeSafe(safe);
                Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
                var temp = _settingsPath + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(safe, Json));
                File.Move(temp, _settingsPath, true);
            }
            _receiver.SetAdditionalUniverses(InputUniverses(next));
            _config = next; _revision++; _error = ""; _outputRetrying=false; _retryAfterUtc=DateTime.MinValue;
        }
    }

    public void SetGeneralTest(bool enabled)
    {
        lock (_gate)
        {
            var next = Clone(_config);
            next.GeneralTest = enabled;
            next.TreeTest.Enabled = false;
            next.HouseTest.Enabled = false;
            next.Receiving = !enabled;
            foreach (var route in next.Routes) route.Source = "Entrada";
            Validate(next);
            _config = next;
            _revision++;
            if (!_outputRetrying) _error = "";
        }
    }

    private static IEnumerable<int> InputUniverses(LiveConfig s) => s.Routes.SelectMany(r => Spans(r.Start, r.Channel, r.Pixels).Select(p => p.Universe));
    internal static string[] AvailableBindAddresses() => NetworkInterface.GetAllNetworkInterfaces()
        .SelectMany(a => a.GetIPProperties().UnicastAddresses).Select(a => a.Address)
        .Where(a => a.AddressFamily == AddressFamily.InterNetwork).Select(a => a.ToString())
        .Append("0.0.0.0").Append("127.0.0.1").Distinct().Order().ToArray();

    internal static List<LiveRoute> DefaultRoutes()
    {
        var routes = new List<LiveRoute>();
        for (int i = 0; i < 6; i++) routes.Add(new LiveRoute { Id = $"flag{i+1}", Name = $"Bandera {i+1}", Pixels = 2520,
            Start = i*15, Protocol = "LMP3", Ip = $"192.168.1.{(i==5?207:201+i)}", Ident = i==5?7:i+1 });
        for (int i = 0; i < 4; i++) routes.Add(new LiveRoute { Id = $"tree{i+1}",
            Name = $"Árbol {(i<2?'A':'B')} · salida {i+1} · {(i%2==0?"01–06":"07–12")}", Pixels = 600,
            Start = 90+i*4, Ip = "192.168.1.91", Dest = (i*600)/170, DestChannel = ((i*600)%170)*3+1 });
        routes.Add(new LiveRoute { Id="hangers-left",Name="Izquierda · colgantes",Pixels=800,Start=106,Ip="192.168.1.93",Dest=2,DestChannel=181 });
        routes.Add(new LiveRoute { Id="hangers-right",Name="Derecha · colgantes",Pixels=800,Start=111,Ip="192.168.1.92",Dest=2,DestChannel=181 });
        routes.Add(new LiveRoute { Id="waves-left",Name="Izquierda · ondas M/W",Pixels=400,Start=116,Ip="192.168.1.93" });
        routes.Add(new LiveRoute { Id="waves-right",Name="Derecha · ondas M/W",Pixels=400,Start=119,Ip="192.168.1.92" });
        routes.Add(new LiveRoute { Id="outline",Name="Contorno",Pixels=446,Start=122 });
        return routes;
    }

    internal static (string RouteId, int Offset, string Vertical) TreeStrip(string strip)
    {
        if (strip.Length != 3 || strip[0] is not ('A' or 'B') || !int.TryParse(strip[1..], out int n) || n is <1 or >12)
            throw new ArgumentException("Tira inválida. Usa A01–A12 o B01–B12.");
        return ($"tree{(strip[0]=='A'?0:2)+(n-1)/6+1}", (n-1)%6*100, n%2==1?"abajo → arriba":"arriba → abajo");
    }

    internal static IEnumerable<(int Universe, int Offset, int Size)> Spans(int universe, int channel, int pixels)
    {
        int offset = channel-1, remaining = checked(pixels*3);
        if (universe < 0 || channel < 1 || channel > 508 || (channel-1)%3 != 0 || pixels < 1)
            throw new ArgumentException("Dirección RGB inválida.");
        while (remaining > 0)
        {
            int size = Math.Min(510-offset, remaining);
            yield return (universe, offset, size);
            remaining -= size; universe++; offset = 0;
        }
    }

    internal static void Validate(LiveConfig s)
    {
        static void Require(bool valid, string message) { if (!valid) throw new ArgumentException(message); }
        static bool IPv4(string text) => IPAddress.TryParse(text, out var ip) && ip.AddressFamily == AddressFamily.InterNetwork;
        static bool Level(double n) => double.IsFinite(n) && n>=0 && n<=100;
        Require(s.Routes is not null && s.Routes.Count<=64, "Máximo 64 rutas.");
        Require(s.Fps>=1 && s.Fps<=40, "FPS: 1–40.");
        Require(Level(s.Master), "Master: 0–100%."); ParseColor(s.Color);
        Require(double.IsFinite(s.Speed) && s.Speed>=1 && s.Speed<=200, "Velocidad: 1–200.");
        Require(IPv4(s.WifiBind) && IPv4(s.ArtnetBind), "IP local inválida.");
        var test = s.TreeTest ?? throw new ArgumentException("Falta tester de árbol.");
        var strip = TreeStrip(test.Strip); ParseColor(test.Color);
        Require(test.Direction is "forward" or "reverse", "Dirección inválida.");
        Require(new[]{"Recorrido","Bloque 10 px","Color fijo","Inicio","Final","RGB"}.Contains(test.Pattern), "Patrón de árbol inválido.");
        Require(Level(test.Brightness) && double.IsFinite(test.Speed) && test.Speed>=1 && test.Speed<=200, "Nivel/velocidad de árbol inválidos.");
        var house = s.HouseTest ?? throw new ArgumentException("Falta tester de casa.");
        Require(house.Pixel is >=0 and <=HousePixels, "Casa: selecciona un LED entre 0 y 400.");
        Require(house.Mode is "Pixel" or "Acumulado", "Modo de casa inválido.");
        ParseColor(house.Color); Require(Level(house.Brightness), "Casa: nivel 0–100%.");
        Require(IPv4(house.Ip), "Casa: IP de destino inválida.");
        var houseIp = IPAddress.Parse(house.Ip);
        Require(!IPAddress.IsLoopback(houseIp) && houseIp.GetAddressBytes()[0] is >0 and <224 && !houseIp.Equals(IPAddress.Broadcast),
            "Casa: usa la IP unicast del controlador; la prueba visual no necesita localhost.");
        Require(Spans(house.Dest,house.DestChannel,HousePixels).Last().Universe<=32767, "Casa: dirección Art-Net fuera de rango.");
        Require(house.HiddenStart>=0 && house.HiddenEnd>=0 && house.HiddenStart+house.HiddenEnd<=HousePixels,
            "Casa: los LED ocultos deben sumar como máximo 400.");
        Require(house.Marks is not null && house.Marks.Count<=100, "Casa: máximo 100 marcas.");
        foreach (var mark in house.Marks!)
            Require(mark is not null && !string.IsNullOrWhiteSpace(mark.Name) && mark.Name.Length<=80 && mark.Pixel is >=0 and <=HousePixels,
                "Casa: marca inválida (nombre y LED entre 0 y 400).");
        Require(!house.Enabled || !test.Enabled, "Detén el tester de árbol antes de probar la casa.");
        Require(!s.GeneralTest || (!house.Enabled && !test.Enabled), "El test general no puede mezclarse con otros testers.");
        var ids = new HashSet<string>(); var ranges = new List<(string Ip,int U,int First,int Last,string Name)>(); var flags = new HashSet<string>();
        foreach (var r in s.Routes!)
        {
            Require(!string.IsNullOrWhiteSpace(r.Id) && ids.Add(r.Id), "ID de ruta vacío o repetido.");
            Require(!string.IsNullOrWhiteSpace(r.Name) && r.Name.Length<=120, "Nombre de ruta inválido.");
            Require(r.Pixels>=1 && r.Pixels<=10000, "Píxeles: 1–10000.");
            Require(r.Protocol is "Art-Net" or "LMP3", "Protocolo inválido.");
            Require(r.Source is "Entrada" or "Tester", "Fuente inválida.");
            Require(new[]{"Color fijo","Recorrido","Inicios","RGB"}.Contains(r.Pattern), "Patrón inválido.");
            Require(Level(r.Brightness), "Nivel de ruta: 0–100%.");
            Require(r.Ident>=1 && r.Ident<=255, "ID bandera: 1–255.");
            if (r.Protocol=="LMP3") Require(r.Pixels==2520, "LMP3 requiere 2520 píxeles.");
            var reference = ReferenceRoutes.FirstOrDefault(v=>v.Id==r.Id);
            if (reference is not null) Require(r.Pixels==reference.Pixels, $"{r.Name}: conserva los {reference.Pixels} píxeles del objeto V20.");
            if (r.Id=="custom-casa-400") Require(r.Pixels==400,"Casa: conserva los 400 LED físicos.");
            foreach(var (u,ch) in new[]{(r.Start,r.Channel),(r.Dest,r.DestChannel)})
                Require(Spans(u,ch,r.Pixels).Last().Universe<=32767, "Universo máximo: 32767.");
            Require(r.Ip=="" || IPv4(r.Ip), "IP destino inválida: "+r.Name);
            if (!r.Enabled) continue;
            Require(IPv4(r.Ip), "Falta IP destino: "+r.Name);
            var address = IPAddress.Parse(r.Ip);
            Require(!IPAddress.IsLoopback(address) && address.GetAddressBytes()[0] is >0 and <224 && !address.Equals(IPAddress.Broadcast),
                "Destino físico unicast requerido. El tester se visualiza internamente, sin enviar a localhost.");
            if (r.Protocol=="LMP3") Require(flags.Add(r.Ip+":"+r.Ident), "Bandera LMP3 duplicada.");
            else foreach(var span in Spans(r.Dest,r.DestChannel,r.Pixels))
            {
                Require(!ranges.Any(a=>a.Ip==r.Ip && a.U==span.Universe && a.First<span.Offset+span.Size && span.Offset<a.Last), "Canales de salida solapados: "+r.Name);
                ranges.Add((r.Ip,span.Universe,span.Offset,span.Offset+span.Size,r.Name));
            }
        }
        if (test.Enabled) Require(s.Routes.Any(r=>r.Id==strip.RouteId), "Falta la ruta de la tira seleccionada.");
    }

    private static byte[] ParseColor(string color)
    {
        if (color is null || color.Length!=7 || color[0]!='#') throw new ArgumentException("Color RGB inválido.");
        return Convert.FromHexString(color[1..]);
    }
    internal static byte[] Pattern(int count, string pattern, string color, double speed, double now)
    {
        var rgb = new byte[count*3]; var c = ParseColor(color); int head = (int)(now*speed%count);
        if (pattern=="RGB") c = ((int)now%3) switch { 0 => [255,0,0], 1 => [0,255,0], _ => [0,0,255] };
        for (int p=0;p<count;p++)
        {
            bool on = pattern switch { "Recorrido"=>p==head, "Inicios"=>p%100==0, "Inicio"=>p<5,
                "Final"=>p>=count-5, "Bloque 10 px"=>(p-head+count)%count<10, _=>true };
            if (on) c.CopyTo(rgb,p*3);
        }
        return rgb;
    }
    internal static byte[] GeneralPattern(int count, double now)
    {
        byte[][] colors = [[255,255,255],[255,0,0],[0,255,0],[0,0,255]];
        var color = colors[(int)Math.Floor(now) % colors.Length];
        var rgb = new byte[count * 3];
        for (int p = 0; p < count; p++) color.CopyTo(rgb, p * 3);
        return rgb;
    }
    private static void Process(byte[] rgb, bool reverse, double level)
    {
        if (reverse) for (int p=0;p<rgb.Length/6;p++) for (int c=0;c<3;c++)
            (rgb[p*3+c],rgb[rgb.Length-3-p*3+c]) = (rgb[rgb.Length-3-p*3+c],rgb[p*3+c]);
        for (int i=0;i<rgb.Length;i++) rgb[i]=(byte)(rgb[i]*level);
    }
    internal static byte[] HousePattern(LiveHouseTest test)
    {
        var data = new byte[HousePixels*3]; var color = ParseColor(test.Color);
        for (int p=0;p<HousePixels;p++)
        {
            bool on = test.Mode=="Acumulado" ? p<test.Pixel : test.Pixel>0 && p==test.Pixel-1;
            if (test.MaskHidden && (p<test.HiddenStart || p>=HousePixels-test.HiddenEnd)) on=false;
            if (on) color.CopyTo(data,p*3);
        }
        return data;
    }
    private static byte[] HouseColors(LiveConfig s)
    {
        if (!s.HouseTest.Enabled) return new byte[HousePixels*3];
        var rgb=HousePattern(s.HouseTest);
        Process(rgb,false,s.Blackout?0:s.Master*s.HouseTest.Brightness/10000);
        return rgb;
    }
    internal static void WriteRgb(Dictionary<int,byte[]> frames, int universe, int channel, byte[] rgb)
    {
        int pos=0;
        foreach (var span in Spans(universe,channel,rgb.Length/3))
        {
            if (!frames.TryGetValue(span.Universe,out var data)) frames[span.Universe]=data=new byte[512];
            Buffer.BlockCopy(rgb,pos,data,span.Offset,span.Size); pos+=span.Size;
        }
    }
    internal static LiveRender Render(LiveConfig s, Dictionary<int,byte[]> input, double now)
    {
        var visual = new Dictionary<int,byte[]>(); var colors = new List<byte[]>(); var previews = new List<LivePreview>();
        var selected = TreeStrip(s.TreeTest.Strip);
        foreach (var r in s.Routes)
        {
            byte[] rgb = new byte[r.Pixels*3]; bool fresh=true;
            string status="SIN SEÑAL";
            if (s.GeneralTest)
            {
                rgb=GeneralPattern(r.Pixels,now);
                Process(rgb,false,s.Blackout?0:s.Master*0.25/100);
                status="TEST GENERAL";
            }
            else if (s.HouseTest.Enabled) status="NEGRO · TEST CASA";
            else if (s.TreeTest.Enabled)
            {
                if (r.Id==selected.RouteId)
                {
                    var test = Pattern(100,s.TreeTest.Pattern,s.TreeTest.Color,s.TreeTest.Speed,now);
                    Process(test,s.TreeTest.Direction=="reverse",s.Blackout?0:s.Master*s.TreeTest.Brightness/10000);
                    Buffer.BlockCopy(test,0,rgb,selected.Offset*3,test.Length); status="TEST "+s.TreeTest.Strip;
                }
                else status="NEGRO · TEST ÁRBOL";
            }
            else
            {
                if (r.Source=="Tester") { rgb=Pattern(r.Pixels,r.Pattern,s.Color,s.Speed,now); status="TESTER"; }
                else if (s.Receiving)
                {
                    int pos=0;
                    foreach(var span in Spans(r.Start,r.Channel,r.Pixels))
                    {
                        if (!input.TryGetValue(span.Universe,out var data) || data.Length<span.Offset+span.Size) fresh=false;
                        else Buffer.BlockCopy(data,span.Offset,rgb,pos,span.Size);
                        pos+=span.Size;
                    }
                    if (!fresh) Array.Clear(rgb); else status="SEÑAL";
                }
                else status="RECEPCIÓN PAUSADA";
                Process(rgb,r.Reverse,s.Blackout?0:s.Master*r.Brightness/10000);
            }
            if (r.Id is "tree2" or "tree4" && rgb.Length==600*3)
            {
                // A09 and B10 contain 99 real LEDs. Keep the 600th reserved
                // address black so later routes stay aligned without a phantom LED.
                Array.Clear(rgb,599*3,3);
            }
            if (r.Id=="custom-casa-400" && rgb.Length==HousePixels*3)
            {
                // Physical numbering is never compacted: hidden leads remain
                // black in reception and the general tester, including reverse.
                Array.Clear(rgb,0,47*3); Array.Clear(rgb,352*3,48*3);
            }
            colors.Add(rgb); previews.Add(new(r.Id,r.Name,status,Convert.ToBase64String(rgb)));
            var reference = ReferenceRoutes.FirstOrDefault(v=>v.Id==r.Id);
            // Preview addresses are the immutable V19/V20 geometry; input/destination edits never move it.
            if (reference is not null) WriteRgb(visual,reference.Start,reference.Channel,rgb);
        }
        previews.Add(new("house-test","Casa · salida 3 · 400 LED",
            s.HouseTest.Enabled?$"TEST CASA · {s.HouseTest.Mode} {s.HouseTest.Pixel}":"CASA · PRUEBA DETENIDA",
            Convert.ToBase64String(HouseColors(s))));
        return new(visual,colors,previews);
    }

    internal static Dictionary<string, byte[]> MonitorFrames(LiveConfig s, List<byte[]> colors)
    {
        var result = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < s.Routes.Count; i++)
        {
            var route = s.Routes[i];
            var rgb = s.Sending && route.Enabled ? colors[i].ToArray() : new byte[route.Pixels * 3];
            if (route.Protocol == "LMP3")
            {
                // The wireless flag protocol transmits RGB565. Reflect that
                // quantisation in the monitor instead of displaying RGB888.
                for (int p = 0; p < route.Pixels; p++)
                {
                    int at = p * 3;
                    int r = rgb[at] >> 3, g = rgb[at + 1] >> 2, b = rgb[at + 2] >> 3;
                    rgb[at] = (byte)(r * 255 / 31);
                    rgb[at + 1] = (byte)(g * 255 / 63);
                    rgb[at + 2] = (byte)(b * 255 / 31);
                }
            }
            result[route.Id] = rgb;
        }
        return result;
    }

    internal static byte[] ArtDmx(int universe, byte[] data, byte sequence)
    {
        if (universe is <0 or >32767 || data.Length>512) throw new ArgumentException("Paquete Art-Net inválido.");
        var p=new byte[530]; "Art-Net\0"u8.CopyTo(p); p[9]=0x50;p[11]=14;p[12]=sequence;
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(14), (ushort)universe); p[16]=2;
        data.CopyTo(p,18); return p;
    }
    internal static byte[][] Lmp3(byte[] rgb, ushort frame, byte ident)
    {
        if (rgb.Length!=7560) throw new ArgumentException("LMP3 requiere 2520 RGB.");
        var packed=new byte[5040];
        for (int i=0;i<2520;i++)
            BinaryPrimitives.WriteUInt16BigEndian(packed.AsSpan(i*2),(ushort)(((rgb[i*3]&248)<<8)|((rgb[i*3+1]&252)<<3)|(rgb[i*3+2]>>3)));
        return Enumerable.Range(0,4).Select(i=>
        {
            int count=Math.Min(1440,packed.Length-i*1440); var p=new byte[16+count];
            "LMP3"u8.CopyTo(p);p[4]=1;p[5]=ident;
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(6),frame);
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(8),(ushort)i);p[10]=4;
            p[12]=(byte)(i*12);p[13]=(byte)Math.Min(12,42-i*12);
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(14),(ushort)count);
            Buffer.BlockCopy(packed,i*1440,p,16,count);return p;
        }).ToArray();
    }
    internal static List<LiveDatagram> BuildDatagrams(LiveConfig s, List<byte[]> colors, byte sequence, ushort frame)
    {
        var result=new List<LiveDatagram>(); if (!s.Sending) return result;
        if (s.HouseTest.Enabled)
        {
            // This test exclusively owns the selected controller span. It is
            // not dependent on an unrelated route's enabled/source settings.
            var frames=new Dictionary<int,byte[]>();
            WriteRgb(frames,s.HouseTest.Dest,s.HouseTest.DestChannel,HouseColors(s));
            foreach (var u in frames)
                result.Add(new(s.ArtnetBind,s.HouseTest.Ip,6454,u.Key,
                    [ArtDmx(u.Key,u.Value,sequence)],[ArtDmx(u.Key,new byte[512],sequence)]));
            return result;
        }
        var artnet=new Dictionary<string,Dictionary<int,byte[]>>();
        for(int i=0;i<s.Routes.Count;i++)
        {
            var r=s.Routes[i]; if (!r.Enabled) continue;
            if(r.Protocol=="LMP3") result.Add(new(s.WifiBind,r.Ip,7777,r.Ident,Lmp3(colors[i],frame,(byte)r.Ident),Lmp3(new byte[7560],frame,(byte)r.Ident)));
            else
            {
                if(!artnet.TryGetValue(r.Ip,out var universes)) artnet[r.Ip]=universes=new();
                WriteRgb(universes,r.Dest,r.DestChannel,colors[i]);
            }
        }
        foreach(var ip in artnet) foreach(var u in ip.Value)
            result.Add(new(s.ArtnetBind,ip.Key,6454,u.Key,[ArtDmx(u.Key,u.Value,sequence)],[ArtDmx(u.Key,new byte[512],sequence)]));
        return result;
    }

    private async Task WorkerAsync()
    {
        var watch=Stopwatch.StartNew(); var sockets=new Dictionary<string,UdpClient>();
        var previous=new Dictionary<string,LiveDatagram>(); byte sequence=0; ushort frameNumber=0;
        void Send(LiveDatagram d, bool black)
        {
            if (_testTransmit is not null) { _testTransmit(d,black); lock(_gate) _tx += (black?d.Black:d.Packets).Length; return; }
            if(!sockets.TryGetValue(d.Bind,out var client))
            {
                client=new UdpClient(AddressFamily.InterNetwork);
                try { client.Client.Bind(new IPEndPoint(IPAddress.Parse(d.Bind),0)); }
                catch { client.Dispose(); throw; }
                sockets[d.Bind]=client;
            }
            foreach(var packet in black?d.Black:d.Packets)
            {
                client.Send(packet,new IPEndPoint(IPAddress.Parse(d.Ip),d.Port));
                lock(_gate) _tx++;
            }
        }
        try
        {
            while(!_stop.IsCancellationRequested)
            {
                var started=watch.Elapsed; LiveConfig s; string input,source; long revision;
                lock(_gate) { s=_config; input=_input;source=_sourceIp;revision=_revision; }
                var snapshot=_receiver.CaptureUniverses(input,source);
                var render=Render(s,snapshot.Universes,watch.Elapsed.TotalSeconds);
                var mapped=ArtNetReceiver.MapUniverses(render.VisualUniverses);
                var monitor=MonitorFrames(s,render.RouteColors);
                lock(_gate)
                {
                    _colors=mapped;
                    if (!_automaticOutput) _outputFrames=monitor;
                    _previews=render.Previews;_rx=snapshot.PacketCount;_sources=snapshot.Sources.ToArray();
                }
                sequence=(byte)(sequence%255+1); frameNumber++;
                try
                {
                    // Serialize config commits and sending: an acknowledged stop cannot be followed by an old lit frame.
                    lock(_gate)
                    {
                        if(revision==_revision && (!_automaticOutput || DateTime.UtcNow>=_retryAfterUtc))
                        {
                            var active=BuildDatagrams(s,render.RouteColors,sequence,frameNumber).ToDictionary(d=>d.Key);
                            foreach(var old in previous.Values.Where(d=>!active.ContainsKey(d.Key)))
                                for(int i=0;i<3;i++) Send(old,true);
                            previous=active; // Retain all targets even if a send fails, so shutdown can clear them.
                            foreach(var d in active.Values) Send(d,false);
                            // This is the last complete frame handed to every configured
                            // output socket. The minimal visualizer reads only this copy.
                            _outputFrames=monitor;
                            if (_automaticOutput && _outputRetrying)
                            {
                                _outputRetrying=false; _retryAfterUtc=DateTime.MinValue; _error="";
                            }
                        }
                    }
                }
                catch(Exception ex)
                {
                    if (_automaticOutput)
                    {
                        foreach (var socket in sockets.Values) socket.Dispose();
                        sockets.Clear();
                        lock(_gate) if (revision==_revision)
                        {
                            _outputRetrying=true; _retryAfterUtc=DateTime.UtcNow.AddSeconds(1);
                            _error="Error de red; reintento automático en 1 s: "+ex.Message;
                        }
                    }
                    else lock(_gate) { _config=Clone(_config);_config.Sending=false;_revision++;_error="Salida detenida: "+ex.Message; }
                }
                int delay=Math.Max(1,(int)(1000.0/s.Fps-(watch.Elapsed-started).TotalMilliseconds));
                await Task.Delay(delay,_stop.Token).ConfigureAwait(false);
            }
        }
        catch(OperationCanceledException) { }
        finally
        {
            foreach(var d in previous.Values) try { for(int i=0;i<3;i++) Send(d,true); } catch { }
            foreach(var s in sockets.Values) s.Dispose();
        }
    }
    public void Dispose() { _stop.Cancel(); try { _worker?.Wait(TimeSpan.FromSeconds(2)); } catch { } _stop.Dispose(); }
}
