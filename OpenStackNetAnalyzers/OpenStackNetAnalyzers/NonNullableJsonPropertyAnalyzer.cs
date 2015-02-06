namespace OpenStackNetAnalyzers
{
    using System;
    using System.Collections.Immutable;
    using System.Linq;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.Diagnostics;

    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class NonNullableJsonPropertyAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "NonNullableJsonProperty";
        internal const string Title = "JSON properties should use nullable types";
        internal const string MessageFormat = "Members with the [JsonProperty] attribute should not use non-nullable value types";
        internal const string Category = "OpenStack.Maintainability";
        internal const string Description = "JSON properties should use nullable types";

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
            context.RegisterSymbolAction(HandleField, SymbolKind.Field);
            context.RegisterSymbolAction(HandleParameter, SymbolKind.Parameter);
            context.RegisterSymbolAction(HandleProperty, SymbolKind.Property);
        }

        private void HandleField(SymbolAnalysisContext context)
        {
            IFieldSymbol symbol = (IFieldSymbol)context.Symbol;
            AnalyzeSymbol(context, symbol, symbol.Type);
        }

        private void HandleParameter(SymbolAnalysisContext context)
        {
            IParameterSymbol symbol = (IParameterSymbol)context.Symbol;
            AnalyzeSymbol(context, symbol, symbol.Type);
        }

        private void HandleProperty(SymbolAnalysisContext context)
        {
            IPropertySymbol symbol = (IPropertySymbol)context.Symbol;
            AnalyzeSymbol(context, symbol, symbol.Type);
        }

        private void AnalyzeSymbol(SymbolAnalysisContext context, ISymbol symbol, ITypeSymbol type)
        {
            if (!type.IsNonNullableValueType())
                return;

            if (type.IsImmutableArray())
            {
                // special-case this value type
                return;
            }

            AttributeData jsonPropertyAttribute = GetJsonPropertyAttributeData(symbol.GetAttributes());
            if (jsonPropertyAttribute == null)
                return;

            ImmutableArray<Location> locations = symbol.Locations;
            context.ReportDiagnostic(Diagnostic.Create(Descriptor, locations.FirstOrDefault(), additionalLocations: locations.Skip(1)));
        }

        private AttributeData GetJsonPropertyAttributeData(ImmutableArray<AttributeData> attributes)
        {
            if (attributes.IsDefaultOrEmpty)
                return null;

            foreach (AttributeData attribute in attributes)
            {
                string fullName = attribute.AttributeClass.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                if (string.Equals("global::Newtonsoft.Json.JsonPropertyAttribute", fullName, StringComparison.Ordinal))
                    return attribute;
            }

            return null;
        }
    }
}
