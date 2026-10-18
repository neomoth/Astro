// Ported from RobustToolbox's ExplicitVirtualAnalyzer (MIT): https://github.com/space-wizards/RobustToolbox
using System.Collections.Immutable;
using System.Linq;
using Astro.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Astro.Roslyn.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ExplicitVirtualAnalyzer : DiagnosticAnalyzer
{
	private const string Attribute = "Astro.Abstractions.VirtualAttribute";

	private static readonly DiagnosticDescriptor ExplicitVirtualRule = new(
		DiagnosticsCodes.ExplicitVirtualAttribute,
		"Class must be explicitly marked as [Virtual], abstract, static, or sealed",
		"Class {0} must be explicitly marked as [Virtual], abstract, static, or sealed",
		"Usage",
		DiagnosticSeverity.Warning,
		true,
		"Class must be explicitly marked as [Virtual], abstract, static, or sealed.");

	private static readonly DiagnosticDescriptor ExclusiveRule = new(
		DiagnosticsCodes.ExclusiveRule,
		"A class marked as [Virtual] cannot be abstract, static, or sealed",
		"Class {0} is marked as [Virtual] and cannot also be abstract, static, or sealed",
		"Usage",
		DiagnosticSeverity.Error,
		true,
		"A class marked as [Virtual] cannot be abstract, static, or sealed.");

	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
		[ExplicitVirtualRule, ExclusiveRule];

	public override void Initialize(AnalysisContext context)
	{
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();
		context.RegisterCompilationStartAction(ctx =>
		{
			// Only projects that can see the attribute (i.e. reference Astro.Abstractions) are checked.
			var attrSymbol = ctx.Compilation.GetTypeByMetadataName(Attribute);
			if (attrSymbol == null) return;

			ctx.RegisterSyntaxNodeAction(nodeContext => AnalyzeNode(nodeContext, attrSymbol),
				SyntaxKind.ClassDeclaration);
		});
	}

	private static void AnalyzeNode(SyntaxNodeAnalysisContext context, INamedTypeSymbol attrSymbol)
	{
		var classDecl = (ClassDeclarationSyntax)context.Node;
		if (context.SemanticModel.GetDeclaredSymbol(classDecl) is not { } classSymbol) return;

		var hasKeyword = classSymbol.IsSealed || classSymbol.IsAbstract || classSymbol.IsStatic;
		var hasAttribute = classSymbol.GetAttributes()
			.Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, attrSymbol));

		if (hasAttribute && hasKeyword)
		{
			// Having both [Virtual] and sealed/abstract/static doesn't make sense.
			context.ReportDiagnostic(Diagnostic.Create(ExclusiveRule, classDecl.Keyword.GetLocation(),
				classSymbol.Name));
		}

		// Having just [Virtual] or sealed/abstract/static is fine.
		if (hasKeyword || hasAttribute) return;

		// Having neither is bad.
		context.ReportDiagnostic(Diagnostic.Create(ExplicitVirtualRule, classDecl.Keyword.GetLocation(),
			classSymbol.Name));
	}
}