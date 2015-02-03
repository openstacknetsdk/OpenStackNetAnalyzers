namespace OpenStackNetAnalyzers
{
    using System;
    using System.Collections.Immutable;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.CSharp.Syntax;
    using Microsoft.CodeAnalysis.Diagnostics;

    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class JsonObjectOptInAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "JsonObjectOptIn";
        internal const string Title = "JSON object should use opt-in serialization";
        internal const string MessageFormat = "[JsonObject] attribute should specify 'MemberSerialization.OptIn'";
        internal const string Category = "OpenStack.Maintainability";
        internal const string Description = "JSON objects should specify MemberSerialization.OptIn";

        private static DiagnosticDescriptor Descriptor =
            new DiagnosticDescriptor(DiagnosticId, Title, MessageFormat, Category, DiagnosticSeverity.Error, isEnabledByDefault: true, description: Description);

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

            if (!string.Equals("JsonObjectAttribute", methodSymbol.ContainingType?.Name, StringComparison.Ordinal))
                return;

            if (syntax.ArgumentList?.Arguments.Count > 0)
            {
                bool? isOptIn = null;

                // check property assignments first, because they override the first argument when both are specified
                foreach (var attributeArgumentSyntax in syntax.ArgumentList.Arguments)
                {
                    if (attributeArgumentSyntax.NameEquals == null)
                        continue;

                    if (!string.Equals("MemberSerialization", attributeArgumentSyntax.NameEquals.Name?.ToString(), StringComparison.Ordinal))
                        continue;

                    if (IsMemberSerializationOptIn(context, attributeArgumentSyntax.Expression))
                        return;

                    isOptIn = false;
                }

                if (!isOptIn.HasValue)
                {
                    AttributeArgumentSyntax firstArgument = syntax.ArgumentList.Arguments[0];
                    if (firstArgument?.Expression != null
                        && (firstArgument.NameColon == null || string.Equals("memberSerialization", firstArgument.NameColon?.Name?.ToString(), StringComparison.Ordinal))
                        && firstArgument.NameEquals == null)
                    {
                        if (IsMemberSerializationOptIn(context, firstArgument?.Expression))
                            return;
                    }
                }
            }

            context.ReportDiagnostic(Diagnostic.Create(Descriptor, syntax.GetLocation()));
        }

        private bool IsMemberSerializationOptIn(SyntaxNodeAnalysisContext context, ExpressionSyntax expression)
        {
            if (expression == null)
                return false;

            SymbolInfo argumentSymbolInfo = context.SemanticModel.GetSymbolInfo(expression, context.CancellationToken);
            IFieldSymbol fieldSymbol = argumentSymbolInfo.Symbol as IFieldSymbol;
            if (string.Equals("OptIn", fieldSymbol?.Name, StringComparison.Ordinal)
                && string.Equals("MemberSerialization", fieldSymbol.ContainingType?.Name, StringComparison.Ordinal))
            {
                return true;
            }

            return false;
        }
    }
}
