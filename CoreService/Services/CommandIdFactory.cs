using System.Security.Cryptography;
using System.Text;

namespace CoreService.Services;

public static class CommandIdFactory
{
    public static string Create(string eventId, string action, string? targetId)
    {
        var input = $"{eventId}:{action}:{targetId ?? string.Empty}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return $"cmd-{Convert.ToHexString(hash)[..32].ToLowerInvariant()}";
    }
}
