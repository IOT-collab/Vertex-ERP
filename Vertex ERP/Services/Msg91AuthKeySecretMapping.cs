namespace VertexERP.Services;

/// <summary>Maps the existing Key Vault secret name to the bound options key.</summary>
public static class Msg91AuthKeySecretMapping
{
    public const string SecretName = "Msg91-AuthKey";
    public const string ConfigurationKey = "Msg91WhatsApp:AuthKey";

    public static bool IsTargetSecret(string? name) =>
        string.Equals(name, SecretName, StringComparison.OrdinalIgnoreCase);

    public static string MapSecretNameToConfigurationKey(string name) =>
        IsTargetSecret(name)
            ? ConfigurationKey
            : throw new ArgumentException("Only the MSG91 auth secret can be mapped.", nameof(name));
}
