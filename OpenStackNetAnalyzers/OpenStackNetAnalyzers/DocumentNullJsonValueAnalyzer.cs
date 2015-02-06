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
    public class DocumentNullJsonValueAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "DocumentNullJsonValue";
        internal const string Title = "Document null JSON value";
        internal const string MessageFormat = "The <value> documentation for JSON property with type '{0}' should include '<token>{1}</token>'";
        internal const string Category = "OpenStack.Documentation";
        internal const string Description = "The <value> documentation for JSON properties should include NullIfNotIncluded or DefaultArrayIfNotIncluded";

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
                if (!(getter?.Body?.Statements.Count == 1))
                    continue;

                ReturnStatementSyntax returnStatement = getter?.Body?.Statements.FirstOrDefault() as ReturnStatementSyntax;
                ExpressionSyntax returnExpression = returnStatement.Expression;
                if (returnExpression.IsKind(SyntaxKind.SimpleMemberAccessExpression))
                {
                    MemberAccessExpressionSyntax memberAccess = (MemberAccessExpressionSyntax)returnExpression;
                    if (!memberAccess.Expression.IsKind(SyntaxKind.ThisExpression))
                        continue;
                }

                SemanticModel semanticModel = context.Compilation.GetSemanticModel(tree);
                SymbolInfo symbolInfo = semanticModel.GetSymbolInfo(returnExpression, context.CancellationToken);
                IFieldSymbol fieldSymbol = symbolInfo.Symbol as IFieldSymbol;
                if (fieldSymbol.ContainingType != propertySymbol.ContainingType)
                    continue;

                if (!fieldSymbol.GetAttributes().Any(i => string.Equals("global::Newtonsoft.Json.JsonPropertyAttribute", i.AttributeClass.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), StringComparison.Ordinal)))
                    continue;

                DocumentationCommentTriviaSyntax documentationCommentSyntax = propertySyntax.GetDocumentationCommentTriviaSyntax();
                if (documentationCommentSyntax == null)
                    continue;

                XmlNodeSyntax valueNode = documentationCommentSyntax.Content.GetFirstXmlElement("value");
                if (valueNode == null)
                    continue;

                string defaultValueToken = "NullIfNotIncluded";
                ITypeSymbol propertyType = propertySymbol.Type;
                if (propertyType.IsImmutableArray())
                    defaultValueToken = "DefaultArrayIfNotIncluded";

                XmlElementSyntax valueElementSyntax = valueNode as XmlElementSyntax;
                if (valueElementSyntax != null)
                {
                    bool foundToken = false;
                    foreach (XmlElementSyntax valueElementChild in valueElementSyntax.Content.OfType<XmlElementSyntax>())
                    {
                        if (!string.Equals("token", valueElementChild.StartTag?.Name?.ToString(), StringComparison.Ordinal))
                            continue;

                        if (!string.Equals(defaultValueToken, valueElementChild.Content.ToFullString()))
                            continue;

                        foundToken = true;
                        break;
                    }

                    if (foundToken)
                        continue;
                }

                string propertyTypeName = propertyType.ToMinimalDisplayString(semanticModel, returnExpression.SpanStart, SymbolDisplayFormat.CSharpErrorMessageFormat);
                context.ReportDiagnostic(Diagnostic.Create(Descriptor, valueNode.GetLocation(), propertyTypeName, defaultValueToken));
            }
        }
    }
}
