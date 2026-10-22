using System.Diagnostics;
using System.Net;
using System.Text;
using Brutal.Numerics;
using Xunit;

namespace KSACars.Tests;

public class MultiplayerTests
{
    private static Route RingAt(double latDeg, double lonDeg, double headingDeg)
    {
        AutopilotTests.Track ring = AutopilotTests.Ring(200.0);
        Circuit moved = ring.Circuit.MovedTo(latDeg, lonDeg, headingDeg, ring.World.RadiusM);
        Route? route = Route.Of(moved, TrackWorld.DirOf, ring.World.RadiusM, ring.World.HeightAt, TrackWorld.LiftM,
                                TrackWorld.SpacingM, null, 0.0, out string why);
        Assert.True(route is not null, why);
        return route!;
    }

    // A car 2 m left of the route and half a metre up, turned 20 degrees left of the road and rolled 5.
    private static (double3 At, doubleQuat Body2Ccf, double3 Velocity) CarOn(Route route, double s)
    {
        (double3 at, double3 up, double3 ahead) = route.Standing(s);
        double3 left = Vec.Unit(Vec.Cross(up, ahead));
        doubleQuat road = Vec.FromColumns(ahead, left, Vec.Cross(ahead, left));
        doubleQuat turn = doubleQuat.CreateFromAxisAngle(new double3(0, 0, 1), 20.0 * Math.PI / 180.0)
                          * doubleQuat.CreateFromAxisAngle(new double3(1, 0, 0), 5.0 * Math.PI / 180.0);
        return (at + (left * 2.0) + (up * 0.5), road * turn, (ahead * 30.0) + (left * 1.0));
    }

    [Fact]
    public void FromColumnsTurnsEachAxisOntoItsColumn()
    {
        double3 x = Vec.Unit(new double3(1, 2, 3));
        double3 y = Vec.Unit(Vec.Cross(new double3(0, 0, 1), x));
        double3 z = Vec.Cross(x, y);
        doubleQuat q = Vec.FromColumns(x, y, z);
        Assert.True(Vec.Len((q * new double3(1, 0, 0)) - x) < 1e-12);
        Assert.True(Vec.Len((q * new double3(0, 1, 0)) - y) < 1e-12);
        Assert.True(Vec.Len(q * new double3(0, 0, 1) - z) < 1e-12);
    }

    [Fact]
    public void ACarIsPutInTheSamePlaceOnACopyOfTheCourseLaidElsewhere()
    {
        Route here = RingAt(0.0, 0.0, 0.0), there = RingAt(35.0, -80.0, 140.0);
        foreach (double s in (double[])[5.0, 300.0, 900.0])
        {
            (double3 at, doubleQuat turned, double3 velocity) = CarOn(here, s);
            int index = -1;
            RacePose pose = RacePose.Of(here, ref index, at, turned, velocity, new double3(0.1, 0.2, 0.3), 50.0);
            Assert.InRange(pose.S, s - 0.05, s + 0.05);

            // On its own copy it is where it was.
            (double3 back, doubleQuat backTurn, double3 backVelocity) = pose.On(here);
            Assert.True(Vec.Len(back - at) < 1e-3, $"{Vec.Len(back - at)} m out at {s}");
            Assert.True(Math.Abs(doubleQuat.Dot(backTurn, turned)) > 1 - 1e-9);
            Assert.True(Vec.Len(backVelocity - velocity) < 1e-6);

            // On the other it is as far along, as far to the side, turned as far from the road and moving the same way along it.
            double sThere = s * there.LengthM / here.LengthM;
            (double3 put, doubleQuat putTurn, double3 putVelocity) = pose.On(there);
            (double3 expected, doubleQuat expectedTurn, double3 expectedVelocity) = CarOn(there, sThere);
            Assert.True(Vec.Len(put - expected) < 0.01, $"{Vec.Len(put - expected)} m out at {s}");
            Assert.True(Math.Abs(doubleQuat.Dot(putTurn, expectedTurn)) > 1 - 1e-6);
            Assert.True(Vec.Len(putVelocity - expectedVelocity) < 0.01);
        }
    }

