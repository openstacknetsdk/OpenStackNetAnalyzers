namespace OpenStackNetAnalyzers
{
    using System;
    using System.Collections.Immutable;
    using System.Linq;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.CSharp.Syntax;
    using Microsoft.CodeAnalysis.Diagnostics;

    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class ImplementBuilderPatternAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "ImplementBuilderPattern";
        internal const string Title = "Implement builder pattern (refactoring)";
        internal const string MessageFormat = "ImplementBuilderPattern";
        internal const string Category = "OpenStack.Refactoring";
        internal const string Description = "Implement builder pattern (refactoring)";

        private static DiagnosticDescriptor Descriptor =
            new DiagnosticDescriptor(DiagnosticId, Title, MessageFormat, Category, DiagnosticSeverity.Hidden, isEnabledByDefault: true, description: Description);

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
            context.RegisterSymbolAction(HandleNamedType, SymbolKind.NamedType);
        }

        private void HandleNamedType(SymbolAnalysisContext context)
        {
            INamedTypeSymbol symbol = (INamedTypeSymbol)context.Symbol;
            if (symbol.TypeKind != TypeKind.Class)
                return;

            if (!symbol.IsExtensibleJsonObject())
                return;

            foreach (var propertySymbol in symbol.GetMembers().OfType<IPropertySymbol>())
            {
                if (propertySymbol.SetMethod != null)
                    continue;

                var locations = propertySymbol.Locations;
                if (locations.IsDefaultOrEmpty)
                    continue;

                var tree = locations[0].SourceTree;
                if (tree == null)
                    continue;

                var root = tree.GetRoot(context.CancellationToken);
                var node = root.FindNode(locations[0].SourceSpan, getInnermostNodeForTie: true);
                var propertySyntax = node.FirstAncestorOrSelf<PropertyDeclarationSyntax>();
                var getter = propertySyntax.AccessorList?.Accessors.FirstOrDefault(i => i.Keyword.IsKind(SyntaxKind.GetKeyword));
                if (getter?.Body?.Statements.Count == 1)
                {
                    ReturnStatementSyntax returnStatement = getter?.Body?.Statements.FirstOrDefault() as ReturnStatementSyntax;
                    ExpressionSyntax returnExpression = returnStatement.Expression;
                    if (returnExpression.IsKind(SyntaxKind.SimpleMemberAccessExpression))
                    {
                        MemberAccessExpressionSyntax memberAccess = (MemberAccessExpressionSyntax)returnExpression;
                        if (!memberAccess.Expression.IsKind(SyntaxKind.ThisExpression))
                            continue;
                    }

                    SymbolInfo symbolInfo = context.Compilation.GetSemanticModel(tree).GetSymbolInfo(returnExpression, context.CancellationToken);
                    IFieldSymbol fieldSymbol = symbolInfo.Symbol as IFieldSymbol;
                    if (fieldSymbol.ContainingType != propertySymbol.ContainingType)
                        continue;

                    context.ReportDiagnostic(Diagnostic.Create(Descriptor, locations.FirstOrDefault(), locations.Skip(1)));
                }
            }
        }
    }
}
