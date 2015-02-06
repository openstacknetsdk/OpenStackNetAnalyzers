namespace OpenStackNetAnalyzers
{
    using Microsoft.CodeAnalysis;

    internal static class TypeSymbolExtensions
    {
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
    }
}
