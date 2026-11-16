using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SKECS.GA.Analysis;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class GaSystemAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => GaSemanticAnalysis.Rules;

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            if (!start.Options.AnalyzerConfigOptionsProvider.GlobalOptions.TryGetValue(
                    "build_property.SKECSGraphicsAccelerated", out var enabled) ||
                !string.Equals(enabled, "true", StringComparison.OrdinalIgnoreCase))
                return;

            start.RegisterSymbolAction(AnalyzeType, SymbolKind.NamedType);
        });
    }

    private static void AnalyzeType(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (!GaSemanticAnalysis.IsGaSystem(type, context.Compilation))
            return;

        var entries = GaSemanticAnalysis.GetEntryPoints(type, context.Compilation);
        if (entries.Length != 1)
        {
            context.ReportDiagnostic(GaSemanticAnalysis.InvalidSystem(type));
            return;
        }

        foreach (var diagnostic in GaSemanticAnalysis.Analyze(entries[0], context.Compilation, context.CancellationToken))
            context.ReportDiagnostic(diagnostic);
    }
}
