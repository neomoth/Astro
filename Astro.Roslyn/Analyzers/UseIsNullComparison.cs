using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Astro.Diagnostics;

namespace Astro.Roslyn.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
internal class UseIsNullComparison : DiagnosticAnalyzer
{
	private static readonly DiagnosticDescriptor Descriptor = new(DiagnosticsCodes.UseIsNullComparison,
		"Use Is Null Comparison", "Use is null/is not null instead of == null/!= null", "Styling",
		DiagnosticSeverity.Warning, true);

	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Descriptor];

	public override void Initialize(AnalysisContext context)
	{
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();
		context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.EqualsExpression, SyntaxKind.NotEqualsExpression);
	}

	private static void Analyze(SyntaxNodeAnalysisContext context)
	{
		var expression = (BinaryExpressionSyntax)context.Node;

		var left = expression.Left;
		var right = expression.Right;

		if (!IsNull(left) && !IsNull(right))
			return;

		var value = IsNull(left) ? right : left;
		var type = context.SemanticModel.GetTypeInfo(value).Type;

		if (type is null)
			return;

		if (type.IsValueType && type.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T)
			return;

		var replacement = expression.IsKind(SyntaxKind.EqualsExpression)
			? "is null"
			: "is not null";

		var original = expression.IsKind(SyntaxKind.EqualsExpression)
			? "== null"
			: "!= null";

		context.ReportDiagnostic(Diagnostic.Create(Descriptor, expression.GetLocation(), replacement, original));
	}

	private static bool IsNull(ExpressionSyntax expression) => expression.IsKind(SyntaxKind.NullLiteralExpression);
}