    [Fact]
    public void BetweenTwoPosesOverTheLineIsTheShortWayRound()
    {
        RacePose a = new(990.0, 1000.0, default, doubleQuat.Identity, new double3(30, 0, 0), default);
        RacePose b = a with { S = 10.0 };
        Assert.Equal(0.0, RacePose.Between(a, b, 0.5).S, 6);
        Assert.Equal(995.0, RacePose.Between(a, b, 0.25).S, 6);
        Assert.Equal(5.0, a.Ahead(0.5).S, 6);
    }

    [Fact]
    public void EveryMessageReadsBackAsItWasWritten()
    {
        RacePose pose = new(123.25, 3562.5, new double3(1, -2, 0.5), doubleQuat.Normalize(new doubleQuat(0.1, 0.2, 0.3, 0.9)),
                            new double3(40, 1, -0.5), new double3(0.01, 0.02, 0.03));
        NetWire.Message[] messages =
        [
            new NetWire.Hello("Laurens", "KSACars_Prefab_F1", "2026.10.13.5602", string.Join(",", Enumerable.Range(0, 300).Select(k => $"Mod{k}"))),
            new NetWire.Welcome(3, ""),
            new NetWire.Welcome(0, "the race is full"),
            new NetWire.Roster("Club Motorsports", "abc123", [new NetWire.Racer(1, "Host", "KSACars_Prefab_F1", 0, "CarA"), new NetWire.Racer(2, "Ünïcødé 🐱", "KSACars_Prefab_Buggy", 1, "")]),
            new NetWire.State(2, 77, 12.5, pose, 0.75f, -0.25f, 0f, 3, true),
            new NetWire.Ping(1.5),
            new NetWire.Pong(1.5, 99.25),
            new NetWire.Start(4, 100.0, 5),
            new NetWire.Bye(),
            new NetWire.Punch(),
            new NetWire.Clock(1.23456789e9, 42.5, 1f),
            new NetWire.Got(7),
            new NetWire.Loaded(7),
        ];
        byte[] buffer = new byte[NetWire.BufferBytes];
        NetWire.Chunk chunk = new(7, 3, 9, [.. Enumerable.Range(0, WorldTransfer.ChunkBytes).Select(k => (byte)k)]);
        NetWire.Chunk chunkBack = Assert.IsType<NetWire.Chunk>(NetWire.Read(buffer.AsSpan(0, NetWire.Write(chunk, buffer)), out _));
        Assert.Equal(chunk.Data, chunkBack.Data);
        Assert.True(NetWire.Write(chunk, buffer) < 1280, "a piece of a world goes whole in any datagram");
        NetWire.Need need = new(7, [.. Enumerable.Range(0, 500).Select(k => (ushort)k)]);
        Assert.Equal(NetWire.MostNeeded, Assert.IsType<NetWire.Need>(NetWire.Read(buffer.AsSpan(0, NetWire.Write(need, buffer)), out _)).Missing.Length);
        foreach (NetWire.Message m in messages)
        {
            int n = NetWire.Write(m, buffer);
            NetWire.Message? back = NetWire.Read(buffer.AsSpan(0, n), out ushort version);
            Assert.Equal(NetWire.Version, version);
            Assert.NotNull(back);
            if (m is NetWire.State s && back is NetWire.State t)
            {
                Assert.Equal(s with { Pose = default }, t with { Pose = default });
                Assert.Equal(s.Pose.S, t.Pose.S);
                Assert.True(Vec.Len(s.Pose.Offset - t.Pose.Offset) < 1e-6);
                Assert.True(Math.Abs(doubleQuat.Dot(s.Pose.Attitude, t.Pose.Attitude)) > 1 - 1e-6);
                Assert.True(n < 128, $"a state is {n} bytes");
            }
            else if (m is NetWire.Hello h && back is NetWire.Hello g)
            {
                Assert.Equal(h.Mods, g.Mods);
                Assert.Equal(h.KsaBuild, g.KsaBuild);
            }
            else if (m is NetWire.Roster r && back is NetWire.Roster q)
            {
                Assert.Equal(r.Racers, q.Racers);
                Assert.Equal(r.CourseHash, q.CourseHash);
            }
            else
            {
                Assert.Equal(m, back);
            }
        }
    }

