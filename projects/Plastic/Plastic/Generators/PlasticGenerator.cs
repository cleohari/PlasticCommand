﻿namespace Plastic.Generators
{
    using System.Collections.Generic;
    using System.Diagnostics.CodeAnalysis;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Text;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp.Syntax;

    [SuppressMessage("MicrosoftCodeAnalysisCorrectness", "RS1024:기호를 올바르게 비교", Justification = "분석기 버그입니다.")]
    [Generator]
    internal class PlasticGenerator : ISourceGenerator
    {
        private const string COMMAND_TEMPLATE =
            "Plastic.Generators.Templates.CommandTemplate.CommandTemplate.txt";

        private const string INITIALIZER_TEMPLATE =
            "Plastic.Generators.Templates.InitializerTemplate.ServiceCollectionExtensionsTemplate.txt";

        private static readonly string ICOMMAND_SPEC_FULL_NAME
            = typeof(ICommandSpecification<,>).FullName;

        public void Execute(GeneratorExecutionContext context)
        {
            var receiver = (SyntaxReceiver)context.SyntaxContextReceiver!;
            if (receiver.Targets.Count <= 0)
                return;

            string commandTemplate = ReadEmbeddedResourceAsString(COMMAND_TEMPLATE);
            var generatedCommands = new HashSet<GeneratedCommandInfo>();
            
            foreach (TypeDeclarationSyntax userCommandSpec in receiver.Targets)
            {
                GeneratedCommandInfo? generatedCommandInfo =
                                GenerateCommands(context, userCommandSpec, commandTemplate);

                if (generatedCommandInfo != null)
                    generatedCommands.Add(generatedCommandInfo);
            }

            GeneratePlasticInitializer(context, generatedCommands);
        }

        private static GeneratedCommandInfo? GenerateCommands(
            GeneratorExecutionContext contextToAdd, TypeDeclarationSyntax userCommandSpec, string commandTemplate)
        {
            SemanticModel model = contextToAdd.Compilation.GetSemanticModel(userCommandSpec.SyntaxTree);
            INamedTypeSymbol originalInterface =
                                        model.Compilation.GetTypeByMetadataName(ICOMMAND_SPEC_FULL_NAME)!;

            if (model.GetDeclaredSymbol(userCommandSpec) is INamedTypeSymbol userCommandSpecSymbol)
            {
                string commandNameGenerated = GenerateCommandName(userCommandSpecSymbol);

                INamedTypeSymbol commandSpecInterface =
                    userCommandSpecSymbol.AllInterfaces.First(q => q.ConstructedFrom == originalInterface);

                ITypeSymbol paramSymbol = commandSpecInterface.TypeArguments[0];
                ITypeSymbol executionResultSymbol = commandSpecInterface.TypeArguments[1];

                string codeForServicesToBeProvided = BuildServiceInjectionCodeForPipelineContext(userCommandSpecSymbol);
                string @namespace = userCommandSpecSymbol.ContainingNamespace.ToString();

                var commandBuilder = new StringBuilder(commandTemplate);
                commandBuilder.Replace("{{ Namespace }}", @namespace);
                commandBuilder.Replace("Plastic.ExecutionResult<Plastic.TTFFResult>", executionResultSymbol.ToString());
                commandBuilder.Replace("Plastic.TTFFCommandSpec", userCommandSpecSymbol.ToString());
                commandBuilder.Replace("TargetParameter", paramSymbol.ToString());
                commandBuilder.Replace("TTFFCommand", commandNameGenerated);
                commandBuilder.Replace("{{ ServicesToBeProvided }}", codeForServicesToBeProvided);
                ReplacingForPipelineContext(commandBuilder, executionResultSymbol);

                contextToAdd.AddSource($"{userCommandSpecSymbol}_{commandNameGenerated}.cs", commandBuilder.ToString());

                return new GeneratedCommandInfo(@namespace + "." +commandNameGenerated, userCommandSpecSymbol.ToString());
            }
            else
                return default;
        }

        private static void ReplacingForPipelineContext(StringBuilder commandBuilder, ITypeSymbol executionResultSymbol)
        {
            var executionResultNamedSymbol = (INamedTypeSymbol)executionResultSymbol;

            if (executionResultNamedSymbol.TypeArguments.Length == 1)
            {
                string resultTypeFullName = executionResultNamedSymbol.TypeArguments[0].ToString();
                commandBuilder.Replace("PipelineContext<Plastic.TTFFResult>", $"PipelineContext<{resultTypeFullName}>");
            }
            else
            {
                commandBuilder.Replace("PipelineContext<Plastic.TTFFResult>", "PipelineContext");
            }
        }

        private static string GenerateCommandName(INamedTypeSymbol userCommandSpecSymbol)
        {
            string attributeName = typeof(CommandNameAttribute).FullName;
            AttributeData? commandNameAtt = userCommandSpecSymbol
                                                                    .GetAttributes()
                                                                    .FirstOrDefault(att => att.AttributeClass?.ToString() == attributeName);

            if (commandNameAtt?.ConstructorArguments.FirstOrDefault().Value is string commandName)
            {
                return commandName;
            }
            else
                return userCommandSpecSymbol.Name.Replace("CommandSpec", string.Empty) + "Command";
        }

        private static void GeneratePlasticInitializer(
            GeneratorExecutionContext contextToAdd, ICollection<GeneratedCommandInfo> generatedCommands)
        {
            string template = ReadEmbeddedResourceAsString(INITIALIZER_TEMPLATE);

            var builder = new StringBuilder();
            foreach (GeneratedCommandInfo commandName in generatedCommands)
            {
                builder.AppendLine($"\t\t\tservices.AddTransient(typeof({commandName.CommandSpecFullName}));");
                builder.AppendLine($"\t\t\tservices.AddTransient(typeof({commandName.GeneratedCommandName}));");
            }

            string generatedCode = template.Replace("{{ ServicesToBeAdded }}", builder.ToString());
            contextToAdd.AddSource("PlasticInitializer.cs", generatedCode);
        }

        private static string BuildServiceInjectionCodeForPipelineContext(INamedTypeSymbol userCommandSpecSymbol)
        {
            IParameterSymbol[] parameters =
                userCommandSpecSymbol.Constructors.SelectMany(q => q.Parameters).Distinct().ToArray();

            if (0 < parameters.Length)
            {
                var builder = new StringBuilder();
                foreach (IParameterSymbol item in parameters)
                {
                    builder.Append($"\t\t\t\tprovider.GetService<{item}>(),\n");
                    builder.Replace("?", string.Empty); // to not null
                }

                builder.Remove(builder.Length - 2, 2);
                return builder.ToString();
            }
            else
                return string.Empty;
        }

        private static string ReadEmbeddedResourceAsString(string resourceName)
        {
            using Stream resourceStream = Assembly.GetExecutingAssembly()
                                                                .GetManifestResourceStream(resourceName);

            using var reader = new StreamReader(resourceStream, Encoding.UTF8);
            return reader.ReadToEnd();
        }

        public void Initialize(GeneratorInitializationContext context)
        {
            context.RegisterForSyntaxNotifications(() => new SyntaxReceiver());
        }

        public class SyntaxReceiver : ISyntaxContextReceiver
        {
            public List<TypeDeclarationSyntax> Targets = new List<TypeDeclarationSyntax>();

            public void OnVisitSyntaxNode(GeneratorSyntaxContext context)
            {
                if (context.Node is TypeDeclarationSyntax typeNode)
                {
                    OnVisitTypeDeclarationSyntax(context, typeNode);
                }
            }

            private void OnVisitTypeDeclarationSyntax(GeneratorSyntaxContext context, TypeDeclarationSyntax typeSyntax)
            {
                ISymbol? symbole = context.SemanticModel.GetDeclaredSymbol(typeSyntax);
                if (symbole is INamedTypeSymbol namedSymbol)
                {
                    if (IsValid(namedSymbol, context))
                    {
                        this.Targets.Add(typeSyntax);
                    }
                }
            }

            private static bool IsValid(INamedTypeSymbol target, GeneratorSyntaxContext context)
            {
                INamedTypeSymbol commandSpecSymbol =
                    context.SemanticModel.Compilation.GetTypeByMetadataName(ICOMMAND_SPEC_FULL_NAME)!;

                return target.IsAbstract == false
                         && target.AllInterfaces.Any(q => q.ConstructedFrom == commandSpecSymbol)
                         && target.ContainingNamespace.GetTypeMembers().Contains(target);
            }
        }
    }
}
