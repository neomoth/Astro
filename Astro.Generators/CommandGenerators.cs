using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Astro.Generators
{
	[Generator]
	public class CommandGenerators : IIncrementalGenerator
	{
		private static readonly DiagnosticDescriptor InvalidCommand = new DiagnosticDescriptor(
			"ASTR0001",
			"Root commands and Subcommands must inherit ICommand",
			"The type {0} is marked with [{1}] but does not implement ICommand",
			"Commands",
			DiagnosticSeverity.Error,
			true);

		private static readonly DiagnosticDescriptor InvalidCommandGroup = new DiagnosticDescriptor(
			"ASTR0002",
			"Command groups must inherit ICommandGroup",
			"The type {0} is marked with [{1}] but does not implement ICommandGroup",
			"Commands",
			DiagnosticSeverity.Error,
			true);

		private static readonly DiagnosticDescriptor InvalidSubcommandParent = new DiagnosticDescriptor(
			"ASTR0004",
			"Subcommands must be parented to a command group or root command",
			"The type {0} must have it's parent property set to a [{1}] or [{2}] instance",
			"Commands",
			DiagnosticSeverity.Error,
			true);

		private static readonly DiagnosticDescriptor InvalidCommandGroupParent = new DiagnosticDescriptor(
			"ASTR0003",
			"Command groups must be parented to a Root Command",
			"The type {0} must have it's parent property set to a [{1}] instance",
			"Commands",
			DiagnosticSeverity.Error,
			true);

		public void Initialize(IncrementalGeneratorInitializationContext context)
		{
			var classDeclarations = context.SyntaxProvider
				.CreateSyntaxProvider(predicate: (s, _) => s is ClassDeclarationSyntax,
					transform: (ctx, _) => ctx);

			context.RegisterSourceOutput(classDeclarations, (spc, ctx) =>
			{
				var model = ctx.SemanticModel;
				var classDecl = (ClassDeclarationSyntax)ctx.Node;
				if (!(model.GetDeclaredSymbol(classDecl) is INamedTypeSymbol symbol)) return;

				var attrs = symbol.GetAttributes();
				foreach (var attrName in attrs.Select(attr => attr.AttributeClass?.Name))
				{
					switch (attrName)
					{
						case "RootCommandAttribute":
						case "SubCommandAttribute":
							if (!InheritsFrom(symbol, "ICommand"))
							{
								spc.ReportDiagnostic(Diagnostic.Create(InvalidCommand,
									classDecl.Identifier.GetLocation(), symbol.Name, attrName));
							}

							if (attrName == "SubCommandAttribute")
								ValidateSubCommandParent(symbol, spc, classDecl);
							break;
						case "CommandGroupAttribute":
							ValidateCommandGroup(symbol, spc, classDecl);
							break;
					}
				}
			});
		}

		private static bool InheritsFrom(INamedTypeSymbol symbol, string iName)
		{
			return symbol.AllInterfaces.Any(i => i.Name == iName);
		}

		private static void ValidateCommandGroup(INamedTypeSymbol symbol, SourceProductionContext context,
			ClassDeclarationSyntax decl)
		{
			if (!InheritsFrom(symbol, "ICommandGroup"))
			{
				context.ReportDiagnostic(Diagnostic.Create(InvalidCommandGroup, decl.Identifier.GetLocation(),
					symbol.Name, "CommandGroup"));
			}

			var attr = symbol.GetAttributes()
				.FirstOrDefault(a => a.AttributeClass?.Name == "CommandGroupAttribute");

			if (attr == null) return;

			var parentArg = attr.ConstructorArguments.FirstOrDefault();
			var parentType = parentArg.Kind == TypedConstantKind.Type ? parentArg.Value as INamedTypeSymbol : null;

			if (parentType == null) return;
			
			if (!parentType.GetAttributes().Any(a => a.AttributeClass?.Name == "RootCommandAttribute"))
			{
				context.ReportDiagnostic(Diagnostic.Create(InvalidCommandGroupParent, decl.Identifier.GetLocation(),
					symbol.Name, "RootCommand"));
			}
		}

		private static void ValidateSubCommandParent(INamedTypeSymbol symbol, SourceProductionContext context,
			ClassDeclarationSyntax decl)
		{
			var attr = symbol.GetAttributes()
				.FirstOrDefault(a => a.AttributeClass?.Name == "SubCommandAttribute");

			if (attr == null) return;

			var parentArg = attr.ConstructorArguments.FirstOrDefault();
			var parentType = parentArg.Kind == TypedConstantKind.Type ? parentArg.Value as INamedTypeSymbol : null;

			if (parentType == null) return;

			var hasValidParent = parentType.GetAttributes().Any(a =>
				a.AttributeClass?.Name == "RootCommandAttribute" || a.AttributeClass?.Name == "CommandGroupAttribute");

			if (!hasValidParent)
			{
				context.ReportDiagnostic(Diagnostic.Create(InvalidSubcommandParent, decl.Identifier.GetLocation(),
					symbol.Name, "RootCommand", "CommandGroup"));
			}
		}
	}
}