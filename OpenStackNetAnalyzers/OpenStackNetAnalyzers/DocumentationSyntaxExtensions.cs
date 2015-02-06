namespace OpenStackNetAnalyzers
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp.Syntax;

    internal static class DocumentationSyntaxExtensions
    {
        public static DocumentationCommentTriviaSyntax GetDocumentationCommentTriviaSyntax(this SyntaxNode node)
        {
            if (node == null)
                return null;

            return node
                .GetLeadingTrivia()
                .Select(i => i.GetStructure())
                .OfType<DocumentationCommentTriviaSyntax>()
                .FirstOrDefault();
        }

        public static XmlNodeSyntax GetFirstXmlElement(this SyntaxList<XmlNodeSyntax> content, string elementName)
        {
            return content.GetXmlElements(elementName).FirstOrDefault();
        }

        public static IEnumerable<XmlNodeSyntax> GetXmlElements(this SyntaxList<XmlNodeSyntax> content, string elementName)
        {
            foreach (XmlNodeSyntax syntax in content)
            {
                XmlEmptyElementSyntax emptyElement = syntax as XmlEmptyElementSyntax;
                if (emptyElement != null)
                {
                    if (string.Equals(elementName, emptyElement.Name.ToString(), StringComparison.Ordinal))
                        yield return emptyElement;

                    continue;
                }

                XmlElementSyntax elementSyntax = syntax as XmlElementSyntax;
                if (elementSyntax != null)
                {
                    if (string.Equals(elementName, elementSyntax.StartTag?.Name?.ToString(), StringComparison.Ordinal))
                        yield return elementSyntax;

                    continue;
                }
            }
        }
    }
}
