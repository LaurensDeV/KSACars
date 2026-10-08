using System.Reflection;
using Brutal.Numerics;
using KSA;
using RenderCore;
using RenderCore.Animation;

namespace KSACars;

/// <summary>
/// The driver kitten's hands on the steering wheel: each arm solved onto its grip on the rim every
/// frame, after the seated animation has posed the skeleton and before it is skinned.
///
/// <para>KSA has no steering animation and no IK in use, but it runs every <see cref="IAnimProcessor"/>
/// on a character's model between those two points, which is where its eyes are turned to look at the
/// camera. The solve places the joints absolutely, so the engine calling it once per viewport changes
/// nothing on the second call.</para>
/// </summary>
internal sealed class DriverHands(BuggyDrive drive) : IAnimProcessor
{
    private static readonly string[] BoneNames = ["Shoulder_R", "Elbow_R", "Wrist_R", "Shoulder_L", "Elbow_L", "Wrist_L"];

    private readonly BuggyDrive _drive = drive;
    private int[]? _bones;
    private bool _failed;

    /// <summary>How many times the arms have been solved, and how far the wrists ended from their grips, cm.</summary>
    public int Solves { get; private set; }

    public double MissCm { get; private set; }

    public float Priority => 5f;

    public bool ShouldUpdate { get; set; } = true;

    public bool CanCacheAnimation => false;

    public void UpdateLocalPose(float4x4 transform, Skeleton skeleton, Span<TransformTRS> localPose, float dt)
    {
    }

    public void UpdateSkeleton(float4x4 transform, Skeleton skeleton, float dt)
    {
        if (_failed) return;

        try
        {
            if ((_bones ??= Resolve(skeleton)) is not { } bones) return;

            (double3 leftPart, double3 rightPart) = SteeringGrip.GripsPart(_drive.Profile, _drive.SteerAngle);
            double right = Hold(skeleton, bones[0], bones[1], bones[2], SteeringGrip.ToKittenModel(_drive.Profile, rightPart), -1.0);
            double left = Hold(skeleton, bones[3], bones[4], bones[5], SteeringGrip.ToKittenModel(_drive.Profile, leftPart), 1.0);
            MissCm = Math.Max(right, left);
            Solves++;
            ReadHead(skeleton);
        }
        catch (Exception e)
        {
            _failed = true;
            Log.Warn($"driver's hands: {e.Message}; the kitten keeps its seated pose");
        }
    }

    /// <summary>
    /// What carries a point of the kitten's unposed mesh to where its head has it this frame, in the
    /// model's centimetres: three rows and a translation, or null before the first solve and where the
    /// kitten has no head bone. Swapped whole, since the frame hook reads it.
    /// </summary>
    public HeadPose? Head { get; private set; }

    public sealed record HeadPose(double3 RowX, double3 RowY, double3 RowZ, double3 TranslationCm);

    private int _head = -2;

    private void ReadHead(Skeleton skeleton)
    {
        if (_head == -2) _head = skeleton.BoneNames?.IndexOf(HeadBone) ?? -1;
        if (_head < 0 || skeleton.InverseBindPose is not { } unposed) return;

        float4x4 m = unposed.AsSpan()[_head] * skeleton.WorldTransforms[_head];
        Head = new HeadPose(new double3(m.M11, m.M12, m.M13), new double3(m.M21, m.M22, m.M23), new double3(m.M31, m.M32, m.M33),
                            double3.Unpack(m.Translation));
    }

    private const string HeadBone = "Head_M";

    private int[]? Resolve(Skeleton skeleton)
    {
        List<string>? names = skeleton.BoneNames;
        int[] found = new int[BoneNames.Length];
        for (int i = 0; i < BoneNames.Length; i++)
        {
            found[i] = names?.IndexOf(BoneNames[i]) ?? -1;
            if (found[i] < 0)
            {
                _failed = true;
                Log.Warn($"driver's hands: the kitten has no bone '{BoneNames[i]}'");
                return null;
            }
        }
        return found;
    }

    // outward is +1 for the left arm and -1 for the right: model +X is the kitten's left.
    // Returns how far the wrist ended from where it was sent.
    private static double Hold(Skeleton skeleton, int shoulder, int elbow, int wrist, double3 grip, double outward)
    {
        double3 s = Position(skeleton, shoulder);
        double3 e = Position(skeleton, elbow);
        double3 w = Position(skeleton, wrist);
        double upper = Vec.Len(e - s), fore = Vec.Len(w - e);
        if (!(upper > 0.0) || !(fore > 0.0)) return double.NaN;

        double3 target = grip + (Vec.Unit(s - grip) * (SteeringGrip.PalmMetres * 100.0));
        (double3 elbowAt, double3 wristAt) = SteeringGrip.SolveElbow(s, target, upper, fore, new double3(0.5 * outward, -1.0, 0.0));

        TurnAbout(skeleton, shoulder, s, e - s, elbowAt - s);
        double3 eNow = Position(skeleton, elbow);
        TurnAbout(skeleton, elbow, eNow, Position(skeleton, wrist) - eNow, wristAt - eNow);
        return Vec.Len(Position(skeleton, wrist) - target);
    }

    private static double3 Position(Skeleton skeleton, int bone) => double3.Unpack(skeleton.WorldTransforms[bone].Translation);

    // Row vectors: a point is carried by M, then turned about the pivot.
    private static void TurnAbout(Skeleton skeleton, int bone, double3 pivot, double3 from, double3 to)
    {
        doubleQuat q = Vec.RotationFromTo(from, to);
        float3 p = float3.Pack(pivot);
        floatQuat qf = new((float)q.X, (float)q.Y, (float)q.Z, (float)q.W);
        float4x4 turned = skeleton.WorldTransforms[bone]
                          * float4x4.CreateTranslation(-p)
                          * float4x4.CreateFromQuaternion(qf)
                          * float4x4.CreateTranslation(p);
        skeleton.SetWorldTransform(bone, turned);
    }

    // --- attaching it ---

    private static readonly FieldInfo? AvatarField =
        typeof(KittenRenderable).GetField("_characterAvatar", BindingFlags.NonPublic | BindingFlags.Instance);

    private static readonly FieldInfo? IdleActionsField =
        typeof(KittenRenderable).GetField("_seatedIdleActionAnims", BindingFlags.NonPublic | BindingFlags.Instance);

    private static bool _warned;

    /// <summary>
    /// Puts a processor on a newly seated driver. The seat builds a new renderable whenever its kitten
    /// changes, and the old one takes its processor with it.
    /// </summary>
    public static DriverHands? TryAttach(KittenRenderable renderable, BuggyDrive drive)
    {
        try
        {
            if (AvatarField?.GetValue(renderable) is not CharacterAvatar avatar)
            {
                WarnOnce("KSA's seated kitten has no _characterAvatar; the driver's hands stay in its lap");
                return null;
            }

            DriverHands hands = new(drive);
            avatar.Core.CharacterModel.AnimProcessors.Add(hands);

            // A driver does not stretch or shuffle between laps: those clips would pull the hands off the rim.
            IdleActionsField?.SetValue(renderable, null);
            Log.Info("the driver has taken the wheel");
            return hands;
        }
        catch (Exception e)
        {
            WarnOnce($"driver's hands could not be attached: {e.Message}");
            return null;
        }
    }

    private static void WarnOnce(string message)
    {
        if (_warned) return;
        _warned = true;
        Log.Warn(message);
    }
}
