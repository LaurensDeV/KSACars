using System.Globalization;
using System.IO;
using System.Text.Json;
using Brutal.Numerics;
using KSA;

namespace KSACars;

/// <summary>
/// Commands from outside the game, read from <c>Logs/bridge/KSACars/in</c> and answered in <c>out</c>: what
/// lets an agent in a terminal load a save, park a car, drive it, step the world and photograph it in a
/// game that stays running. <c>tools/ksa-mcp/server.py</c> is the other end. Only a developer's install
/// starts it.
///
/// <para><b>Files, not a socket</b>: a folder needs no port and works across the WSL boundary as it
/// is.</para>
///
/// <para>One command at a time, oldest first. A command that takes frames -- a step, a capture --
/// holds the queue until it answers, so a sequence of them is a script that runs in order.</para>
/// </summary>
internal sealed class Bridge
{
    // A few times a second is quick enough for a person and costs one directory listing.
    private const double PollSeconds = 0.1;
    private double _sincePoll;

    private Func<double, double, Reply?>? _running;
    private BridgeCommand? _current;

    // A folder of its own: every mod built from the same tooling has a bridge, and two reading one
    // folder each answer whichever command they reach first.
    internal static string Root => Path.Combine(Log.Folder, "bridge", "KSACars");
    private static string Inbox => Path.Combine(Root, "in");
    private static string Outbox => Path.Combine(Root, "out");

    private readonly record struct Reply(bool Ok, string Error, Dictionary<string, object?> Data);

    private static Reply Done(Dictionary<string, object?>? data = null) => new(true, string.Empty, data ?? []);
    private static Reply Failed(string why) => new(false, why, []);

    /// <summary>
    /// One frame: advances the command running, or takes the next. Called from the one hook KSA always
    /// calls, so it runs in the menu and with the UI hidden. Nothing here may throw.
    /// </summary>
    public void Update(double dtPlayer, double dtSim)
    {
        // Ahead of the queue, which a step holds for as long as the world runs.
        Laps.Update(dtPlayer);

        try
        {
            if (_running is not null && _current is not null)
            {
                if (_running(dtPlayer, dtSim) is { } reply) Answer(_current, reply);
                return;
            }

            _sincePoll += dtPlayer;
            if (_sincePoll < PollSeconds) return;
            _sincePoll = 0.0;

            if (!Directory.Exists(Inbox)) return;

            string? next = Directory.GetFiles(Inbox, "*.json").Order(StringComparer.Ordinal).FirstOrDefault();
            if (next is null) return;

            string text = File.ReadAllText(next);
            File.Delete(next);

            if (!BridgeCommand.TryParse(text, out BridgeCommand? command, out string trouble))
            {
                Log.Warn($"bridge: refused {Path.GetFileName(next)} -- {trouble}");
                return;
            }

            Start(command!);
        }
        catch (Exception e)
        {
            if (_current is { } command) Answer(command, Failed($"threw: {e.Message}"));
            else Log.Warn($"bridge: {e.Message}");
        }
    }

    private void Start(BridgeCommand command)
    {
        _current = command;
        Log.Info($"bridge: {command.Name} ({command.Id})");

        Reply? now = command.Name switch
        {
            "status" => Status(),
            "pause" => KsaWorld.SetPaused(true) ? Done() : Failed("the world would not pause"),
            "resume" => KsaWorld.SetPaused(false) ? Done() : Failed("the world would not resume"),
            "speed" => KsaWorld.SetSimulationSpeed(command.Number("x", 1.0)) ? Done() : Failed("speed refused"),
            "site" => Site(command),
            "step" => BeginStep(command),
            "capture" => BeginCapture(command),
            "load" => BeginLoad(command),
            "spawn" => Spawn(command),
            "drive" => Drive(command),
            "ground" => Ground(command),
            "road" => Road(command),
            "lap" => Lap(command),
            "save" => SaveGame(command),
            _ => Failed($"no command '{command.Name}'"),
        };

        if (now is { } reply) Answer(command, reply);
    }