    [Fact]
    public void ADatagramThatIsNotOursOrIsCutShortIsDropped()
    {
        byte[] buffer = new byte[NetWire.BufferBytes];
        int n = NetWire.Write(new NetWire.State(2, 1, 0.0, new RacePose(1, 2, default, doubleQuat.Identity, default, default), 0, 0, 0, 0, false), buffer);
        for (int cut = 0; cut < n; cut++) Assert.Null(NetWire.Read(buffer.AsSpan(0, cut), out _));
        Assert.Null(NetWire.Read("hello there, this is not a race"u8, out _));

        // Of another version: dropped, and the version said.
        buffer[4] = 99;
        Assert.Null(NetWire.Read(buffer.AsSpan(0, n), out ushort version));
        Assert.Equal(99, version);

        // A position that is not a number is no state.
        n = NetWire.Write(new NetWire.State(2, 1, 0.0, new RacePose(double.NaN, 2, default, doubleQuat.Identity, default, default), 0, 0, 0, 0, false), buffer);
        Assert.Null(NetWire.Read(buffer.AsSpan(0, n), out _));
    }

    [Fact]
    public void AStunAnswerSaysTheAddressSeenFromOutside()
    {
        byte[] transaction = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12];
        byte[] request = Stun.Request(transaction);
        Assert.Equal(20, request.Length);
        Assert.True(Stun.IsStun(request));

