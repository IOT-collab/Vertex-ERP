using System.ComponentModel.DataAnnotations;

namespace VertexERP.Models;

public class EmployeeCompany
{
    [Key, MaxLength(3)] public string Code { get; set; } = string.Empty;
    [Required, MaxLength(120)] public string Name { get; set; } = string.Empty;
    [ConcurrencyCheck] public int LastIssuedNumber { get; set; }

    public static string DisplayName(string? code) => code switch
    {
        "VAS" => "Vertex Automation System Pvt. Ltd.",
        "VPC" => "Vertex Power Controls Pvt. Ltd.",
        _ => "Not assigned"
    };
    public static bool IsValid(string? code) => code is "VAS" or "VPC";
}
