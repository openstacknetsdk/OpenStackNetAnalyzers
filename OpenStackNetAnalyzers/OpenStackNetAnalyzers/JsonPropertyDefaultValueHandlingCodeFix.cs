namespace OpenStackNetAnalyzers
{
    using System;
    using System.Collections.Immutable;
    using System.Composition;
    using System.Threading.Tasks;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CodeActions;
    using Microsoft.CodeAnalysis.CodeFixes;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.CSharp.Syntax;
    using Microsoft.CodeAnalysis.Simplification;

    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(JsonPropertyDefaultValueHandlingCodeFix))]
    [Shared]
    public class JsonPropertyDefaultValueHandlingCodeFix : CodeFixProvider
    {
        private static readonly ImmutableArray<string> _fixableDiagnostics =
            ImmutableArray.Create(JsonPropertyDefaultValueHandlingAnalyzer.DiagnosticId);

        public sealed override ImmutableArray<string> FixableDiagnosticIds => _fixableDiagnostics;

        public override FixAllProvider GetFixAllProvider()
        {
            return WellKnownFixAllProviders.BatchFixer;
        }

        public override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            foreach (var diagnostic in context.Diagnostics)
            {
                if (!string.Equals(diagnostic.Id, JsonPropertyDefaultValueHandlingAnalyzer.DiagnosticId, StringComparison.Ordinal))
                    continue;

                var documentRoot = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
                AttributeSyntax syntax = documentRoot.FindNode(diagnostic.Location.SourceSpan) as AttributeSyntax;
                if (syntax == null)
                    continue;

                ExpressionSyntax expression =
                    SyntaxFactory.ParseExpression("global::Newtonsoft.Json.DefaultValueHandling.Ignore")
                    .WithAdditionalAnnotations(Simplifier.Annotation);

                AttributeSyntax newAttribute = null;
                AttributeArgumentListSyntax argumentList = syntax.ArgumentList;
                if (argumentList != null)
                {
                    AttributeArgumentSyntax existingArgument = null;
                    foreach (var attributeArgument in argumentList.Arguments)
                    {
                        if (string.Equals("DefaultValueHandling", attributeArgument.NameEquals?.Name?.Identifier.ValueText, StringComparison.Ordinal))
                        {
                            existingArgument = attributeArgument;
                            break;
                        }
                    }

                    if (existingArgument != null)
                    {
                        var newArgument =
                            existingArgument
                            .WithExpression(expression)
                            .WithTriviaFrom(existingArgument)
                            .WithoutFormatting();

                        newAttribute = syntax.ReplaceNode(existingArgument, newArgument);
                    }
                }

                if (newAttribute == null)
                {
                    NameEqualsSyntax nameEquals = SyntaxFactory.NameEquals("DefaultValueHandling");

                    AttributeArgumentSyntax defaultValueArgument = SyntaxFactory.AttributeArgument(nameEquals, null, expression);

                    if (argumentList == null)
                        argumentList = SyntaxFactory.AttributeArgumentList();

                    argumentList = argumentList.AddArguments(defaultValueArgument);

                    newAttribute = syntax.WithArgumentList(argumentList);
                }

                SyntaxNode newRoot = documentRoot.ReplaceNode(syntax, newAttribute);
                Document newDocument = context.Document.WithSyntaxRoot(newRoot);
                context.RegisterCodeFix(CodeAction.Create("Add DefaultValueHandling.Ignore", _ => Task.FromResult(newDocument)), diagnostic);
            }
        }
    }
}
