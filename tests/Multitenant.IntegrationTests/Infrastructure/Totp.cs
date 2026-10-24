using System.Globalization;
using System.Security.Cryptography;

namespace Multitenant.IntegrationTests.Infrastructure;

/// <summary>
/// Minimal RFC 6238 TOTP implementation, used to exercise Identity's real
/// authenticator token provider in the two-factor integration test.
/// </summary>
public static class Totp
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string Compute(string base32Secret, DateTimeOffset now, int digits = 6, int period = 30)
    {
        var key = Base32Decode(base32Secret);
        var counter = now.ToUnixTimeSeconds() / period;

        var counterBytes = new byte[8];
        for (var index = 7; index >= 0; index--)
        {
            counterBytes[index] = (byte)(counter & 0xFF);
            counter >>= 8;
        }

        // RFC 6238 authenticator apps use HMAC-SHA1 by convention; Identity's
        // authenticator token provider does the same.
#pragma warning disable CA5350 // Do not use weak cryptographic algorithms
        using var hmac = new HMACSHA1(key);
#pragma warning restore CA5350
        var hash = hmac.ComputeHash(counterBytes);
        var offset = hash[^1] & 0x0F;

        var binary = ((hash[offset] & 0x7F) << 24)
            | ((hash[offset + 1] & 0xFF) << 16)
            | ((hash[offset + 2] & 0xFF) << 8)
            | (hash[offset + 3] & 0xFF);

        var code = binary % (int)Math.Pow(10, digits);
        return code.ToString(new string('0', digits), CultureInfo.InvariantCulture);
    }

    private static byte[] Base32Decode(string input)
    {
        var normalized = input
            .Trim()
            .TrimEnd('=')
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .ToUpperInvariant();

        var output = new List<byte>(normalized.Length * 5 / 8);
        var buffer = 0;
        var bitsLeft = 0;

        foreach (var character in normalized)
        {
            var value = Alphabet.IndexOf(character, StringComparison.Ordinal);
            if (value < 0)
            {
                throw new FormatException($"'{character}' is not a valid base32 character.");
            }

            buffer = (buffer << 5) | value;
            bitsLeft += 5;

            if (bitsLeft < 8)
            {
                continue;
            }

            bitsLeft -= 8;
            output.Add((byte)((buffer >> bitsLeft) & 0xFF));
        }

        return [.. output];
    }
}
