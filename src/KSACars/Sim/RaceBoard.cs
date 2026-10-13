using Brutal.Numerics;

namespace KSACars;

/// <summary>
/// What a race is to a board that shows it: how far each car has gone round a closed route, which lap
/// that puts it on and where in the order, and where a place is on a map drawn about one car with the
/// way that car faces at the top.
/// </summary>
internal static class RaceBoard
{
    /// <summary>How far apart the checkpoints of a course are, as near as a whole number of them round the lap comes, and the fewest and most a course has.</summary>
    public const double GateEveryM = 300.0;
    public const int FewestGates = 4, MostGates = 24;

    /// <summary>Moved further than this along the route between two looks, a car was not driven there: it was put there, or found again after cutting across.</summary>
    public const double JumpM = 60.0;

    /// <summary>Past a checkpoint by more than this without having been through it, a car is told it missed one.</summary>
    public const double MissedByM = 30.0;

    /// <summary>How many checkpoints a lap of this length has, the line being the first.</summary>
    public static int Gates(double lengthM) => Math.Clamp((int)Math.Round(lengthM / GateEveryM), FewestGates, MostGates);

    /// <summary>
    /// One car's race round a closed route: the checkpoints it has been through, in order, the line
    /// being the first of each lap. A checkpoint counts when the car is driven over it, forwards and
    /// on the road; a car found again further on, having cut across or been put there, has not been
    /// through what is between, and until it goes back for the one it missed neither its laps nor
    /// its place go on.
    /// </summary>
    public sealed class Tally(double lengthM, double lineM)
    {
        private readonly int _gates = Gates(lengthM);
        private double _last;
        private bool _seen;

        // How far it has been driven since the last checkpoint it went through, back counting against forward: not how far it is from it.
        private double _driven;

        // When it was last seen and when its lap began, s; the time into the lap it was at each checkpoint, this lap and on its best.
        private double _lastTime, _lapBegan = double.NaN, _raceBegan = double.NaN;
        private readonly double[] _splits = new double[Gates(lengthM)], _bestSplits = new double[Gates(lengthM)];

        /// <summary>How long its last whole lap took and its quickest took, s, or nothing before it has one.</summary>
        public double? LastLapS { get; private set; }

        public double? BestLapS { get; private set; }

        /// <summary>
        /// At the last checkpoint it went through, how much later than on its best lap it was there, s,
        /// earlier being less than nothing, and when that was; at the line it is the whole lap against
        /// the best before it. Nothing until it has a lap to be measured against.
        /// </summary>
        public (double DeltaS, double At)? Split { get; private set; }

        /// <summary>How long it has been racing, s, from when it was first over the line, or nothing before that.</summary>
        public double? RaceSoFar(double now) => double.IsNaN(_raceBegan) ? null : now - _raceBegan;

        /// <summary>How long it has been on the lap it is on, s, or nothing before it has been over the line.</summary>
        public double? LapSoFar(double now) => double.IsNaN(_lapBegan) ? null : now - _lapBegan;

        /// <summary>The checkpoints it has been through since the start, the line that started its first lap among them.</summary>
        public int Passed { get; private set; }

        /// <summary>Whether it is past the checkpoint it is due at without having been through it.</summary>
        public bool Missed { get; private set; }

        /// <summary>How far it has raced, m: to the last checkpoint it went through and on from there no further than the next. Less than nothing on the grid.</summary>
        public double ProgressM { get; private set; }

        /// <summary>The lap it is on, from 1: the first until it has been through every checkpoint and over the line again.</summary>
        public int Lap => Passed == 0 ? 1 : ((Passed - 1) / _gates) + 1;

