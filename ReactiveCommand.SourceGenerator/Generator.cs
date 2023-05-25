﻿using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ReactiveCommand.SourceGenerator;

[Generator]
public class Generator : ISourceGenerator
{
    private const string LogPath = @"C:\Users\Sparky\Desktop\log.txt";
    public static readonly StreamWriter Log = new(new FileStream(LogPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };

    public Generator()
    {
        Console.SetOut(Log);
    }

    public void Initialize(GeneratorInitializationContext context)
    {
    }

    public void Execute(GeneratorExecutionContext context)
    {
        var extensionInfos = ExtractMethodExtensionInfos(context.Compilation);
        foreach (var classExtensionInfo in extensionInfos)
        {
            Console.Out.WriteLine($"Will process: {classExtensionInfo.ClassName}");
            var stringStream = new StringWriter();
            var writer = new IndentedTextWriter(stringStream, "\t");
            writer.WriteLine("// <auto-generated>");
            writer.WriteLine($"namespace {classExtensionInfo.ClassNamespace}");
            writer.WriteLine("{");
            writer.Indent++;
            writer.WriteLine($"public partial class {classExtensionInfo.ClassName}");
            writer.WriteLine("{");
            writer.Indent++;
            foreach (var commandExtensionInfo in classExtensionInfo.CommandExtensionInfos)
            {
                var outputType = commandExtensionInfo.GetOutputTypeText();
                var inputType = commandExtensionInfo.GetInputTypeText();
                writer.WriteLine($"public ReactiveUI.ReactiveCommand<{inputType}, {outputType}> " +
                                 $"{commandExtensionInfo.MethodName}Command {{ get; private set; }}");
            }

            writer.WriteLine();

            writer.WriteLine("protected void InitializeCommands()");
            writer.WriteLine("{");
            writer.Indent++;
            foreach (var commandExtensionInfo in classExtensionInfo.CommandExtensionInfos)
            {
                var commandName = $"{commandExtensionInfo.MethodName}Command";
                var outputType = commandExtensionInfo.GetOutputTypeText();
                var inputType = commandExtensionInfo.GetInputTypeText();
                if (commandExtensionInfo.ArgumentType == null)
                {
                    writer.WriteLine(!commandExtensionInfo.IsTask
                        ? $"{commandName} = ReactiveUI.ReactiveCommand.Create({commandExtensionInfo.MethodName});"
                        : $"{commandName} = ReactiveUI.ReactiveCommand.CreateFromTask({commandExtensionInfo.MethodName});");
                }
                else if(commandExtensionInfo.ArgumentType != null && !commandExtensionInfo.IsReturnTypeVoid)
                {
                    writer.WriteLine(!commandExtensionInfo.IsTask
                        ? $"{commandName} = ReactiveUI.ReactiveCommand.Create<{inputType}, {outputType}>({commandExtensionInfo.MethodName});"
                        : $"{commandName} = ReactiveUI.ReactiveCommand.CreateFromTask<{inputType}, {outputType}>({commandExtensionInfo.MethodName});");
                }
                else if (commandExtensionInfo.ArgumentType != null && commandExtensionInfo.IsReturnTypeVoid)
                {
                    writer.WriteLine(!commandExtensionInfo.IsTask
                        ? $"{commandName} = ReactiveUI.ReactiveCommand.Create<{inputType}>({commandExtensionInfo.MethodName});"
                        : $"{commandName} = ReactiveUI.ReactiveCommand.CreateFromTask<{inputType}>({commandExtensionInfo.MethodName});");
                }
            }

            writer.Indent--;
            writer.WriteLine("}");

            writer.Indent--;
            writer.WriteLine("}");
            writer.Indent--;
            writer.WriteLine("}");

            context.AddSource($"{classExtensionInfo.ClassName}.g.cs", stringStream.ToString());
            Console.Out.WriteLine(stringStream);
        }
    }

    private static List<ClassExtensionInfo> ExtractMethodExtensionInfos(Compilation compilation)
    {
        var classExtensionInfos = new List<ClassExtensionInfo>();
        foreach (var compilationSyntaxTree in compilation.SyntaxTrees)
        {
            var semanticModel = compilation.GetSemanticModel(compilationSyntaxTree);
            var declaredClasses = compilationSyntaxTree.GetRoot().DescendantNodesAndSelf().OfType<ClassDeclarationSyntax>();
            foreach (var declaredClass in declaredClasses)
            {
                if (!declaredClass.Modifiers.Any(SyntaxKind.PartialKeyword))
                    continue; // Not a partial class, continue

                var classSymbol = (INamedTypeSymbol)ModelExtensions.GetDeclaredSymbol(semanticModel, declaredClass)!;
                var classNamespace = classSymbol.ContainingNamespace.ToString();
                var typeName = declaredClass.Identifier.ValueText;


                var classExtensionInfo = new ClassExtensionInfo
                {
                    ClassName = typeName,
                    ClassNamespace = classNamespace
                };

                var methodMembers = declaredClass.Members
                    .OfType<MethodDeclarationSyntax>()
                    .ToList();
                foreach (var methodSyntax in methodMembers)
                {
                    var methodSymbol = (IMethodSymbol)ModelExtensions.GetDeclaredSymbol(semanticModel, methodSyntax)!;
                    var methodAttributes = methodSyntax.AttributeLists
                        .SelectMany(a => a.Attributes)
                        .Where(a => a.Name.ToString() == nameof(ReactiveCommand))
                        .ToList();
                    if (methodAttributes.Any())
                    {
                        bool isTask = IsTaskReturnType(methodSymbol.ReturnType);
                        var realReturnType = isTask ? GetTaskReturnType(compilation, methodSymbol.ReturnType) : methodSymbol.ReturnType;
                        var isReturnTypeVoid = SymbolEqualityComparer.Default.Equals(realReturnType, compilation.GetSpecialType(SpecialType.System_Void));
                        var methodParameters = methodSymbol.Parameters.ToList();
                        if (methodParameters.Count > 1)
                            continue; // Too many parameters, continue

                        classExtensionInfo.CommandExtensionInfos.Add(new()
                        {
                            MethodName = methodSymbol.Name,
                            MethodReturnType = realReturnType,
                            IsTask = isTask,
                            IsReturnTypeVoid = isReturnTypeVoid,
                            ArgumentType = methodParameters.SingleOrDefault()?.Type
                        });
                    }
                }

                if (classExtensionInfo.CommandExtensionInfos.Any())
                    classExtensionInfos.Add(classExtensionInfo);
            }
        }

        return classExtensionInfos;
    }

    private static ITypeSymbol GetTaskReturnType(Compilation compilation, ITypeSymbol typeSymbol)
    {
        if (typeSymbol is INamedTypeSymbol { TypeArguments.Length: 1 } namedTypeSymbol)
            return namedTypeSymbol.TypeArguments[0];
        return compilation.GetSpecialType(SpecialType.System_Void);
    }

    private static bool IsTaskReturnType(ITypeSymbol typeSymbol)
    {
        var nameFormat = SymbolDisplayFormat.FullyQualifiedFormat;
        do
        {
            var typeName = typeSymbol?.ToDisplayString(nameFormat);
            if (typeName == "global::System.Threading.Tasks.Task")
                return true;

            typeSymbol = typeSymbol?.BaseType;
        } while (typeSymbol != null);

        return false;
    }
}