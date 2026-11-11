using System;
using System.IO;
using System.Security.Cryptography;

namespace IKnowAGuyVR.SubsystemProbe;

// The native XR parameter ABI and renderer measurements are specific to this build.
internal static class EngineBinaryContract
{
    private static bool validated;

    internal static void ValidateBinary()
    {
        if (validated) return;
        foreach (var identity in new[] {
            ("UnityPlayer.dll", "5158C9F49DB8CE4ED77A5BF10D0AD0FAEE7A9F246EC9A9C97E7CB9AE1CCCEC1D"),
            ("GameAssembly.dll", "76B84137A6B03C89D2CC914F5E1FFC8F56182A24013C47A2F9F1C71C2C220ABF") })
        {
            using var stream = File.OpenRead(Path.Combine(BepInEx.Paths.GameRootPath, identity.Item1));
            using var hash = SHA256.Create();
            if (Convert.ToHexString(hash.ComputeHash(stream)) != identity.Item2)
                throw new InvalidOperationException("Unsupported trial ABI: " + identity.Item1);
        }
        validated = true;
    }

}
