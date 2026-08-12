using Microsoft.AspNetCore.DataProtection;
namespace VertexERP.Services;
public sealed class BankAccountProtectionService
{
    private readonly IDataProtector _protector;
    public BankAccountProtectionService(IDataProtectionProvider provider) => _protector = provider.CreateProtector("VertexERP.BankAccount.v1");
    public string Protect(string value) => _protector.Protect(value.Trim());
    public string Unprotect(string value) => _protector.Unprotect(value);
}
