namespace OpenStackNetAnalyzers
{
    using System;
    using Microsoft.CodeAnalysis;

    internal static class SdkTypeSymbolExtensions
    {
        private const string FullyQualifiedExtensibleJsonObject = "global::OpenStack.ObjectModel.ExtensibleJsonObject";

        public static bool IsExtensibleJsonObject(this INamedTypeSymbol symbol)
        {
            while (symbol != null && symbol.SpecialType != SpecialType.System_Object)
            {
                if (string.Equals(FullyQualifiedExtensibleJsonObject, symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), StringComparison.Ordinal))
                    return true;

                symbol = symbol.BaseType;
            }

            return false;
        }
    }
}