        // RFC 5769's sample IPv4 response: 192.0.2.1:32853 under transaction b7e7a701bc34d686fa87dfae.
        byte[] answer = Convert.FromHexString(
            "0101003c2112a442b7e7a701bc34d686fa87dfae"
            + "8022000b7465737420766563746f7220"
            + "002000080001a147e112a643"
            + "000800149ec49b7a2bc5a9ab5d4bf5b9cd6b0b23d0d4f7a7"
            + "80280004c8fb0b4c");
        byte[] theirs = Convert.FromHexString("b7e7a701bc34d686fa87dfae");
        Assert.Equal(new IPEndPoint(IPAddress.Parse("192.0.2.1"), 32853), Stun.Answer(answer, theirs));
        Assert.Null(Stun.Answer(answer, transaction));
        byte[] buffer = new byte[NetWire.BufferBytes];
        Assert.False(Stun.IsStun(buffer.AsSpan(0, NetWire.Write(new NetWire.Ping(1.0), buffer))));
    }

    [Fact]
    public void ACodeReadsBackAsTheAddressItWasMadeOf()
    {
        foreach (string at in (string[])["203.0.113.7:50000", "1.2.3.4:1", "255.255.255.255:65535", "86.12.200.3:61234"])
        {
            IPEndPoint end = IPEndPoint.Parse(at);
            string code = JoinCode.Of(end);
            Assert.Matches("^[0-9A-HJKMNP-TV-Z]{5}-[0-9A-HJKMNP-TV-Z]{5}$", code);
            Assert.Equal((end.Address.ToString(), end.Port), JoinCode.Read(code, out _));
            // Typed by hand: lower case, no dash, an O for a 0 and an L for a 1.
            string typed = code.Replace("-", " ").ToLowerInvariant().Replace('0', 'o').Replace('1', 'l');
            Assert.Equal((end.Address.ToString(), end.Port), JoinCode.Read(typed, out _));
        }
        Assert.Equal(("relay.example.net", 12345), JoinCode.Read(" relay.example.net:12345 ", out _));
        Assert.Equal(("100.101.102.103", 50000), JoinCode.Read("100.101.102.103:50000", out _));
        Assert.Null(JoinCode.Read("ABCDE", out string why));
        Assert.Contains("ten", why);
        Assert.Null(JoinCode.Read("host:99999", out _));
        Assert.True(JoinCode.IsPrivate(IPAddress.Parse("192.168.1.20")));
        Assert.True(JoinCode.IsPrivate(IPAddress.Parse("100.72.1.1")));
        Assert.False(JoinCode.IsPrivate(IPAddress.Parse("86.12.200.3")));
    }

    [Fact]
    public void ARoutersAnswersAreRead()
    {
        Uri? location = Upnp.Location("HTTP/1.1 200 OK\r\nCACHE-CONTROL: max-age=120\r\nST: upnp:rootdevice\r\nLocation: http://192.168.1.1:5000/rootDesc.xml\r\n\r\n");
        Assert.Equal(new Uri("http://192.168.1.1:5000/rootDesc.xml"), location);

        const string description = """
            <?xml version="1.0"?>
            <root xmlns="urn:schemas-upnp-org:device-1-0">
              <device><deviceType>urn:schemas-upnp-org:device:InternetGatewayDevice:1</deviceType>
                <deviceList><device><deviceType>urn:schemas-upnp-org:device:WANDevice:1</deviceType>
                  <deviceList><device><deviceType>urn:schemas-upnp-org:device:WANConnectionDevice:1</deviceType>
                    <serviceList>
                      <service><serviceType>urn:schemas-upnp-org:service:WANCommonInterfaceConfig:1</serviceType><controlURL>/ctl/CmnIfCfg</controlURL></service>
                      <service><serviceType>urn:schemas-upnp-org:service:WANIPConnection:1</serviceType><controlURL>/ctl/IPConn</controlURL></service>
                    </serviceList>
                  </device></deviceList>
                </device></deviceList>
              </device>
            </root>
            """;
        Assert.Equal((new Uri("http://192.168.1.1:5000/ctl/IPConn"), "urn:schemas-upnp-org:service:WANIPConnection:1"),
                     Upnp.ControlOf(description, location!));

        string add = Upnp.AddPortMapping("urn:schemas-upnp-org:service:WANIPConnection:1", 50000, IPAddress.Parse("192.168.1.20"), "Fast & Purrious", 3600);
        Assert.Contains("<NewInternalClient>192.168.1.20</NewInternalClient>", add);
        Assert.Contains("<NewProtocol>UDP</NewProtocol>", add);
        Assert.Contains("Fast &amp; Purrious", add);

        Assert.Equal(IPAddress.Parse("86.12.200.3"), Upnp.ExternalOf(
            "<s:Envelope><s:Body><u:GetExternalIPAddressResponse><NewExternalIPAddress>86.12.200.3</NewExternalIPAddress></u:GetExternalIPAddressResponse></s:Body></s:Envelope>"));
        Assert.Equal("error 725, OnlyPermanentLeasesSupported", Upnp.FaultOf(
            "<s:Fault><detail><UPnPError><errorCode>725</errorCode><errorDescription>OnlyPermanentLeasesSupported</errorDescription></UPnPError></detail></s:Fault>"));
    }

    [Fact]
    public void ACarArrivingLateOrOutOfOrderIsDrawnBetweenItsStates()
    {
        RemoteTrack track = new();
        static NetWire.State At(uint seq, double sent, double s) =>
            new(2, seq, sent, new RacePose(s, 1000.0, default, doubleQuat.Identity, new double3(30, 0, 0), default), 1f, 0f, 0f, 2, false);

        // Sent every 1/30 s on a clock 500 s ahead, arriving 40 ms later and the third before the second.
        track.Add(At(1, 500.0, 100.0), 0.04);
        track.Add(At(3, 500.0 + (2.0 / 30.0), 102.0), 0.04 + (2.0 / 30.0));
        track.Add(At(2, 500.0 + (1.0 / 30.0), 101.0), 0.04 + (2.0 / 30.0) + 0.01);
        track.Add(At(2, 500.0 + (1.0 / 30.0), 101.0), 0.2);

        // Drawn a delay behind, on the sender's states put on this clock.
        (RacePose pose, _) = track.At(0.04 + (1.5 / 30.0) + RemoteTrack.DelaySeconds)!.Value;
        Assert.Equal(101.5, pose.S, 6);

        // Past the newest it carries on at 30 m/s for no more than a moment.
        (pose, _) = track.At(0.04 + (2.0 / 30.0) + RemoteTrack.DelaySeconds + 0.1)!.Value;
        Assert.Equal(105.0, pose.S, 6);
        (pose, _) = track.At(10.0)!.Value;
        Assert.Equal(102.0 + (30.0 * RemoteTrack.CarryOnSeconds), pose.S, 6);
    }

    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static double Now() => Clock.Elapsed.TotalSeconds;

    // Steps both sessions until a condition holds or the time runs out: long enough for the whole suite running beside them.
    private static void Until(Func<bool> done, params NetSession[] sessions)
    {
        double until = Now() + 20.0;
        while (!done() && Now() < until)
        {
            foreach (NetSession s in sessions) s.Update();
            Thread.Sleep(5);
        }
        Assert.True(done(), "it never happened");
    }

    [Fact]
    public void TwoGamesOnOneMachineRaceEachOther()
    {
        using NetSession host = new(NetSession.Role.Host, "Host", "KSACars_Prefab_F1", 0, Now);
        using NetSession client = new(NetSession.Role.Client, "Guest", "KSACars_Prefab_Buggy", 0, Now);
        using NetSession third = new(NetSession.Role.Client, "Guest", "KSACars_Prefab_Eldorado", 0, Now);
        host.Set("Club Motorsports", "abc123", "KSACars_Prefab_F1");
        IPEndPoint at = new(IPAddress.Loopback, host.Port);
        client.Join(at);
        third.Join(at);

        // Each is let in, with a box of its own and a name told from the other's.
        Until(() => client.Joined && third.Joined && client.Racers.Count == 3 && third.Racers.Count == 3, host, client, third);
        Assert.Equal("Club Motorsports", client.Course);
        Assert.Equal("abc123", third.CourseHash);
        Assert.Equal(3, client.Racers.Select(r => r.Box).Distinct().Count());
        Assert.Equal(["Guest", "Guest 2", "Host"], client.Racers.Select(r => r.Name).Order());

        // A guest's car reaches the host and, through it, the other guest.
        RacePose pose = new(250.0, 3500.0, new double3(0, 1.5, 0.4), doubleQuat.Identity, new double3(42, 0, 0), default);
        client.Send(pose, 1f, 0.1f, 0f, 4, true);
        NetSession.Racer InThird() => third.Racers.First(r => r.Id == client.Me);
        Until(() => InThird().Track.Newest is not null, host, client, third);
        Assert.Equal(250.0, InThird().Track.Newest!.Pose.S);
        Assert.Equal(4, InThird().Track.Newest!.Gear);
        Assert.NotNull(host.Racers.First(r => r.Id == client.Me).Track.Newest);

        // The host's start is the same moment on every clock: here they are one clock.
        Until(() => client.ClockKnown, host, client, third);
        Assert.InRange(client.HostOffset, -0.02, 0.02);
        host.StartRace(3.0, 5);
        Until(() => client.Start is not null && third.Start is not null, host, client, third);
        Assert.InRange(client.Start!.Value.At, host.Start!.Value.At - 0.02, host.Start!.Value.At + 0.02);
        Assert.Equal(5, third.Start!.Value.Laps);

        // One leaves, and the other is told.
        byte gone = third.Me;
        third.Dispose();
        Until(() => client.Racers.All(r => r.Id != gone), host, client);
    }

    [Fact]
    public void AGameOfAnotherVersionIsToldWhyItIsRefused()
    {
        using NetSession host = new(NetSession.Role.Host, "Host", "car", 0, Now);
        using System.Net.Sockets.UdpClient other = new(0);
        byte[] buffer = new byte[NetWire.BufferBytes];
        int n = NetWire.Write(new NetWire.Hello("Old", "car", "", ""), buffer);
        buffer[4] = 99;
        other.Send(buffer, n, new IPEndPoint(IPAddress.Loopback, host.Port));
        other.Client.ReceiveTimeout = 3000;
        double until = Now() + 3.0;
        while (other.Available == 0 && Now() < until)
        {
            host.Update();
            Thread.Sleep(5);
        }
        IPEndPoint? from = null;
        byte[] got = other.Receive(ref from);
        NetWire.Welcome? refused = NetWire.Read(got, out _) as NetWire.Welcome;
        Assert.NotNull(refused);
        Assert.Equal(0, refused!.Id);
        Assert.Contains("version", refused.Refused);
        Assert.Single(host.Racers);
    }
    [Fact]
    public void AWorldComesWholeThroughALineThatLosesAPieceInThree()
    {
        byte[] world = new byte[(WorldTransfer.ChunkBytes * 40) + 123];
        new Random(5).NextBytes(world);
        WorldTransfer.Outgoing going = new(9, world);
        WorldTransfer.Incoming? coming = null;
        Random line = new(11);
        int sent = 0;
        for (int step = 0; step < 2000 && coming is not { Complete: true }; step++)
        {
            foreach (NetWire.Chunk chunk in going.Next(1.0 / 60.0))
            {
                sent++;
                if (line.Next(3) == 0) continue;
                coming ??= new WorldTransfer.Incoming(chunk.Transfer, chunk.Count);
                coming.Add(chunk);
            }
            // Every quarter of a second the guest asks for what it is without, and for all of it once the pieces stop.
            if (step % 15 == 0 && coming is not null) going.Need(coming.Missing(all: step % 60 == 0));
        }
        Assert.True(coming is { Complete: true }, "the world never all came");
        Assert.Equal(world, coming!.Assemble());
        Assert.InRange(sent, 41, 41 * 3);
    }

    [Fact]
    public void AWorldUnpacksAsItWasPackedAndNothingClimbsOutOfItsFolder()
    {
        byte[] universe = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("<Vehicle Id=\"CarA\"/>", 20_000)));
        byte[] packed = WorldTransfer.Pack([("save/universe.xml", universe), ("save/meta.toml", "name = \"x\""u8.ToArray()), ("circuit.json", "{}"u8.ToArray())]);
        Assert.True(packed.Length < universe.Length / 20, $"{packed.Length} bytes packed of {universe.Length}");
        List<(string Name, byte[] Data)> back = WorldTransfer.Unpack(packed);
        Assert.Equal(["circuit.json", "save/meta.toml", "save/universe.xml"], back.Select(f => f.Name).Order());
        Assert.Equal(universe, back.First(f => f.Name == "save/universe.xml").Data);

        foreach (string bad in (string[])["../evil.dll", "save/../../evil", "/etc/passwd", "C:/Windows/x", "save\\x", "", "a//b"]) Assert.False(WorldTransfer.SafeName(bad), bad);
        Assert.Throws<InvalidDataException>(() => WorldTransfer.Unpack(WorldTransfer.Pack([("../evil.dll", [1])])));
    }

    [Fact]
    public void AGuestsWorldIsRunAShadeFasterOrSlowerToKeepTheHostsTimeAndNeverWarps()
    {
        Assert.Equal(1.0, ClockLock.Speed(0.0, 1.0), 12);
        Assert.Equal(1.002, ClockLock.Speed(0.01, 1.0), 12);
        Assert.Equal(ClockLock.MostFaster, ClockLock.Speed(60.0, 1.0), 12);
        Assert.Equal(0.0, ClockLock.Speed(-60.0, 1.0), 12);
        Assert.Equal(0.0, ClockLock.Speed(5.0, 0.0), 12);
        Assert.Equal(1010.5, ClockLock.HostNow(1000.0, 50.0, 1.0, 60.5), 9);
        Assert.Equal(1000.0, ClockLock.HostNow(1000.0, 50.0, 0.0, 60.5), 9);

        // A world 0.4 s behind, stepped at 60 Hz, closes the gap and holds it.
        double host = 0.0, guest = -0.4;
        for (int k = 0; k < 60 * 45; k++)
        {
            double speed = ClockLock.Speed(host - guest, 1.0);
            host += 1.0 / 60.0;
            guest += speed / 60.0;
        }
        Assert.InRange(host - guest, -0.001, 0.001);
    }

    [Fact]
    public void AGuestIsSentTheHostsWorldAndTheHostsWorldWaitsForItToLoad()
    {
        double universe = 5000.0, speed = 1.0;
        using NetSession host = new(NetSession.Role.Host, "Host", "car", 0, Now) { KsaBuild = "b1", Mods = ["Core", "FastAndPurrious"] };
        host.UniverseClock = () => (universe, speed);
        using NetSession guest = new(NetSession.Role.Client, "Guest", "car", 0, Now) { KsaBuild = "b1", Mods = ["Core", "FastAndPurrious", "Extra"] };
        guest.Join(new IPEndPoint(IPAddress.Loopback, host.Port));

        NetSession.Racer? arriving = null;
        Until(() => (arriving ??= host.NextArrival()) is not null && guest.Joined, host, guest);
        Assert.True(host.Handing);
        byte[] world = new byte[300_000];
        new Random(3).NextBytes(world);
        host.SendWorld(arriving!, world);
        host.SetCraft(arriving!.Id, "Guest (online)");
        Until(() => guest.World is not null, host, guest);
        Assert.Equal(world, guest.World!.Value.Data);
        Until(() => guest.Racers.Any(r => r.Craft == "Guest (online)"), host, guest);

        // Held until the guest has loaded it, and running again after.
        Assert.True(host.Handing);
        guest.SayLoaded();
        Until(() => !host.Handing, host, guest);

        // The guest knows the host's universe time.
        Until(() => guest.HostUniverse is not null, host, guest);
        Assert.InRange(guest.HostUniverse!.Value.Universe, 5000.0, 5001.0);
    }

    [Fact]
    public void AGuestWithoutAModTheHostHasIsToldWhichAndKeptOut()
    {
        using NetSession host = new(NetSession.Role.Host, "Host", "car", 0, Now) { KsaBuild = "b1", Mods = ["Core", "KSArmory", "FastAndPurrious"] };
        using NetSession guest = new(NetSession.Role.Client, "Guest", "car", 0, Now) { KsaBuild = "b1", Mods = ["Core", "FastAndPurrious"] };
        guest.Join(new IPEndPoint(IPAddress.Loopback, host.Port));
        Until(() => guest.Out.Length > 0, host, guest);
        Assert.Contains("KSArmory", guest.Out);
        Assert.Single(host.Racers);

        using NetSession other = new(NetSession.Role.Client, "Other", "car", 0, Now) { KsaBuild = "b2", Mods = ["Core", "KSArmory", "FastAndPurrious"] };
        other.Join(new IPEndPoint(IPAddress.Loopback, host.Port));
        Until(() => other.Out.Length > 0, host, other);
        Assert.Contains("b2", other.Out);
    }
    [Fact]
    public void AHostThatStandsStillAnsweringAPingDoesNotPutTheGuestsClockOut()
    {
        using NetSession host = new(NetSession.Role.Host, "Host", "car", 0, Now);
        using NetSession guest = new(NetSession.Role.Client, "Guest", "car", 0, Now);
        guest.Join(new IPEndPoint(IPAddress.Loopback, host.Port));
        Until(() => guest.Joined, host, guest);

        // The host saving its world: a second and a half with the guest's first ping unanswered.
        double until = Now() + 1.5;
        while (Now() < until)
        {
            guest.Update();
            Thread.Sleep(5);
        }
        Until(() => guest.ClockKnown, host, guest);
        Assert.InRange(guest.HostOffset, -0.05, 0.05);
    }
    [Fact]
    public void AHostWhoseWorldCannotKeepUpIsFollowedAtTheRateItReallyGoes()
    {
        // A host drawing too slowly for its steps to keep up: its world goes at half the wall clock, at speed 1.
        double start = Now();
        using NetSession host = new(NetSession.Role.Host, "Host", "car", 0, Now);
        host.UniverseClock = () => (1000.0 + (0.5 * (Now() - start)), 1.0);
        using NetSession guest = new(NetSession.Role.Client, "Guest", "car", 0, Now);
        guest.Join(new IPEndPoint(IPAddress.Loopback, host.Port));
        Until(() => guest.Joined && guest.Racers.Count == 2 && guest.ClockKnown, host, guest);
        double until = Now() + 1.5;
        while (Now() < until)
        {
            host.Update();
            guest.Update();
            Thread.Sleep(5);
        }
        (double universe, double rate) = guest.HostUniverse!.Value;
        Assert.InRange(rate, 0.45, 0.55);
        Assert.InRange(universe - (1000.0 + (0.5 * (Now() - start))), -0.05, 0.05);
    }
    [Fact]
    public void ALineMadeBadOnPurposeHoldsBackAndLosesWhatItCarries()
    {
        using NetSession host = new(NetSession.Role.Host, "Host", "car", 0, Now);
        using NetSession guest = new(NetSession.Role.Client, "Guest", "car", 0, Now);
        guest.Join(new IPEndPoint(IPAddress.Loopback, host.Port));
        Until(() => guest.Joined && guest.Racers.Count == 2, host, guest);
        NetSession.Racer seen = host.Racers.First(r => r.Id == guest.Me);

        // Held back 0.3 s: a state is not there sooner.
        guest.Impaired = new NetSession.Line(0.3, 0.0, 0.0);
        double sent = Now();
        guest.Send(new RacePose(1, 100, default, doubleQuat.Identity, default, default), 0, 0, 0, 0, false);
        Until(() => seen.Track.Newest is not null, host, guest);
        Assert.True(Now() - sent >= 0.3, $"came in {Now() - sent:F3} s");

        // Half lost: of 400, about 200 go, and the newest that comes is near the last sent.
        guest.Impaired = new NetSession.Line(0.0, 0.0, 0.5);
        for (int k = 0; k < 400; k++) guest.Send(new RacePose(2 + k, 1000, default, doubleQuat.Identity, default, default), 0, 0, 0, 0, false);
        Until(() => seen.Track.Newest!.Pose.S > 390.0, host, guest);
        Assert.InRange(guest.Dropped, 150, 250);
    }
    [Fact]
    public void AGuestJoiningAgainTakesTheNewHostsWorldThoughItIsNumberedAsTheLastWas()
    {
        using NetSession guest = new(NetSession.Role.Client, "Guest", "car", 0, Now);
        foreach (byte fill in (byte[])[1, 2])
        {
            using NetSession host = new(NetSession.Role.Host, "Host", "car", 0, Now);
            guest.Join(new IPEndPoint(IPAddress.Loopback, host.Port));
            NetSession.Racer? arriving = null;
            Until(() => (arriving ??= host.NextArrival()) is not null, host, guest);
            Assert.Null(guest.World);
            byte[] world = [.. Enumerable.Repeat(fill, 5000)];
            host.SendWorld(arriving!, world);
            Until(() => guest.World is not null, host, guest);
            Assert.Equal(1u, guest.World!.Value.Transfer);
            Assert.Equal(world, guest.World.Value.Data);
            Assert.Equal(fill, guest.Joins);
        }
    }

    [Fact]
    public void AHostCanPutAGuestOutAndTellItWhy()
    {
        using NetSession host = new(NetSession.Role.Host, "Host", "car", 0, Now);
        using NetSession guest = new(NetSession.Role.Client, "Guest", "car", 0, Now);
        guest.Join(new IPEndPoint(IPAddress.Loopback, host.Port));
        NetSession.Racer? arriving = null;
        Until(() => (arriving ??= host.NextArrival()) is not null && guest.Joined, host, guest);
        host.Drop(arriving!, "it did not load the world in 120 s");
        Until(() => guest.Out.Length > 0, host, guest);
        Assert.False(host.Handing);
        Assert.Single(host.Racers);
    }
    [Fact]
    public void AGuestBehindTheHostsRouterFindsItOnItsOwnNetworkAndIsTakenOnOnce()
    {
        using NetSession host = new(NetSession.Role.Host, "Host", "car", 0, Now);
        using NetSession guest = new(NetSession.Role.Client, "Guest", "car", 0, Now);

        // The code's address goes nowhere, as a router's own public address does from behind it.
        guest.Join(new IPEndPoint(IPAddress.Parse("192.0.2.1"), host.Port), nearby: true);
        Until(() => guest.Joined, host, guest);

        NetSession.Racer? arriving = null;
        Until(() => (arriving ??= host.NextArrival()) is not null, host, guest);
        Assert.Equal(guest.Me, arriving!.Id);

        // Heard at more than one address, it is one racer once the others have gone quiet.
        double until = Now() + 11.0;
        while (Now() < until)
        {
            host.Update();
            guest.Update();
            Thread.Sleep(20);
        }
        Assert.Equal(2, host.Racers.Count);
        Assert.Null(host.NextArrival());
    }
}
