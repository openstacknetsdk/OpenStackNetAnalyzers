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
    using Microsoft.CodeAnalysis.Simplification;

    [ExportCodeFixProvider(nameof(DocumentDelegatingApiCallCodeFix), LanguageNames.CSharp)]
    [Shared]
    public class DocumentDelegatingApiCallCodeFix : CodeFixProvider
    {
        private static readonly ImmutableArray<string> _fixableDiagnostics =
            ImmutableArray.Create(DocumentDelegatingApiCallAnalyzer.DiagnosticId);

        public sealed override ImmutableArray<string> FixableDiagnosticIds => _fixableDiagnostics;

        public override FixAllProvider GetFixAllProvider()
        {
            return WellKnownFixAllProviders.BatchFixer;
        }

        public override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            foreach (var diagnostic in context.Diagnostics)
            {
                if (!string.Equals(diagnostic.Id, DocumentDelegatingApiCallAnalyzer.DiagnosticId, StringComparison.Ordinal))
                    continue;

                var documentRoot = await context.Document.GetSyntaxRootAsync(context.CancellationToken);
                SyntaxNode syntax = documentRoot.FindNode(diagnostic.Location.SourceSpan);
                if (syntax == null)
                    continue;

                ClassDeclarationSyntax classDeclarationSyntax = syntax.FirstAncestorOrSelf<ClassDeclarationSyntax>();
                if (classDeclarationSyntax == null)
                    continue;

                string description = "Add documentation for delegating HTTP API call";
                context.RegisterCodeFix(CodeAction.Create(description, cancellationToken => CreateChangedDocument(context, classDeclarationSyntax, cancellationToken)), diagnostic);
            }
        }

        private async Task<Document> CreateChangedDocument(CodeFixContext context, ClassDeclarationSyntax classDeclarationSyntax, CancellationToken cancellationToken)
        {
            string serviceInterfaceName = "IUnknownService";
            string serviceExtensionsClassName = "UnknownServiceExtensions";
            INamedTypeSymbol serviceInterface = await GetServiceInterfaceAsync(context, classDeclarationSyntax, cancellationToken);
            if (serviceInterface != null)
            {
                serviceInterfaceName = serviceInterface.MetadataName;
                serviceExtensionsClassName = serviceInterfaceName + "Extensions";
                if (serviceInterfaceName.StartsWith("I"))
                    serviceExtensionsClassName = serviceExtensionsClassName.Substring(1);
            }

            string fullServiceName = "Unknown Service";
            if (serviceInterface != null)
                fullServiceName = ExtractFullServiceName(serviceInterface);

            string callName = classDeclarationSyntax.Identifier.ValueText;
            int apiCallSuffix = callName.IndexOf("ApiCall", StringComparison.Ordinal);
            if (apiCallSuffix > 0)
                callName = callName.Substring(0, apiCallSuffix);

            ClassDeclarationSyntax newClassDeclaration = classDeclarationSyntax;

            ConstructorDeclarationSyntax constructor = await FindApiCallConstructorAsync(context, classDeclarationSyntax, cancellationToken);
            ConstructorDeclarationSyntax newConstructor = await DocumentConstructorAsync(context, constructor, cancellationToken);
            if (newConstructor != null)
                newClassDeclaration = newClassDeclaration.ReplaceNode(constructor, newConstructor);

            DocumentationCommentTriviaSyntax documentationComment = XmlSyntaxFactory.DocumentationComment(
                XmlSyntaxFactory.SummaryElement(
                    XmlSyntaxFactory.Text("This class represents an HTTP API call to "),
                    XmlSyntaxFactory.PlaceholderElement(XmlSyntaxFactory.Text(callName)),
                    XmlSyntaxFactory.Text(" with the "),
                    XmlSyntaxFactory.PlaceholderElement(XmlSyntaxFactory.Text(fullServiceName)),
                    XmlSyntaxFactory.Text(".")),
                XmlSyntaxFactory.NewLine(),
                XmlSyntaxFactory.SeeAlsoElement(SyntaxFactory.NameMemberCref(SyntaxFactory.ParseName($"{serviceInterfaceName}.Prepare{callName}Async"))),
                XmlSyntaxFactory.NewLine(),
                XmlSyntaxFactory.SeeAlsoElement(SyntaxFactory.NameMemberCref(SyntaxFactory.ParseName($"{serviceExtensionsClassName}.{callName}Async"))),
                XmlSyntaxFactory.NewLine(),
                XmlSyntaxFactory.ThreadSafetyElement(),
                XmlSyntaxFactory.NewLine(),
                XmlSyntaxFactory.PreliminaryElement())
                .WithAdditionalAnnotations(Simplifier.Annotation);

            SyntaxTrivia documentationTrivia = SyntaxFactory.Trivia(documentationComment);
            newClassDeclaration = newClassDeclaration.WithLeadingTrivia(newClassDeclaration.GetLeadingTrivia().Add(documentationTrivia));

            SyntaxNode root = await context.Document.GetSyntaxRootAsync(cancellationToken);
            SyntaxNode newRoot = root.ReplaceNode(classDeclarationSyntax, newClassDeclaration);
            return context.Document.WithSyntaxRoot(newRoot);
        }

        private async Task<ConstructorDeclarationSyntax> DocumentConstructorAsync(CodeFixContext context, ConstructorDeclarationSyntax constructor, CancellationToken cancellationToken)
        {
            if (constructor == null)
                return null;

            SemanticModel semanticModel = await context.Document.GetSemanticModelAsync(cancellationToken);
            INamedTypeSymbol apiCallClass = semanticModel.GetDeclaredSymbol(constructor.FirstAncestorOrSelf<ClassDeclarationSyntax>(), cancellationToken);
            string parameterName = constructor.ParameterList.Parameters[0].Identifier.ValueText;

            DocumentationCommentTriviaSyntax documentationComment = XmlSyntaxFactory.DocumentationComment(
                XmlSyntaxFactory.SummaryElement(
                    XmlSyntaxFactory.Text("Initializes a new instance of the "),
                    XmlSyntaxFactory.SeeElement(SyntaxFactory.TypeCref(SyntaxFactory.ParseTypeName(apiCallClass.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)))),
                    XmlSyntaxFactory.Text(" class"),
                    XmlSyntaxFactory.NewLine(),
                    XmlSyntaxFactory.Text("with the behavior provided by another "),
                    XmlSyntaxFactory.SeeElement(SyntaxFactory.TypeCref(SyntaxFactory.ParseTypeName("global::OpenStack.Net.IHttpApiCall<T>"))),
                    XmlSyntaxFactory.Text(" instance.")),
                XmlSyntaxFactory.NewLine(),
                XmlSyntaxFactory.ParamElement(
                    parameterName,
                    XmlSyntaxFactory.List(
                        XmlSyntaxFactory.Text("The "),
                        XmlSyntaxFactory.SeeElement(SyntaxFactory.TypeCref(SyntaxFactory.ParseTypeName("global::OpenStack.Net.IHttpApiCall<T>"))),
                        XmlSyntaxFactory.Text(" providing the behavior for the API call."))),
                XmlSyntaxFactory.NewLine(),
                XmlSyntaxFactory.ExceptionElement(
                    SyntaxFactory.TypeCref(SyntaxFactory.ParseTypeName("global::System.ArgumentNullException")),
                    XmlSyntaxFactory.List(
                        XmlSyntaxFactory.Text("If "),
                        XmlSyntaxFactory.ParamRefElement(parameterName),
                        XmlSyntaxFactory.Text(" is "),
                        XmlSyntaxFactory.NullKeywordElement(),
                        XmlSyntaxFactory.Text("."))))
                .WithAdditionalAnnotations(Simplifier.Annotation);

            SyntaxTrivia documentationTrivia = SyntaxFactory.Trivia(documentationComment);
            return constructor.WithLeadingTrivia(constructor.GetLeadingTrivia().Add(documentationTrivia));
        }

        private async Task<ConstructorDeclarationSyntax> FindApiCallConstructorAsync(CodeFixContext context, ClassDeclarationSyntax classDeclarationSyntax, CancellationToken cancellationToken)
        {
            SemanticModel semanticModel = null;

            foreach (var constructorSyntax in classDeclarationSyntax.Members.OfType<ConstructorDeclarationSyntax>())
            {
                // We are looking for a constructor with exactly one parameter. The '==' operator here is lifted-to-null,
                // allowing a single condition to cover both cases where a syntax element is missing (null) and
                // constructors with the wrong number of arguments.
                if (!(constructorSyntax.ParameterList?.Parameters.Count == 1))
                    continue;

                ParameterSyntax firstParameter = constructorSyntax.ParameterList.Parameters[0];
                if (firstParameter.Identifier.IsMissing)
                    continue;

                TypeSyntax parameterType = firstParameter.Type;
                if (parameterType == null)
                    continue;

                if (semanticModel == null)
                    semanticModel = await context.Document.GetSemanticModelAsync(cancellationToken);

                INamedTypeSymbol symbol = semanticModel.GetSymbolInfo(parameterType, cancellationToken).Symbol as INamedTypeSymbol;
                if (symbol == null || !symbol.IsGenericType)
                    continue;

                string fullName = symbol.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                string expectedName = "global::OpenStack.Net.IHttpApiCall<T>";
                if (string.Equals(expectedName, fullName, StringComparison.Ordinal))
                    return constructorSyntax;
            }

            return null;
        }

        private string ExtractFullServiceName(INamedTypeSymbol serviceInterface)
        {
            string vendor = ExtractVendorName(serviceInterface);
            string version = ExtractServiceVersion(serviceInterface);
            string service = ExtractServiceName(serviceInterface);
            return $"{vendor} {service} Service {version}";
        }

        private string ExtractVendorName(INamedTypeSymbol serviceInterface)
        {
            INamespaceSymbol topLevelNamespace = serviceInterface.ContainingNamespace;
            while (topLevelNamespace != null)
            {
                if (topLevelNamespace.ContainingNamespace == null || topLevelNamespace.ContainingNamespace.IsGlobalNamespace)
                    break;

                topLevelNamespace = topLevelNamespace.ContainingNamespace;
            }

            if (topLevelNamespace == null || topLevelNamespace.IsGlobalNamespace)
                return "[Vendor]";

            return topLevelNamespace.Name;
        }

        private string ExtractServiceVersion(INamedTypeSymbol serviceInterface)
        {
            const string UnknownVersion = "[Version]";
            if (serviceInterface.ContainingNamespace == null)
                return UnknownVersion;

            if (serviceInterface.ContainingNamespace.IsGlobalNamespace)
                return UnknownVersion;

            string version = serviceInterface.ContainingNamespace.Name;
            if (!version.StartsWith("V"))
                return UnknownVersion;

            return version;
        }

        private string ExtractServiceName(INamedTypeSymbol serviceInterface)
        {
            string interfaceName = serviceInterface.Name;
            if (interfaceName.StartsWith("I"))
                interfaceName = interfaceName.Substring(1);

            if (interfaceName.EndsWith("Service"))
                interfaceName = interfaceName.Substring(0, interfaceName.LastIndexOf("Service"));

            return interfaceName;
        }

        private async Task<INamedTypeSymbol> GetServiceInterfaceAsync(CodeFixContext context, ClassDeclarationSyntax classDeclarationSyntax, CancellationToken cancellationToken)
        {
            SemanticModel semanticModel = await context.Document.GetSemanticModelAsync(cancellationToken);
            INamedTypeSymbol apiCallSymbol = semanticModel.GetDeclaredSymbol(classDeclarationSyntax, cancellationToken);
            foreach (INamedTypeSymbol type in apiCallSymbol.ContainingNamespace.GetTypeMembers())
            {
                if (type.TypeKind != TypeKind.Interface)
                    continue;

                foreach (INamedTypeSymbol interfaceType in type.AllInterfaces)
                {
                    if (string.Equals("IHttpService", interfaceType.MetadataName))
                        return type;
                }
            }

            return null;
        }
    }
}
