using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace VertexERP.Services;

public static class EmployeeCredentialService
{
    public static (string Username, string Password) Generate(string? firstName, string? lastName)
    {
        var stem = Regex.Replace($"{firstName}.{lastName}".ToLowerInvariant(), "[^a-z0-9.]", "").Trim('.');
        if (stem.Length == 0) stem = "employee";
        stem = stem[..Math.Min(stem.Length, 30)];
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789!@#$%";
        var password = new string(Enumerable.Range(0, 16).Select(_ => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)]).ToArray());
        return ($"{stem}.{Convert.ToHexString(RandomNumberGenerator.GetBytes(5)).ToLowerInvariant()}", password);
    }
}