    private void Answer(BridgeCommand command, Reply reply)
    {
        _running = null;
        _current = null;

        Directory.CreateDirectory(Outbox);

        Dictionary<string, object?> body = new()
        {
            ["id"] = command.Id,
            ["cmd"] = command.Name,
            ["ok"] = reply.Ok,
            ["error"] = reply.Ok ? null : reply.Error,
            ["data"] = reply.Data,
        };

        // Written aside and moved, so the other end never reads half a reply.
        string final = Path.Combine(Outbox, command.Id + ".json");
        string partial = final + ".part";
        File.WriteAllText(partial, JsonSerializer.Serialize(body, JsonOptions));
        File.Move(partial, final, overwrite: true);

        if (!reply.Ok) Log.Warn($"bridge: {command.Name} ({command.Id}) failed -- {reply.Error}");
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    // ---- commands that answer at once -------------------------------------------------------

    private static Reply Status() => Done(new()
    {
        ["build"] = Build.Version,
        ["in_flight"] = KsaWorld.InFlightScene,
        ["craft"] = KsaWorld.ControlledVehicle is { } craft ? KsaWorld.DisplayName(craft) : null,
        ["paused"] = KsaWorld.IsPaused,
        ["speed"] = KsaWorld.SimulationSpeed,
        ["others"] = OthersFromFlown(),
    });

    // Every craft within 200 km of the one being flown: how far, which way, and what it is doing.
    private static List<object?> OthersFromFlown()
    {
        List<object?> seen = [];
        if (KsaWorld.ControlledVehicle is not { } flown) return seen;

        double3 here = KsaWorld.PositionEcl(flown);
        double3 up = KsaWorld.LocalUp(flown);
        foreach (Vehicle v in KsaWorld.Vehicles)
        {
            if (ReferenceEquals(v, flown) || !KsaWorld.IsAlive(v)) continue;

            double3 to = KsaWorld.PositionEcl(v) - here;
            double range = Vec.Len(to);
            if (range > 200_000.0) continue;

            seen.Add(new Dictionary<string, object?>
            {
                ["name"] = KsaWorld.DisplayName(v),
                ["range_m"] = Math.Round(range),
                ["elevation_deg"] = Math.Round(double.RadiansToDegrees(Math.Asin(Math.Clamp(Vec.Dot(to, up) / range, -1.0, 1.0))), 1),
                ["situation"] = v.Situation.ToString(),
            });
        }

        seen.Add(new Dictionary<string, object?>
        {
            ["name"] = "(flown) " + KsaWorld.DisplayName(flown),
            ["speed_ms"] = Math.Round(Vec.Len(KsaWorld.VelocityEcl(flown) - KsaWorld.GroundVelocityAt(flown, here)), 1),
            ["situation"] = flown.Situation.ToString(),
        });

        return seen;
    }

    // A craft from a vehicle library parked on the ground at lat/lon: craft is the library save's name.
    private static Reply Spawn(BridgeCommand command)
    {
        if (KsaWorld.ControlledVehicle is not { } flown) return Failed("no craft is being flown");
        if (KsaWorld.ParentBody(flown) is not { } body) return Failed("no body under the craft");

        string stock = command.String("craft");
        if (stock.Length == 0) return Failed("spawn needs a craft");
        string name = command.String("name");
        if (name.Length == 0) name = stock;

        return CraftSpawner.SpawnParked(flown, stock, name, body, command.Number("lat", 0.0), command.Number("lon", 0.0)) is { }
            ? Done(new() { ["name"] = name })
            : Failed($"could not park a {stock}");
    }

    // A buggy's throttle (-1..1) and steer (-1 right..1 left) held for so many simulated seconds, and
    // what it is doing now. focus=true hands the player's controls and view to it first.
    private static Reply Drive(BridgeCommand command)
    {
        if (CraftNamed(command.String("craft")) is not { } craft) return Failed("no such craft");
        if (command.Flag("focus", false)) KsaWorld.GoTo(craft);
        if (command.Has("cam_elevation_deg") || command.Has("cam_distance"))
        {
            // The orbit camera on whatever is being flown, lifted out of the grass to see a car's crew. The
            // stored view, not the controller, whose angles are sprung back towards it every frame; and a
            // car's orbit elevation runs downward, so it is negated to read as degrees above the car.
            if (Program.GetMainCamera()?.Following?.OrbitView is { } view)
            {
                double deg = Math.PI / 180.0;
                KsaWorld.TryWriteMainOrbit(command.Number("cam_azimuth_deg", view.Azimuth / deg) * deg,
                                           -command.Number("cam_elevation_deg", -view.Elevation / deg) * deg);
                if (command.Has("cam_distance")) view.DistancePower = command.Number("cam_distance", view.DistancePower);
            }
        }
        if (command.Has("log_every_s")) Buggies.LogEverySeconds = Math.Max(command.Number("log_every_s", 1.0), 0.02);
        if (command.String("seat_kittens") is { Length: > 0 } wanted)
        {
            // Named kittens into the seats in order, for a test that needs a particular one aboard.
            string[] names = wanted.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            int i = 0;
            foreach (IVASeat seat in craft.Parts.Modules.Get<IVASeat>())
            {
                if (i >= names.Length) break;
                KittenRosterEntryData? k = Universe.KittenRoster.Kittens.FirstOrDefault(x => x.Name == names[i]);
                if (k is not null) Vehicle.SetSeatCrew(seat, k.NameHash, craft.Id, hasLaunched: true);
                i++;
            }
        }
        if (command.Flag("crew", false))
        {
            // What the launch window's Fill Seats button does, for a craft set down without one.
            List<IVASeat> seats = [.. craft.Parts.Modules.Get<IVASeat>()];
            CrewAssignmentWindow.FillSeats(seats, craft.Parts, craft.Id, hasLaunched: true);
        }

        if (command.Has("seconds"))
        {
            DriveInput input = new(Math.Clamp(command.Number("throttle", 0.0), -1.0, 1.0),
                                   Math.Clamp(command.Number("steer", 0.0), -1.0, 1.0));
            if (!Buggies.Hold(craft, input, command.Number("seconds", 0.0), Math.Clamp(command.Number("turn", 0.0), -1.0, 1.0),
                              command.Flag("boost", false)))
            {
                return Failed("that craft is not a buggy");
            }
        }

        if (command.Has("lights"))
        {
            if (Headlights.Parse(command.String("lights")) is not { } setting) return Failed("lights is off, low or high");
            if (Buggies.Of(craft) is not { } car) return Failed("that craft is not a buggy");
            car.Beam = setting;
        }
        if (command.Has("lift"))
        {
            if (Buggies.Of(craft) is not { } flyer) return Failed("that craft is not a buggy");
            if (!flyer.Drive.Profile.HasRockets) return Failed("that car has no rockets");
            double lift = Math.Clamp(command.Number("lift", 0.0), 0.0, 1.0);
            if (lift > 0.0) flyer.Throttle = lift;
            Buggies.Ignite(flyer, lift > 0.0);
        }
        if (command.Has("game_drag")) Buggies.ShedsGameDrag = !command.Flag("game_drag", false);
        if (command.Has("hull_margin")) Buggies.HoldsHullMargin = command.Flag("hull_margin", true);
        if (command.Has("downforce"))
        {
            if (Buggies.Of(craft) is not { } pressed) return Failed("that craft is not a buggy");
            if (!pressed.Drive.Profile.HasDownforce) return Failed("that car has no downward thrusters");
            pressed.Downforce = command.Flag("downforce", false);
        }
        if (command.Has("rock_weight")) KsaWorld.RockWeight = Math.Clamp(command.Number("rock_weight", 0.02), 0.0001, 1.0);
        if (command.Has("scoop"))
        {
            if (Buggies.Of(craft) is not { } carrier) return Failed("that craft is not a buggy");
            ScoopProfile[] scoops = carrier.Drive.Profile.Scoops;
            if (scoops.Length == 0) return Failed("that car has no scoop");

            // true and false for the first scoop and none, or a scoop by name.
            string named = command.String("scoop");
            int byName = Array.FindIndex(scoops, s => string.Equals(s.Name, named, StringComparison.OrdinalIgnoreCase)
                                                      || string.Equals(s.SubpartSuffix, named, StringComparison.OrdinalIgnoreCase));
            carrier.Scoop = byName >= 0 ? byName : command.Flag("scoop", false) ? 0 : -1;
        }
        if (command.Flag("flip", false) && !Buggies.Right(craft, tip: true)) return Failed("that craft is not a buggy");
        if (command.Flag("unflip", false) && !Buggies.Right(craft)) return Failed("that craft is not a buggy");

        // What the crew portrait's EVA button does: the driver of a buggy, the first kitten aboard anything else.
        if (command.Flag("eva", false))
        {
            string who = command.String("kitten");
            IVASeat? seat = who.Length > 0 ? null : Buggies.DriverSeatOf(craft);
            if (who.Length > 0)
            {
                foreach (IVASeat s in craft.Parts.Modules.Get<IVASeat>())
                {
                    if (Universe.KittenRoster.Find(s.AssignedKittenHash) is { } k && k.Name == who) { seat = s; break; }
                }
            }
            else if (seat is null)
            {
                foreach (IVASeat s in craft.Parts.Modules.Get<IVASeat>())
                {
                    if (s.AssignedKittenHash != KeyHash.Zero) { seat = s; break; }
                }
            }
            if (seat is null || !EVADoor.PerformEvaForSeat(craft, seat)) return Failed("nobody aboard to get out");
        }

        if (Buggies.Report(craft) is { } report) return Done(report);
        return command.Flag("eva", false) || command.Flag("crew", false) ? Done(new() { ["seats"] = craft.SeatCount })
                                                                          : Failed("that craft is not a buggy");
    }

    // A driver that follows a route round the laid circuit: start=true sets one going on a car, stop=true
    // ends it, and either way or with neither what it has come to so far is answered. It never holds the queue.
    private static Reply Lap(BridgeCommand command)
    {
        if (CraftNamed(command.String("craft")) is not { } craft) return Failed("no such craft");

        if (command.Flag("start", false))
        {
            List<int>? through = null;
            if (command.String("route") is { Length: > 0 } listed)
            {
                through = [];
                foreach (string id in listed.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                {
                    if (!int.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out int node)) return Failed($"route: '{id}' is not a point's id");
                    through.Add(node);
                }
            }
            // Stretches where a crest is a jump that is meant: "3651-4091,5000-5100", metres along the route.
            List<(double, double)> zones = [];
            foreach (string zone in (command.String("jump_zones") ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                string[] ends = zone.Split('-');
                if (ends.Length != 2 || !double.TryParse(ends[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double from)
                    || !double.TryParse(ends[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double to))
                {
                    return Failed($"jump_zones: '{zone}' is not from-to in metres");
                }
                zones.Add((from, to));
            }
            List<double> nudges = [];
            foreach (string nudge in (command.String("line") ?? "").Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (!double.TryParse(nudge, NumberStyles.Float, CultureInfo.InvariantCulture, out double metres)) return Failed($"line: '{nudge}' is not metres");
                nudges.Add(metres);
            }
            Autopilot.Tuning? tune = new Autopilot.Tuning().With(command.String("tune") ?? "", out string badTune);
            if (tune is null) return Failed($"tune: {badTune}");
            if (!Laps.Start(craft, through, (int)command.Number("laps", 1.0), command.Number("speed", 0.0), command.Number("offset", 0.0),
                            command.Number("timeout", 600.0), command.Flag("place", true), command.Flag("rows", false),
                            command.Flag("jump", false), out string why, command.Flag("racing", false),
                            Math.Clamp(command.Number("push", 0.0), 0.0, 1.0), zones, tune, command.Number("inside", 0.0), nudges))
            {
                return Failed(why);
            }
        }
        else if (command.Flag("stop", false) && !Laps.Stop(craft))
        {
            return Failed("that craft has no lap running");
        }

        return Laps.Status(craft) is { } status ? Done(status) : Failed("that craft has driven no lap");
    }

    // A test road laid from lat/lon along a heading, on the ground the whole way; clear=true takes it up.
    private static Reply Road(BridgeCommand command)
    {
        if (command.Flag("clear", false))
        {
            Roads.Clear();
            return Done(new() { ["laid"] = false });
        }
        if (KsaWorld.ControlledVehicle is not { } flown || KsaWorld.ParentBody(flown) is not { } body)
        {
            return Failed("no body under the craft");
        }
        if (command.Has("racing_line"))
        {
            RacingLine.Set(command.Flag("racing_line", true));
            return Done(RacingLine.Report());
        }
        if (command.Has("box_top"))
        {
            if (!RoadColliders.Installed) return Failed("the road colliders are not hooked");
            double size = command.Number("box_size", 12.0);
            double3 over = (KsaWorld.PositionEcl(flown) - body.GetPositionEcl()).Transform(body.GetCcf2Cce().Inverse());
            Roads.TestBox(body, over, size, command.Number("box_thick", 0.5), command.Number("box_top", 0.6));
            return Done(new() { ["box_m"] = size, ["drawn"] = false, ["adds_so_far"] = RoadColliders.Adds });
        }
        if (command.Has("mesh_test") || command.Has("mesh_size") || command.Has("mesh_bend"))
        {
            if (!RoadDrawHook.Installed) return Failed("the road hook is not installed");
            double bend = command.Number("mesh_bend", 0.0);
            double3 under = (KsaWorld.PositionEcl(flown) - body.GetPositionEcl()).Transform(body.GetCcf2Cce().Inverse());
            Dictionary<string, object?> report = Roads.TestMesh(body, under, command.Number("mesh_size", 10.0),
                command.Number("mesh_test", 3.0), bend, (int)command.Number("mesh_cells", bend == 0.0 ? MeshPatch.MaxCells : 12));
            return report.TryGetValue("error", out object? error) ? new Reply(false, error?.ToString() ?? "failed", report) : Done(report);
        }
        if (command.Has("clutter_near"))
        {
            double3 hereCcf = (KsaWorld.PositionEcl(flown) - body.GetPositionEcl()).Transform(body.GetCcf2Cce().Inverse());
            List<Dictionary<string, object?>> near = Roads.ClutterNear(body, hereCcf, Math.Clamp(command.Number("clutter_near", 10.0), 1.0, 60.0));
            return Done(new() { ["cover_here"] = Roads.SurfaceOn(body)?.Over(Vec.Unit(hereCcf)).ToString(), ["count"] = near.Count, ["instances"] = near.Take(40).ToList() });
        }
        if (command.Flag("probe_clutter", false)) return Done(Roads.ProbeClutter(body).ToDictionary(k => k.Key, k => (object?)k.Value));

        Circuit circuit;
        if (command.String("circuit") is { Length: > 0 } name)
        {
            if (CircuitLibrary.Load(name, out string why) is not { } read) return Failed(why);
            circuit = read;

            // With a place it is laid there and not where its file has it, on whatever body the craft is on.
            if (command.Has("lat") && command.Has("lon"))
            {
                circuit = read.MovedTo(command.Number("lat", 0.0), command.Number("lon", 0.0), command.Number("heading", 0.0), body.MeanRadius, body.Id);
            }
            else if (read.Body != body.Id)
            {
                return Failed($"'{read.Name}' is on {read.Body}: give lat and lon to lay it here");
            }
            if (command.String("save_as") is { Length: > 0 } kept && !CircuitLibrary.Save(circuit with { Name = kept, RadiusM = body.MeanRadius }, out string notKept))
            {
                return Failed(notKept);
            }
        }
        else
        {
            circuit = TestCircuit(body, command.Number("lat", 0.0), command.Number("lon", 0.0), command.Number("heading", 0.0),
                Math.Clamp(command.Number("length", 300.0), 10.0, 20_000.0), Math.Clamp(command.Number("width", 8.0), 1.0, 40.0));
            if (command.String("save_as") is { Length: > 0 } saveAs && !CircuitLibrary.Save(circuit with { Name = saveAs, RadiusM = body.MeanRadius }, out string failed))
            {
                return Failed(failed);
            }
        }

        if (command.Has("edit"))
        {
            RoadEditor.AskEnabled = command.Flag("edit", false);
            RoadEditor.AskCircuit = circuit;
            if (command.Has("view_distance"))
            {
                RoadEditor.AskView = (command.Number("view_yaw", 0.0), command.Number("view_pitch", 60.0), command.Number("view_distance", 250.0));
            }
            return Done(new() { ["editing"] = RoadEditor.AskEnabled, ["roads"] = circuit.Roads.Count });
        }

        // A point dragged a metre a step, as the editor lays it: what each frame of a drag costs.
        if (command.Has("drag_node") && circuit.Find((int)command.Number("drag_node", 0.0)) is { } dragged)
        {
            Roads.CachesDragGround = command.Flag("ground_cache", true);
            Roads.TakesGoneRuns = command.Flag("take_gone", true);
            double stepDeg = command.Number("step_m", 1.0) / 111_319.0;
            int stale = 0;
            HashSet<int> touched = [dragged.Id];
            foreach (Circuit.Road road in circuit.Roads.Where(r => r.Touches(dragged.Id)))
            {
                touched.Add(road.From);
                touched.Add(road.To);
            }
            List<double> lays = [], meshes = [];
            int reads = 0, steps = (int)Math.Clamp(command.Number("steps", 20.0), 1.0, 200.0);
            for (int k = 0; k < steps; k++)
            {
                Circuit moved = circuit.MoveNode(dragged.Id, dragged.LatDeg + (k * stepDeg), dragged.LonDeg + (k * stepDeg));
                Roads.Lay(body, moved, 0.07, 6.0, whole: false, touched);
                lays.Add(Roads.LastLay.Ms);
                reads += Roads.LastLay.TerrainReads;
                Roads.MeshPending();
                meshes.Add(Roads.LastDragMeshMs);
                stale = Math.Max(stale, Roads.StaleRuns);
            }
            Roads.Lay(body, circuit, 0.07, 2.0, whole: true);
            Roads.TakesGoneRuns = true;
            return Done(new()
            {
                ["steps"] = steps, ["touched_points"] = touched.Count, ["ground_cache"] = Roads.CachesDragGround,
                ["lay_ms_first"] = Math.Round(lays[0], 1), ["lay_ms_mean_after"] = Math.Round(lays.Skip(1).DefaultIfEmpty(0.0).Average(), 1),
                ["lay_ms_worst_after"] = Math.Round(lays.Skip(1).DefaultIfEmpty(0.0).Max(), 1), ["terrain_reads_a_step"] = reads / steps,
                ["mesh_ms_mean"] = Math.Round(meshes.Average(), 1), ["mesh_ms_worst"] = Math.Round(meshes.Max(), 1),
                ["whole_lay_ms"] = Math.Round(Roads.LastLay.Ms, 1), ["most_stale_runs"] = stale,
            });
        }

        (int points, double low, double high) = Roads.Lay(body, circuit, command.Number("lift", 0.07),
            Math.Clamp(command.Number("spacing", 2.0), 0.25, 20.0), whole: true);
        KsaWorld.TrySeaLevel(body, out double sea);
        if (command.Flag("clutter", true)) Roads.ClearClutter(command.Number("margin", 1.5));
        Dictionary<string, object?> reply = new()
        {
            ["laid"] = Roads.Any, ["roads"] = circuit.Roads.Count, ["points"] = points, ["cleared_before"] = Roads.ClutterTaken,
            ["lowest_ground_m"] = Math.Round(low - sea, 2), ["highest_ground_m"] = Math.Round(high - sea, 2),
            ["library"] = CircuitLibrary.Names(),
        };
        foreach ((string key, object? value) in Roads.Status()) reply[key] = value;
        return Done(reply);
    }

    // A through road with a bend in it, a side road off its middle and a road closing the two into a
    // loop: every kind of meeting the layout has, starting at the place asked for.
    private static Circuit TestCircuit(KSA.Celestial body, double lat, double lon, double headingDeg, double length, double width)
    {
        double heading = headingDeg * Math.PI / 180.0;
        double perDeg = body.MeanRadius * Math.PI / 180.0;
        (double Lat, double Lon) At(double ahead, double right)
        {
            double north = (ahead * Math.Cos(heading)) - (right * Math.Sin(heading));
            double east = (ahead * Math.Sin(heading)) + (right * Math.Cos(heading));
            return (lat + (north / perDeg), lon + (east / (perDeg * Math.Cos(lat * Math.PI / 180.0))));
        }

        (double Lat, double Lon) b = At(0.5 * length, 0.0), c = At(length, 0.3 * length), d = At(0.5 * length, 0.5 * length);
        return new Circuit { Name = "Test", Body = body.Id, WidthM = width }
            .AddNode(lat, lon, out int start).Extend(start, b.Lat, b.Lon, out int mid).Extend(mid, c.Lat, c.Lon, out int end)
            .Extend(mid, d.Lat, d.Lon, out int side).Connect(side, end);
    }

    // The ground's height against sea level at lat/lon, negative where it is seabed -- or along a line
    // to to_lat/to_lon in steps -- so a place can be surveyed without setting a craft down on it.
    private static Reply Ground(BridgeCommand command)
    {
        if (KsaWorld.ControlledVehicle is not { } flown || KsaWorld.ParentBody(flown) is not { } body)
        {
            return Failed("no body under the craft");
        }
        KsaWorld.TrySeaLevel(body, out double sea);

        double lat = command.Number("lat", 0.0), lon = command.Number("lon", 0.0);
        double toLat = command.Number("to_lat", lat), toLon = command.Number("to_lon", lon);
        int steps = Math.Clamp((int)command.Number("steps", 0.0), 0, 200);

        List<object?> line = [];
        for (int i = 0; i <= steps; i++)
        {
            double t = steps == 0 ? 0.0 : (double)i / steps;
            double la = lat + ((toLat - lat) * t), lo = lon + ((toLon - lon) * t);
            double3 dir = body.GetDirCcfFromLatLon(la, lo);
            double height = body.GetTerrainHeightFromDirCcf(dir, accurate: true);
            line.Add(new Dictionary<string, object?>
            {
                ["lat"] = Math.Round(la, 5), ["lon"] = Math.Round(lo, 5), ["ground_m"] = Math.Round(height - sea, 1),
            });
        }

        return Done(new() { ["sea_level_m"] = Math.Round(sea, 1), ["points"] = line });
    }

    // The game written to a save of this name, as KSA's own save console command writes it.
    private static Reply SaveGame(BridgeCommand command)
    {
        string name = command.String("name");
        if (name.Length == 0) return Failed("a save needs a name");
        GameSaves.MakeUncompressedSave(name);
        return Done(new() { ["name"] = name });
    }

    // A craft by the name it shows, falling back to the one being flown.
    private static Vehicle? CraftNamed(string name)
    {
        if (string.IsNullOrEmpty(name)) return KsaWorld.ControlledVehicle;

        foreach (Vehicle v in KsaWorld.Vehicles)
        {
            if (KsaWorld.IsAlive(v) && KsaWorld.DisplayName(v) == name) return v;
        }

        return null;
    }

    private Reply? Site(BridgeCommand command)
    {
        if (CraftNamed(command.String("craft")) is not { } craft) return Failed("no such craft");
        if (KsaWorld.ParentBody(craft) is not { } here) return Failed("no body under the craft");

        string body = command.String("body");
        if (body.Length == 0) body = here.Id;

        if (!KsaWorld.TryPlaceOnSurface(craft, body, command.Number("lat", 0.0), command.Number("lon", 0.0)))
        {
            return Failed($"could not place the craft on {body}");
        }

        // Answered a moment later: the move lands on a later frame, and the sun read on this one is
        // the sun where the craft was.
        double waited = 0.0;
        _running = (dtPlayer, _) =>
        {
            waited += dtPlayer;
            if (waited < 1.0) return null;

            return KsaWorld.ParentBody(craft) is { } now
                       ? Done(new() { ["body"] = now.Id,
                                      ["sun_elevation_deg"] = Math.Round(
                                          KsaWorld.SunElevationDeg(now, KsaWorld.PositionEcl(craft)), 2) })
                       : Done();
        };

        return null;
    }

    // ---- commands that take frames ----------------------------------------------------------

    // Runs the world for so many simulated seconds and stops it again, so what is photographed next
    // is at an age that was asked for rather than one that happened.
    private Reply? BeginStep(BridgeCommand command)
    {
        double seconds = command.Number("seconds", 1.0);
        if (!(seconds > 0.0)) return Failed("seconds must be positive");

        double advanced = 0.0;
        double waited = 0.0;
        KsaWorld.SetPaused(false);

        _running = (dtPlayer, dtSim) =>
        {
            waited += dtPlayer;
            advanced += Math.Max(dtSim, 0.0);

            if (advanced >= seconds)
            {
                KsaWorld.SetPaused(true);
                return Done(new() { ["advanced_s"] = Math.Round(advanced, 3) });
            }

            return waited > (seconds * 20.0) + 30.0 ? Failed($"only {advanced:F2} s passed") : null;
        };

        return null;
    }

    private Reply? BeginLoad(BridgeCommand command)
    {
        string save = command.String("save");
        if (save.Length == 0) return Failed("load needs a save");

        try
        {
            GameSaves.LoadSaveGame(save);
        }
        catch (Exception e)
        {
            return Failed($"could not load '{save}': {e.Message}");
        }

        double waited = 0.0;

        _running = (dtPlayer, _) =>
        {
            waited += dtPlayer;

            // A beat past the craft appearing, for the world to settle round it.
            if (waited > 3.0 && KsaWorld.InFlight)
            {
                return Done(new() { ["craft"] = KsaWorld.DisplayName(KsaWorld.ControlledVehicle!) });
            }

            return waited > 60.0 ? Failed("no craft after 60 s") : null;
        };

        return null;
    }

    // Photographs through KSA's own capture, which names files to the second: so each one is moved
    // aside and renamed before the next is asked for, and two can never be one file. Every picture
    // gets a manifest beside it -- the time, the camera and where the craft is on screen -- so nothing
    // has to be matched up by its time afterwards.
    private Reply? BeginCapture(BridgeCommand command)
    {
        string label = command.String("label");
        if (label.Length == 0) label = "shot";
        label = string.Concat(label.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));

        int frames = Math.Clamp((int)command.Number("frames", 1.0), 1, 120);
        double everySim = Math.Max(command.Number("every_s", 0.0), 0.0);
        int everyFrames = Math.Max((int)command.Number("every_frames", 1.0), 1);

        string folder = Path.Combine(Outbox, command.Id);
        Directory.CreateDirectory(folder);

        string shots = Path.Combine(KSA.Constants.DocumentsFolderPath, "exports", "screenshots");

        List<Dictionary<string, object?>> taken = [];
        int index = 0;
        bool waiting = false;
        DateTime askedAt = default;
        Dictionary<string, object?>? manifest = null;
        double sinceSim = 0.0;
        int sinceFrames = 0;
        double waited = 0.0;
        int warming = 0;

        // Paused with a spacing asked for, the world is run that far and stopped again before each
        // picture, so the ages are exactly the ones asked for however long a command takes to arrive.
        bool stepping = KsaWorld.IsPaused && everySim > 0.0;
        int settle = 0;

        _running = (dtPlayer, dtSim) =>
        {
            if (!waiting)
            {
                sinceSim += Math.Max(dtSim, 0.0);
                sinceFrames++;

                if (stepping && index > 0)
                {
                    if (sinceSim < everySim)
                    {
                        if (KsaWorld.IsPaused) KsaWorld.SetPaused(false);
                        return null;
                    }

                    if (!KsaWorld.IsPaused)
                    {
                        KsaWorld.SetPaused(true);
                        settle = 0;
                    }

                    // A frame or two for the paused world to be the one drawn.
                    if (++settle < 3) return null;
                }

                bool due = index == 0 || stepping
                           || (everySim > 0.0 ? sinceSim >= everySim : sinceFrames >= everyFrames);
                if (!due) return null;

                askedAt = DateTime.UtcNow.AddSeconds(-0.5);

                // KSA's own warm-up hides the UI for these frames before the picture, so nothing blended
                // over frames still carries a window; the manifest is written on the frame the picture
                // is actually taken, so it describes that instant.
                if (!KsaWorld.TryRequestScreenshot(flags: $"warm={WarmFrames}"))
                {
                    return Failed("KSA would not take a screenshot");
                }

                manifest = null;
                warming = WarmFrames;
                waiting = true;
                waited = 0.0;
                return null;
            }

            if (manifest is null && --warming <= 0) manifest = Manifest(label, index);

            waited += dtPlayer;
            if (waited > 10.0) return Failed($"screenshot {index} never arrived");

            string? file = Directory.Exists(shots)
                               ? new DirectoryInfo(shots).GetFiles("ksa_*.png")
                                                         .Where(f => f.LastWriteTimeUtc >= askedAt && f.Length > 0)
                                                         .OrderByDescending(f => f.LastWriteTimeUtc)
                                                         .FirstOrDefault()?.FullName
                               : null;
            if (file is null) return null;

            string name = $"{index:D2}-{label}";
            string target = Path.Combine(folder, name + ".png");

            try
            {
                File.Move(file, target, overwrite: true);
            }
            catch (IOException)
            {
                // Still being written; the next frame will find it finished.
                return null;
            }

            manifest ??= Manifest(label, index);
            manifest["file"] = target;
            File.WriteAllText(Path.Combine(folder, name + ".json"), JsonSerializer.Serialize(manifest, JsonOptions));
            taken.Add(manifest);

            index++;
            waiting = false;
            sinceSim = 0.0;
            sinceFrames = 0;

            return index >= frames ? Done(new() { ["folder"] = folder, ["frames"] = taken }) : null;
        };

        return null;
    }

    // How many frames the UI is hidden for before a capture.
    private const int WarmFrames = 24;

    // What a picture was of, recorded on the frame it was taken.
    private static Dictionary<string, object?> Manifest(string label, int index)
    {
        Dictionary<string, object?> m = new()
        {
            ["label"] = label,
            ["index"] = index,
            ["wall"] = DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture),
            ["paused"] = KsaWorld.IsPaused,
            ["speed"] = KsaWorld.SimulationSpeed,
            ["fov_deg"] = Math.Round(KsaWorld.MainViewFovDeg(), 2),
        };

        if (KsaWorld.ControlledVehicle is { } craft)
        {
            m["craft"] = KsaWorld.DisplayName(craft);
            m["craft_screen"] = Screen(KsaWorld.PositionEcl(craft));
        }

        return m;
    }

    // Where a point falls in the picture, as fractions from the top left, or null when it is behind
    // the camera. The same matrix the frame was drawn with, so a crop taken off it is exact.
    private static double[]? Screen(double3 pointEcl)
    {
        if (Program.GetMainCamera() is not { } camera) return null;

        double4 clip = camera.EgoToClipDouble(pointEcl - camera.PositionEcl);
        if (!(clip.W > 1e-6)) return null;

        return [Math.Round((clip.X / clip.W * 0.5) + 0.5, 4), Math.Round((clip.Y / clip.W * 0.5) + 0.5, 4)];
    }
}
