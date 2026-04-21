using System.Security.Cryptography;
using System.Text;

namespace SmartLogisticsTracker.Api.Services;

public interface IPasswordHasher
{
    string Hash(string rawPassword);

    bool Verify(string rawPassword, string passwordHash);
}

public sealed class Sha256PasswordHasher : IPasswordHasher
{
    public string Hash(string rawPassword)
    {
        var bytes = Encoding.UTF8.GetBytes(rawPassword);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    public bool Verify(string rawPassword, string passwordHash)
    {
        var computed = Hash(rawPassword);
        return string.Equals(computed, passwordHash, StringComparison.OrdinalIgnoreCase);
    }
}
