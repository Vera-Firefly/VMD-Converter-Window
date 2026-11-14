using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;

namespace VeraFirefly.VmdConverter.Core.Editor
{

// Keep sampled muscle values. Guard scalar cuts and bound every cubic interior.
internal static class PmxUpperCurveTangents
{
    internal static (float[] Incoming, float[] Outgoing) Create(CurveData curve, float maximumDeviation)
    {
        int n = curve.Times.Length;
        var incoming = new float[n];
        var outgoing = new float[n];
        if (n < 2) return (incoming, outgoing);
        var h = new double[n - 1];
        var d = new double[n - 1];
        var slopes = new double[n];
        var linear = curve.Protected == null ? new bool[n - 1] : (bool[])curve.Protected.Clone();
        for (int i = 0; i < n - 1; i++)
        {
            h[i] = (double)curve.Times[i + 1] - curve.Times[i];
            d[i] = ((double)curve.Values[i + 1] - curve.Values[i]) / h[i];
            if (Math.Abs(curve.Values[i + 1] - curve.Values[i]) > .5f)
                for (int j = Math.Max(0, i - 1); j <= Math.Min(n - 2, i + 1); j++) linear[j] = true;
        }
        slopes[0] = d[0];
        slopes[n - 1] = d[n - 2];
        for (int i = 1; i < n - 1; i++)
        {
            if (d[i - 1] * d[i] <= 0) continue;
            double w1 = 2 * h[i] + h[i - 1], w2 = h[i] + 2 * h[i - 1];
            slopes[i] = (w1 + w2) / (w1 / d[i - 1] + w2 / d[i]);
        }
        for (int i = 0; i < n - 1; i++)
        {
            double a = h[i] * (slopes[i] - d[i]);
            double b = h[i] * (slopes[i + 1] - d[i]);
            // Hermite minus secant: (a+b)t^3-(2a+b)t^2+at.
            // Its two stationary points give an exact all-subframe bound.
            double cubic = a + b, quadratic = -(2 * a + b), maximum = 0;
            void Check(double t)
            {
                if (t > 0 && t < 1)
                    maximum = Math.Max(maximum, Math.Abs(((cubic * t + quadratic) * t + a) * t));
            }
            if (Math.Abs(cubic) < 1e-20)
            {
                if (Math.Abs(quadratic) > 1e-20) Check(-a / (2 * quadratic));
            }
            else
            {
                double discriminant = 4 * quadratic * quadratic - 12 * cubic * a;
                if (discriminant >= 0)
                {
                    double root = Math.Sqrt(discriminant);
                    Check((-2 * quadratic - root) / (6 * cubic));
                    Check((-2 * quadratic + root) / (6 * cubic));
                }
            }
            bool keepLinear = linear[i] || maximumDeviation <= 0 || maximum > maximumDeviation;
            // Match the existing writer's float arithmetic exactly on fallback.
            float secant = (curve.Values[i + 1] - curve.Values[i]) /
                (curve.Times[i + 1] - curve.Times[i]);
            outgoing[i] = keepLinear ? secant : (float)slopes[i];
            incoming[i + 1] = keepLinear ? secant : (float)slopes[i + 1];
        }
        incoming[0] = outgoing[0];
        outgoing[n - 1] = incoming[n - 1];
        return (incoming, outgoing);
    }

