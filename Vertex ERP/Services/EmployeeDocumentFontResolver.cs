using PdfSharp.Fonts;

namespace VertexERP.Services;

public sealed class EmployeeDocumentFontResolver : IFontResolver
{
    private const string RegularFace = "VertexSans-Regular";
    private const string BoldFace = "VertexSans-Bold";
    private readonly byte[] _regular;
    private readonly byte[] _bold;

    public EmployeeDocumentFontResolver(string templateDirectory)
    {
        _regular = File.ReadAllBytes(Path.Combine(templateDirectory, "Ubuntu-Regular.ttf"));
        _bold = File.ReadAllBytes(Path.Combine(templateDirectory, "Ubuntu-Bold.ttf"));
    }

    public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic) =>
        new(isBold ? BoldFace : RegularFace);

    public byte[]? GetFont(string faceName) => faceName switch
    {
        RegularFace => _regular,
        BoldFace => _bold,
        _ => null
    };
}
