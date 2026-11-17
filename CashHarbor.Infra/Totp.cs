using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;

namespace CashHarbor.Infra;

// RFC 6238, SHA-1 / six digits / 30 seconds. Shared with standard authenticator apps.
public static class Totp
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
    public static string NewSecret() => Encode(RandomNumberGenerator.GetBytes(20));
    public static string Encode(byte[] bytes)
    {
        var bits = 0; var value = 0; var output = new System.Text.StringBuilder();
        foreach (var b in bytes) { value = (value << 8) | b; bits += 8; while (bits >= 5) { output.Append(Alphabet[(value >> (bits - 5)) & 31]); bits -= 5; } }
        if (bits > 0) output.Append(Alphabet[(value << (5 - bits)) & 31]);
        return output.ToString();
    }
    private static byte[] Decode(string text)
    {
        var output = new List<byte>(); var bits = 0; var value = 0;
        foreach (var c in text) { var index = Alphabet.IndexOf(c); if (index < 0) throw new FormatException("Invalid Base32."); value = (value << 5) | index; bits += 5; if (bits >= 8) { output.Add((byte)(value >> (bits - 8))); bits -= 8; } }
        return output.ToArray();
    }
    public static string Code(string secret, long step, int digits = 6)
    {
        Span<byte> counter = stackalloc byte[8]; BinaryPrimitives.WriteInt64BigEndian(counter, step);
        var key = Decode(secret);
        try
        {
            var hash = HMACSHA1.HashData(key, counter); var offset = hash[^1] & 15;
            var value = BinaryPrimitives.ReadInt32BigEndian(hash.AsSpan(offset, 4)) & 0x7fffffff;
            return (value % (digits == 8 ? 100_000_000 : 1_000_000)).ToString(digits == 8 ? "D8" : "D6", CultureInfo.InvariantCulture);
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }
    public static long? Match(string secret, string code, DateTimeOffset now, long lastStep = -1)
    {
        if (code.Length != 6 || code.Any(c => c is < '0' or > '9')) return null;
        var step = now.ToUnixTimeSeconds() / 30;
        for (var drift = -1; drift <= 1; drift++)
            if (step + drift > lastStep && CryptographicOperations.FixedTimeEquals(System.Text.Encoding.ASCII.GetBytes(Code(secret, step + drift)), System.Text.Encoding.ASCII.GetBytes(code))) return step + drift;
        return null;
    }
}
