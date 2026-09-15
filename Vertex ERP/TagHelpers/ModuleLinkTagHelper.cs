using Microsoft.AspNetCore.Razor.TagHelpers;
using VertexERP.Services;
namespace VertexERP.TagHelpers;
[HtmlTargetElement("a")]
public sealed class ModuleLinkTagHelper(ModuleAccessService access) : TagHelper
{
    public override int Order => 1000;
    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var href = output.Attributes["href"]?.Value?.ToString();
        if (string.IsNullOrWhiteSpace(href) || !href.StartsWith('/') || href.StartsWith("//")) return;
        var parts = href.Split('?', '#')[0].Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 1 && !await access.AllowedAsync(parts[0], parts.Length >= 2 ? parts[1] : "Index")) output.SuppressOutput();
    }
}
