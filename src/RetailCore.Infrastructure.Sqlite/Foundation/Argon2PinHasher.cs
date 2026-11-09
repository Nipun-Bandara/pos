using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using RetailCore.Application.Foundation;

namespace RetailCore.Infrastructure.Sqlite.Foundation;

/// <summary>PHC Argon2id v19: 64 MiB, three passes, one lane, 128-bit random salt.</summary>
public sealed class Argon2PinHasher : IPinHasher
{
    private const int Memory = 65536;
    private const int Iterations = 3;

    public string Hash(string pin)
    {
        ArgumentNullException.ThrowIfNull(pin);
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Derive(pin, salt);
        return string.Create(CultureInfo.InvariantCulture,
            $"$argon2id$v=19$m={Memory},t={Iterations},p=1${Encode(salt)}${Encode(hash)}");
    }

    public bool Verify(string pin, string encodedHash)
    {
        if (pin is null || pin.Length > 12 || encodedHash is null || encodedHash.Length > 256) { return false; }
        var parts = encodedHash.Split('$');
        if (parts.Length != 6 || parts[1] != "argon2id" || parts[2] != "v=19"
            || parts[3] != "m=65536,t=3,p=1") { return false; }
        try
        {
            var salt = Decode(parts[4]);
            var expected = Decode(parts[5]);
            return salt.Length == 16 && expected.Length == 32
                && CryptographicOperations.FixedTimeEquals(Derive(pin, salt), expected);
        }
        catch (FormatException) { return false; }
    }

    private static string Encode(byte[] value) => Convert.ToBase64String(value).TrimEnd('=');

    private static byte[] Decode(string value) =>
        Convert.FromBase64String(value + new string('=', (4 - value.Length % 4) % 4));

    private static byte[] Derive(string pin, byte[] salt)
    {
        var bytes = Encoding.UTF8.GetBytes(pin);
        try
        {
            var generator = new Argon2BytesGenerator();
            generator.Init(new Argon2Parameters.Builder(Argon2Parameters.Argon2id)
                .WithVersion(Argon2Parameters.Version13).WithMemoryAsKB(Memory)
                .WithIterations(Iterations).WithParallelism(1).WithSalt(salt).Build());
            var result = new byte[32];
            generator.GenerateBytes(bytes, result);
            return result;
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
}
