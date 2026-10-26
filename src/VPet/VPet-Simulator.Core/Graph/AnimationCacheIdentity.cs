using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace VPet_Simulator.Core;

public static class AnimationCacheIdentity
{
    /// <summary>Names include ordering, duration-bearing filenames, resolution and exact source bytes.</summary>
    public static string Name(int resolution, FileInfo[] frames)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes($"nanally-sheet-v2\0{resolution}\0{frames.Length}\0"));
        foreach (var frame in frames)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(frame.Name + "\0"));
            using var input = frame.OpenRead();
            hash.AppendData(SHA256.HashData(input));
        }
        return $"{resolution}_{System.Convert.ToHexString(hash.GetHashAndReset())}_{frames.Length}.png";
    }
}
