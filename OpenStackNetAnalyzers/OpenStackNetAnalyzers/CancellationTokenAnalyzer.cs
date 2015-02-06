namespace OpenStackNetAnalyzers
{
    using System.Collections.Immutable;
    using System.Linq;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.Diagnostics;

    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class CancellationTokenAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "CancellationToken";
        internal const string Title = "Asynchronous methods should accept a CancellationToken";
        internal const string MessageFormat = "Asynchronous methods should accept a CancellationToken";
        internal const string Category = "OpenStack.Maintainability";
        internal const string Description = "Asynchronous methods should accept a CancellationToken";

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
            context.RegisterSymbolAction(HandleMethod, SymbolKind.Method);
        }

        private void HandleMethod(SymbolAnalysisContext context)
        {
            IMethodSymbol method = (IMethodSymbol)context.Symbol;
            if (!method.Name.EndsWith("Async"))
                return;

            if (!method.ReturnType.IsTask())
                return;

            foreach (IParameterSymbol parameter in method.Parameters)
            {
                if (parameter.Type.IsCancellationToken())
                    return;
            }

            ImmutableArray<Location> locations = method.Locations;
            context.ReportDiagnostic(Diagnostic.Create(Descriptor, locations.FirstOrDefault(), locations.Skip(1)));
        }
    }
}
