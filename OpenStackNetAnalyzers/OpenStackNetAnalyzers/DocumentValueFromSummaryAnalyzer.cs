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
    public class DocumentValueFromSummaryAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "DocumentValueFromSummary";
        internal const string Title = "JSON property values should be documented";
        internal const string MessageFormat = "JSON property values should be documented";
        internal const string Category = "OpenStack.Documentation";
        internal const string Description = "JSON property values should be documented";

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
            context.RegisterSyntaxNodeAction(HandlePropertyDeclaration, SyntaxKind.PropertyDeclaration);
        }

        private void HandlePropertyDeclaration(SyntaxNodeAnalysisContext context)
        {
            PropertyDeclarationSyntax syntax = (PropertyDeclarationSyntax)context.Node;
            if (syntax.Identifier.IsMissing)
                return;

            if (!syntax.Modifiers.Any(SyntaxKind.PublicKeyword) && !syntax.Modifiers.Any(SyntaxKind.ProtectedKeyword))
                return;

            SemanticModel semanticModel = context.SemanticModel;
            IPropertySymbol propertySymbol = semanticModel.GetDeclaredSymbol(syntax, context.CancellationToken);
            if (propertySymbol == null)
                return;

            INamedTypeSymbol declaringType = propertySymbol.ContainingType;
            if (!declaringType.IsExtensibleJsonObject())
                return;

            DocumentationCommentTriviaSyntax documentationTriviaSyntax = syntax.GetDocumentationCommentTriviaSyntax();
            if (documentationTriviaSyntax == null)
                return;

            if (documentationTriviaSyntax.Content.GetXmlElements("value").Any())
                return;

            context.ReportDiagnostic(Diagnostic.Create(Descriptor, syntax.Identifier.GetLocation()));
        }
    }
}
