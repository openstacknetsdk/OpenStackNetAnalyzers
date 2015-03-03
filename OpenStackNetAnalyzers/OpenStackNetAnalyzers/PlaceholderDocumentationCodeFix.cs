namespace OpenStackNetAnalyzers
{
    using System;
    using System.Collections.Immutable;
    using System.Composition;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CodeActions;
    using Microsoft.CodeAnalysis.CodeFixes;
    using Microsoft.CodeAnalysis.CSharp.Syntax;

    [ExportCodeFixProvider(nameof(PlaceholderDocumentationCodeFix), LanguageNames.CSharp)]
    [Shared]
    public class PlaceholderDocumentationCodeFix : CodeFixProvider
    {
        private static readonly ImmutableArray<string> _fixableDiagnostics =
            ImmutableArray.Create(PlaceholderDocumentationAnalyzer.DiagnosticId);

        public sealed override ImmutableArray<string> FixableDiagnosticIds => _fixableDiagnostics;

        public override FixAllProvider GetFixAllProvider()
        {
            // Require users review each removal of placeholder tags.
            return null;
        }

        public override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            foreach (var diagnostic in context.Diagnostics)
            {
                if (!string.Equals(diagnostic.Id, PlaceholderDocumentationAnalyzer.DiagnosticId, StringComparison.Ordinal))
                    continue;

                var documentRoot = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
                SyntaxNode syntax = documentRoot.FindNode(diagnostic.Location.SourceSpan, findInsideTrivia: true, getInnermostNodeForTie: true);
                if (syntax == null)
                    continue;

                XmlElementSyntax xmlElementSyntax = syntax as XmlElementSyntax;
                if (xmlElementSyntax == null)
                {
                    // We continue even for placeholders if they are empty elements (XmlEmptyElementSyntax)
                    continue;
                }

                if (string.IsNullOrWhiteSpace(xmlElementSyntax.Content.ToString()))
                {
                    // The placeholder hasn't been updated yet.
                    continue;
                }

                string description = "Finalize placeholder text";
                context.RegisterCodeFix(CodeAction.Create(description, cancellationToken => CreateChangedDocument(context, xmlElementSyntax, cancellationToken)), diagnostic);
            }
        }

        private async Task<Document> CreateChangedDocument(CodeFixContext context, XmlElementSyntax elementSyntax, CancellationToken cancellationToken)
        {
            SyntaxList<XmlNodeSyntax> content = elementSyntax.Content;
            if (content.Count == 0)
                return context.Document;

            var leadingTrivia = elementSyntax.StartTag.GetLeadingTrivia();
            leadingTrivia = leadingTrivia.AddRange(elementSyntax.EndTag.GetTrailingTrivia());
            leadingTrivia = leadingTrivia.AddRange(content[0].GetLeadingTrivia());
            content = content.Replace(content[0], content[0].WithLeadingTrivia(leadingTrivia));

            var trailingTrivia = content[content.Count - 1].GetTrailingTrivia();
            trailingTrivia = trailingTrivia.AddRange(elementSyntax.EndTag.GetLeadingTrivia());
            trailingTrivia = trailingTrivia.AddRange(elementSyntax.EndTag.GetTrailingTrivia());
            content = content.Replace(content[content.Count - 1], content[content.Count - 1].WithTrailingTrivia(trailingTrivia));

            SyntaxNode root = await context.Document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            SyntaxNode newRoot = root.ReplaceNode(elementSyntax, content);
            return context.Document.WithSyntaxRoot(newRoot);
        }
    }
}
