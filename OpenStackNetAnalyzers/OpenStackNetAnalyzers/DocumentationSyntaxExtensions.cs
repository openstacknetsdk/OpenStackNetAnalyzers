﻿namespace OpenStackNetAnalyzers
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
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

        public static DocumentationCommentTriviaSyntax ReplaceExteriorTrivia(this DocumentationCommentTriviaSyntax node, SyntaxTrivia trivia)
        {
            return node.ReplaceExteriorTriviaImpl(trivia);
        }

        public static T ReplaceExteriorTrivia<T>(this T node, SyntaxTrivia trivia)
            where T : XmlNodeSyntax
        {
            return node.ReplaceExteriorTriviaImpl(trivia);
        }

        private static T ReplaceExteriorTriviaImpl<T>(this T node, SyntaxTrivia trivia)
            where T : SyntaxNode
        {
            // Make sure to include a space after the '///' characters.
            SyntaxTrivia triviaWithSpace = SyntaxFactory.DocumentationCommentExterior(trivia.ToString() + " ");

            return node.ReplaceTrivia(
                node.DescendantTrivia(descendIntoTrivia: true).Where(i => i.IsKind(SyntaxKind.DocumentationCommentExteriorTrivia)),
                (originalTrivia, rewrittenTrivia) => SelectExteriorTrivia(rewrittenTrivia, trivia, triviaWithSpace));
        }

        private static SyntaxTrivia SelectExteriorTrivia(SyntaxTrivia rewrittenTrivia, SyntaxTrivia trivia, SyntaxTrivia triviaWithSpace)
        {
            // if the trivia had a trailing space, make sure to preserve it
            if (rewrittenTrivia.ToString().EndsWith(" "))
                return triviaWithSpace;

            // otherwise the space is part of the leading trivia of the following token, so don't add an extra one to
            // the exterior trivia
            return trivia;
        }

        public static SyntaxList<XmlNodeSyntax> WithoutFirstAndLastNewlines(this SyntaxList<XmlNodeSyntax> summaryContent)
        {
            if (summaryContent.Count == 0)
                return summaryContent;

            XmlTextSyntax firstSyntax = summaryContent[0] as XmlTextSyntax;
            if (firstSyntax == null)
                return summaryContent;

            XmlTextSyntax lastSyntax = summaryContent[summaryContent.Count - 1] as XmlTextSyntax;
            if (lastSyntax == null)
                return summaryContent;

            SyntaxTokenList firstSyntaxTokens = firstSyntax.TextTokens;

            int removeFromStart;
            if (IsXmlNewLine(firstSyntaxTokens[0]))
            {
                removeFromStart = 1;
            }
            else
            {
                if (!IsXmlWhitespace(firstSyntaxTokens[0]))
                    return summaryContent;

                if (!IsXmlNewLine(firstSyntaxTokens[1]))
                    return summaryContent;

                removeFromStart = 2;
            }

            SyntaxTokenList lastSyntaxTokens = lastSyntax.TextTokens;

            int removeFromEnd;
            if (IsXmlNewLine(lastSyntaxTokens[lastSyntaxTokens.Count - 1]))
            {
                removeFromEnd = 1;
            }
            else
            {
                if (!IsXmlWhitespace(lastSyntaxTokens[lastSyntaxTokens.Count - 1]))
                    return summaryContent;

                if (!IsXmlNewLine(lastSyntaxTokens[lastSyntaxTokens.Count - 2]))
                    return summaryContent;

                removeFromEnd = 2;
            }

            for (int i = 0; i < removeFromStart; i++)
            {
                firstSyntaxTokens = firstSyntaxTokens.RemoveAt(0);
            }

            if (firstSyntax == lastSyntax)
            {
                lastSyntaxTokens = firstSyntaxTokens;
            }

            for (int i = 0; i < removeFromEnd; i++)
            {
                lastSyntaxTokens = lastSyntaxTokens.RemoveAt(lastSyntaxTokens.Count - 1);
            }

            summaryContent = summaryContent.RemoveAt(summaryContent.Count - 1);
            if (lastSyntaxTokens.Count != 0)
                summaryContent = summaryContent.Add(lastSyntax.WithTextTokens(lastSyntaxTokens));

            if (firstSyntax != lastSyntax)
            {
                summaryContent = summaryContent.RemoveAt(0);
                if (firstSyntaxTokens.Count != 0)
                    summaryContent = summaryContent.Insert(0, firstSyntax.WithTextTokens(firstSyntaxTokens));
            }

            if (summaryContent.Count > 0)
            {
                // Make sure to remove the leading trivia
                summaryContent = summaryContent.Replace(summaryContent[0], summaryContent[0].WithLeadingTrivia());

                // Remove leading spaces (between the <para> start tag and the start of the paragraph content)
                XmlTextSyntax firstTextSyntax = summaryContent[0] as XmlTextSyntax;
                if (firstTextSyntax != null && firstTextSyntax.TextTokens.Count > 0)
                {
                    SyntaxToken firstTextToken = firstTextSyntax.TextTokens[0];
                    string firstTokenText = firstTextToken.Text;
                    string trimmed = firstTokenText.TrimStart();
                    if (trimmed != firstTokenText)
                    {
                        SyntaxToken newFirstToken = SyntaxFactory.Token(
                            firstTextToken.LeadingTrivia,
                            firstTextToken.CSharpKind(),
                            trimmed,
                            firstTextToken.ValueText.TrimStart(),
                            firstTextToken.TrailingTrivia);

                        summaryContent = summaryContent.Replace(firstTextSyntax, firstTextSyntax.ReplaceToken(firstTextToken, newFirstToken));
                    }
                }
            }

            return summaryContent;
        }

        public static bool IsXmlNewLine(this SyntaxToken node)
        {
            return node.IsKind(SyntaxKind.XmlTextLiteralNewLineToken);
        }

        public static bool IsXmlWhitespace(this SyntaxToken node)
        {
            return node.IsKind(SyntaxKind.XmlTextLiteralToken)
                && string.IsNullOrWhiteSpace(node.Text);
        }
    }
}
