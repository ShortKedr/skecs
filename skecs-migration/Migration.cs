using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Skecs.Migration;

public sealed record FileChange(string Path, byte[] Original, byte[] Updated);

public static class Migration
{
    private static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase)
    { ".git", ".godot", ".vs", "bin", "obj", "node_modules", "Library", "Temp", "Logs", "artifacts", "__pycache__" };

    public static string RewriteCSharp(string text)
    {
        var tree = CSharpSyntaxTree.ParseText(text, new CSharpParseOptions(LanguageVersion.Preview));
        var error = tree.GetDiagnostics().FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error);
        if (error is not null) throw new InvalidOperationException($"Invalid C# syntax: {error}");
        var root = tree.GetRoot();
        foreach (var token in root.DescendantTokens())
        {
            if (!token.IsKind(SyntaxKind.IdentifierToken) || token.ValueText != "SECS") continue;
            if (token.Parent?.Parent is NameEqualsSyntax)
                throw new InvalidOperationException("SECS is a using alias in this file; review it manually");
            // A locally declared name can shadow the old namespace. Without the
            // consumer's semantic compilation, require review instead of guessing.
            if (token.Parent is not IdentifierNameSyntax)
                throw new InvalidOperationException("SECS is declared as a name or alias in this file; review it manually");
            if (token.Parent.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().Any(n => n.Name.Span.Contains(token.Span)))
                throw new InvalidOperationException("A namespace named SECS is declared in this file; review it manually");
        }
        if (root.DescendantTrivia().Any(t => t.IsKind(SyntaxKind.DisabledTextTrivia) && Regex.IsMatch(t.ToFullString(), @"\bSECS\b")))
            throw new InvalidOperationException("SECS appears in an inactive preprocessor branch; review that branch manually");
        return new NamespaceRewriter().Visit(root)!.ToFullString();
    }

    private sealed class NamespaceRewriter : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
        {
            if (node.Identifier.ValueText != "SECS") return base.VisitIdentifierName(node);
            var isRoot = node.Parent switch
            {
                QualifiedNameSyntax name => name.Left == node,
                AliasQualifiedNameSyntax name => name.Alias.Identifier.ValueText == "global" && name.Name == node,
                MemberAccessExpressionSyntax access => access.Expression == node,
                UsingDirectiveSyntax directive => directive.Name == node,
                _ => false
            };
            if (!isRoot) return node;
            var token = node.Identifier;
            return node.WithIdentifier(SyntaxFactory.Identifier(token.LeadingTrivia,
                token.Text.StartsWith('@') ? "@SKECS" : "SKECS", token.TrailingTrivia));
        }
    }

    public static string RewriteProject(string text)
    {
        var document = XDocument.Parse(text, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
        var lines = new List<int> { 0 };
        foreach (Match newline in Regex.Matches(text, "\r\n|\r|\n")) lines.Add(newline.Index + newline.Length);
        var edits = new List<(int Start, int Length, string Text)>();
        // Use parsed XML to select fields and source positions to edit only
        // their values. Conditions, comments and formatting remain byte-stable.
        foreach (var package in document.Descendants().Where(e => e.Name.Namespace == document.Root?.Name.Namespace && e.Name.LocalName is "PackageReference" or "PackageVersion"))
        {
            var identity = new[] { package.Attribute("Include"), package.Attribute("Update") }
                .FirstOrDefault(a => a?.Value == "ShortKedr.SECS");
            if (identity is null) continue;
            EditAttribute(identity, "ShortKedr.SKECS");
            var count = 0;
            foreach (var attribute in package.Attributes().Where(a => a.Name.NamespaceName.Length == 0 && a.Name.LocalName is "Version" or "VersionOverride"))
            {
                CheckVersion(attribute.Value);
                EditAttribute(attribute, "0.2.0");
                count++;
            }
            foreach (var element in package.Elements().Where(e => e.Name.Namespace == package.Name.Namespace && e.Name.LocalName is "Version" or "VersionOverride"))
            {
                CheckVersion(element.Value.Trim());
                if (element.Nodes().Any(node => node is not XText || node is XCData))
                    throw new InvalidOperationException("Version must be a plain literal; review nested XML manually");
                var start = Offset(element.Nodes().OfType<XText>().Single());
                var end = text.IndexOf('<', start);
                while (start < end && char.IsWhiteSpace(text[start])) start++;
                while (end > start && char.IsWhiteSpace(text[end - 1])) end--;
                edits.Add((start, end - start, "0.2.0"));
                count++;
            }
            if (package.Name.LocalName == "PackageVersion" && count == 0)
                throw new InvalidOperationException("PackageVersion requires literal version 0.1.0");
        }
        foreach (var edit in edits.OrderByDescending(e => e.Start))
            text = text.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.Text);
        return text;

        int Offset(XObject node)
        {
            var line = (IXmlLineInfo)node;
            return lines[line.LineNumber - 1] + line.LinePosition - 1;
        }
        void EditAttribute(XAttribute attribute, string replacement)
        {
            var quote = text.IndexOf('=', Offset(attribute)) + 1;
            while (char.IsWhiteSpace(text[quote])) quote++;
            var end = text.IndexOf(text[quote], quote + 1);
            edits.Add((quote + 1, end - quote - 1, replacement));
        }
        static void CheckVersion(string value)
        {
            if (value != "0.1.0") throw new InvalidOperationException("ShortKedr.SECS must have literal version 0.1.0; resolve properties/ranges manually");
        }
    }

    public static IReadOnlyList<FileChange> Plan(string root)
    {
        root = Path.GetFullPath(root);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("Consumer project directory does not exist");
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("Select a real consumer directory, not a symbolic link");
        var changes = new List<FileChange>();
        Scan(root);
        return changes.AsReadOnly();

        void Scan(string directory)
        {
            foreach (var path in Directory.EnumerateFileSystemEntries(directory).Order(StringComparer.Ordinal))
            {
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    if (!Excluded.Contains(Path.GetFileName(path))) Scan(path);
                    continue;
                }
                var extension = Path.GetExtension(path).ToLowerInvariant();
                if (extension is not (".cs" or ".csproj" or ".props" or ".targets")) continue;
                try
                {
                    var original = File.ReadAllBytes(path);
                    var (text, encoding, bomLength) = Decode(original);
                    var updated = extension == ".cs" ? RewriteCSharp(text) : RewriteProject(text);
                    if (updated == text) continue;
                    changes.Add(new FileChange(path, original,
                        original[..bomLength].Concat(encoding.GetBytes(updated)).ToArray()));
                }
                catch (Exception error) when (error is InvalidOperationException or System.Xml.XmlException or DecoderFallbackException)
                {
                    throw new InvalidOperationException($"{Path.GetRelativePath(root, path)}: {error.Message}", error);
                }
            }
        }
    }

    internal static (string Text, Encoding Encoding, int BomLength) Decode(byte[] data)
    {
        Encoding encoding;
        int bom;
        if (data.AsSpan().StartsWith(new byte[] { 0xff, 0xfe, 0x00, 0x00 })) { encoding = new UTF32Encoding(false, false, true); bom = 4; }
        else if (data.AsSpan().StartsWith(new byte[] { 0x00, 0x00, 0xfe, 0xff })) { encoding = new UTF32Encoding(true, false, true); bom = 4; }
        else if (data.AsSpan().StartsWith(new byte[] { 0xff, 0xfe })) { encoding = new UnicodeEncoding(false, false, true); bom = 2; }
        else if (data.AsSpan().StartsWith(new byte[] { 0xfe, 0xff })) { encoding = new UnicodeEncoding(true, false, true); bom = 2; }
        else
        {
            encoding = new UTF8Encoding(false, true);
            bom = data.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }) ? 3 : 0;
        }
        return (encoding.GetString(data, bom, data.Length - bom), encoding, bom);
    }

    public static void Apply(IReadOnlyList<FileChange> changes)
    {
        // Validate the complete plan before the first write.
        foreach (var change in changes) EnsureUnchanged(change);
        foreach (var change in changes)
        {
            EnsureUnchanged(change);
            var temporary = Path.Combine(Path.GetDirectoryName(change.Path)!, $".skecs-migration-{Guid.NewGuid():N}.tmp");
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write)) stream.Write(change.Updated);
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, File.GetUnixFileMode(change.Path));
                File.SetAttributes(temporary, File.GetAttributes(change.Path));
                File.Move(temporary, change.Path, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
    }

    private static void EnsureUnchanged(FileChange change)
    {
        if (!File.ReadAllBytes(change.Path).AsSpan().SequenceEqual(change.Original))
            throw new InvalidOperationException($"{change.Path}: changed since migration was planned");
    }
}
