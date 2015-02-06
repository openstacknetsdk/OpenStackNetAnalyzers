namespace OpenStackNetAnalyzers
{
    using System;
    using Microsoft.CodeAnalysis;

    internal static class TypeSymbolExtensions
    {
        private const string FullyQualifiedImmutableArrayT = "global::System.Collections.Immutable.ImmutableArray<T>";

        private const string FullyQualifiedTask = "global::System.Threading.Tasks.Task";

        public static bool IsNonNullableValueType(this ITypeSymbol type)
        {
            if (type == null)
                return false;

            if (!type.IsValueType)
                return false;

            ITypeSymbol originalDefinition = type.OriginalDefinition;
            if (originalDefinition == null)
                return false;

            if (originalDefinition.SpecialType == SpecialType.System_Nullable_T
                || originalDefinition.SpecialType == SpecialType.System_Enum
                || originalDefinition.SpecialType == SpecialType.System_ValueType)
            {
                return false;
            }

            return true;
        }

        public static bool IsImmutableArray(this ITypeSymbol type)
        {
            if (type == null)
                return false;

            if (!type.IsValueType)
                return false;

            INamedTypeSymbol namedType = type as INamedTypeSymbol;
            if (namedType == null || !namedType.IsGenericType)
                return false;

            INamedTypeSymbol originalDefinition = namedType.OriginalDefinition;
            if (originalDefinition == null)
                return false;

            return string.Equals(FullyQualifiedImmutableArrayT, originalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), StringComparison.Ordinal);
        }

        public static bool IsTask(this INamedTypeSymbol symbol)
        {
            while (symbol != null && symbol.SpecialType != SpecialType.System_Object)
            {
                if (!symbol.IsGenericType)
                {
                    if (string.Equals(FullyQualifiedTask, symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), StringComparison.Ordinal))
                        return true;
                }

                symbol = symbol.BaseType;
            }

            return false;
        }
    }
}