        // Over a checkpoint at a time: the line ends a lap and begins the next, any other is a split of the lap it is on.
        private void Through(int gate, double at)
        {
            if (gate != 0)
            {
                if (double.IsNaN(_lapBegan)) return;
                _splits[gate] = at - _lapBegan;
                if (BestLapS is not null && _bestSplits[gate] > 0.0) Split = (_splits[gate] - _bestSplits[gate], at);
                return;
            }
            if (!double.IsNaN(_lapBegan))
            {
                double lap = at - _lapBegan;
                if (BestLapS is { } best) Split = (lap - best, at);
                LastLapS = lap;
                if (BestLapS is null || lap < BestLapS)
                {
                    BestLapS = lap;
                    Array.Copy(_splits, _bestSplits, _gates);
                }
            }
            _lapBegan = at;
            if (double.IsNaN(_raceBegan)) _raceBegan = at;
            Array.Clear(_splits);
        }

        private double Spacing => lengthM / _gates;

        /// <summary>Where along the route the checkpoint it is due at is, m.</summary>
        public double NextGateM => Wrapped(lineM + ((Passed % _gates) * Spacing));

        private double Wrapped(double s) => ((s % lengthM) + lengthM) % lengthM;

        /// <summary>Told where along the route the car is now, whether it is on the road there, and the time, s.</summary>
        public void See(double s, bool onRoad, double now = 0.0)
        {
            if (!(lengthM > 0.0)) return;
            double moved = _seen ? s - _last : 0.0;
            if (moved > 0.5 * lengthM) moved -= lengthM;
            else if (moved < -0.5 * lengthM) moved += lengthM;

            // Driven over the checkpoint it is due at: between where it was and where it is, going forwards.
            double toGate = Wrapped(NextGateM - _last);
            bool driven = _seen && Math.Abs(moved) <= JumpM;
            if (driven) _driven += moved;
            if (driven && onRoad && moved > 0.0 && toGate > 0.0 && toGate <= moved)
            {
                // When it was over it: as far through the time between the two looks as the checkpoint is through the way between them.
                Through(Passed % _gates, _lastTime + ((now - _lastTime) * toGate / moved));
                (Passed, _driven) = (Passed + 1, moved - toGate);
            }
            (_seen, _last, _lastTime) = (true, s, now);

            if (Passed == 0)
            {
                // On the grid, behind the line: the nearer it, the further forward.
                (ProgressM, Missed) = (-Wrapped(lineM - s), false);
                return;
            }
            double beyond = Wrapped(s - Wrapped(lineM + (((Passed - 1) % _gates) * Spacing)));
            // Not between the last one and the next: driven back to before the last, or past the next without going through it.
            if (beyond > Spacing && _driven < 0.0) beyond = 0.0;
            Missed = beyond > Spacing + MissedByM;
            ProgressM = ((Passed - 1) * Spacing) + Math.Min(beyond, Spacing);
        }
    }

    /// <summary>A time as a lap is written: minutes, seconds and thousandths, "1:26.540".</summary>
    public static string Clock(double seconds)
    {
        double s = Math.Max(seconds, 0.0);
        int minutes = (int)(s / 60.0);
        return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{minutes}:{s - (minutes * 60.0):00.000}");
    }

    /// <summary>A difference of time as a split is written: "-0.312" for earlier, "+1.045" for later.</summary>
    public static string Gap(double seconds) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{(seconds < 0.0 ? "-" : "+")}{Math.Abs(seconds):0.000}");

    /// <summary>A car's place in the order, from 1: one more than the cars that have gone further.</summary>
    public static int Position(ReadOnlySpan<double> totalsM, int car)
    {
        int ahead = 0;
        for (int i = 0; i < totalsM.Length; i++)
        {
            if (i != car && totalsM[i] > totalsM[car]) ahead++;
        }
        return ahead + 1;
    }

    /// <summary>
    /// Where a place is on a map about <paramref name="centre"/>, in metres to the right of it and
    /// metres down the picture, with <paramref name="ahead"/> at the top: the map turns as the car does.
    /// </summary>
    public static (double Right, double Down) OnMap(double3 place, double3 centre, double3 ahead)
    {
        double3 up = Vec.Unit(centre), nose = Vec.Unit(Vec.RejectFrom(ahead, up));
        double3 to = place - centre;
        return (-Vec.Dot(to, Vec.Cross(up, nose)), -Vec.Dot(to, nose));
    }
}
