namespace OpenStackNetAnalyzers
{
    using System;
    using System.Collections.Immutable;
    using System.Linq;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.Diagnostics;

    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class ServiceMethodPrepareAsyncAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "ServiceMethodPrepareAsync";
        internal const string Title = "Service methods should be named Prepare{Name}Async";
        internal const string MessageFormat = "Service methods should be named Prepare{Name}Async";
        internal const string Category = "OpenStack.Maintainability";
        internal const string Description = "Service methods should be named Prepare{Name}Async";

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
            if (!symbol.IsHttpServiceInterface())
                return;

            foreach (IMethodSymbol method in symbol.GetMembers().OfType<IMethodSymbol>())
            {
                if (string.IsNullOrEmpty(method.Name))
                    continue;

                if (method.Name.StartsWith("Prepare", StringComparison.Ordinal) && method.Name.EndsWith("Async", StringComparison.Ordinal))
                {
                    // TODO check letter following 'Prepare'
                    continue;
                }

                ImmutableArray<Location> locations = method.Locations;
                context.ReportDiagnostic(Diagnostic.Create(Descriptor, locations.FirstOrDefault(), locations.Skip(1)));
            }
        }
    }
}
