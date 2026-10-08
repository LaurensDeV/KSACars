using System.Numerics;
using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.CollisionDetection;
using BepuPhysics.Constraints;
using BepuUtilities;
using BepuUtilities.Memory;
using Brutal.Numerics;
using KSACars;

// A box the size of a car's hull flown along a circuit's road just clear of it, in the game's own
// physics engine, one step at a time from a fresh state: every place the engine changes its speed
// is a contact that is not there.
//
//   dotnet run -c Release --project tools/bepurig -- <circuit.json> [m/s] [m clear] [margin cap] [least clear]
//
// The margin cap is the body's most speculative margin, which KSA leaves unbounded. The box is rigid
// and the road is not flat, so where it bends the box's ends come nearer than it was set: a change of
// speed with its underside nearer the road than `least clear` is counted apart, as a scrape.
const double Radius = 6_371_000.0;
string file = args[0];
double speed = args.Length > 1 ? double.Parse(args[1]) : 25.0;
double clear = args.Length > 2 ? double.Parse(args[2]) : 0.14;
float cap = args.Length > 3 ? float.Parse(args[3]) : float.MaxValue;
double dt = 1.0 / 60.0;

static double3 DirOf(double latDeg, double lonDeg)
{
    double lat = latDeg * Math.PI / 180.0, lon = lonDeg * Math.PI / 180.0;
    return new double3(Math.Cos(lat) * Math.Cos(lon), Math.Cos(lat) * Math.Sin(lon), Math.Sin(lat));
}

Circuit circuit = Circuit.FromJson(File.ReadAllText(file), out string why, out _) ?? throw new Exception(why);
RoadLaying.Network laid = RoadLaying.Laid(circuit, DirOf, Radius, _ => 0.0, 0.0, 1.0);
List<RoadMeshData> meshes = [];
foreach (RoadRibbon ribbon in laid.Ribbons) meshes.AddRange(RoadTessellation.Mesh(ribbon, RoadDrawList.Fit));
foreach (RoadJunction junction in laid.Junctions) meshes.Add(RoadTessellation.Mesh(junction));
RoadCollider solid = RoadCollider.Of(meshes) ?? throw new Exception("no triangles");
Route route = Route.Of(circuit, DirOf, Radius, _ => 0.0, 0.0, 1.0, null, 0.0, out why) ?? throw new Exception(why);

RoadSurface surface = new(laid.Ribbons);
BufferPool pool = new();
Simulation sim = Simulation.Create(pool, new Callbacks(), new NoGravity(), new SolveDescription(8, 1));
pool.Take(solid.Triangles, out Buffer<Triangle> triangles);
for (int t = 0; t < solid.Triangles; t++)
{
    triangles[t] = new Triangle(V(solid.Corners[3 * t]), V(solid.Corners[(3 * t) + 1]), V(solid.Corners[(3 * t) + 2]));
}
sim.Statics.Add(new StaticDescription(Vector3.Zero, Quaternion.Identity, sim.Shapes.Add(new Mesh(triangles, Vector3.One, pool))));

// Up, along, across: 0.56 x 4.5 x 1.36, as the F2004's hull box.
Box hull = new(0.56f, 4.5f, 1.36f);
CollidableDescription collidable = new(sim.Shapes.Add(hull), 0f, cap, ContinuousDetection.Discrete);
BodyHandle body = sim.Bodies.Add(BodyDescription.CreateDynamic(new RigidPose(Vector3.Zero), hull.ComputeInertia(605f), collidable, new BodyActivityDescription(-1f)));

