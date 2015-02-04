namespace OpenStackNetAnalyzers
{
    using System;
    using System.Xml.Linq;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.CSharp.Syntax;

    internal static class XmlSyntaxFactory
    {
        public static DocumentationCommentTriviaSyntax DocumentationComment(params XmlNodeSyntax[] content)
        {
            return SyntaxFactory.DocumentationCommentTrivia(SyntaxKind.SingleLineDocumentationCommentTrivia, List(content))
                .WithLeadingTrivia(SyntaxFactory.DocumentationCommentExterior("/// "))
                .WithTrailingTrivia(SyntaxFactory.EndOfLine(Environment.NewLine));
        }

        public static XmlElementSyntax MultiLineElement(string localName, SyntaxList<XmlNodeSyntax> content)
        {
            return MultiLineElement(SyntaxFactory.XmlName(localName), content);
        }

        public static XmlElementSyntax MultiLineElement(XmlNameSyntax name, SyntaxList<XmlNodeSyntax> content)
        {
            return SyntaxFactory.XmlElement(
                SyntaxFactory.XmlElementStartTag(name),
                content.Insert(0, NewLine()).Add(NewLine()),
                SyntaxFactory.XmlElementEndTag(name));
        }

        public static XmlElementSyntax Element(string localName, SyntaxList<XmlNodeSyntax> content)
        {
            return Element(SyntaxFactory.XmlName(localName), content);
        }

        public static XmlElementSyntax Element(XmlNameSyntax name, SyntaxList<XmlNodeSyntax> content)
        {
            return SyntaxFactory.XmlElement(
                SyntaxFactory.XmlElementStartTag(name),
                content,
                SyntaxFactory.XmlElementEndTag(name));
        }

        public static XmlEmptyElementSyntax EmptyElement(string localName)
        {
            return SyntaxFactory.XmlEmptyElement(SyntaxFactory.XmlName(localName));
        }

        public static SyntaxList<XmlNodeSyntax> List(params XmlNodeSyntax[] nodes)
        {
            return SyntaxFactory.List(nodes);
        }

        public static XmlTextSyntax Text(string value)
        {
            return Text(TextLiteral(value));
        }

        public static XmlTextSyntax Text(params SyntaxToken[] textTokens)
        {
            return SyntaxFactory.XmlText(SyntaxFactory.TokenList(textTokens));
        }

        public static XmlTextAttributeSyntax TextAttribute(string name, string value)
        {
            return TextAttribute(name, TextLiteral(value));
        }

        public static XmlTextAttributeSyntax TextAttribute(string name, params SyntaxToken[] textTokens)
        {
            return TextAttribute(SyntaxFactory.XmlName(name), SyntaxKind.DoubleQuoteToken, SyntaxFactory.TokenList(textTokens));
        }

        public static XmlTextAttributeSyntax TextAttribute(string name, SyntaxKind quoteKind, SyntaxTokenList textTokens)
        {
            return TextAttribute(SyntaxFactory.XmlName(name), SyntaxKind.DoubleQuoteToken, textTokens);
        }

        public static XmlTextAttributeSyntax TextAttribute(XmlNameSyntax name, SyntaxKind quoteKind, SyntaxTokenList textTokens)
        {
            return SyntaxFactory.XmlTextAttribute(
                name,
                SyntaxFactory.Token(quoteKind),
                textTokens,
                SyntaxFactory.Token(quoteKind))
                .WithLeadingTrivia(SyntaxFactory.Whitespace(" "));
        }

        public static XmlCrefAttributeSyntax CrefAttribute(CrefSyntax cref)
        {
            return CrefAttribute(cref, SyntaxKind.DoubleQuoteToken);
        }

        public static XmlCrefAttributeSyntax CrefAttribute(CrefSyntax cref, SyntaxKind quoteKind)
        {
            return SyntaxFactory.XmlCrefAttribute(
                SyntaxFactory.XmlName("cref"),
                SyntaxFactory.Token(quoteKind),
                cref,
                SyntaxFactory.Token(quoteKind))
                .WithLeadingTrivia(SyntaxFactory.Whitespace(" "));
        }

        public static XmlTextSyntax NewLine()
        {
            return Text(TextNewLine());
        }

        public static SyntaxToken TextNewLine()
        {
            return TextNewLine(true);
        }

        public static SyntaxToken TextNewLine(bool continueComment)
        {
            SyntaxToken token = SyntaxFactory.XmlTextNewLine(
                SyntaxFactory.TriviaList(),
                Environment.NewLine,
                Environment.NewLine,
                SyntaxFactory.TriviaList());

            if (continueComment)
                token = token.WithTrailingTrivia(SyntaxFactory.DocumentationCommentExterior("/// "));

            return token;
        }

        public static SyntaxToken TextLiteral(string value)
        {
            string encoded = new XText(value).ToString();
            return SyntaxFactory.XmlTextLiteral(
                SyntaxFactory.TriviaList(),
                encoded,
                value,
                SyntaxFactory.TriviaList());
        }

        public static XmlElementSyntax SummaryElement(params XmlNodeSyntax[] content)
        {
            return SummaryElement(List(content));
        }

        public static XmlElementSyntax SummaryElement(SyntaxList<XmlNodeSyntax> content)
        {
            return MultiLineElement("summary", content);
        }

        public static XmlElementSyntax RemarksElement(params XmlNodeSyntax[] content)
        {
            return RemarksElement(List(content));
        }

        public static XmlElementSyntax RemarksElement(SyntaxList<XmlNodeSyntax> content)
        {
            return MultiLineElement("remarks", content);
        }

        public static XmlElementSyntax ReturnsElement(params XmlNodeSyntax[] content)
        {
            return ReturnsElement(List(content));
        }

        public static XmlElementSyntax ReturnsElement(SyntaxList<XmlNodeSyntax> content)
        {
            return MultiLineElement("returns", content);
        }

        public static XmlElementSyntax ValueElement(params XmlNodeSyntax[] content)
        {
            return ValueElement(List(content));
        }

        public static XmlElementSyntax ValueElement(SyntaxList<XmlNodeSyntax> content)
        {
            return MultiLineElement("value", content);
        }

        public static XmlElementSyntax ParaElement(params XmlNodeSyntax[] content)
        {
            return ParaElement(List(content));
        }

        public static XmlElementSyntax ParaElement(SyntaxList<XmlNodeSyntax> content)
        {
            return Element("para", content);
        }

        public static XmlEmptyElementSyntax SeeElement(CrefSyntax cref)
        {
            return EmptyElement("see").AddAttributes(CrefAttribute(cref));
        }

        public static XmlEmptyElementSyntax ThreadSafetyElement()
        {
            return ThreadSafetyElement(true, false);
        }

        public static XmlEmptyElementSyntax ThreadSafetyElement(bool @static, bool instance)
        {
            return EmptyElement("threadsafety").AddAttributes(
                TextAttribute("static", @static.ToString().ToLowerInvariant()),
                TextAttribute("instance", instance.ToString().ToLowerInvariant()));
        }

        public static XmlEmptyElementSyntax PreliminaryElement()
        {
            return EmptyElement("preliminary");
        }
    }
}
