namespace OpenStackNetAnalyzers
{
    using System.Collections.Immutable;
    using System.Linq;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.Diagnostics;

    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class ServiceMethodReturnValueAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "ServiceMethodReturnValue";
        internal const string Title = "Service interface methods should return a Task<T> with a result that implements IHttpApiCall<T>";
        internal const string MessageFormat = "Service interface methods should return a Task<T> with a result that implements IHttpApiCall<T>";
        internal const string Category = "OpenStack.Maintainability";
        internal const string Description = "Service interface methods should return a Task<T> with a result that implements IHttpApiCall<T>";

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
                INamedTypeSymbol returnType = method.ReturnType as INamedTypeSymbol;
                if (returnType.IsTask() && returnType.IsGenericType && returnType.TypeArguments.Length == 1)
                {
                    INamedTypeSymbol genericArgument = returnType.TypeArguments[0] as INamedTypeSymbol;
                    if (genericArgument.IsDelegatingHttpApiCall())
                    {
                        // the method returns the expected type
                        continue;
                    }
                }

                ImmutableArray<Location> locations = method.Locations;
                context.ReportDiagnostic(Diagnostic.Create(Descriptor, locations.FirstOrDefault(), locations.Skip(1)));
            }
        }
    }
}
