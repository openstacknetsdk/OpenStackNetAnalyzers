namespace OpenStackNetAnalyzers
{
    using System;
    using System.Collections.Immutable;
    using System.Linq;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.Diagnostics;

    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class DocumentDelegatingApiCallAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "DocumentDelegatingApiCall";
        internal const string Title = "Document delegating HTTP API call";
        internal const string MessageFormat = "Document delegating HTTP API call";
        internal const string Category = "OpenStack.Documentation";
        internal const string Description = "Document delegating HTTP API call";

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

            if (!IsDelegatingHttpApiCall(context, symbol))
                return;

            if (!string.IsNullOrEmpty(symbol.GetDocumentationCommentXml(cancellationToken: context.CancellationToken)))
                return;

            var locations = symbol.Locations;
            context.ReportDiagnostic(Diagnostic.Create(Descriptor, locations.FirstOrDefault(), locations.Skip(1)));
        }

        private bool IsDelegatingHttpApiCall(SymbolAnalysisContext context, INamedTypeSymbol symbol)
        {
            while (symbol != null && symbol.SpecialType != SpecialType.System_Object)
            {
                if (symbol.IsGenericType)
                {
                    var originalDefinition = symbol.OriginalDefinition;
                    string fullyQualifiedName = originalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    if (string.Equals("global::OpenStack.Net.DelegatingHttpApiCall<T>", fullyQualifiedName, StringComparison.Ordinal))
                        return true;
                }

                symbol = symbol.BaseType;
            }

            return false;
        }
    }
}
