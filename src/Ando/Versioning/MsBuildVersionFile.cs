// =============================================================================
// MsBuildVersionFile.cs
//
// Summary: Strict <Version> access for MSBuild files listed in ando.config versionFiles.
//
// Design Decisions:
// - Exactly one <Version> element that is a direct child of a <PropertyGroup> counts;
//   attributes (e.g. Condition) are allowed. Legacy <PackageReference><Version> children
//   and Version elements elsewhere are ignored and never rewritten.
// - 0 or more than 1 such elements is an error naming the file
// - Only the element's text is replaced; everything else (line endings, a UTF-8 BOM,
//   indentation) is preserved byte for byte
// =============================================================================

using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Ando.Versioning;

public static partial class MsBuildVersionFile
{
    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    // Matches at the offset of the element name: Version, optional attributes, value, end tag.
    [GeneratedRegex(@"\GVersion(?:\s(?:""[^""]*""|'[^']*'|[^>""'])*)?>([^<]*)</Version\s*>")]
    private static partial Regex ElementRegex();

    /// <summary>Reads the single PropertyGroup Version value; throws naming the file otherwise.</summary>
    public static string Read(string path)
    {
        var (text, _) = Decode(path);
        var (_, _, value) = Locate(text, path);
        return value.Trim();
    }

    /// <summary>Replaces only the value of the single PropertyGroup Version element.</summary>
    public static void Write(string path, string version)
    {
        var (text, bom) = Decode(path);
        var (start, length, _) = Locate(text, path);
        var updated = text[..start] + version + text[(start + length)..];

        var body = new UTF8Encoding(false).GetBytes(updated);
        File.WriteAllBytes(path, bom ? [.. Utf8Bom, .. body] : body);
    }

    private static (string Text, bool Bom) Decode(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var bom = bytes.AsSpan().StartsWith(Utf8Bom);
        try
        {
            // Refuse non-UTF-8 files instead of rewriting their bytes.
            var strict = new UTF8Encoding(false, throwOnInvalidBytes: true);
            return (strict.GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0)), bom);
        }
        catch (DecoderFallbackException)
        {
            throw new InvalidOperationException($"{path} is not UTF-8 encoded; ANDO only updates UTF-8 version files.");
        }
    }

    private static (int Start, int Length, string Value) Locate(string text, string path)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Parse(text, LoadOptions.SetLineInfo);
        }
        catch (System.Xml.XmlException ex)
        {
            throw new InvalidOperationException($"Cannot parse {path} as XML: {ex.Message}");
        }

        var versions = doc.Descendants()
            .Where(e => e.Name.LocalName == "Version" && e.Parent?.Name.LocalName == "PropertyGroup")
            .ToList();
        if (versions.Count != 1)
        {
            throw new InvalidOperationException(versions.Count == 0
                ? $"No <Version> element directly inside a <PropertyGroup> found in {path}."
                : $"{versions.Count} <Version> elements directly inside a <PropertyGroup> found in {path}; expected exactly one.");
        }

        var info = (System.Xml.IXmlLineInfo)versions[0];
        var offset = OffsetOf(text, info.LineNumber, info.LinePosition);
        var match = ElementRegex().Match(text, offset);
        if (!match.Success || match.Index != offset)
        {
            throw new InvalidOperationException(
                $"The <Version> element in {path} has an unsupported form (expected plain text content).");
        }

        var group = match.Groups[1];
        return (group.Index, group.Length, group.Value);
    }

    // XML line breaks are \r\n, \n and a lone \r; line and position are 1-based.
    private static int OffsetOf(string text, int line, int position)
    {
        var current = 1;
        var i = 0;
        while (current < line && i < text.Length)
        {
            if (text[i] == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n') i++;
                current++;
            }
            else if (text[i] == '\n')
            {
                current++;
            }

            i++;
        }

        return i + position - 1;
    }
}
