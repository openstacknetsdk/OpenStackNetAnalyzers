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

    [ExportCodeFixProvider(nameof(ImplementBuilderPatternCodeFix), LanguageNames.CSharp)]
    [Shared]
    public class ImplementBuilderPatternCodeFix : CodeFixProvider
    {
        private static readonly ImmutableArray<string> _fixableDiagnostics =
            ImmutableArray.Create(ImplementBuilderPatternAnalyzer.DiagnosticId);

        public sealed override ImmutableArray<string> FixableDiagnosticIds => _fixableDiagnostics;

        public override FixAllProvider GetFixAllProvider()
        {
            // This code fix can only be applied to one property at a time
            return null;
        }

        public override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            foreach (var diagnostic in context.Diagnostics)
            {
                if (!string.Equals(diagnostic.Id, ImplementBuilderPatternAnalyzer.DiagnosticId, StringComparison.Ordinal))
                    continue;

                var documentRoot = await context.Document.GetSyntaxRootAsync(context.CancellationToken);
                SyntaxNode syntax = documentRoot.FindNode(diagnostic.Location.SourceSpan);
                if (syntax == null)
                    continue;

                PropertyDeclarationSyntax propertyDeclaration = syntax.FirstAncestorOrSelf<PropertyDeclarationSyntax>();
                if (propertyDeclaration == null)
                    continue;

                ClassDeclarationSyntax classDeclarationSyntax = propertyDeclaration.FirstAncestorOrSelf<ClassDeclarationSyntax>();
                if (classDeclarationSyntax == null)
                    continue;

                string description = string.Format("Implement builder method 'With{0}'", propertyDeclaration.Identifier.ValueText);
                context.RegisterCodeFix(CodeAction.Create(description, cancellationToken => CreateChangedSolution(context, classDeclarationSyntax, propertyDeclaration, cancellationToken)), diagnostic);
            }
        }

        private async Task<Solution> CreateChangedSolution(CodeFixContext context, ClassDeclarationSyntax classDeclarationSyntax, PropertyDeclarationSyntax propertyDeclarationSyntax, CancellationToken cancellationToken)
        {
            Solution solution = context.Document.Project.Solution;
            Solution newSolution = solution;

            /*
             * Add the implementation method to the class containing the property.
             */
            AccessorDeclarationSyntax propertyGetter = propertyDeclarationSyntax.AccessorList.Accessors.First(accessor => accessor.Keyword.IsKind(SyntaxKind.GetKeyword));

            SyntaxNode root = classDeclarationSyntax.SyntaxTree.GetRoot(cancellationToken);
            SemanticModel semanticModel = await context.Document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            INamedTypeSymbol declaringType = semanticModel.GetDeclaredSymbol(classDeclarationSyntax);
            IPropertySymbol property = semanticModel.GetDeclaredSymbol(propertyDeclarationSyntax);
            SyntaxNode newRoot = root.ReplaceNode(
                classDeclarationSyntax,
                classDeclarationSyntax.AddMembers(
                    BuildImplementationMethod(semanticModel, declaringType, property, propertyGetter)));

            newSolution = newSolution.WithDocumentSyntaxRoot(context.Document.Id, newRoot);

            /*
             * Generate the extension method
             */
            MethodDeclarationSyntax extensionMethod = BuildGenericWrapperMethod(declaringType, property);

            /*
             * Try to locate an existing extension methods class for the type.
             */
            string extensionClassName = $"{declaringType.Name}Extensions";
            INamedTypeSymbol extensionMethodsClass = declaringType.ContainingNamespace.GetTypeMembers(extensionClassName, 0).FirstOrDefault();
            if (extensionMethodsClass != null)
            {
                // Add the new extension method to the existing class.
                Location location = extensionMethodsClass.Locations.FirstOrDefault(i => i.IsInSource);
                if (location == null)
                    return solution;

                Document extensionsDocument = context.Document.Project.Solution.GetDocument(location.SourceTree);
                if (extensionsDocument == null)
                    return solution;

                SyntaxNode extensionsRoot = await extensionsDocument.GetSyntaxRootAsync(cancellationToken);
                ClassDeclarationSyntax extensionsClass = extensionsRoot.FindNode(location.SourceSpan, getInnermostNodeForTie: true).FirstAncestorOrSelf<ClassDeclarationSyntax>();
                if (extensionsClass == null)
                    return solution;

                SyntaxNode newExtensionsRoot = extensionsRoot.ReplaceNode(extensionsClass, extensionsClass.AddMembers(extensionMethod));
                newSolution = newSolution.WithDocumentSyntaxRoot(extensionsDocument.Id, newExtensionsRoot);
            }
            else
            {
                // Need to add a new class for the extension methods.

                DocumentationCommentTriviaSyntax documentationComment = XmlSyntaxFactory.DocumentationComment(
                    XmlSyntaxFactory.SummaryElement(
                        XmlSyntaxFactory.Text("This class provides extension methods for the "),
                        XmlSyntaxFactory.SeeElement(
                            SyntaxFactory.TypeCref(SyntaxFactory.ParseTypeName(declaringType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)))),
                        XmlSyntaxFactory.Text(" class.")),
                    XmlSyntaxFactory.NewLine(),
                    XmlSyntaxFactory.ThreadSafetyElement(),
                    XmlSyntaxFactory.NewLine(),
                    XmlSyntaxFactory.PreliminaryElement());

                SyntaxNode extensionsClassRoot = SyntaxFactory.CompilationUnit().AddMembers(
                    SyntaxFactory.NamespaceDeclaration(SyntaxFactory.ParseName(declaringType.ContainingNamespace.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)))
                        .AddUsings(SyntaxFactory.UsingDirective(SyntaxFactory.ParseName("global::System")))
                        .AddMembers(
                            SyntaxFactory.ClassDeclaration(extensionClassName)
                                .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword), SyntaxFactory.Token(SyntaxKind.StaticKeyword))
                                .AddMembers(extensionMethod)
                                .WithLeadingTrivia(SyntaxFactory.TriviaList(SyntaxFactory.Trivia(documentationComment)))))
                    .WithAdditionalAnnotations(Simplifier.Annotation);

                DocumentId extensionsDocumentId = DocumentId.CreateNewId(context.Document.Project.Id);
                newSolution = newSolution
                    .AddDocument(extensionsDocumentId, $"{extensionClassName}.cs", extensionsClassRoot, context.Document.Folders);

                // Make sure to also add the file to linked projects.
                foreach (var linkedDocumentId in context.Document.GetLinkedDocumentIds())
                {
                    DocumentId linkedExtensionDocumentId = DocumentId.CreateNewId(linkedDocumentId.ProjectId);
                    newSolution = newSolution
                        .AddDocument(linkedExtensionDocumentId, $"{extensionClassName}.cs", extensionsClassRoot, context.Document.Folders);
                }
            }

            return newSolution;
        }

        private MethodDeclarationSyntax BuildImplementationMethod(SemanticModel semanticModel, INamedTypeSymbol declaringType, IPropertySymbol property, AccessorDeclarationSyntax propertyGetter)
        {
            // by this point we know the getter contains a simple return statement for a field
            ExpressionSyntax fieldExpression = ((ReturnStatementSyntax)propertyGetter.Body.Statements[0]).Expression;
            IFieldSymbol fieldSymbol = (IFieldSymbol)semanticModel.GetSymbolInfo(fieldExpression).Symbol;

            SyntaxList<AttributeListSyntax> attributeLists = SyntaxFactory.List<AttributeListSyntax>();

            SyntaxTokenList modifiers = SyntaxFactory.TokenList(
                SyntaxFactory.Token(SyntaxKind.ProtectedKeyword),
                SyntaxFactory.Token(SyntaxKind.InternalKeyword));

            if (!declaringType.IsSealed)
                modifiers = modifiers.Add(SyntaxFactory.Token(SyntaxKind.VirtualKeyword));

            TypeSyntax returnType = SyntaxFactory.ParseTypeName(declaringType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
            TypeSyntax propertyType = SyntaxFactory.ParseTypeName(property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));

            ExplicitInterfaceSpecifierSyntax explicitInterfaceSpecifier = null;
            SyntaxToken identifier = SyntaxFactory.Identifier($"With{property.Name}Impl");
            TypeParameterListSyntax typeParameterList = null;
            ParameterListSyntax parameterList = SyntaxFactory.ParameterList(SyntaxFactory.SeparatedList(new[] { SyntaxFactory.Parameter(SyntaxFactory.Identifier("value")).WithType(propertyType) }));
            SyntaxList<TypeParameterConstraintClauseSyntax> constraintClauses = SyntaxFactory.List<TypeParameterConstraintClauseSyntax>();

            ExpressionSyntax resultInitializerExpression =
                SyntaxFactory.CastExpression(returnType, SyntaxFactory.ParseExpression("MemberwiseClone()"));

            ExpressionStatementSyntax propertyUpdate =
                SyntaxFactory.ExpressionStatement(SyntaxFactory.AssignmentExpression(
                    SyntaxKind.SimpleAssignmentExpression,
                    SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, SyntaxFactory.IdentifierName("result"), SyntaxFactory.IdentifierName(fieldSymbol.Name)),
                    SyntaxFactory.IdentifierName("value")));

            ReturnStatementSyntax returnSyntax =
                SyntaxFactory.ReturnStatement(SyntaxFactory.IdentifierName("result"));

            BlockSyntax body = SyntaxFactory.Block(
                SyntaxFactory.LocalDeclarationStatement(SyntaxFactory.VariableDeclaration(returnType).AddVariables(SyntaxFactory.VariableDeclarator("result").WithInitializer(SyntaxFactory.EqualsValueClause(resultInitializerExpression)))),
                propertyUpdate,
                returnSyntax);

            SyntaxToken semicolonToken = default(SyntaxToken);

            return SyntaxFactory.MethodDeclaration(attributeLists, modifiers, returnType, explicitInterfaceSpecifier, identifier, typeParameterList, parameterList, constraintClauses, body, semicolonToken)
                .WithAdditionalAnnotations(Simplifier.Annotation);
        }

        private MethodDeclarationSyntax BuildGenericWrapperMethod(INamedTypeSymbol declaringType, IPropertySymbol property)
        {
            SyntaxList<AttributeListSyntax> attributeLists = SyntaxFactory.List<AttributeListSyntax>();

            SyntaxTokenList modifiers = SyntaxFactory.TokenList(
                SyntaxFactory.Token(SyntaxKind.PublicKeyword),
                SyntaxFactory.Token(SyntaxKind.StaticKeyword));

            TypeSyntax declaringTypeSyntax = SyntaxFactory.ParseTypeName(declaringType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
            TypeSyntax returnType = SyntaxFactory.IdentifierName("T");
            TypeSyntax propertyType = SyntaxFactory.ParseTypeName(property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));

            ExplicitInterfaceSpecifierSyntax explicitInterfaceSpecifier = null;
            SyntaxToken identifier = SyntaxFactory.Identifier($"With{property.Name}");
            TypeParameterListSyntax typeParameterList = SyntaxFactory.TypeParameterList().AddParameters(SyntaxFactory.TypeParameter("T"));
            ParameterListSyntax parameterList = SyntaxFactory.ParameterList(SyntaxFactory.SeparatedList<ParameterSyntax>()
                .Add(SyntaxFactory.Parameter(SyntaxFactory.Identifier("obj")).WithType(returnType).AddModifiers(SyntaxFactory.Token(SyntaxKind.ThisKeyword)))
                .Add(SyntaxFactory.Parameter(SyntaxFactory.Identifier("value")).WithType(propertyType)));
            SyntaxList<TypeParameterConstraintClauseSyntax> constraintClauses = SyntaxFactory.List<TypeParameterConstraintClauseSyntax>()
                .Add(SyntaxFactory.TypeParameterConstraintClause("T").AddConstraints(SyntaxFactory.TypeConstraint(declaringTypeSyntax)));

            StatementSyntax argumentNullCheck =
                SyntaxFactory.IfStatement(
                    SyntaxFactory.BinaryExpression(SyntaxKind.EqualsExpression, SyntaxFactory.IdentifierName("obj"), SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression)),
                    SyntaxFactory.ThrowStatement(
                        SyntaxFactory.ObjectCreationExpression(SyntaxFactory.ParseTypeName("global::System.ArgumentNullException"),
                        SyntaxFactory.ArgumentList().AddArguments(
                            SyntaxFactory.Argument(SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal("obj")))),
                        default(InitializerExpressionSyntax))));

            StatementSyntax returnStatement =
                SyntaxFactory.ReturnStatement(
                    SyntaxFactory.CastExpression(
                        SyntaxFactory.IdentifierName("T"),
                        SyntaxFactory.InvocationExpression(
                            SyntaxFactory.MemberAccessExpression(
                                SyntaxKind.SimpleMemberAccessExpression,
                                SyntaxFactory.IdentifierName("obj"),
                                SyntaxFactory.IdentifierName($"With{property.Name}Impl")),
                            SyntaxFactory.ArgumentList().AddArguments(
                                SyntaxFactory.Argument(SyntaxFactory.IdentifierName("value"))))));

            BlockSyntax body = SyntaxFactory.Block(
                argumentNullCheck,
                returnStatement);

            SyntaxToken semicolonToken = default(SyntaxToken);

            return SyntaxFactory.MethodDeclaration(attributeLists, modifiers, returnType, explicitInterfaceSpecifier, identifier, typeParameterList, parameterList, constraintClauses, body, semicolonToken)
                .WithAdditionalAnnotations(Simplifier.Annotation);
        }
    }
}