    // Convert a physical-angle cubic back to normalized muscle units. Zero is a
    // derivative boundary when the Avatar has different positive/negative ranges.
    internal static (CurveData Curve, float[] Incoming, float[] Outgoing) CreatePhysical(
        CurveData curve, double negativeRange, double positiveRange, float maximumDeviation)
    {
        var fallback = Create(curve, maximumDeviation);
        if (curve.Times.Length < 2 || maximumDeviation <= 0 ||
            double.IsNaN(negativeRange) || double.IsInfinity(negativeRange) || double.IsNaN(positiveRange) || double.IsInfinity(positiveRange) ||
            negativeRange <= 1e-5 || positiveRange <= 1e-5 ||
            Math.Abs(negativeRange - positiveRange) < 1e-4)
            return (curve, fallback.Incoming, fallback.Outgoing);
        int n = curve.Times.Length;
        double Range(double value) => value < 0 ? negativeRange : positiveRange;
        var y = curve.Values.Select(v => v * Range(v)).ToArray();
        var h = new double[n - 1]; var d = new double[n - 1];
        var slope = new double[n]; var guarded = curve.Protected == null ? new bool[n - 1] : (bool[])curve.Protected.Clone();
        for (int i = 0; i < n - 1; i++)
        {
            h[i] = (double)curve.Times[i + 1] - curve.Times[i];
            d[i] = (y[i + 1] - y[i]) / h[i];
            if (Math.Abs(curve.Values[i + 1] - curve.Values[i]) > .5f)
                for (int j = Math.Max(0, i - 1); j <= Math.Min(n - 2, i + 1); j++) guarded[j] = true;
        }
        slope[0] = d[0]; slope[n - 1] = d[n - 2];
        for (int i = 1; i < n - 1; i++)
            if (d[i - 1] * d[i] > 0)
            {
                double w1 = 2 * h[i] + h[i - 1], w2 = h[i] + 2 * h[i - 1];
                slope[i] = (w1 + w2) / (w1 / d[i - 1] + w2 / d[i]);
            }
        var times = new List<float> { curve.Times[0] };
        var values = new List<float> { curve.Values[0] };
        var incoming = new List<float> { 0 }; var outgoing = new List<float> { 0 };
        for (int i = 0; i < n - 1; i++)
        {
            double c0 = y[i], c1 = h[i] * slope[i];
            double c2 = 3 * (y[i + 1] - y[i]) - 2 * c1 - h[i] * slope[i + 1];
            double c3 = 2 * (y[i] - y[i + 1]) + c1 + h[i] * slope[i + 1];
            double Value(double u) => ((c3 * u + c2) * u + c1) * u + c0;
            double Derivative(double u) => ((3 * c3 * u + 2 * c2) * u + c1) / h[i];
            double zero = -1;
            if (y[i] * y[i + 1] < 0)
            {
                double lo = 0, hi = 1;
                for (int k = 0; k < 55; k++) { double mid = (lo + hi) / 2; if (Value(mid) * y[i] > 0) lo = mid; else hi = mid; }
                zero = (lo + hi) / 2;
            }
            var splits = zero > 0 ? new[] { 0d, zero, 1d } : new[] { 0d, 1d };
            double maximum = 0;
            for (int j = 0; j < splits.Length - 1; j++)
            {
                double lo = splits[j], hi = splits[j + 1], scale = Range(Value((lo + hi) / 2));
                double a = c3 / scale, b = c2 / scale;
                double c = c1 / scale - (curve.Values[i + 1] - curve.Values[i]);
                double e = c0 / scale - curve.Values[i];
                void Check(double u) { if (u >= lo && u <= hi) maximum = Math.Max(maximum, Math.Abs(((a * u + b) * u + c) * u + e)); }
                Check(lo); Check(hi);
                if (Math.Abs(a) < 1e-20) { if (Math.Abs(b) > 1e-20) Check(-c / (2 * b)); }
                else { double disc = 4 * b * b - 12 * a * c; if (disc >= 0) { double root = Math.Sqrt(disc); Check((-2 * b - root) / (6 * a)); Check((-2 * b + root) / (6 * a)); } }
            }
            float zeroTime = (float)(curve.Times[i] + zero * h[i]);
            bool split = zero > 0 && zeroTime > curve.Times[i] && zeroTime < curve.Times[i + 1];
            bool linear = guarded[i] || maximum > maximumDeviation || zero > 0 && !split;
            if (linear)
            {
                float secant = (curve.Values[i + 1] - curve.Values[i]) / (curve.Times[i + 1] - curve.Times[i]);
                outgoing[outgoing.Count - 1] = secant;
                times.Add(curve.Times[i + 1]); values.Add(curve.Values[i + 1]); incoming.Add(secant); outgoing.Add(0);
                continue;
            }
            double firstRange = Range(Value(split ? zero / 2 : .5));
            double lastRange = Range(Value(split ? (1 + zero) / 2 : .5));
            outgoing[outgoing.Count - 1] = (float)(slope[i] / firstRange);
            if (split)
            {
                times.Add(zeroTime); values.Add(0);
                incoming.Add((float)(Derivative(zero) / firstRange)); outgoing.Add((float)(Derivative(zero) / lastRange));
            }
            times.Add(curve.Times[i + 1]); values.Add(curve.Values[i + 1]); incoming.Add((float)(slope[i + 1] / lastRange)); outgoing.Add(0);
        }
        incoming[0] = outgoing[0]; outgoing[outgoing.Count - 1] = incoming[incoming.Count - 1];
        return (new CurveData { Times = times.ToArray(), Values = values.ToArray() }, incoming.ToArray(), outgoing.ToArray());
    }

    internal sealed class CurveData { internal float[] Times, Values; internal bool[] Protected; }

    // Read Avatar limits on the main thread, before optimizing curves in parallel.
    internal static double[,] ReadRanges(Avatar avatar)
    {
        var ranges = new double[18, 2];
        for (int slot = 0; slot < 18; slot++)
        {
            int muscle = 37 + slot;
            ranges[slot, 0] = -HumanTrait.GetMuscleDefaultMin(muscle);
            ranges[slot, 1] = HumanTrait.GetMuscleDefaultMax(muscle);
            if (avatar == null) continue;
            int bone = HumanTrait.BoneFromMuscle(muscle);
            foreach (HumanBone human in avatar.humanDescription.human)
            {
                if (human.humanName != HumanTrait.BoneName[bone] || human.limit.useDefaultValues) continue;
                for (int axis = 0; axis < 3; axis++)
                    if (HumanTrait.MuscleFromBone(bone, axis) == muscle)
                    { ranges[slot, 0] = -human.limit.min[axis]; ranges[slot, 1] = human.limit.max[axis]; }
            }
        }
        return ranges;
    }

    internal static Keyframe[] Smooth(Keyframe[] keys, int muscle, float budget, double[,] ranges, float[][] cutWindows)
    {
        if (budget <= 0 || muscle < 37 || muscle > 54 || keys.Length < 2) return keys;
        var curve = new CurveData { Times = keys.Select(k => k.time).ToArray(), Values = keys.Select(k => k.value).ToArray() };
        curve.Protected = new bool[keys.Length - 1];
        if (cutWindows != null) for (int i = 0; i < curve.Protected.Length; i++)
            foreach (var cut in cutWindows)
                if (keys[i].time < cut[1] && keys[i + 1].time > cut[0]) { curve.Protected[i] = true; break; }
        float[] incoming, outgoing;
        if (muscle == 37 || muscle == 39 || muscle == 46 || muscle == 48)
        {
            var physical = CreatePhysical(curve, ranges[muscle - 37, 0], ranges[muscle - 37, 1], budget);
            curve = physical.Curve; incoming = physical.Incoming; outgoing = physical.Outgoing;
        }
        else { var scalar = Create(curve, budget); incoming = scalar.Incoming; outgoing = scalar.Outgoing; }
        var result = new Keyframe[curve.Times.Length];
        for (int i = 0; i < result.Length; i++) result[i] = new Keyframe(curve.Times[i], curve.Values[i], incoming[i], outgoing[i]);
        return result;
    }
}

}
