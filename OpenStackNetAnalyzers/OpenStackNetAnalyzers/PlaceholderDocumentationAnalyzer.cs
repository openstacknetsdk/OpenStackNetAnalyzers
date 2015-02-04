namespace OpenStackNetAnalyzers
{
    using System;
    using System.Collections.Immutable;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.CSharp.Syntax;
    using Microsoft.CodeAnalysis.Diagnostics;

    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class PlaceholderDocumentationAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "PlaceholderDocumentation";
        internal const string Title = "Do not use placeholders in documentation";
        internal const string MessageFormat = "Do not use placeholders in documentation";
        internal const string Category = "OpenStack.Documentation";
        internal const string Description = "Do not use placeholders in documentation";

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
            context.RegisterSyntaxNodeAction(HandleXmlElement, SyntaxKind.XmlElement);
            context.RegisterSyntaxNodeAction(HandleXmlEmptyElement, SyntaxKind.XmlEmptyElement);
        }

        private void HandleXmlElement(SyntaxNodeAnalysisContext context)
        {
            XmlElementSyntax syntax = (XmlElementSyntax)context.Node;
            if (!string.Equals("placeholder", syntax.StartTag?.Name?.ToString(), StringComparison.Ordinal))
                return;

            context.ReportDiagnostic(Diagnostic.Create(Descriptor, syntax.GetLocation()));
        }

        private void HandleXmlEmptyElement(SyntaxNodeAnalysisContext context)
        {
            XmlEmptyElementSyntax syntax = (XmlEmptyElementSyntax)context.Node;
            if (!string.Equals("placeholder", syntax.Name?.ToString(), StringComparison.Ordinal))
                return;

            context.ReportDiagnostic(Diagnostic.Create(Descriptor, syntax.GetLocation()));
        }
    }
}
