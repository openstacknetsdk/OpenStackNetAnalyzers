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

        public sealed override ImmutableArray<string> FixableDiagnosticIds => _fixableDiagnostics;

        public override FixAllProvider GetFixAllProvider()
        {
            return WellKnownFixAllProviders.BatchFixer;
        }

        public override async Task RegisterCodeFixesAsync(CodeFixContext context)
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
                context.RegisterCodeFix(CodeAction.Create(description, cancellationToken => CreateChangedDocument(context, propertyDeclarationSyntax, cancellationToken)), diagnostic);
            }
        }

        private async Task<Document> CreateChangedDocument(CodeFixContext context, PropertyDeclarationSyntax propertyDeclarationSyntax, CancellationToken cancellationToken)
        {
            DocumentationCommentTriviaSyntax documentationComment = propertyDeclarationSyntax.GetDocumentationCommentTriviaSyntax();
            if (documentationComment == null)
                return context.Document;

            XmlElementSyntax summaryElement = (XmlElementSyntax)documentationComment.Content.GetFirstXmlElement("summary");
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

                        SyntaxToken replaced = SyntaxFactory.Token(getsToken.LeadingTrivia, getsToken.Kind(), text, valueText, getsToken.TrailingTrivia);
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
                        XmlSyntaxFactory.ParaElement(XmlSyntaxFactory.PlaceholderElement(summaryContent.WithoutFirstAndLastNewlines())),
                        XmlSyntaxFactory.NewLine(),
                        XmlSyntaxFactory.TokenElement(defaultValueToken)));

            XmlNodeSyntax leadingNewLine = XmlSyntaxFactory.NewLine();

            // HACK: The formatter isn't working when contents are added to an existing documentation comment, so we
            // manually apply the indentation from the last line of the existing comment to each new line of the
            // generated content.
            SyntaxTrivia exteriorTrivia = GetLastDocumentationCommentExteriorTrivia(documentationComment);
            if (!exteriorTrivia.Token.IsMissing)
            {
                leadingNewLine = leadingNewLine.ReplaceExteriorTrivia(exteriorTrivia);
                valueElement = valueElement.ReplaceExteriorTrivia(exteriorTrivia);
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
            switch (syntax.Kind())
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
    }
}
