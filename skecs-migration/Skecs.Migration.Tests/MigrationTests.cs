using System.Text;
using Skecs.Migration;
using MigrationEngine = Skecs.Migration.Migration;

namespace Skecs.Migration.Tests;

public sealed class MigrationTests
{
    [Fact]
    public void CSharpImportsAndQualifiedTypesChangeWithoutRewritingLiteralText()
    {
        const string before = """"
            global using SECS;
            using Ecs = global::SECS.World;
            using Core = SECS;
            using GlobalCore = global::SECS;
            using static SECS.SomeType;
            using @SECS;
            var world = new SECS.World();
            var other = new global::SECS.World();
            // using SECS; new SECS.World();
            /* SECS.World */
            var text = "SECS.World";
            var verbatim = @"SECS.World and ""SECS.World""";
            var raw = """SECS.World""";
            var character = '\'';
            Other.SECS.World unchanged;
            """";

        var after = MigrationEngine.RewriteCSharp(before);

        foreach (var expected in new[]
        {
            "global using SKECS;", "using Ecs = global::SKECS.World;", "using Core = SKECS;",
            "using GlobalCore = global::SKECS;", "using static SKECS.SomeType;", "using @SKECS;",
            "new SKECS.World()", "new global::SKECS.World()"
        }) Assert.Contains(expected, after);

        var commentOffset = before.IndexOf("// using", StringComparison.Ordinal);
        Assert.Equal(before[commentOffset..], after[after.IndexOf("// using", StringComparison.Ordinal)..]);
        Assert.Equal(after, MigrationEngine.RewriteCSharp(after));
    }

    [Fact]
    public void CSharpTriviaIsPreservedExactly()
    {
        const string before = "using /* old */ SECS /* x */;\r\nSECS /* x */ .World w;\r\n";
        const string expected = "using /* old */ SKECS /* x */;\r\nSKECS /* x */ .World w;\r\n";
        Assert.Equal(expected, MigrationEngine.RewriteCSharp(before));
    }

    [Fact]
    public void InterpolationExpressionsChangeButNestedStringContentsRemainLiteral()
    {
        const string before = """"
            using SECS;
            var s = $"SECS.World {nameof(SECS.World)} {F("SECS.World")}";
            var r = $$"""SECS.World {{nameof(SECS.World)}} {{F("""SECS.World""")}}""";
            """";
        const string expected = """"
            using SKECS;
            var s = $"SECS.World {nameof(SKECS.World)} {F("SECS.World")}";
            var r = $$"""SECS.World {{nameof(SKECS.World)}} {{F("""SECS.World""")}}""";
            """";
        Assert.Equal(expected, MigrationEngine.RewriteCSharp(before));
    }

    [Fact]
    public void AliasNamedSecsRequiresReviewRatherThanChangingAnotherNamespace()
    {
        Assert.ThrowsAny<Exception>(() => MigrationEngine.RewriteCSharp(
            "using SECS = OtherNamespace; SECS.World w;"));
    }

    [Theory]
    [InlineData("using SECS; var SECS = factory; SECS.World();")]
    [InlineData("using SECS; class SECS { }")]
    [InlineData("using SECS; class Consumer { void Run(object SECS) { SECS.ToString(); } }")]
    public void DeclaredSecsIdentifiersRequireReview(string source)
    {
        Assert.ThrowsAny<Exception>(() => MigrationEngine.RewriteCSharp(source));
    }

    [Fact]
    public void InactivePreprocessorBranchWithSecsRequiresReview()
    {
        const string source = "#if OLD_CONSUMER\nusing SECS;\n#endif\nusing System;\n";
        Assert.ThrowsAny<Exception>(() => MigrationEngine.RewriteCSharp(source));
    }

    [Theory]
    [InlineData("using SECS; /*")]
    [InlineData("using SECS; var x = \"unterminated")]
    [InlineData("using SECS; var x = $\"{F(\"x\")}")]
    public void MalformedCSharpIsRejected(string source)
    {
        Assert.ThrowsAny<Exception>(() => MigrationEngine.RewriteCSharp(source));
    }

    [Fact]
    public void PackageReferencesAndCentralVersionsChangeWhileOtherXmlIsPreserved()
    {
        const string before = """
            <?xml version="1.0"?>
            <Project><ItemGroup>
              <PackageReference Include="shortkedr.secs" Version="0.1.0" />
              <PackageReference Include='ShortKedr.SECS'><Version>0.1.0</Version></PackageReference>
              <PackageReference Include="ShortKedr.SECS" />
              <PackageReference Update="ShortKedr.SECS" VersionOverride="0.1.0" />
              <PackageVersion Include="ShortKedr.SECS" Version="0.1.0" />
              <PackageReference Include="Unrelated.SECS" Version="0.1.0" />
              <!-- <PackageReference Include="ShortKedr.SECS" Version="0.1.0" /> -->
            </ItemGroup></Project>
            """;
        var after = MigrationEngine.RewriteProject(before);

        Assert.Equal(5, after.Split("ShortKedr.SKECS", StringSplitOptions.None).Length - 1);
        Assert.Contains("<Version>0.2.0</Version>", after);
        Assert.Contains("VersionOverride=\"0.2.0\"", after);
        Assert.Contains("Unrelated.SECS\" Version=\"0.1.0\"", after);
        Assert.Contains("<!-- <PackageReference Include=\"ShortKedr.SECS\" Version=\"0.1.0\" /> -->", after);
        Assert.Contains("Include='ShortKedr.SKECS'", after);
        Assert.Equal(after, MigrationEngine.RewriteProject(after));
    }

    [Fact]
    public void CDataAndProcessingInstructionsAreNotPackageReferences()
    {
        const string before = "<?custom key=\"ShortKedr.SECS\"?><Project><Code><![CDATA[<PackageReference Include=\"ShortKedr.SECS\" Version=\"0.1.0\" />]]></Code></Project>";
        Assert.Equal(before, MigrationEngine.RewriteProject(before));
    }

    [Fact]
    public void CommentsAndCDataInsidePackageReferenceDoNotHideNestedVersion()
    {
        const string before = """
            <Project><ItemGroup>
              <PackageReference Include="ShortKedr.SECS">
                <!-- Keep <Version>9.9.9</Version> as a historical explanation. -->
                <Notes><![CDATA[<Version>8.8.8</Version> and ShortKedr.SECS]]></Notes>
                <Version>0.1.0</Version>
              </PackageReference>
            </ItemGroup></Project>
            """;
        var expected = before.Replace("Include=\"ShortKedr.SECS\"", "Include=\"ShortKedr.SKECS\"", StringComparison.Ordinal)
            .Replace("<Version>0.1.0</Version>", "<Version>0.2.0</Version>", StringComparison.Ordinal);
        Assert.Equal(expected, MigrationEngine.RewriteProject(before));
    }

    [Fact]
    public void ConditionTextThatLooksLikeAnAttributeIsPreserved()
    {
        const string before = """
            <Project><ItemGroup>
              <PackageReference Include="ShortKedr.SECS" Condition="'$(Feature)' == &quot;Version='9.9.9'&quot;" Version="0.1.0" />
              <PackageReference Include="Other.Library" Condition="'$(Choice)' == &quot;Include='ShortKedr.SECS' Version='0.1.0'&quot;" Version="7.0.0" />
            </ItemGroup></Project>
            """;
        var expected = before.Replace("Include=\"ShortKedr.SECS\"", "Include=\"ShortKedr.SKECS\"", StringComparison.Ordinal)
            .Replace("Version=\"0.1.0\"", "Version=\"0.2.0\"", StringComparison.Ordinal);
        Assert.Equal(expected, MigrationEngine.RewriteProject(before));
    }

    [Fact]
    public void NestedVersionConditionWithGreaterThanDoesNotShiftTheValueEdit()
    {
        const string before = "<Project><PackageReference Include=\"ShortKedr.SECS\"><Version Condition=\"'$(Level)' > '0'\">0.1.0</Version></PackageReference></Project>";
        var expected = before.Replace("ShortKedr.SECS", "ShortKedr.SKECS", StringComparison.Ordinal)
            .Replace(">0.1.0</Version>", ">0.2.0</Version>", StringComparison.Ordinal);
        Assert.Equal(expected, MigrationEngine.RewriteProject(before));
    }

    [Theory]
    [InlineData("0.3.0")]
    [InlineData("$(SecsVersion)")]
    [InlineData("[0.1.0,0.2.0)")]
    public void UnsupportedPackageVersionsAreRejected(string version)
    {
        Assert.ThrowsAny<Exception>(() => MigrationEngine.RewriteProject(
            $"<Project><PackageReference Include=\"ShortKedr.SECS\" Version=\"{version}\" /></Project>"));
    }

    [Fact]
    public void CentralPackageVersionMustDeclareItsVersion()
    {
        Assert.ThrowsAny<Exception>(() => MigrationEngine.RewriteProject(
            "<Project><PackageVersion Include=\"ShortKedr.SECS\" /></Project>"));
    }

    [Fact]
    public void OnlyUnityPrecompiledSecsDllReferencesChange()
    {
        using var fixture = new ConsumerFixture();
        const string before = """
            {
              "name": "SECS",
              "references": ["SECS"],
              "description": "До обновления: SECS.dll",
              "custom": {"precompiledReferences": ["SECS.dll"]},
              "overrideReferences": true,
              "precompiledReferences": ["Other.dll", "SECS.dll", "Other.SECS.dll"]
            }
            """;
        const string expected = """
            {
              "name": "SECS",
              "references": ["SECS"],
              "description": "До обновления: SECS.dll",
              "custom": {"precompiledReferences": ["SECS.dll"]},
              "overrideReferences": true,
              "precompiledReferences": ["Other.dll", "SKECS.dll", "Other.SECS.dll"]
            }
            """;
        var asmdef = fixture.WriteBytes("Assets/Game.asmdef", WithPreamble(new UTF8Encoding(true), before));
        const string asmrefText = """{"reference":"SECS"}""";
        var asmref = fixture.Write("Assets/Other/Game.asmref", asmrefText);

        var changes = MigrationEngine.Plan(fixture.Root);
        Assert.Equal(Path.GetFullPath(asmdef), Assert.Single(changes).Path);
        Assert.Equal(WithPreamble(new UTF8Encoding(true), before), File.ReadAllBytes(asmdef));
        MigrationEngine.Apply(changes);

        Assert.Equal(WithPreamble(new UTF8Encoding(true), expected), File.ReadAllBytes(asmdef));
        Assert.Equal(asmrefText, File.ReadAllText(asmref));
        Assert.Empty(MigrationEngine.Plan(fixture.Root));
    }

    [Fact]
    public void MalformedUnityAssemblyDefinitionStopsApplyBeforeAnyWrites()
    {
        using var fixture = new ConsumerFixture();
        var valid = fixture.Write("A.cs", "using SECS;");
        fixture.Write("Z.asmdef", """{"precompiledReferences":["SECS.dll",]}""");
        using var output = new StringWriter();
        using var error = new StringWriter();

        Assert.Equal(1, MigrationCli.Run([.. fixture.Arguments, "--apply"], output, error));
        Assert.Equal("using SECS;", File.ReadAllText(valid));
        Assert.Contains("Z.asmdef", error.ToString());
    }

    [Fact]
    public void PreviewWithoutOldPackageReportsUnverifiedEntitiesWithoutChangingTheirUses()
    {
        using var fixture = new ConsumerFixture();
        const string source = """
            using SECS;
            var saved = new Entity(id, version);
            _ = saved.IsValid;
            _ = saved == other;
            var foreign = new Other.Entity(id, version);
            _ = foreign.IsValid;
            """;
        var file = fixture.Write("Program.cs", source);
        using var output = new StringWriter();
        using var error = new StringWriter();

        Assert.Equal(0, MigrationCli.Run(fixture.Arguments, output, error));
        Assert.Equal(source, File.ReadAllText(file));
        var preview = output.ToString();
        Assert.Contains("Program.cs(2,13): Unverified Entity construction.", preview);
        Assert.Contains("Program.cs(3,5): Unverified Entity API use.", preview);
        Assert.Contains("Program.cs(4,5): Unverified Entity comparison.", preview);
        Assert.DoesNotContain("Program.cs(5", preview);
        Assert.DoesNotContain("Program.cs(6", preview);
        Assert.DoesNotContain("BindEntity", preview);
    }

    [Fact]
    public void DryRunDoesNotWriteAndApplyPreservesBomEncodingAndLineEndings()
    {
        using var fixture = new ConsumerFixture();
        const string sourceText = "using SECS;\r\nvar w = new SECS.World();\r\n";
        const string projectText = "<Project>\r\n<PackageReference Include=\"ShortKedr.SECS\" Version=\"0.1.0\" />\r\n</Project>";
        var source = fixture.WriteBytes("Program.cs", WithPreamble(new UTF8Encoding(true), sourceText));
        var project = fixture.WriteBytes("App.csproj", WithPreamble(Encoding.Unicode, projectText));
        fixture.Write("Directory.Packages.props", "<Project><PackageVersion Include=\"ShortKedr.SECS\" Version=\"0.1.0\" /></Project>");
        fixture.Write("obj/Generated.cs", "using SECS;");
        fixture.Write("node_modules/Generated.cs", "using SECS;");
        var sourceBefore = File.ReadAllBytes(source);
        var projectBefore = File.ReadAllBytes(project);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var args = fixture.Arguments;

        Assert.Equal(0, MigrationCli.Run(args, output, error));
        Assert.Equal(sourceBefore, File.ReadAllBytes(source));
        Assert.Equal(projectBefore, File.ReadAllBytes(project));
        Assert.Contains("SKECS", output.ToString());

        Assert.Equal(0, MigrationCli.Run([.. args, "--apply"], output, error));
        Assert.Equal(WithPreamble(new UTF8Encoding(true), sourceText.Replace("SECS", "SKECS")), File.ReadAllBytes(source));
        Assert.Equal(WithPreamble(Encoding.Unicode, projectText.Replace("ShortKedr.SECS", "ShortKedr.SKECS").Replace("0.1.0", "0.2.0")), File.ReadAllBytes(project));
        Assert.Contains("ShortKedr.SKECS", File.ReadAllText(Path.Combine(fixture.Root, "Directory.Packages.props")));
        Assert.Equal("using SECS;", File.ReadAllText(Path.Combine(fixture.Root, "obj/Generated.cs")));
        Assert.Equal("using SECS;", File.ReadAllText(Path.Combine(fixture.Root, "node_modules/Generated.cs")));
        Assert.Empty(MigrationEngine.Plan(fixture.Root));
        Assert.Equal(0, MigrationCli.Run([.. args, "--apply"], output, error));
        Assert.Equal(WithPreamble(new UTF8Encoding(true), sourceText.Replace("SECS", "SKECS")), File.ReadAllBytes(source));
    }

    [Fact]
    public void Utf16BigEndianIsPreserved()
    {
        using var fixture = new ConsumerFixture();
        var file = fixture.WriteBytes("Program.cs", WithPreamble(Encoding.BigEndianUnicode, "using SECS;\n"));
        MigrationEngine.Apply(MigrationEngine.Plan(fixture.Root));
        Assert.Equal(WithPreamble(Encoding.BigEndianUnicode, "using SKECS;\n"), File.ReadAllBytes(file));
    }

    [Fact]
    public void PreflightFailurePreventsAllWrites()
    {
        using var fixture = new ConsumerFixture();
        var valid = fixture.Write("A.cs", "using SECS;");
        fixture.Write("ZBad.csproj", "<Project><PackageReference Include=\"ShortKedr.SECS\" Version=\"$(Version)\" /></Project>");
        using var output = new StringWriter();
        using var error = new StringWriter();

        Assert.Equal(1, MigrationCli.Run([.. fixture.Arguments, "--apply"], output, error));
        Assert.Equal("using SECS;", File.ReadAllText(valid));
        Assert.NotEmpty(error.ToString());
    }

    [Fact]
    public void ConcurrentEditIsNotOverwrittenByApply()
    {
        using var fixture = new ConsumerFixture();
        var earlierFile = fixture.Write("A.cs", "using SECS;");
        var file = fixture.Write("Z.cs", "using SECS;");
        var changes = MigrationEngine.Plan(fixture.Root);
        File.WriteAllText(file, "using SECS; // edited after preview", new UTF8Encoding(false));

        Assert.ThrowsAny<Exception>(() => MigrationEngine.Apply(changes));
        Assert.Equal("using SECS;", File.ReadAllText(earlierFile));
        Assert.Equal("using SECS; // edited after preview", File.ReadAllText(file));
    }

    [Fact]
    public void UnsupportedRouteAndUnknownArgumentsFailWithoutWrites()
    {
        using var fixture = new ConsumerFixture();
        var file = fixture.Write("Program.cs", "using SECS;");
        using var output = new StringWriter();
        using var error = new StringWriter();
        Assert.Equal(2, MigrationCli.Run([fixture.Root, "--from", "0.1.0", "--to", "9.9.9", "--apply"], output, error));
        Assert.Equal(2, MigrationCli.Run([.. fixture.Arguments, "--unknown"], output, error));
        Assert.Equal("using SECS;", File.ReadAllText(file));
    }

    private static byte[] WithPreamble(Encoding encoding, string text) => [.. encoding.GetPreamble(), .. encoding.GetBytes(text)];

    private sealed class ConsumerFixture : IDisposable
    {
        private readonly string tempParent;
        public string Root { get; }
        public string[] Arguments => [Root, "--from", "0.1.0", "--to", "0.2.0"];

        public ConsumerFixture()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SKECS.sln"))) directory = directory.Parent;
            if (directory is null) throw new InvalidOperationException("Tests must run inside the SKECS workspace");
            tempParent = Path.GetFullPath(Path.Combine(directory.FullName, "artifacts/tooling/tmp/migration-dotnet-tests"));
            Root = Path.Combine(tempParent, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Write(string name, string text) => WriteBytes(name, new UTF8Encoding(false).GetBytes(text));

        public string WriteBytes(string name, byte[] bytes)
        {
            var file = Path.Combine(Root, name);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllBytes(file, bytes);
            return file;
        }

        public void Dispose()
        {
            if (!Path.GetFullPath(Root).StartsWith(tempParent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Fixture cleanup escaped the workspace temporary directory");
            Directory.Delete(Root, recursive: true);
        }
    }
}
