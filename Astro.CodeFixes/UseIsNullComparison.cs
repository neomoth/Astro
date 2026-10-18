using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Astro.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Astro.Roslyn.CodeFixes;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(UseIsNullComparison))]
internal class UseIsNullComparison : CodeFixProvider
{
	public override ImmutableArray<string> FixableDiagnosticIds => [DiagnosticsCodes.UseIsNullComparison];

	public sealed override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

	public override async Task RegisterCodeFixesAsync(CodeFixContext context)
	{
		var diagnostic = context.Diagnostics[0];
		var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken);

		// innermost so wrapper nodes sharing the same span (e.g. ArgumentSyntax in Debug.Assert(x != null)) aren't returned instead
		var expression = root?.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true)
			.FirstAncestorOrSelf<BinaryExpressionSyntax>();
		if (expression is null) return;

		var title = expression.IsKind(SyntaxKind.EqualsExpression)
			? "Use 'is null'"
			: "Use 'is not null'";

		context.RegisterCodeFix(
			CodeAction.Create(title, token => ConvertToPatternAsync(context.Document, expression, token),
				equivalenceKey: title), diagnostic);
	}

	private static async Task<Document> ConvertToPatternAsync(
		Document document,
		BinaryExpressionSyntax expression,
		CancellationToken cancellationToken)
	{
		var nullIsLeft =
			expression.Left.IsKind(SyntaxKind.NullLiteralExpression);

		var value = nullIsLeft
			? expression.Right
			: expression.Left;

		var pattern = expression.IsKind(SyntaxKind.EqualsExpression)
			? SyntaxFactory.IsPatternExpression(
				value,
				SyntaxFactory.ConstantPattern(
					SyntaxFactory.LiteralExpression(
						SyntaxKind.NullLiteralExpression)))
			: SyntaxFactory.IsPatternExpression(
				value,
				SyntaxFactory.UnaryPattern(
					SyntaxFactory.Token(SyntaxKind.NotKeyword),
					SyntaxFactory.ConstantPattern(
						SyntaxFactory.LiteralExpression(
							SyntaxKind.NullLiteralExpression))));

		pattern = pattern
			.WithTriviaFrom(expression);

		return document.WithSyntaxRoot(
			(await document.GetSyntaxRootAsync(cancellationToken))!
			.ReplaceNode(expression, pattern));
	}
}
