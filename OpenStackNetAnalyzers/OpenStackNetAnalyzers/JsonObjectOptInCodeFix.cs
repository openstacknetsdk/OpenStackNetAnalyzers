namespace OpenStackNetAnalyzers
{
    using System;
    using System.Collections.Immutable;
    using System.Composition;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CodeActions;
    using Microsoft.CodeAnalysis.CodeFixes;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.CSharp.Syntax;
    using Microsoft.CodeAnalysis.Simplification;

    [ExportCodeFixProvider(nameof(JsonObjectOptInCodeFix), LanguageNames.CSharp)]
    [Shared]
    public class JsonObjectOptInCodeFix : CodeFixProvider
    {
        private static readonly ImmutableArray<string> _fixableDiagnostics =
            ImmutableArray.Create(JsonObjectOptInAnalyzer.DiagnosticId);

        public sealed override ImmutableArray<string> FixableDiagnosticIds => _fixableDiagnostics;

        public override FixAllProvider GetFixAllProvider()
        {
            return WellKnownFixAllProviders.BatchFixer;
        }

        public override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            foreach (var diagnostic in context.Diagnostics)
            {
                if (!string.Equals(diagnostic.Id, JsonObjectOptInAnalyzer.DiagnosticId, StringComparison.Ordinal))
                    continue;

                var documentRoot = await context.Document.GetSyntaxRootAsync(context.CancellationToken);
                AttributeSyntax syntax = documentRoot.FindNode(diagnostic.Location.SourceSpan) as AttributeSyntax;
                if (syntax == null)
                    continue;

                ExpressionSyntax expression =
                    SyntaxFactory.ParseExpression("global::Newtonsoft.Json.MemberSerialization.OptIn")
                    .WithAdditionalAnnotations(Simplifier.Annotation);

                AttributeSyntax newAttribute = null;
                AttributeArgumentListSyntax argumentList = syntax.ArgumentList;
                if (argumentList != null)
                {
                    AttributeArgumentSyntax existingArgument = null;
                    foreach (var attributeArgument in argumentList.Arguments)
                    {
                        if (string.Equals("MemberSerialization", attributeArgument.NameEquals?.Name?.Identifier.ValueText, StringComparison.Ordinal))
                        {
                            existingArgument = attributeArgument;
                            break;
                        }
                    }

                    if (existingArgument == null)
                    {
                        SemanticModel semanticModel = null;
                        foreach (var attributeArgument in argumentList.Arguments)
                        {
                            // this time only interested in arguments (no NameEquals clause)
                            if (attributeArgument.NameEquals != null)
                                continue;

                            if (string.Equals("memberSerialization", attributeArgument.NameColon?.Name?.Identifier.ValueText, StringComparison.Ordinal))
                            {
                                existingArgument = attributeArgument;
                                break;
                            }

                            if (attributeArgument.NameColon != null)
                                continue;

                            if (semanticModel == null)
                                semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken);

                            if (IsMemberSerializationArgument(semanticModel, attributeArgument.Expression, context.CancellationToken))
                            {
                                existingArgument = attributeArgument;
                                break;
                            }
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
                    if (argumentList == null)
                        argumentList = SyntaxFactory.AttributeArgumentList();

                    NameEqualsSyntax nameEquals;
                    if (argumentList.Arguments.Any(argument => argument.NameEquals == null))
                        nameEquals = SyntaxFactory.NameEquals("MemberSerialization");
                    else
                        nameEquals = null;

                    AttributeArgumentSyntax defaultValueArgument = SyntaxFactory.AttributeArgument(nameEquals, null, expression);

                    argumentList = argumentList.AddArguments(defaultValueArgument);
                    newAttribute = syntax.WithArgumentList(argumentList);
                }

                SyntaxNode newRoot = documentRoot.ReplaceNode(syntax, newAttribute);
                Document newDocument = context.Document.WithSyntaxRoot(newRoot);
                context.RegisterCodeFix(CodeAction.Create("Add MemberSerialization.OptIn", _ => Task.FromResult(newDocument)), diagnostic);
            }
        }

        private bool IsMemberSerializationArgument(SemanticModel semanticModel, ExpressionSyntax expression, CancellationToken cancellationToken)
        {
            if (expression == null)
                return false;

            SymbolInfo argumentSymbolInfo = semanticModel.GetSymbolInfo(expression, cancellationToken);
            IFieldSymbol fieldSymbol = argumentSymbolInfo.Symbol as IFieldSymbol;
            if (string.Equals("MemberSerialization", fieldSymbol?.ContainingType?.Name, StringComparison.Ordinal))
            {
                return true;
            }

            return false;
        }
    }
}
