using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VeraFirefly.VmdConverter.Core.Editor
{
    // Native Unity evaluator, owned by one bake. Does not change the sampling rig,
    // body/root channels, fingers, legs or the selected conversion mode.
    internal sealed class PmxHumanPoseContinuity : IDisposable
    {
        // A branch correction must not replace the original clavicle motion.
        private const float MaximumShoulderCorrectionDegrees = 15f;
        private readonly Transform[] _input = new Transform[8];
        private readonly Transform[] _decoded = new Transform[8];
        private readonly HumanPoseHandler _handler;
        private readonly GameObject _root;
        private readonly float[] _negative = new float[18], _positive = new float[18];
        private static readonly int[][] ArmBones = { new[] { 0, 2, 4, 6 }, new[] { 1, 3, 5, 7 } };
        private readonly Quaternion[] _raw = new Quaternion[8], _expected = new Quaternion[4];
        private readonly float[] _baseline = new float[HumanTrait.MuscleCount],
            _start = new float[HumanTrait.MuscleCount], _best = new float[HumanTrait.MuscleCount],
            _values = new float[HumanTrait.MuscleCount], _probe = new float[HumanTrait.MuscleCount],
            _next = new float[HumanTrait.MuscleCount];
        private readonly double[] _residual = new double[30], _probeResidual = new double[30],
            _candidateResidual = new double[30], _scoreResidual = new double[30];
        private readonly double[,] _jacobian = new double[9, 30], _matrix = new double[9, 10];
        private bool _hasPrevious;
        private float[] _previousMuscles;
        private readonly List<HumanPose> _original = new List<HumanPose>();
        private readonly List<HumanPose> _solved = new List<HumanPose>();
        internal int CorrectedFrames { get; private set; }

        internal PmxHumanPoseContinuity(Avatar avatar, Transform sourceRoot,
            Func<HumanBodyBones, Transform> resolve)
        {
            var map = new Dictionary<Transform, Transform>();
            var skeleton = PmxTargetFkAnimationClipBaker.BuildSkeletonLookup(avatar);
            _root = new GameObject(sourceRoot.name) { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                map[sourceRoot] = _root.transform;
                CloneChildren(sourceRoot, _root.transform, skeleton, map);
                for (int i = 0; i < 8; i++)
                {
                    _input[i] = resolve((HumanBodyBones)(11 + i));
                    if (_input[i] != null) _decoded[i] = map[_input[i]];
                }
                for (int i = 0; i < 18; i++)
                {
                    _negative[i] = -HumanTrait.GetMuscleDefaultMin(37 + i);
                    _positive[i] = HumanTrait.GetMuscleDefaultMax(37 + i);
                }
                _handler = new HumanPoseHandler(avatar, _root.transform);
            }
            catch { UnityEngine.Object.DestroyImmediate(_root); throw; }
        }

        private static void CloneChildren(Transform source, Transform parent,
            IReadOnlyDictionary<string, SkeletonBone> skeleton, IDictionary<Transform, Transform> map)
        {
            for (int i = 0; i < source.childCount; i++)
            {
                Transform original = source.GetChild(i);
                var go = new GameObject(original.name) { hideFlags = HideFlags.HideAndDontSave };
                Transform clone = go.transform; clone.SetParent(parent, false);
                if (skeleton.TryGetValue(original.name, out SkeletonBone rest))
                { clone.localPosition = rest.position; clone.localRotation = rest.rotation; clone.localScale = rest.scale; }
                else
                { clone.localPosition = original.localPosition; clone.localRotation = original.localRotation; clone.localScale = original.localScale; }
                map[original] = clone; CloneChildren(original, clone, skeleton, map);
            }
        }

        private static void Capture(Transform[] bones, Quaternion[] rotations)
        {
            for (int i = 0; i < rotations.Length; i++)
                rotations[i] = bones[i] != null && bones[i % 2] != null ? Quaternion.Inverse(bones[i % 2].parent.rotation) * bones[i].rotation : Quaternion.identity;
        }
        private Quaternion ArmRotation(int bone, int side)
        { return Quaternion.Inverse(_decoded[side].parent.rotation) * _decoded[bone].rotation; }
        private void Set(HumanPose source, float[] muscles)
        { source.muscles = muscles; _handler.SetHumanPose(ref source); }

        internal void Stabilize(ref HumanPose pose)
        {
            _original.Add(Copy(pose));
            Quaternion[] raw = _raw; Capture(_input, raw); Set(pose, pose.muscles);
            if (_hasPrevious)
            {
                bool changed = false;
                for (int side = 0; side < 2; side++)
                {
                    int upper = 2 + side, lower = 4 + side, hand = 6 + side;
                    if (_input[side] == null || _decoded[side] == null || _decoded[upper] == null || _decoded[lower] == null || _decoded[hand] == null) continue;
                    int[] bones = ArmBones[side];
                    Quaternion[] expected = _expected; float largest = 0;
                    for (int j = 0; j < 4; j++)
                    {
                        int b = bones[j];
                        expected[j] = raw[b];
                        if (_decoded[b] != null) largest = Mathf.Max(largest, Quaternion.Angle(ArmRotation(b, side), expected[j]));
                    }
                    bool nearCut = false, crossing = false;
                    for (int c = 0; c < 9; c++)
                    {
                        if (c == 5) continue; // A full hinge rotation has no half-twist cut.
                        int slot = side * 9 + c;
                        nearCut |= Math.Abs(Principal(Degrees(pose.muscles[37 + slot], slot))) > 178.75;
                        crossing |= Math.Abs(Principal(Degrees(pose.muscles[37 + slot], slot)) -
                            Principal(Degrees(_previousMuscles[37 + slot], slot))) > 180;
                    }
                    if (largest < 100f && !nearCut && !crossing) continue;
                    float[] baseline = _baseline; Array.Copy(pose.muscles, baseline, baseline.Length);
                    Quaternion shoulderRotation=_decoded[side]!=null?_decoded[side].localRotation:Quaternion.identity;
                    Vector3 wrist = _decoded[hand].position, elbow = _decoded[lower].position;
                    Quaternion wristRotation = _decoded[hand].rotation;
                    float length = Mathf.Max(.001f, Vector3.Distance(_decoded[upper].position, elbow) + Vector3.Distance(elbow, wrist));
                    var originalWorld = new Quaternion[4];
                    for (int j = 0; j < 4; j++) originalWorld[j] = _decoded[bones[j]] != null ? ArmRotation(bones[j], side) : Quaternion.identity;
                    Residual(pose, baseline, baseline, side, bones, expected, wrist, elbow, wristRotation, length, _scoreResidual);
                    float[] best = null; double bestScore = Squared(_scoreResidual);
                    for (int seed = 0; seed < 2; seed++)
                    {
                        float[] start = _start; Array.Copy(baseline, start, start.Length);
                        if (seed == 1) for (int c = 0; c < 9; c++) start[37 + side * 9 + c] = _previousMuscles[37 + side * 9 + c];
                        Bound(start, side);
                        float[] fitted = Fit(pose, start, baseline, side, bones, expected, wrist, elbow, wristRotation, length);
                        Residual(pose, fitted, baseline, side, bones, expected, wrist, elbow, wristRotation, length, _scoreResidual);
                        double score = Squared(_scoreResidual);
                        if (PreservesSegmentPose(originalWorld, bones, expected, side) && score < bestScore) { Array.Copy(fitted, _best, fitted.Length); best = _best; bestScore = score; }
                    }
                    if (best != null)
                    {
                        Set(pose,best);
                        bool preserveGoal=_decoded[side]==null||Quaternion.Angle(shoulderRotation,_decoded[side].localRotation)<=MaximumShoulderCorrectionDegrees;
                        if(preserveGoal){for(int c=0;c<9;c++)pose.muscles[37+side*9+c]=best[37+side*9+c];changed=true;}

                    }
                    Set(pose,pose.muscles);
                }
                if (changed) CorrectedFrames++;
            }
            if (!_hasPrevious) { _previousMuscles = new float[HumanTrait.MuscleCount]; _hasPrevious = true; }
            Array.Copy(pose.muscles, _previousMuscles, _previousMuscles.Length);
            _solved.Add(Copy(pose));
        }

        private static HumanPose Copy(HumanPose pose)
        { pose.muscles = (float[])pose.muscles.Clone(); return pose; }

        // Check complete changed runs, including their exit and curve interiors.
        // A per-frame fit may otherwise just move the native twist cut earlier.
        internal void ValidateSequence(PmxHumanoidAnimationClipBaker.FloatCurveAccumulator[] curves)
        {
            for (int side = 0; side < 2; side++)
            {
            if (ArmBones[side].Any(b => _decoded[b] == null)) continue;
            for (int first = 0; first < _solved.Count; first++)
            {
                bool Changed(int f) { for (int c = 0; c < 9; c++) if (_solved[f].muscles[37 + side * 9 + c] != _original[f].muscles[37 + side * 9 + c]) return true; return false; }
                if (!Changed(first)) continue;
                int last = first; while (last + 1 < _solved.Count && Changed(last + 1)) last++;
                bool reject = false; var beforeTravel = new float[3]; var afterTravel = new float[3];
                for (int f = Math.Max(1, first); f <= Math.Min(_solved.Count - 1, last + 1) && !reject; f++)
                {
                    float[] before = InteriorSteps(_original[f - 1], _original[f], side);
                    float[] after = InteriorSteps(_solved[f - 1], _solved[f], side);
                    for (int k = 0; k < 3; k++)
                    {
                        beforeTravel[k] += before[k + 3]; afterTravel[k] += after[k + 3];
                        if (after[k] > before[k] + 2 || after[k + 3] > before[k + 3] + 5) reject = true;
                    }
                }
                for (int k = 0; k < 3; k++) if (afterTravel[k] > beforeTravel[k] + 5) reject = true;
                if (reject) for (int f = first; f <= last; f++) for (int c = 0; c < 9; c++)
                {
                    int m = 37 + side * 9 + c;
                    _solved[f].muscles[m] = _original[f].muscles[m]; curves[m].SetValue(f, _original[f].muscles[m]);
                }
                first = last;
            }
            }
        }

        private float[] InteriorSteps(HumanPose a, HumanPose b, int side)
        {
            var result = new float[6]; var previous = new Quaternion[3];
            HumanPose pose = Copy(a); int[] bones = ArmBones[side];
            for (int step = 0; step <= 32; step++)
            {
                float t = step / 32f;
                for (int m = 37; m <= 54; m++) pose.muscles[m] = Mathf.LerpUnclamped(a.muscles[m], b.muscles[m], t);
                Set(pose, pose.muscles);
                for (int k = 0; k < 3; k++)
                {
                    Quaternion rotation = _decoded[bones[k + 1]] != null ? _decoded[bones[k + 1]].rotation : Quaternion.identity;
                    if (step > 0) { float angle = Quaternion.Angle(previous[k], rotation); result[k] = Mathf.Max(result[k], angle); result[k + 3] += angle; }
                    previous[k] = rotation;
                }
            }
            return result;
        }

        // Use the current physical source pose for acceptance. Endpoint motion
        // alone can improve while opposing upper/forearm errors twist the mesh.
        private bool PreservesSegmentPose(Quaternion[] baseline, int[] bones, Quaternion[] source, int side)
        {
            var candidate = new Quaternion[4];
            for (int k = 0; k < 4; k++) candidate[k] = _decoded[bones[k]] != null ? ArmRotation(bones[k], side) : Quaternion.identity;
            for (int k = 1; k <= 2; k++)
            {
                float before = Quaternion.Angle(source[k], baseline[k]);
                float after = Quaternion.Angle(source[k], candidate[k]);
                if (after > before + 2 || after > 30 && after >= before - 2) return false;
            }
            for (int k = 2; k <= 3; k++)
            {
                Quaternion goal = Quaternion.Inverse(source[k - 1]) * source[k];
                float before = Quaternion.Angle(goal, Quaternion.Inverse(baseline[k - 1]) * baseline[k]);
                float after = Quaternion.Angle(goal, Quaternion.Inverse(candidate[k - 1]) * candidate[k]);
                if (after > before + 2) return false;
            }
            return true;
        }

        private double Degrees(float value, int slot) => value * (value < 0 ? _negative[slot] : _positive[slot]);
        private float Normalized(double angle, int slot) => (float)(angle / (angle < 0 ? _negative[slot] : _positive[slot]));
        private static double Principal(double angle)
        { angle %= 360; return angle > 180 ? angle - 360 : angle < -180 ? angle + 360 : angle; }
        private void Bound(float[] values, int side)
        {
            for (int c = 0; c < 9; c++)
            {
                int slot = side * 9 + c, m = 37 + slot;
                double anchor = Degrees(_previousMuscles[m], slot), angle = Degrees(values[m], slot);
                bool guardedChart = c != 5;
                if (guardedChart) { anchor = Principal(anchor); angle = Principal(angle); }
                while (angle - anchor > 180) angle -= 360;
                while (angle - anchor < -180) angle += 360;
                if (guardedChart) anchor = Math.Max(-179, Math.Min(179, anchor));
                double low = anchor - 60, high = anchor + 60;
                if (guardedChart) { low = Math.Max(low, -179); high = Math.Min(high, 179); }
                values[m] = Normalized(Math.Max(low, Math.Min(high, angle)), slot);
            }
        }

        private static Vector3 RotationResidual(Quaternion target, Quaternion actual)
        {
            Quaternion q = (Quaternion.Inverse(target) * actual).normalized;
            if (q.w < 0) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
            double n = Math.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z);
            double scale = n < 1e-10 ? 2 : 2 * Math.Atan2(n, q.w) / n;
            return new Vector3((float)(q.x * scale), (float)(q.y * scale), (float)(q.z * scale));
        }
        private void Residual(HumanPose pose, float[] values, float[] baseline, int side,
            int[] bones, Quaternion[] expected, Vector3 wrist, Vector3 elbow, Quaternion wristRotation, float length,
            double[] result)
        {
            Set(pose, values); int n = 0;
            void Add(Vector3 v) { result[n++] = v.x; result[n++] = v.y; result[n++] = v.z; }
            for (int i = 0; i < 4; i++) Add(_decoded[bones[i]] != null ? RotationResidual(expected[i], ArmRotation(bones[i], side)) : Vector3.zero);
            Add(RotationResidual(wristRotation, _decoded[bones[3]].rotation) * 3f);
            Add((_decoded[bones[3]].position - wrist) * (5f / length));
            Add((_decoded[bones[2]].position - elbow) * (5f / length));
            for (int c = 0; c < 9; c++)
            {
                int slot = side * 9 + c, m = 37 + slot;
                result[n++] = .02 * Principal(Degrees(values[m], slot) - Degrees(baseline[m], slot)) / 90;
            }
        }
        private static double Squared(double[] v) { double s = 0; foreach (double x in v) s += x * x; return s; }
        private float[] Fit(HumanPose pose, float[] start, float[] baseline, int side, int[] bones,
            Quaternion[] expected, Vector3 wrist, Vector3 elbow, Quaternion wristRotation, float length)
        {
            float[] values = _values; Array.Copy(start, values, values.Length); double lambda = .001;
            double[] residual = _residual;
            Residual(pose, values, baseline, side, bones, expected, wrist, elbow, wristRotation, length, residual);
            double cost = Squared(residual);
            for (int iteration = 0; iteration < 18; iteration++)
            {
                const float epsilon = .001f; double[,] jacobian = _jacobian;
                for (int c = 0; c < 9; c++)
                {
                    float[] probe = _probe; Array.Copy(values, probe, probe.Length); probe[37 + side * 9 + c] += epsilon;
                    double[] r = _probeResidual;
                    Residual(pose, probe, baseline, side, bones, expected, wrist, elbow, wristRotation, length, r);
                    for (int j = 0; j < r.Length; j++) jacobian[c, j] = (r[j] - residual[j]) / epsilon;
                }
                double[,] matrix = _matrix; Array.Clear(matrix, 0, matrix.Length);
                for (int a = 0; a < 9; a++)
                {
                    for (int b = 0; b < 9; b++) for (int j = 0; j < residual.Length; j++) matrix[a, b] += jacobian[a, j] * jacobian[b, j];
                    for (int j = 0; j < residual.Length; j++) matrix[a, 9] -= jacobian[a, j] * residual[j];
                    matrix[a, a] += lambda * Math.Max(1, matrix[a, a]);
                }
                for (int a = 0; a < 9; a++)
                {
                    int pivot = a; for (int b = a + 1; b < 9; b++) if (Math.Abs(matrix[b, a]) > Math.Abs(matrix[pivot, a])) pivot = b;
                    if (pivot != a) for (int j = a; j <= 9; j++) { double temp = matrix[a, j]; matrix[a, j] = matrix[pivot, j]; matrix[pivot, j] = temp; }
                    double d = matrix[a, a]; for (int j = a; j <= 9; j++) matrix[a, j] /= d;
                    for (int b = 0; b < 9; b++) if (a != b) { d = matrix[b, a]; for (int j = a; j <= 9; j++) matrix[b, j] -= d * matrix[a, j]; }
                }
                float[] next = _next; Array.Copy(values, next, next.Length);
                for (int c = 0; c < 9; c++) next[37 + side * 9 + c] += (float)Math.Max(-.2, Math.Min(.2, matrix[c, 9]));
                Bound(next, side); double[] candidate = _candidateResidual;
                Residual(pose, next, baseline, side, bones, expected, wrist, elbow, wristRotation, length, candidate);
                double nextCost = Squared(candidate);
                if (nextCost < cost) { double gain = cost - nextCost; Array.Copy(next, values, values.Length); Array.Copy(candidate, residual, residual.Length); cost = nextCost; lambda = Math.Max(1e-8, lambda / 3); if (gain < 1e-7) break; }
                else { lambda *= 5; if (lambda > 1e8) break; }
            }
            return values;
        }

        internal static void UnwrapPeriodic(float[] values, int count, int muscle)
        {
            if (!(muscle == 37 || muscle == 38 || muscle == 39 || muscle == 40 || muscle == 42 || muscle == 44 || muscle == 45 ||
                  muscle == 46 || muscle == 47 || muscle == 48 || muscle == 49 || muscle == 51 || muscle == 53 || muscle == 54)) return;
            double negative = -HumanTrait.GetMuscleDefaultMin(muscle), positive = HumanTrait.GetMuscleDefaultMax(muscle), previous = 0;
            for (int i = 0; i < count; i++)
            {
                double angle = values[i] * (values[i] < 0 ? negative : positive);
                if (i > 0) { while (angle - previous > 180) angle -= 360; while (angle - previous < -180) angle += 360; }
                previous = angle; values[i] = (float)(angle / (angle < 0 ? negative : positive));
            }
        }
        public void Dispose() { _handler?.Dispose(); if (_root != null) UnityEngine.Object.DestroyImmediate(_root); }
    }
}
