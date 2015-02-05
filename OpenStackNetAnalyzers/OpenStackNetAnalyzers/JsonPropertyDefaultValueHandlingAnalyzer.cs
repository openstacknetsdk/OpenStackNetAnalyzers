namespace OpenStackNetAnalyzers
{
    using System;
    using System.Collections.Immutable;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.CSharp.Syntax;
    using Microsoft.CodeAnalysis.Diagnostics;

    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class JsonPropertyDefaultValueHandlingAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "JsonPropertyDefaultValueHandling";
        internal const string Title = "JSON properties should use DefaultValueHandling.Ignore";
        internal const string MessageFormat = "[JsonProperty] attribute should specify 'DefaultValueHandling.Ignore'";
        internal const string Category = "OpenStack.Maintainability";
        internal const string Description = "JSON properties should specify DefaultValueHandling.Ignore";

        private static DiagnosticDescriptor Descriptor =
            new DiagnosticDescriptor(DiagnosticId, Title, MessageFormat, Category, DiagnosticSeverity.Warning, isEnabledByDefault: true, description: Description);

        private static readonly ImmutableArray<DiagnosticDescriptor> _supportedDiagnostics =
            ImmutableArray.Create(Descriptor);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        {
            get
            {
                return _supportedDiagnostics;
            }
        }

        public override void Initialize(AnalysisContext context)
        {
            context.RegisterSyntaxNodeAction(HandleAttribute, SyntaxKind.Attribute);
        }

        private void HandleAttribute(SyntaxNodeAnalysisContext context)
        {
            AttributeSyntax syntax = (AttributeSyntax)context.Node;
            SymbolInfo symbolInfo = context.SemanticModel.GetSymbolInfo(syntax, context.CancellationToken);
            IMethodSymbol methodSymbol = symbolInfo.Symbol as IMethodSymbol;
            if (methodSymbol == null)
                return;

            if (!string.Equals("JsonPropertyAttribute", methodSymbol.ContainingType?.Name, StringComparison.Ordinal))
                return;

            if (syntax.ArgumentList?.Arguments.Count > 0)
            {
                foreach (var attributeArgumentSyntax in syntax.ArgumentList.Arguments)
                {
                    if (attributeArgumentSyntax.NameEquals == null)
                        continue;

                    if (!string.Equals("DefaultValueHandling", attributeArgumentSyntax.NameEquals.Name?.ToString(), StringComparison.Ordinal))
                        continue;

                    if (IsDefaultValueHandlingIgnore(context, attributeArgumentSyntax.Expression))
                        return;
                }
            }

            context.ReportDiagnostic(Diagnostic.Create(Descriptor, syntax.GetLocation()));
        }

        private bool IsDefaultValueHandlingIgnore(SyntaxNodeAnalysisContext context, ExpressionSyntax expression)
        {
            if (expression == null)
                return false;

            SymbolInfo argumentSymbolInfo = context.SemanticModel.GetSymbolInfo(expression, context.CancellationToken);
            IFieldSymbol fieldSymbol = argumentSymbolInfo.Symbol as IFieldSymbol;
            if (string.Equals("Ignore", fieldSymbol?.Name, StringComparison.Ordinal)
                && string.Equals("DefaultValueHandling", fieldSymbol.ContainingType?.Name, StringComparison.Ordinal))
            {
                return true;
            }

            return false;
        }
    }
}
