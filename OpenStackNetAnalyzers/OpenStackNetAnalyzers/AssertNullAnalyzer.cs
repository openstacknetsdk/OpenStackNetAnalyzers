namespace OpenStackNetAnalyzers
{
    using System;
    using System.Collections.Immutable;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.CSharp.Syntax;
    using Microsoft.CodeAnalysis.Diagnostics;

    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class AssertNullAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "AssertNull";
        internal const string Title = "Assert.IsNull and Assert.IsNotNull should only be used with nullable types";
        internal const string MessageFormat = "'Assert.{0}' should not be used with the value type '{1}'";
        internal const string Category = "OpenStack.Maintainability";
        internal const string Description = "Assert.IsNull and Assert.IsNotNull should only be used with nullable types";

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
            context.RegisterSyntaxNodeAction(HandleInvocationExpression, SyntaxKind.InvocationExpression);
        }

        private void HandleInvocationExpression(SyntaxNodeAnalysisContext context)
        {
            InvocationExpressionSyntax syntax = (InvocationExpressionSyntax)context.Node;
            if (syntax.Expression == null || !(syntax.ArgumentList?.Arguments.Count > 0))
                return;

            SymbolInfo symbolInfo = context.SemanticModel.GetSymbolInfo(syntax.Expression, context.CancellationToken);
            IMethodSymbol methodSymbol = symbolInfo.Symbol as IMethodSymbol;
            if (methodSymbol == null)
                return;

            if (!string.Equals("IsNotNull", methodSymbol.Name, StringComparison.Ordinal)
                && !string.Equals("IsNull", methodSymbol.Name, StringComparison.Ordinal))
            {
                return;
            }

            var containingType = methodSymbol.ContainingType;
            if (!string.Equals("Assert", containingType?.Name, StringComparison.Ordinal))
                return;

            if (syntax.ArgumentList.Arguments[0].NameColon != null)
                return;

            ExpressionSyntax argumentExpression = syntax.ArgumentList.Arguments[0]?.Expression;
            if (argumentExpression == null)
                return;

            TypeInfo typeInfo = context.SemanticModel.GetTypeInfo(argumentExpression);
            INamedTypeSymbol namedType = typeInfo.Type as INamedTypeSymbol;
            if (namedType == null)
                return;

            if (!namedType.IsValueType)
            {
                // don't report the diagnostic for reference types
                return;
            }

            INamedTypeSymbol originalDefinition = namedType.OriginalDefinition;
            if (originalDefinition == null
                || originalDefinition.SpecialType == SpecialType.System_Nullable_T
                || originalDefinition.SpecialType == SpecialType.System_ValueType
                || originalDefinition.SpecialType == SpecialType.System_Enum)
            {
                // don't report the diagnostic for "special" and nullable value types
                return;
            }

            string typeName = namedType.ToMinimalDisplayString(context.SemanticModel, argumentExpression.SpanStart, SymbolDisplayFormat.CSharpErrorMessageFormat);
            context.ReportDiagnostic(Diagnostic.Create(Descriptor, syntax.GetLocation(), methodSymbol.Name, typeName));
        }
    }
}
