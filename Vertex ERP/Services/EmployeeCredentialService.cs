using System.Security.Cryptography;
using System.Text.RegularExpressions;
using VertexERP.Data;
using VertexERP.Models;

namespace VertexERP.Services;

public static class EmployeeCredentialService
{
    public static void Apply(AppUser account, Employee employee, string username, string? password)
    {
        account.Username = username.Trim();
        account.NormalizedUsername = DatabaseInitializer.NormalizeUsername(account.Username);
        account.Employee = employee;
        account.FullName = employee.FullName;
        if (string.IsNullOrEmpty(account.Role)) account.Role = AccountRoleService.Employee;
        if (!string.IsNullOrEmpty(password))
        {
            var hash = PasswordHashService.HashPassword(password);
            if (!PasswordHashService.VerifyPassword(password, hash))
                throw new InvalidOperationException("Unable to create a valid employee login password.");
            account.PasswordHash = hash;
            account.MustChangePassword = false;
        }
    }

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
