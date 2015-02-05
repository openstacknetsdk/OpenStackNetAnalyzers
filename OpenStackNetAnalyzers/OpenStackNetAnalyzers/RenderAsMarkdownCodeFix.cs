namespace OpenStackNetAnalyzers
{
    using System;
    using System.Collections.Immutable;
    using System.Composition;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using CommonMark;
    using CommonMark.Syntax;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CodeActions;
    using Microsoft.CodeAnalysis.CodeFixes;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.CSharp.Syntax;

    [ExportCodeFixProvider(nameof(RenderAsMarkdownCodeFix), LanguageNames.CSharp)]
    [Shared]
    public class RenderAsMarkdownCodeFix : CodeFixProvider
    {
        private static readonly ImmutableArray<string> _fixableDiagnostics =
            ImmutableArray.Create(RenderAsMarkdownAnalyzer.DiagnosticId);

        public sealed override ImmutableArray<string> GetFixableDiagnosticIds()
        {
            return _fixableDiagnostics;
        }

        public override FixAllProvider GetFixAllProvider()
        {
            // this is unlikely to work as expected
            return null;
        }

        public override async Task ComputeFixesAsync(CodeFixContext context)
        {
            foreach (var diagnostic in context.Diagnostics)
            {
                if (!string.Equals(diagnostic.Id, RenderAsMarkdownAnalyzer.DiagnosticId, StringComparison.Ordinal))
                    continue;

                var documentRoot = await context.Document.GetSyntaxRootAsync(context.CancellationToken);
                SyntaxNode syntax = documentRoot.FindNode(diagnostic.Location.SourceSpan, findInsideTrivia: true, getInnermostNodeForTie: true);
                if (syntax == null)
                    continue;

                DocumentationCommentTriviaSyntax documentationCommentTriviaSyntax = syntax.FirstAncestorOrSelf<DocumentationCommentTriviaSyntax>();
                if (documentationCommentTriviaSyntax == null)
                    continue;

                string description = "Render documentation as Markdown";
                context.RegisterFix(CodeAction.Create(description, cancellationToken => CreateChangedDocument(context, documentationCommentTriviaSyntax, cancellationToken)), diagnostic);
            }
        }

        private async Task<Document> CreateChangedDocument(CodeFixContext context, DocumentationCommentTriviaSyntax documentationCommentTriviaSyntax, CancellationToken cancellationToken)
        {
            StringBuilder leadingTriviaBuilder = new StringBuilder();
            SyntaxToken parentToken = documentationCommentTriviaSyntax.ParentTrivia.Token;
            int documentationCommentIndex = parentToken.LeadingTrivia.IndexOf(documentationCommentTriviaSyntax.ParentTrivia);
            for (int i = 0; i < documentationCommentIndex; i++)
            {
                SyntaxTrivia trivia = parentToken.LeadingTrivia[i];
                switch (trivia.CSharpKind())
                {
                case SyntaxKind.EndOfLineTrivia:
                    leadingTriviaBuilder.Clear();
                    break;

                case SyntaxKind.WhitespaceTrivia:
                    leadingTriviaBuilder.Append(trivia.ToFullString());
                    break;

                default:
                    break;
                }
            }

            leadingTriviaBuilder.Append(documentationCommentTriviaSyntax.GetLeadingTrivia().ToFullString());

            // this is the trivia that should appear at the beginning of each line of the comment.
            SyntaxTrivia leadingTrivia = SyntaxFactory.DocumentationCommentExterior(leadingTriviaBuilder.ToString());

            DocumentationCommentTriviaSyntax contentsOnly = RemoveExteriorTrivia(documentationCommentTriviaSyntax);
            contentsOnly = contentsOnly.ReplaceNodes(contentsOnly.DescendantNodes(), RenderBlockElementAsMarkdown);
            string renderedContent = contentsOnly.Content.ToFullString();
            string[] lines = renderedContent.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            SyntaxList<XmlNodeSyntax> newContent = XmlSyntaxFactory.List();
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (string.IsNullOrWhiteSpace(line))
                {
                    if (i == lines.Length - 1)
                        break;

                    line = string.Empty;
                }

                if (newContent.Count > 0)
                    newContent = newContent.Add(XmlSyntaxFactory.NewLine());

                newContent = newContent.Add(XmlSyntaxFactory.Text(line.TrimEnd(), true));
            }

            contentsOnly = contentsOnly
                .WithContent(newContent)
                .WithLeadingTrivia(SyntaxFactory.DocumentationCommentExterior("///"))
                .WithTrailingTrivia(SyntaxFactory.EndOfLine(Environment.NewLine));

            string fullContent = contentsOnly.ToFullString();
            SyntaxTriviaList parsedTrivia = SyntaxFactory.ParseLeadingTrivia(fullContent);
            SyntaxTrivia documentationTrivia = parsedTrivia.FirstOrDefault(i => i.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia));
            contentsOnly = documentationTrivia.GetStructure() as DocumentationCommentTriviaSyntax;
            if (contentsOnly == null)
                return context.Document;

            contentsOnly =
                contentsOnly
                .ReplaceExteriorTrivia(leadingTrivia)
                .WithLeadingTrivia(SyntaxFactory.DocumentationCommentExterior("///"));

            // Remove unnecessary nested paragraph elements
            contentsOnly = contentsOnly.ReplaceNodes(contentsOnly.DescendantNodes().OfType<XmlElementSyntax>(), RemoveNestedParagraphs);

            SyntaxNode root = await context.Document.GetSyntaxRootAsync(cancellationToken);
            SyntaxNode newRoot = root.ReplaceNode(documentationCommentTriviaSyntax, contentsOnly);
            if (documentationCommentTriviaSyntax.IsEquivalentTo(contentsOnly))
                return context.Document;

            return context.Document.WithSyntaxRoot(newRoot);
        }

        private SyntaxNode RemoveNestedParagraphs(SyntaxNode originalNode, SyntaxNode rewrittenNode)
        {
            XmlElementSyntax elementSyntax = rewrittenNode as XmlElementSyntax;
            if (!IsUnnecessaryParaElement(elementSyntax))
                return rewrittenNode;

            return elementSyntax.Content[0].WithTriviaFrom(rewrittenNode);
        }

        private static bool IsUnnecessaryParaElement(XmlElementSyntax elementSyntax)
        {
            if (elementSyntax == null)
                return false;

            if (HasAttributes(elementSyntax))
                return false;

            if (!IsParaElement(elementSyntax))
                return false;

            if (elementSyntax.Content.Count != 1)
                return false;

            if (!IsParaElement(elementSyntax.Content[0] as XmlElementSyntax))
                return false;

            return true;
        }

        private static bool HasAttributes(XmlElementSyntax syntax)
        {
            return syntax.StartTag?.Attributes.Count > 0;
        }

        private static bool IsParaElement(XmlElementSyntax syntax)
        {
            return string.Equals("para", syntax?.StartTag?.Name?.ToString(), StringComparison.Ordinal);
        }

        private SyntaxNode RenderBlockElementAsMarkdown(SyntaxNode originalNode, SyntaxNode rewrittenNode)
        {
            XmlElementSyntax elementSyntax = rewrittenNode as XmlElementSyntax;
            if (elementSyntax == null)
                return rewrittenNode;

            switch (elementSyntax.StartTag?.Name?.ToString())
            {
            case "summary":
            case "remarks":
            case "returns":
            case "value":
                break;

            default:
                return rewrittenNode;
            }

            string rendered = RenderAsMarkdown(elementSyntax.Content.ToString()).Trim();
            return elementSyntax.WithContent(
                XmlSyntaxFactory.List(
                    XmlSyntaxFactory.NewLine().WithoutTrailingTrivia(),
                    XmlSyntaxFactory.Text(rendered, true),
                    XmlSyntaxFactory.NewLine().WithoutTrailingTrivia()));
        }

        private string RenderAsMarkdown(string text)
        {
            Block document;
            using (System.IO.StringReader reader = new System.IO.StringReader(text))
            {
                document = CommonMarkConverter.ProcessStage1(reader, CommonMarkSettings.Default);
                CommonMarkConverter.ProcessStage2(document, CommonMarkSettings.Default);
            }

            StringBuilder builder = new StringBuilder();
            using (System.IO.StringWriter writer = new System.IO.StringWriter(builder))
            {
                DocumentationCommentPrinter.BlocksToHtml(writer, document, CommonMarkSettings.Default);
            }

            return builder.ToString();
        }

        private DocumentationCommentTriviaSyntax RemoveExteriorTrivia(DocumentationCommentTriviaSyntax documentationComment)
        {
            return documentationComment.ReplaceTrivia(
                documentationComment.DescendantTrivia(descendIntoTrivia: true).Where(i => i.IsKind(SyntaxKind.DocumentationCommentExteriorTrivia)),
                (originalTrivia, rewrittenTrivia) => default(SyntaxTrivia));
        }
    }
}
