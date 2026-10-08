using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace VertexERP.Services;

public sealed record FirebasePhoneIdentity(string PhoneNumber, string Subject);

public interface IFirebaseIdTokenVerifier
{
    Task<FirebasePhoneIdentity?> VerifyAsync(string? idToken, CancellationToken cancellationToken = default);
}

/// <summary>Validates Firebase Secure Token ID tokens using Google's public signing certificates.</summary>
public sealed class FirebaseIdTokenVerifier(HttpClient client, IConfiguration configuration) : IFirebaseIdTokenVerifier
{
    private const string CertificateUrl = "https://www.googleapis.com/robot/v1/metadata/x509/securetoken@system.gserviceaccount.com";
    private static readonly SemaphoreSlim CertificateLock = new(1, 1);
    private static IReadOnlyDictionary<string, string>? _certificates;
    private static DateTimeOffset _certificatesExpireAt;

    public async Task<FirebasePhoneIdentity?> VerifyAsync(string? idToken, CancellationToken cancellationToken = default)
    {
        var projectId = configuration["Firebase:ProjectId"];
        if (string.IsNullOrWhiteSpace(projectId) || string.IsNullOrWhiteSpace(idToken) || idToken.Length > 16_384)
            return null;

        try
        {
            var segments = idToken.Split('.');
            if (segments.Length != 3) return null;
            using var header = JsonDocument.Parse(DecodeBase64Url(segments[0]));
            using var claims = JsonDocument.Parse(DecodeBase64Url(segments[1]));
            var headerRoot = header.RootElement;
            var claimRoot = claims.RootElement;
            if (GetString(headerRoot, "alg") != "RS256") return null;
            var keyId = GetString(headerRoot, "kid");
            if (string.IsNullOrWhiteSpace(keyId)) return null;

            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (GetString(claimRoot, "aud") != projectId ||
                GetString(claimRoot, "iss") != $"https://securetoken.google.com/{projectId}") return null;
            var subject = GetString(claimRoot, "sub");
            var phone = GetString(claimRoot, "phone_number");
            if (string.IsNullOrWhiteSpace(subject) || subject.Length > 128 || string.IsNullOrWhiteSpace(phone)) return null;
            if (!TryGetInt64(claimRoot, "exp", out var expires) || expires <= now ||
                !TryGetInt64(claimRoot, "iat", out var issued) || issued > now + 300 ||
                !TryGetInt64(claimRoot, "auth_time", out var authenticated) || authenticated > now + 60 || authenticated < now - 600)
                return null;

            var certificates = await GetCertificatesAsync(cancellationToken);
            if (!certificates.TryGetValue(keyId, out var certificatePem)) return null;
            using var certificate = X509Certificate2.CreateFromPem(certificatePem);
            using var rsa = certificate.GetRSAPublicKey();
            if (rsa is null) return null;
            var signedData = Encoding.ASCII.GetBytes(segments[0] + "." + segments[1]);
            var signature = DecodeBase64Url(segments[2]);
            if (!rsa.VerifyData(signedData, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)) return null;
            return new FirebasePhoneIdentity(phone, subject);
        }
        catch (Exception exception) when (exception is FormatException or JsonException or CryptographicException or ArgumentException or HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }

    private async Task<IReadOnlyDictionary<string, string>> GetCertificatesAsync(CancellationToken cancellationToken)
    {
        if (_certificates is not null && _certificatesExpireAt > DateTimeOffset.UtcNow) return _certificates;
        await CertificateLock.WaitAsync(cancellationToken);
        try
        {
            if (_certificates is not null && _certificatesExpireAt > DateTimeOffset.UtcNow) return _certificates;
            using var response = await client.GetAsync(CertificateUrl, cancellationToken);
            response.EnsureSuccessStatusCode();
            var certs = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>(cancellationToken: cancellationToken)
                ?? throw new InvalidOperationException("Google returned no Firebase signing certificates.");
            var cacheDuration = response.Headers.CacheControl?.MaxAge ?? TimeSpan.FromMinutes(5);
            _certificates = certs;
            _certificatesExpireAt = DateTimeOffset.UtcNow.Add(cacheDuration);
            return certs;
        }
        finally
        {
            CertificateLock.Release();
        }
    }

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool TryGetInt64(JsonElement element, string property, out long value)
    {
        if (element.TryGetProperty(property, out var item) && item.TryGetInt64(out value)) return true;
        value = 0;
        return false;
    }

    private static byte[] DecodeBase64Url(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        base64 += new string('=', (4 - base64.Length % 4) % 4);
        return Convert.FromBase64String(base64);
    }
}
