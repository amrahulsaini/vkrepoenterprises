using System.Security.Cryptography;
using System.Text;

namespace VKApiServer;

internal static class MgrKeys
{
    private static byte[][] _keys = Array.Empty<byte[]>();

    public static void Set(string current, string? previous)
    {
        var keys = new List<byte[]> { Encoding.UTF8.GetBytes(current) };
        if (!string.IsNullOrEmpty(previous) && previous != current)
            keys.Add(Encoding.UTF8.GetBytes(previous));
        _keys = keys.ToArray();
    }

    public static bool Matches(string? presented)
    {
        if (string.IsNullOrEmpty(presented)) return false;
        var bytes = Encoding.UTF8.GetBytes(presented);
        bool ok = false;
        foreach (var k in _keys)
            ok |= CryptographicOperations.FixedTimeEquals(bytes, k);
        return ok;
    }
}
