using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace LightmanZapravka3D;

internal static class MinimalSelfTest
{
    private static readonly List<string> Results = [];
    private static void Check(bool value, string description)
    {
        if (!value) throw new Exception(description);
        Results.Add("PASS · " + description);
    }

    private static void Until(Func<bool> condition, string description, int timeout = 4000)
    {
        var end = Environment.TickCount64 + timeout;
        while (!condition())
        {
            if (Environment.TickCount64 > end) throw new TimeoutException(description);
            Thread.Sleep(10);
        }
    }

    internal static void Run()
    {
        Results.Clear();
        string manifestPath = Path.Combine(AppContext.BaseDirectory, "Patch", "SHOW-V20-MINIMAL.json");
        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var root = manifest.RootElement;
        var configuration = root.GetProperty("configuration");
        var visualLayout = configuration.GetProperty("visual_layout");
        var elements = manifest.RootElement.GetProperty("elements").EnumerateArray().ToArray();
        var outputs = manifest.RootElement.GetProperty("outputs").EnumerateArray().ToArray();
        static double[] Numbers(JsonElement value) => value.EnumerateArray().Select(item => item.GetDouble()).ToArray();
        static bool Near(double[] actual, params double[] expected) => actual.Length == expected.Length &&
            actual.Zip(expected).All(pair => Math.Abs(pair.First - pair.Second) < 1e-9);

        Check(root.GetProperty("format").GetString() == "lightman-v20-show-manifest" &&
              root.GetProperty("schema_version").GetInt32() == 4 &&
              configuration.GetProperty("manifest_schema_version").GetInt32() == 4 &&
              configuration.GetProperty("mapping_revision").GetString() == "V20-MINIMAL-4-PORTABLE-VISUAL-BRIDGE" &&
              visualLayout.GetProperty("schema_version").GetInt32() == 4,
            "manifiesto V4 portátil declara formato y esquema sin depender del frontend");
        var coordinateSystem = visualLayout.GetProperty("coordinate_system");
        var axes = coordinateSystem.GetProperty("axes");
        Check(coordinateSystem.GetProperty("units").GetString() == "meters" &&
              coordinateSystem.GetProperty("handedness").GetString() == "right-handed" &&
              coordinateSystem.GetProperty("angles").GetString() == "degrees" &&
              coordinateSystem.GetProperty("euler_order").GetString() == "XYZ" &&
              axes.GetProperty("x").GetString() == "right" && axes.GetProperty("y").GetString() == "up" &&
              axes.GetProperty("z").GetString() == "front-toward-audience",
            "V4 fija unidades, ejes, lateralidad y orden Euler inequívocos");
        var initialCamera = visualLayout.GetProperty("camera").GetProperty("initial");
        Check(Near(Numbers(initialCamera.GetProperty("position_m")), 0, 2.65, 9.4) &&
              Near(Numbers(initialCamera.GetProperty("target_m")), 0, 2.0, -0.08),
            "V4 incluye la vista frontal inicial en el mismo JSON");
        Check(elements.Length == 52, "manifiesto único con 52 elementos visibles");
        Check(elements.Select(e => e.GetProperty("element_id").GetString()).Distinct().Count() == elements.Length,
            "IDs visuales únicos");
        Check(elements.Sum(e => e.GetProperty("pixel_count").GetInt32()) == 20223, "20 223 píxeles visibles exactos");
        var flags = elements.Where(e => e.GetProperty("kind").GetString() == "flag").ToArray();
        var expectedFlagVisuals = new[]
        {
            ("FLAG-1", "flag1", "upper-left-outer"),
            ("FLAG-2", "flag2", "upper-left-inner"),
            ("FLAG-3", "flag3", "upper-right-inner"),
            ("FLAG-4", "flag4", "upper-right-outer"),
            ("FLAG-5", "flag5", "lower-left"),
            ("FLAG-6", "flag6", "lower-right")
        };
        Check(flags.Select(e => (e.GetProperty("element_id").GetString()!,
                    e.GetProperty("route_id").GetString()!, e.GetProperty("visual_slot").GetString()!))
                .SequenceEqual(expectedFlagVisuals),
            "orden frontal de banderas 1–6 enlazado explícitamente a sus rutas");
        Check(flags.All(e => e.GetProperty("pixel_count").GetInt32() == 2520 &&
                             e.GetProperty("route_offset").GetInt32() == 0 &&
                             e.GetProperty("direction").GetString() == "matrix-snake-14"),
            "las seis banderas conservan 2520 píxeles y snake sin desplazamiento");
        Check(flags.Where(e => e.GetProperty("visual_rotate_180").GetBoolean())
                .Select(e => e.GetProperty("element_id").GetString()).SequenceEqual(new[] { "FLAG-5", "FLAG-6" }),
            "solo las banderas de piso 5 y 6 giran visualmente 180 grados");
        var flagSlots = visualLayout.GetProperty("flags").GetProperty("slots");
        Check(expectedFlagVisuals.All(expected => flagSlots.TryGetProperty(expected.Item3, out _)),
            "los seis visual_slot resuelven dentro del manifiesto, sin tabla privada en JavaScript");
        Check(Near(Numbers(flagSlots.GetProperty("upper-left-outer").GetProperty("assembly_transform").GetProperty("position_m")), -2.582, 2.398, 0.3904) &&
              Near(Numbers(flagSlots.GetProperty("upper-left-inner").GetProperty("assembly_transform").GetProperty("position_m")), -1.314, 3.114, 0.334) &&
              Near(Numbers(flagSlots.GetProperty("upper-right-inner").GetProperty("assembly_transform").GetProperty("position_m")), 1.314, 3.114, 0.334) &&
              Near(Numbers(flagSlots.GetProperty("upper-right-outer").GetProperty("assembly_transform").GetProperty("position_m")), 2.582, 2.398, 0.3904) &&
              Near(Numbers(flagSlots.GetProperty("lower-left").GetProperty("assembly_transform").GetProperty("position_m")), -2.9824, 0.922, 0.6584) &&
              Near(Numbers(flagSlots.GetProperty("lower-right").GetProperty("assembly_transform").GetProperty("position_m")), 2.9824, 0.922, 0.6584),
            "posiciones frontales de las seis banderas están materializadas y son simétricas");
        Check(flags.All(flag =>
        {
            var visual = flag.GetProperty("visual");
            var mapping = visual.GetProperty("pixel_mapping");
            bool floor = flag.GetProperty("element_id").GetString() is "FLAG-5" or "FLAG-6";
            var operations = mapping.GetProperty("visual_pixel_transform").EnumerateArray().ToArray();
            return visual.GetProperty("slot").GetString() == flag.GetProperty("visual_slot").GetString() &&
                   visual.GetProperty("slot_ref").GetString() == $"#/configuration/visual_layout/flags/slots/{visual.GetProperty("slot").GetString()}" &&
                   operations.Length == (floor ? 1 : 0) &&
                   (!floor || operations[0].GetProperty("operation").GetString() == "rotate-180");
        }), "cada bandera enlaza su slot V4 y solo 5/6 rotan el muestreo visual");
        var waves = elements.Where(e => e.GetProperty("kind").GetString() == "wave").ToArray();
        Check(waves[0].GetProperty("element_id").GetString() == "WAVE-LEFT" &&
              !waves[0].GetProperty("visual_flip_x").GetBoolean() &&
              waves[0].GetProperty("direction").GetString() == "row-snake-100" &&
              waves[1].GetProperty("element_id").GetString() == "WAVE-RIGHT" &&
              waves[1].GetProperty("visual_flip_x").GetBoolean() &&
              waves[1].GetProperty("direction").GetString() == "row-snake-100-right",
            "solo la onda derecha aplica espejo X y conserva su cableado snake");
        var waveGroups = visualLayout.GetProperty("waves").GetProperty("groups");
        var waveLeftGroup = waveGroups.GetProperty("WAVE-LEFT");
        var waveRightGroup = waveGroups.GetProperty("WAVE-RIGHT");
        var rightWaveVisual = waves[1].GetProperty("visual");
        Check(waveLeftGroup.GetProperty("geometry_transform").GetProperty("mirror_axis").ValueKind == JsonValueKind.Null &&
              waveRightGroup.GetProperty("geometry_transform").GetProperty("mirror_axis").GetString() == "X" &&
              waveRightGroup.GetProperty("mirror_of").GetString() == "WAVE-LEFT" &&
              Near(Numbers(waveRightGroup.GetProperty("assembly_transform").GetProperty("scale_xyz")), 1, 1, 1) &&
              rightWaveVisual.GetProperty("geometry_transform").GetProperty("mirror_axis").GetString() == "X" &&
              rightWaveVisual.GetProperty("pixel_mapping").GetProperty("visual_pixel_transform")[0]
                  .GetProperty("operation").GetString() == "flip-x",
            "WAVE-RIGHT declara un solo espejo geométrico X y un flip-X de píxeles, sin doble escala negativa");

        var ceilingGroups = visualLayout.GetProperty("ceiling").GetProperty("groups");
        var ceilingLeft = ceilingGroups.GetProperty("CEILING-LEFT");
        var ceilingRight = ceilingGroups.GetProperty("CEILING-RIGHT");
        Check(Near(Numbers(ceilingLeft.GetProperty("pivot_m")), -2.1, 2.7023936175515635, 0) &&
              Near(Numbers(ceilingRight.GetProperty("pivot_m")), 2.1, 2.7023936175515635, 0) &&
              Near(Numbers(ceilingLeft.GetProperty("transform").GetProperty("rotation_deg_xyz")), 0, 180, 0) &&
              Near(Numbers(ceilingRight.GetProperty("transform").GetProperty("rotation_deg_xyz")), 0, 180, 0) &&
              !ceilingLeft.GetProperty("affects_signal_mapping").GetBoolean() &&
              !ceilingRight.GetProperty("affects_signal_mapping").GetBoolean(),
            "ambos conjuntos de techo publican pivote y RY 180° sin alterar señal");
        var ceilingElements = elements.Where(e => e.GetProperty("kind").GetString() == "ceiling").ToArray();
        Check(ceilingElements.All(element =>
        {
            var visual = element.GetProperty("visual");
            var mapping = visual.GetProperty("pixel_mapping");
            string expectedGroup = element.GetProperty("layout_index").GetInt32() < 10 ? "CEILING-LEFT" : "CEILING-RIGHT";
            return visual.GetProperty("group").GetString() == expectedGroup &&
                   visual.GetProperty("group_ref").GetString() == $"#/configuration/visual_layout/ceiling/groups/{expectedGroup}" &&
                   !mapping.GetProperty("visual_pixel_transform").EnumerateArray().Any();
        }), "los 16 colgantes enlazan el grupo volteado correcto y conservan el orden de píxel");

        Check(elements.All(element =>
        {
            var mapping = element.GetProperty("visual").GetProperty("pixel_mapping");
            int offset = element.GetProperty("route_offset").GetInt32();
            int count = element.GetProperty("pixel_count").GetInt32();
            int[] routePixels = mapping.GetProperty("route_pixel_indices").EnumerateArray().Select(value => value.GetInt32()).ToArray();
            return mapping.GetProperty("route_id").GetString() == element.GetProperty("route_id").GetString() &&
                   mapping.GetProperty("route_offset_pixels").GetInt32() == offset &&
                   routePixels.Length == count && routePixels.Distinct().Count() == count &&
                   routePixels.Order().SequenceEqual(Enumerable.Range(offset, count));
        }), "cada elemento V4 expande una permutación completa y sin duplicados de sus píxeles físicos");
        var expectedFlagRoutes = new[]
        {
            ("flag1", 0, "192.168.1.201", 1),
            ("flag2", 15, "192.168.1.202", 2),
            ("flag3", 30, "192.168.1.203", 3),
            ("flag4", 45, "192.168.1.204", 4),
            ("flag5", 60, "192.168.1.205", 5),
            ("flag6", 75, "192.168.1.207", 7)
        };
        Check(LiveEngine.DefaultRoutes().Where(r => r.Id.StartsWith("flag"))
                .Select(r => (r.Id, r.Start, r.Ip, r.Ident)).SequenceEqual(expectedFlagRoutes),
            "orden visual no altera entrada ni destino físico de ninguna bandera");
        var bridgeRoutes = configuration.GetProperty("bridge_routes").EnumerateArray().ToArray();
        Check(bridgeRoutes.Length == 15 &&
              bridgeRoutes.Select(route => route.GetProperty("route_id").GetString()).Distinct().Count() == 15,
            "V4 publica exactamente las quince rutas físicas del puente");
        var expectedPortableFlagRoutes = expectedFlagRoutes.Select(route =>
            (route.Item1, route.Item2, route.Item3, route.Item4, Pixels: 2520)).ToArray();
        Check(bridgeRoutes.Where(route => route.GetProperty("route_id").GetString()!.StartsWith("flag"))
                .Select(route =>
                {
                    var input = route.GetProperty("bridge_input");
                    var physical = route.GetProperty("physical_output");
                    return (route.GetProperty("route_id").GetString()!, input.GetProperty("universe").GetInt32(),
                        physical.GetProperty("ip").GetString()!, physical.GetProperty("ident").GetInt32(),
                        route.GetProperty("pixels").GetInt32());
                }).SequenceEqual(expectedPortableFlagRoutes) &&
              bridgeRoutes.Where(route => route.GetProperty("route_id").GetString()!.StartsWith("flag"))
                  .All(route => route.GetProperty("physical_output").GetProperty("protocol").GetString() == "LMP3" &&
                                route.GetProperty("physical_output").GetProperty("port").GetInt32() == 7777),
            "rutas portátiles de banderas conservan universos, IP, puerto e identificadores exactos");
        var expectedArtNetRoutes = new[]
        {
            ("tree1", 90, 600, "192.168.1.91", 0, 1),
            ("tree2", 94, 600, "192.168.1.91", 3, 271),
            ("tree3", 98, 600, "192.168.1.91", 7, 31),
            ("tree4", 102, 600, "192.168.1.91", 10, 301),
            ("hangers-left", 106, 800, "192.168.1.93", 2, 181),
            ("hangers-right", 111, 800, "192.168.1.92", 2, 181),
            ("waves-left", 116, 400, "192.168.1.93", 0, 1),
            ("waves-right", 119, 400, "192.168.1.92", 0, 1),
            ("custom-casa-400", 122, 400, "192.168.1.93", 7, 31)
        };
        Check(bridgeRoutes.Where(route => !route.GetProperty("route_id").GetString()!.StartsWith("flag"))
                .Select(route =>
                {
                    var input = route.GetProperty("bridge_input");
                    var physical = route.GetProperty("physical_output");
                    return (route.GetProperty("route_id").GetString()!, input.GetProperty("universe").GetInt32(),
                        route.GetProperty("pixels").GetInt32(), physical.GetProperty("ip").GetString()!,
                        physical.GetProperty("universe").GetInt32(), physical.GetProperty("channel").GetInt32());
                }).SequenceEqual(expectedArtNetRoutes) &&
              bridgeRoutes.Where(route => !route.GetProperty("route_id").GetString()!.StartsWith("flag"))
                  .All(route => route.GetProperty("physical_output").GetProperty("protocol").GetString() == "Art-Net" &&
                                route.GetProperty("physical_output").GetProperty("port").GetInt32() == 6454),
            "árbol, colgantes, ondas y casa publican entradas y salidas Art-Net exactas");
        var bridgeRouteById = bridgeRoutes.ToDictionary(route => route.GetProperty("route_id").GetString()!, route => route);
        Check(elements.All(element =>
        {
            var legacyInput = element.GetProperty("bridge_input");
            var physical = element.GetProperty("physical_output");
            var route = bridgeRouteById[element.GetProperty("route_id").GetString()!];
            var routeOutput = route.GetProperty("physical_output");
            int offset = element.GetProperty("route_offset").GetInt32();
            int count = element.GetProperty("pixel_count").GetInt32();
            if (element.GetProperty("target_ip_role").GetString() != "bridge-input-destination" ||
                legacyInput.GetProperty("ip").GetString() != "127.0.0.2" ||
                legacyInput.GetProperty("universe_start").GetInt32() != element.GetProperty("universe_start").GetInt32() ||
                legacyInput.GetProperty("channel_start").GetInt32() != element.GetProperty("channel_start").GetInt32() ||
                physical.GetProperty("protocol").GetString() != routeOutput.GetProperty("protocol").GetString() ||
                physical.GetProperty("ip").GetString() != routeOutput.GetProperty("ip").GetString() ||
                physical.GetProperty("port").GetInt32() != routeOutput.GetProperty("port").GetInt32()) return false;
            if (physical.GetProperty("protocol").GetString() == "LMP3")
                return physical.GetProperty("ident").GetInt32() == routeOutput.GetProperty("ident").GetInt32() &&
                       physical.GetProperty("pixel_start_0_based").GetInt32() == offset &&
                       physical.GetProperty("pixel_end_0_based").GetInt32() == offset + count - 1;
            int startPixel = routeOutput.GetProperty("universe").GetInt32() * 170 +
                             (routeOutput.GetProperty("channel").GetInt32() - 1) / 3 + offset;
            int endPixel = startPixel + count - 1;
            return physical.GetProperty("universe_start").GetInt32() == startPixel / 170 &&
                   physical.GetProperty("channel_start").GetInt32() == startPixel % 170 * 3 + 1 &&
                   physical.GetProperty("universe_end").GetInt32() == endPixel / 170 &&
                   physical.GetProperty("channel_end").GetInt32() == endPixel % 170 * 3 + 3;
        }), "cada elemento distingue entrada localhost y destino físico resuelto exacto");
        Check(elements.Single(e => e.TryGetProperty("tree_name", out var n) && n.GetString() == "A09").GetProperty("pixel_count").GetInt32() == 99,
            "A09 tiene 99 píxeles");
        Check(elements.Single(e => e.TryGetProperty("tree_name", out var n) && n.GetString() == "B10").GetProperty("pixel_count").GetInt32() == 99,
            "B10 tiene 99 píxeles");
        var tree = elements.Where(e => e.TryGetProperty("tree_name", out _))
            .ToDictionary(e => e.GetProperty("tree_name").GetString()!, e => e);
        Check(tree["A10"].GetProperty("route_offset").GetInt32() == 299 &&
              tree["A11"].GetProperty("route_offset").GetInt32() == 399 &&
              tree["A12"].GetProperty("route_offset").GetInt32() == 499 &&
              tree["B11"].GetProperty("route_offset").GetInt32() == 399 &&
              tree["B12"].GetProperty("route_offset").GetInt32() == 499,
            "offsets posteriores a A09/B10 conservan continuidad sin correr un LED");
        Check(elements.Where(e => e.GetProperty("kind").GetString() == "outline").Sum(e => e.GetProperty("pixel_count").GetInt32()) == 305,
            "casa visual: 305 visibles y sin línea de piso");
        Check(elements.Where(e => e.GetProperty("kind").GetString() == "outline")
                .Select(e => (e.GetProperty("route_offset").GetInt32(), e.GetProperty("pixel_count").GetInt32()))
                .SequenceEqual(new[] { (47, 82), (129, 71), (200, 71), (271, 81) }),
            "casa respeta exactamente LED 48–352 en cuatro tramos");
        Check(outputs.Where(o => o.GetProperty("controller").ValueKind == JsonValueKind.Number &&
                    o.GetProperty("controller").GetInt32() is 101 or 102 && o.GetProperty("output").GetInt32() == 2)
                .All(o => o.GetProperty("used_pixels").GetInt32() == 599 &&
                          o.GetProperty("reserved_pixels").GetInt32() == 600 &&
                          o.GetProperty("used_channel_end").GetInt32() == 267 &&
                          o.GetProperty("reserved_channel_end").GetInt32() == 270),
            "salidas A2/B2 distinguen 599 usados de 600 reservados");
        var houseOutput = outputs.Single(o => o.GetProperty("controller").ValueKind == JsonValueKind.Number &&
            o.GetProperty("controller").GetInt32() == 107);
        Check(houseOutput.GetProperty("physical_pixels").GetInt32() == 400 &&
              houseOutput.GetProperty("visible_pixels").GetInt32() == 305 &&
              houseOutput.GetProperty("universe_end").GetInt32() == 124 &&
              houseOutput.GetProperty("channel_end").GetInt32() == 180,
            "salida de casa distingue 400 físicos de 305 visibles");
        Check(elements.All(e =>
        {
            int start = e.GetProperty("pixel_start").GetInt32();
            int count = e.GetProperty("pixel_count").GetInt32();
            int end = start + count - 1;
            return e.GetProperty("universe_start").GetInt32() == start / 170 &&
                   e.GetProperty("channel_start").GetInt32() == start % 170 * 3 + 1 &&
                   e.GetProperty("universe_end").GetInt32() == end / 170 &&
                   e.GetProperty("channel_end").GetInt32() == end % 170 * 3 + 3;
        }), "universo/canal coincide con cada tramo visible");

        using (var receiver = new ArtNetReceiver(0))
        {
            byte[] Frame(byte value) => Enumerable.Repeat(value, 512).ToArray();
            receiver.InjectUniverseForTest(0, Frame(21), "127.0.0.1");
            receiver.InjectUniverseForTest(0, Frame(42), "127.0.0.3");
            receiver.InjectUniverseForTest(0, Frame(84), "192.0.2.84");
            Check(receiver.CaptureUniverses("resolume").Universes[0][0] == 21, "Resolume aislado");
            Check(receiver.CaptureUniverses("xlights").Universes[0][0] == 42, "xLights aislado");
            Check(receiver.CaptureUniverses("tracking").Universes[0][0] == 84, "tracking aislado");
            receiver.InjectUniverseForTest(0, Frame(96), "127.0.0.4");
            Check(receiver.CaptureUniverses("tracking", "127.0.0.4").Universes[0][0] == 96,
                "tracking local aislado en 127.0.0.4");
        }

        // Prove that the three loopback destinations can coexist on one UDP
        // port and are classified by the address to which each packet arrives.
        int isolatedPort;
        using (var reservation = new UdpClient(new IPEndPoint(IPAddress.Any, 0)))
            isolatedPort = ((IPEndPoint)reservation.Client.LocalEndPoint!).Port;
        using (var receiver = new ArtNetReceiver(isolatedPort))
        using (var udp = new UdpClient(AddressFamily.InterNetwork))
        {
            static byte[] ArtDmx(byte value)
            {
                var packet = new byte[530];
                "Art-Net\0"u8.CopyTo(packet);
                packet[9] = 0x50;
                packet[11] = 14;
                packet[16] = 2;
                for (int i = 18; i < packet.Length; i++) packet[i] = value;
                return packet;
            }
            receiver.Start();
            udp.Send(ArtDmx(31), new IPEndPoint(IPAddress.Parse("127.0.0.2"), isolatedPort));
            udp.Send(ArtDmx(62), new IPEndPoint(IPAddress.Parse("127.0.0.3"), isolatedPort));
            udp.Send(ArtDmx(93), new IPEndPoint(IPAddress.Parse("127.0.0.4"), isolatedPort));
            Until(() => receiver.CaptureUniverses("resolume").Universes.TryGetValue(0, out var a) && a[0] == 31 &&
                        receiver.CaptureUniverses("xlights").Universes.TryGetValue(0, out var b) && b[0] == 62 &&
                        receiver.CaptureUniverses("tracking", "127.0.0.1").Universes.TryGetValue(0, out var c) && c[0] == 93,
                "los tres destinos localhost no quedaron aislados");
            Check(true, "recepción UDP separada en 127.0.0.2 / .3 / .4");
        }

        int collisionPort;
        using (var reservation = new UdpClient(new IPEndPoint(IPAddress.Any, 0)))
            collisionPort = ((IPEndPoint)reservation.Client.LocalEndPoint!).Port;
        using (var blocker = new UdpClient(AddressFamily.InterNetwork))
        using (var receiver = new ArtNetReceiver(collisionPort))
        {
            blocker.ExclusiveAddressUse = true;
            blocker.Client.Bind(new IPEndPoint(IPAddress.Parse("127.0.0.3"), collisionPort));
            bool failed = false;
            try { receiver.Start(); } catch (InvalidOperationException) { failed = true; }
            Check(failed && !receiver.IsRunning, "fallo parcial revierte todos los listeners");
            using (var probe = new UdpClient(AddressFamily.InterNetwork))
            {
                probe.ExclusiveAddressUse = true;
                probe.Client.Bind(new IPEndPoint(IPAddress.Parse("127.0.0.2"), collisionPort));
            }
            blocker.Dispose();
            receiver.Start();
            Check(receiver.IsRunning, "receptor puede reiniciarse después de una colisión corregida");
        }

        var automatic = new LiveConfig();
        LiveEngine.PrepareAutomatic(automatic, true);
        var houseRoute = automatic.Routes.Single(r => r.Id == "custom-casa-400");
        Check(houseRoute.Ip == "192.168.1.93" && houseRoute.Dest == 7 && houseRoute.DestChannel == 31,
            "casa sale por 192.168.1.93 U7 C31");
        automatic.GeneralTest = true;
        automatic.Receiving = false;
        var rendered = LiveEngine.Render(automatic, [], 0);
        var monitor = LiveEngine.MonitorFrames(automatic, rendered.RouteColors);
        Check(monitor.Count == 15 && monitor.Values.All(v => v.Any(b => b != 0)), "test general ilumina las quince rutas");
        var house = monitor["custom-casa-400"];
        Check(house.Take(47 * 3).All(b => b == 0) && house.Skip(352 * 3).All(b => b == 0) &&
              house.Skip(47 * 3).Take(305 * 3).All(b => b == 63),
            "casa conserva LED ocultos negros en test general");
        Check(monitor["tree2"].Skip(599 * 3).Take(3).All(b => b == 0) &&
              monitor["tree4"].Skip(599 * 3).Take(3).All(b => b == 0),
            "colas reservadas A/B permanecen negras");
        Check(monitor.Where(p => p.Key.StartsWith("flag")).All(p =>
                p.Value.Chunk(3).All(rgb => rgb.SequenceEqual(new byte[] { 57, 60, 57 }))),
            "monitor de banderas refleja cuantización RGB565");

        var sent = new ConcurrentQueue<(LiveDatagram Datagram, bool Black)>();
        using (var receiver = new ArtNetReceiver(0))
        using (var engine = new LiveEngine(receiver, null, (d, b) => sent.Enqueue((d, b)), automaticOutput: true))
        {
            engine.SetGeneralTest(true);
            engine.Start();
            string[] expectedIps = ["192.168.1.201", "192.168.1.202", "192.168.1.203", "192.168.1.204", "192.168.1.205", "192.168.1.207", "192.168.1.91", "192.168.1.92", "192.168.1.93"];
            Until(() => expectedIps.All(ip => sent.Any(p => !p.Black && p.Datagram.Ip == ip)),
                "el test general no produjo todas las salidas interceptadas");
            Check(sent.Where(p => !p.Black).Select(p => p.Datagram.Ip).Distinct().Order()
                .SequenceEqual(expectedIps.Order()),
                "test general cubre banderas, árbol y ambos laterales");
            var lit = sent.Where(p => !p.Black).Select(p => p.Datagram).ToArray();
            Check(lit.Where(d => d.Port == 7777).Select(d => (d.Ip, d.Slot)).Distinct().Order()
                    .SequenceEqual(new[] {
                        ("192.168.1.201", 1), ("192.168.1.202", 2), ("192.168.1.203", 3),
                        ("192.168.1.204", 4), ("192.168.1.205", 5), ("192.168.1.207", 7) }.Order()),
                "banderas usan LMP3 puerto 7777 e identificadores 1–5/7");
            Check(lit.Where(d => d.Ip == "192.168.1.91" && d.Port == 6454).Select(d => d.Slot).Distinct().Order()
                    .SequenceEqual(Enumerable.Range(0, 15)),
                "árbol físico cubre U0–14 en 192.168.1.91");
            Check(lit.Where(d => d.Ip == "192.168.1.92" && d.Port == 6454).Select(d => d.Slot).Distinct().Order()
                    .SequenceEqual(Enumerable.Range(0, 8)),
                "lado derecho cubre U0–7 en 192.168.1.92");
            Check(lit.Where(d => d.Ip == "192.168.1.93" && d.Port == 6454).Select(d => d.Slot).Distinct().Order()
                    .SequenceEqual(Enumerable.Range(0, 10)),
                "lado izquierdo y casa cubren U0–9 en 192.168.1.93");
        }

        int selected = 0, tested = 0;
        int paused = 0, stopped = 0;
        string selectedValue = "";
        string playedShow = "";
        bool testedValue = false;
        string web = Path.Combine(AppContext.BaseDirectory, "Web");
        using (var server = new TabletServer(web, 0, () => new { source = "resolume", generalTest = false },
                   value => { selectedValue = value; Interlocked.Increment(ref selected); return true; },
                   value => { testedValue = value; Interlocked.Increment(ref tested); return true; },
                   value => { playedShow = value; return TabletActionResult.Success(); },
                   () => { Interlocked.Increment(ref paused); return TabletActionResult.Success(); },
                   () => { Interlocked.Increment(ref stopped); return TabletActionResult.Success(); }))
        {
            server.Start();
            using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{server.ListeningPort}") };
            Check(http.GetStringAsync("/health").GetAwaiter().GetResult().Contains("LIGHTMAN V20 MINIMAL"), "servidor tablet responde en LAN/HTTP");
            var sourceResponse = http.PostAsync("/api/source", new StringContent("{\"source\":\"xlights\"}", Encoding.UTF8, "application/json")).GetAwaiter().GetResult();
            var testResponse = http.PostAsync("/api/test", new StringContent("{\"enabled\":true}", Encoding.UTF8, "application/json")).GetAwaiter().GetResult();
            Until(() => selected == 1 && tested == 1, "API tablet no aplicó los comandos");
            Check(sourceResponse.IsSuccessStatusCode && testResponse.IsSuccessStatusCode && selectedValue == "xlights" && testedValue,
                "tablet confirma fuente y test después de aplicarlos");
            var playResponse = http.PostAsync("/api/show/play", new StringContent("{\"id\":\"skeewiff\"}", Encoding.UTF8, "application/json")).GetAwaiter().GetResult();
            var pauseResponse = http.PostAsync("/api/show/pause", new StringContent("{}", Encoding.UTF8, "application/json")).GetAwaiter().GetResult();
            var stopResponse = http.PostAsync("/api/show/stop", new StringContent("{}", Encoding.UTF8, "application/json")).GetAwaiter().GetResult();
            Check(playResponse.IsSuccessStatusCode && pauseResponse.IsSuccessStatusCode && stopResponse.IsSuccessStatusCode &&
                playedShow == "skeewiff" && paused == 1 && stopped == 1,
                "tablet lanza, pausa y detiene shows mediante IDs controlados");
            Check((int)http.PostAsync("/api/test", new StringContent("{\"enabled\":true}", Encoding.UTF8, "text/plain")).GetAwaiter().GetResult().StatusCode == 415,
                "API rechaza POST simple sin application/json");
            using var foreign = new HttpRequestMessage(HttpMethod.Post, "/api/test")
            {
                Content = new StringContent("{\"enabled\":true}", Encoding.UTF8, "application/json")
            };
            foreign.Headers.Add("Origin", "https://sitio-ajeno.invalid");
            Check((int)http.Send(foreign).StatusCode == 403, "API rechaza origen web ajeno");
            Check(http.GetStringAsync("/api/state").GetAwaiter().GetResult().Contains("resolume"), "API tablet publica estado");
            Check(http.GetStringAsync("/").GetAwaiter().GetResult().Contains("CONTROL DEL SHOW"), "página de tablet incluida");
        }

        File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "SELFTEST-MINIMAL.txt"), Results);
    }
}
