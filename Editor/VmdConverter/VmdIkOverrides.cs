using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using VeraFirefly.VmdConverter.Core;

internal enum VmdIkOverrideMode
{
    FollowVmd,
    ForceOn,
    ForceOff
}

[Serializable]
internal sealed class VmdIkOverride
{
    public string BoneName;
    public VmdIkOverrideMode Mode;
}

// Supply the same overridden IK tracks to every parser, including the external
// Core executable. Only temporary copies are written; source VMDs stay intact.
internal sealed class VmdIkOverrideBatch : IDisposable
{
    private string directory;

    internal string Prepare(string sourcePath, PmxModel model, IReadOnlyList<VmdIkOverride> settings)
    {
        var states = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (PmxBone bone in model.Bones)
        {
            if (bone.Ik == null) continue;
            string name = GetBoneName(bone);
            VmdIkOverride setting = settings?.FirstOrDefault(item => item.BoneName == name);
            if (setting == null || setting.Mode == VmdIkOverrideMode.FollowVmd) continue;
            bool enabled = setting.Mode == VmdIkOverrideMode.ForceOn;
            if (!string.IsNullOrEmpty(bone.NameJapanese)) states[bone.NameJapanese] = enabled;
            // PMX English aliases can exceed VMD's fixed name field. A Japanese
            // override already takes precedence in the evaluator in that case.
            if (!string.IsNullOrEmpty(bone.NameEnglish) &&
                (string.IsNullOrEmpty(bone.NameJapanese) || Encoding.GetEncoding(932).GetByteCount(bone.NameEnglish) <= 20))
                states[bone.NameEnglish] = enabled;
        }
        if (states.Count == 0) return sourcePath;
        if (directory == null)
        {
            directory = Path.Combine(Path.GetTempPath(), "VmdcwIk", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
        }
        string output = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".vmd");
        Rewrite(sourcePath, output, states);
        return output;
    }

    internal static string GetBoneName(PmxBone bone) =>
        string.IsNullOrEmpty(bone.NameJapanese) ? bone.NameEnglish : bone.NameJapanese;

    private static void Rewrite(string sourcePath, string outputPath, Dictionary<string, bool> overrides)
    {
        Encoding encoding = Encoding.GetEncoding(932);
        using (var input = File.OpenRead(sourcePath))
        using (var reader = new BinaryReader(input, Encoding.UTF8, true))
        using (var output = File.Create(outputPath))
        using (var writer = new BinaryWriter(output, Encoding.UTF8, true))
        {
            string header = Encoding.ASCII.GetString(reader.ReadBytes(30));
            if (!header.StartsWith("Vocaloid Motion Data", StringComparison.Ordinal))
                throw new InvalidDataException("The file is not a VMD motion.");
            input.Position = 30 + (header.Contains("0002") ? 20 : 10);
            SkipRecords(reader, 111); // bones
            SkipRecords(reader, 23); // morphs
            int missingSections = 0;
            foreach (int size in new[] { 61, 28, 9 }) // camera, light, shadow
            {
                if (input.Position == input.Length) missingSections++;
                else SkipRecords(reader, size);
            }
            long ikStart = input.Position;
            var keys = new List<VmdIkKeyframe>();
            if (input.Position < input.Length)
            {
                uint count = ReadCount(reader);
                for (uint i = 0; i < count; i++)
                {
                    var key = new VmdIkKeyframe { Frame = reader.ReadUInt32(), ModelVisible = reader.ReadByte() != 0 };
                    uint stateCount = ReadCount(reader);
                    for (uint j = 0; j < stateCount; j++)
                    {
                        byte[] bytes = reader.ReadBytes(20);
                        if (bytes.Length != 20) throw new EndOfStreamException();
                        int end = Array.IndexOf(bytes, (byte)0);
                        key.States.Add(new VmdIkState
                        {
                            Name = encoding.GetString(bytes, 0, end < 0 ? 20 : end),
                            Enabled = reader.ReadByte() != 0
                        });
                    }
                    keys.Add(key);
                }
            }
            long tailStart = input.Position;
            input.Position = 0;
            var buffer = new byte[81920];
            long remaining = ikStart;
            while (remaining > 0)
            {
                int read = input.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                if (read == 0) throw new EndOfStreamException();
                output.Write(buffer, 0, read);
                remaining -= read;
            }
            for (int i = 0; i < missingSections; i++) writer.Write(0u);
            if (!keys.Any(key => key.Frame == 0))
                keys.Insert(0, new VmdIkKeyframe { Frame = 0, ModelVisible = true });
            writer.Write((uint)keys.Count);
            foreach (VmdIkKeyframe key in keys)
            {
                foreach (VmdIkState state in key.States)
                    if (overrides.TryGetValue(state.Name, out bool enabled)) state.Enabled = enabled;
                foreach (var item in overrides)
                    if (!key.States.Any(state => state.Name == item.Key))
                        key.States.Add(new VmdIkState { Name = item.Key, Enabled = item.Value });
                writer.Write(key.Frame);
                writer.Write((byte)(key.ModelVisible ? 1 : 0));
                writer.Write((uint)key.States.Count);
                foreach (VmdIkState state in key.States)
                {
                    byte[] bytes = encoding.GetBytes(state.Name);
                    if (bytes.Length > 20)
                        throw new InvalidDataException("IK name exceeds the VMD 20-byte limit: " + state.Name);
                    writer.Write(bytes);
                    writer.Write(new byte[20 - bytes.Length]);
                    writer.Write((byte)(state.Enabled ? 1 : 0));
                }
            }
            input.Position = tailStart;
            input.CopyTo(output);
        }
    }

    private static uint ReadCount(BinaryReader reader)
    {
        uint count = reader.ReadUInt32();
        if (count > 10000000) throw new InvalidDataException("Invalid VMD record count.");
        return count;
    }

    private static void SkipRecords(BinaryReader reader, int size)
    {
        long bytes = (long)ReadCount(reader) * size;
        if (bytes > reader.BaseStream.Length - reader.BaseStream.Position)
            throw new EndOfStreamException("Incomplete VMD section.");
        reader.BaseStream.Seek(bytes, SeekOrigin.Current);
    }

    public void Dispose()
    {
        if (directory != null && Directory.Exists(directory)) Directory.Delete(directory, true);
        directory = null;
    }
}
