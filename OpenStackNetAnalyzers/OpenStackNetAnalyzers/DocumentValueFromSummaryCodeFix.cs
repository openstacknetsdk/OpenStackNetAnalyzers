namespace OpenStackNetAnalyzers
{
    using System;
    using System.Collections.Immutable;
    using System.Composition;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CodeActions;
    using Microsoft.CodeAnalysis.CodeFixes;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.CSharp.Syntax;

    [ExportCodeFixProvider(nameof(DocumentDelegatingApiCallCodeFix), LanguageNames.CSharp)]
    [Shared]
    public class DocumentValueFromSummaryCodeFix : CodeFixProvider
    {
        private static readonly ImmutableArray<string> _fixableDiagnostics =
            ImmutableArray.Create(DocumentValueFromSummaryAnalyzer.DiagnosticId);

        public sealed override ImmutableArray<string> GetFixableDiagnosticIds()
        {
            return _fixableDiagnostics;
        }

        public override FixAllProvider GetFixAllProvider()
        {
            return WellKnownFixAllProviders.BatchFixer;
        }

        public override async Task ComputeFixesAsync(CodeFixContext context)
        {
            foreach (var diagnostic in context.Diagnostics)
            {
                if (!string.Equals(diagnostic.Id, DocumentValueFromSummaryAnalyzer.DiagnosticId, StringComparison.Ordinal))
                    continue;

                var documentRoot = await context.Document.GetSyntaxRootAsync(context.CancellationToken);
                SyntaxNode syntax = documentRoot.FindNode(diagnostic.Location.SourceSpan);
                if (syntax == null)
                    continue;

                PropertyDeclarationSyntax propertyDeclarationSyntax = syntax.FirstAncestorOrSelf<PropertyDeclarationSyntax>();
                if (propertyDeclarationSyntax == null)
                    continue;

                string description = "Document value from summary";
                context.RegisterFix(CodeAction.Create(description, cancellationToken => CreateChangedDocument(context, propertyDeclarationSyntax, cancellationToken)), diagnostic);
            }
        }

        private async Task<Document> CreateChangedDocument(CodeFixContext context, PropertyDeclarationSyntax propertyDeclarationSyntax, CancellationToken cancellationToken)
        {
            DocumentationCommentTriviaSyntax documentationComment = DocumentValueFromSummaryAnalyzer.GetDocumentationCommentTriviaSyntax(propertyDeclarationSyntax);
            if (documentationComment == null)
                return context.Document;

            XmlElementSyntax summaryElement = (XmlElementSyntax)DocumentValueFromSummaryAnalyzer.GetXmlElement(documentationComment.Content, "summary");
            if (summaryElement == null)
                return context.Document;

            SyntaxList<XmlNodeSyntax> summaryContent = summaryElement.Content;
            XmlNodeSyntax firstContent = summaryContent.FirstOrDefault(IsContentElement);
            XmlTextSyntax firstText = firstContent as XmlTextSyntax;
            if (firstText != null)
            {
                string firstTextContent = string.Concat(firstText.DescendantTokens());
                if (firstTextContent.TrimStart().StartsWith("Gets ", StringComparison.Ordinal))
                {
                    // Find the token containing "Gets "
                    SyntaxToken getsToken = default(SyntaxToken);
                    foreach (SyntaxToken textToken in firstText.TextTokens)
                    {
                        if (textToken.IsMissing)
                            continue;

                        if (!textToken.Text.TrimStart().StartsWith("Gets ", StringComparison.Ordinal))
                            continue;

                        getsToken = textToken;
                        break;
                    }

                    if (!getsToken.IsMissing)
                    {
                        string text = getsToken.Text;
                        string valueText = getsToken.ValueText;
                        int index = text.IndexOf("Gets ");
                        if (index >= 0)
                        {
                            bool additionalCharacters = index + 5 < text.Length;
                            text = text.Substring(0, index)
                                + (additionalCharacters ? char.ToUpperInvariant(text[index + 5]).ToString() : string.Empty)
                                + text.Substring(index + (additionalCharacters ? (5 + 1) : 5));
                        }

                        index = valueText.IndexOf("Gets ");
                        if (index >= 0)
                            valueText = valueText.Remove(index, 5);

                        SyntaxToken replaced = SyntaxFactory.Token(getsToken.LeadingTrivia, getsToken.CSharpKind(), text, valueText, getsToken.TrailingTrivia);
                        summaryContent = summaryContent.Replace(firstText, firstText.ReplaceToken(getsToken, replaced));
                    }
                }
            }

            string defaultValueToken = "NullIfNotIncluded";
            SemanticModel semanticModel = await context.Document.GetSemanticModelAsync(cancellationToken);
            IPropertySymbol propertySymbol = semanticModel.GetDeclaredSymbol(propertyDeclarationSyntax);
            if (propertySymbol != null)
            {
                ITypeSymbol propertyType = propertySymbol.Type;
                if (propertyType.IsImmutableArray())
                    defaultValueToken = "DefaultArrayIfNotIncluded";
            }

            XmlElementSyntax valueElement =
                XmlSyntaxFactory.MultiLineElement(
                    "value",
                    XmlSyntaxFactory.List(
                        XmlSyntaxFactory.ParaElement(XmlSyntaxFactory.PlaceholderElement(RemoveFirstAndListNewlines(summaryContent))),
                        XmlSyntaxFactory.NewLine(),
                        XmlSyntaxFactory.TokenElement(defaultValueToken)));

            XmlNodeSyntax leadingNewLine = XmlSyntaxFactory.NewLine();

            // HACK: The formatter isn't working when contents are added to an existing documentation comment, so we
            // manually apply the indentation from the last line of the existing comment to each new line of the
            // generated content.
            SyntaxTrivia exteriorTrivia = GetLastDocumentationCommentExteriorTrivia(documentationComment);
            if (!exteriorTrivia.Token.IsMissing)
            {
                leadingNewLine = ReplaceExteriorTrivia(leadingNewLine, exteriorTrivia);
                valueElement = ReplaceExteriorTrivia(valueElement, exteriorTrivia);
            }

            DocumentationCommentTriviaSyntax newDocumentationComment = documentationComment.WithContent(
                documentationComment.Content.InsertRange(documentationComment.Content.Count - 1,
                XmlSyntaxFactory.List(
                    leadingNewLine,
                    valueElement)));

            SyntaxNode root = await context.Document.GetSyntaxRootAsync(cancellationToken);
            SyntaxNode newRoot = root.ReplaceNode(documentationComment, newDocumentationComment);
            return context.Document.WithSyntaxRoot(newRoot);
        }

        private bool IsContentElement(XmlNodeSyntax syntax)
        {
            switch (syntax.CSharpKind())
            {
            case SyntaxKind.XmlCDataSection:
            case SyntaxKind.XmlElement:
            case SyntaxKind.XmlEmptyElement:
            case SyntaxKind.XmlText:
                return true;

            default:
                return false;
            }
        }

        private SyntaxTrivia GetLastDocumentationCommentExteriorTrivia(SyntaxNode node)
        {
            return node
                .DescendantTrivia(descendIntoTrivia: true)
                .Where(trivia => trivia.IsKind(SyntaxKind.DocumentationCommentExteriorTrivia))
                .LastOrDefault();
        }

        private T ReplaceExteriorTrivia<T>(T node, SyntaxTrivia trivia)
            where T : SyntaxNode
        {
            // Make sure to include a space after the '///' characters.
            SyntaxTrivia triviaWithSpace = SyntaxFactory.DocumentationCommentExterior(trivia.ToString() + " ");

            return node.ReplaceTrivia(
                node.DescendantTrivia(descendIntoTrivia: true).Where(i => i.IsKind(SyntaxKind.DocumentationCommentExteriorTrivia)),
                (originalTrivia, rewrittenTrivia) => SelectExteriorTrivia(rewrittenTrivia, trivia, triviaWithSpace));
        }

        private SyntaxTrivia SelectExteriorTrivia(SyntaxTrivia rewrittenTrivia, SyntaxTrivia trivia, SyntaxTrivia triviaWithSpace)
        {
            // if the trivia had a trailing space, make sure to preserve it
            if (rewrittenTrivia.ToString().EndsWith(" "))
                return triviaWithSpace;

            // otherwise the space is part of the leading trivia of the following token, so don't add an extra one to
            // the exterior trivia
            return trivia;
        }

        private SyntaxList<XmlNodeSyntax> RemoveFirstAndListNewlines(SyntaxList<XmlNodeSyntax> summaryContent)
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
            if (IsNewLine(firstSyntaxTokens[0]))
            {
                removeFromStart = 1;
            }
            else
            {
                if (!IsWhitespace(firstSyntaxTokens[0]))
                    return summaryContent;

                if (!IsNewLine(firstSyntaxTokens[1]))
                    return summaryContent;

                removeFromStart = 2;
            }

            SyntaxTokenList lastSyntaxTokens = lastSyntax.TextTokens;

            int removeFromEnd;
            if (IsNewLine(lastSyntaxTokens[lastSyntaxTokens.Count - 1]))
            {
                removeFromEnd = 1;
            }
            else
            {
                if (!IsWhitespace(lastSyntaxTokens[lastSyntaxTokens.Count - 1]))
                    return summaryContent;

                if (!IsNewLine(lastSyntaxTokens[lastSyntaxTokens.Count - 2]))
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

        private bool IsNewLine(SyntaxToken node)
        {
            return node.IsKind(SyntaxKind.XmlTextLiteralNewLineToken);
        }

        private bool IsWhitespace(SyntaxToken node)
        {
            return node.IsKind(SyntaxKind.XmlTextLiteralToken)
                && string.IsNullOrWhiteSpace(node.Text);
        }
    }
}
