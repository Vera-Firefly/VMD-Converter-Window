using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using VeraFirefly.VmdConverter.Core;
using VeraFirefly.VmdConverter.Core.Editor;

/// <summary>Owns the VPD parse, PMX evaluation and AnimationClip asset write pipeline.</summary>
internal sealed class VpdAnimationConverter : IDisposable
{
    private readonly GameObject targetAvatar;
    private readonly MotionOutputType outputType;
    private readonly PmxDebugTargetFkBakeOptions fkOptions;
    private PmxHumanoidAnimationClipBaker.BatchSession batchSession;

    internal static string GetOutputPath(string sourcePath, string outputFolder)
    {
        string fileName = Path.GetFileNameWithoutExtension(sourcePath) + "_VPD.anim";
        if (string.IsNullOrEmpty(outputFolder))
        {
            string sourceFolder = Path.GetDirectoryName(sourcePath)?.Replace('\\', '/');
            return string.IsNullOrEmpty(sourceFolder) ? fileName : sourceFolder + "/" + fileName;
        }
        return outputFolder.TrimEnd('/') + "/" + fileName;
    }

    internal VpdAnimationConverter(
        string modelPath,
        GameObject targetAvatar,
        MotionOutputType outputType,
        float curveReductionError,
        bool debugTargetUpperLimbInverseSolve,
        PmxTargetSolveScope targetSolveScope = PmxTargetSolveScope.Normal,
        PmxDebugTargetFkBakeOptions fkOptions = null)
    {
        PmxModel model = PmxModelCache.Load(modelPath);
        var options = new PmxAnimationClipBakerOptions
        {
            SampleRate = 30f,
            PositionScale = 0.08f,
            CoordinateConversion = PmxCoordinateConversion.RotateY180,
            BakeMorphs = true,
            CurveReductionError = Mathf.Clamp(curveReductionError, 0f, 0.001f),
            DebugTargetUpperLimbInverseSolve = debugTargetUpperLimbInverseSolve,
            TargetSolveScope = targetSolveScope
        };
        this.targetAvatar = targetAvatar;
        this.outputType = outputType;
        this.fkOptions = fkOptions ?? new PmxDebugTargetFkBakeOptions { TargetSolveScope = targetSolveScope };
        batchSession = new PmxHumanoidAnimationClipBaker.BatchSession(model, options);
    }

    internal AnimationClip ConvertToAsset(
        string sourceAssetPath,
        string outputAssetPath,
        Action<float, string> progress = null)
    {
        if (batchSession == null) throw new ObjectDisposedException(nameof(VpdAnimationConverter));
        progress?.Invoke(0.02f, "Reading VPD pose");
        VmdMotion motion = VpdParser.Parse(PmxAnimationClipBaker.ToPhysicalPath(sourceAssetPath));
        PmxHumanoidAnimationClipBaker.PreparedMotion prepared = batchSession.Prepare(motion, 1);
        PmxAnimationBakeReport report;
        if (outputType == MotionOutputType.TargetFk)
        {
            report = batchSession.Debug_BakeTargetFk(prepared, targetAvatar, fkOptions,
                (value, message) => progress?.Invoke(0.12f + value * 0.78f, message));
            if (targetAvatar != null)
                report.BoundMorphCount = PmxAnimationClipBaker.BakeMorphCurves(
                    report.Clip, motion, targetAvatar.transform);
        }
        else if (outputType == MotionOutputType.TargetCalibratedHumanoid)
        {
            report = batchSession.BakeTargetHumanoid(
                prepared, targetAvatar,
                (value, message) => progress?.Invoke(0.12f + value * 0.78f, message));
            if (targetAvatar != null)
                report.BoundMorphCount = PmxAnimationClipBaker.BakeMorphCurves(
                    report.Clip, motion, targetAvatar.transform);
        }
        else
        {
            report = batchSession.Bake(
                prepared,
                targetAvatar != null ? targetAvatar.transform : null,
                (value, message) => progress?.Invoke(0.12f + value * 0.78f, message));
        }

        // VPD is a static pose. Keep its duration even when the FK baker reduces
        // constant curves, and avoid solver warm-up differences between samples.
        foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(report.Clip))
        {
            AnimationCurve curve = AnimationUtility.GetEditorCurve(report.Clip, binding);
            float value = curve.Evaluate(0f);
            AnimationUtility.SetEditorCurve(report.Clip, binding,
                AnimationCurve.Linear(0f, value, 1f / 30f, value));
        }

        progress?.Invoke(0.92f, "Writing AnimationClip asset");
        report.Clip.name = Path.GetFileNameWithoutExtension(outputAssetPath);
        VmdCoreAssetPaths.EnsureAssetFolderExists(
            Path.GetDirectoryName(outputAssetPath)?.Replace('\\', '/'));
        AssetDatabase.DeleteAsset(outputAssetPath);
        AssetDatabase.CreateAsset(report.Clip, outputAssetPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(outputAssetPath, ImportAssetOptions.ForceSynchronousImport);
        AnimationClip result = AssetDatabase.LoadAssetAtPath<AnimationClip>(outputAssetPath);
        if (result == null)
            throw new InvalidDataException("Unity could not reload the generated VPD AnimationClip.");
        progress?.Invoke(1f, "Conversion complete");
        return result;
    }

    public void Dispose()
    {
        if (batchSession == null) return;
        batchSession.Dispose();
        batchSession = null;
    }
}