double least = args.Length > 4 ? double.Parse(args[4]) : 0.0;
int jolts = 0, shallow = 0, backs = 0;
double back = 0.0;
double worst = 0.0;
for (double s = 5.0; s < route.LengthM - 5.0; s += speed * dt * 0.37)
{
    (double3 at, double3 up, double3 ahead) = route.Standing(s);
    double3 left = Vec.Cross(up, ahead);
    double3 centre = at + (up * (clear + 0.28)) - solid.Origin;
    Matrix3x3 basis = new() { X = V(up), Y = V(ahead), Z = V(Vec.Cross(up, ahead)) };
    BodyReference b = sim.Bodies[body];
    b.Pose = new RigidPose(V(centre), Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(new Matrix4x4(
        basis.X.X, basis.X.Y, basis.X.Z, 0, basis.Y.X, basis.Y.Y, basis.Y.Z, 0, basis.Z.X, basis.Z.Y, basis.Z.Z, 0, 0, 0, 0, 1))));
    b.Velocity = new BodyVelocity(V(ahead * speed), Vector3.Zero);
    b.UpdateBounds();
    sim.Timestep((float)dt);
    Vector3 dv = sim.Bodies[body].Velocity.Linear - V(ahead * speed);
    double lost = dv.Length();
    if (lost > 0.3)
    {
        // How near the road the box's underside really is, straight up and down, at its nearest.
        double nearest = double.PositiveInfinity;
        for (int i = -4; i <= 4; i++)
        {
            for (int j = -1; j <= 1; j++)
            {
                double3 under = at + (up * clear) + (ahead * (2.25 * i / 4.0)) + (left * (0.68 * j));
                if (surface.TryHeightOver(under + Vec.Unit(under), out double over)) nearest = Math.Min(nearest, (over - 1.0) * Vec.Dot(up, Vec.Unit(under)));
            }
        }
        back = Math.Max(back, -Vector3.Dot(dv, V(ahead)));
        if (-Vector3.Dot(dv, V(ahead)) > 2.0) backs++;
        if (nearest < least) { shallow++; continue; }
        jolts++;
        worst = Math.Max(worst, lost);
        if (jolts <= 25)
        {
            Console.WriteLine($"  s={s,8:F2} h={Vec.Len(at) - Radius,7:F2}  dv {lost,5:F1} m/s: {Vector3.Dot(dv, V(ahead)),6:F1} ahead {Vector3.Dot(dv, V(up)),6:F1} up {Vector3.Dot(dv, V(left)),6:F1} left, underside {nearest:F3} m clear");
        }
    }
}
Console.WriteLine($"{Path.GetFileNameWithoutExtension(file)}: {solid.Triangles} triangles, {route.LengthM:F0} m at {speed} m/s, {clear} m clear, margin cap {cap}: {jolts} jolts with the underside {least} m clear or more ({shallow} nearer), worst {worst:F1} m/s; over all, {backs} took 2 m/s or more off its speed, the most {back:F1}");

static Vector3 V<T>(T v) => v switch
{
    double3 d => new Vector3((float)d.X, (float)d.Y, (float)d.Z),
    float3 f => new Vector3(f.X, f.Y, f.Z),
    _ => throw new ArgumentException(),
};

struct Callbacks : INarrowPhaseCallbacks
{
    public void Initialize(Simulation simulation) { }
    public bool AllowContactGeneration(int workerIndex, CollidableReference a, CollidableReference b, ref float speculativeMargin) => true;
    public bool AllowContactGeneration(int workerIndex, CollidablePair pair, int childIndexA, int childIndexB) => true;
    public bool ConfigureContactManifold<TManifold>(int workerIndex, CollidablePair pair, ref TManifold manifold, out PairMaterialProperties material)
        where TManifold : unmanaged, IContactManifold<TManifold>
    {
        material = new PairMaterialProperties(1f, 2f, new SpringSettings(30f, 1f));
        return true;
    }
    public bool ConfigureContactManifold(int workerIndex, CollidablePair pair, int childIndexA, int childIndexB, ref ConvexContactManifold manifold) => true;
    public void Dispose() { }
}

struct NoGravity : IPoseIntegratorCallbacks
{
    public readonly AngularIntegrationMode AngularIntegrationMode => AngularIntegrationMode.Nonconserving;
    public readonly bool AllowSubstepsForUnconstrainedBodies => false;
    public readonly bool IntegrateVelocityForKinematics => false;
    public void Initialize(Simulation simulation) { }
    public void PrepareForIntegration(float dt) { }
    public void IntegrateVelocity(Vector<int> bodyIndices, Vector3Wide position, QuaternionWide orientation, BodyInertiaWide localInertia,
                                  Vector<int> integrationMask, int workerIndex, Vector<float> dt, ref BodyVelocityWide velocity) { }
}
