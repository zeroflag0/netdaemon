using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CSharp.RuntimeBinder;
using Microsoft.Extensions.Logging.Abstractions;

namespace NetDaemon.AppModel.Internal.Compiler;

internal record CompiledAssemblyResult(CollectableAssemblyLoadContext AssemblyContext, Assembly CompiledAssembly);

internal class Compiler : ICompiler
{
    private readonly ILogger<Compiler> _logger;
    private readonly bool _useDebug;
    private readonly ISyntaxTreeResolver _syntaxResolver;

    public Compiler(
        ISyntaxTreeResolver syntaxResolver,
        ILogger<Compiler> logger,
        IOptions<CompileSettings> compileSettings)
    {
        _syntaxResolver = syntaxResolver;
        _logger = logger;
        _useDebug = compileSettings.Value.UseDebug;
    }

    public CompiledAssemblyResult Compile()
    {
        CollectableAssemblyLoadContext context = new();

        var compilation = GetSharpCompilation();

        using var peStream = new MemoryStream();
        using MemoryStream? symStream = _useDebug ? new MemoryStream() : null;

        var emitResult = compilation.Emit(peStream, symStream);

        if (emitResult.Success)
        {
            peStream.Seek(0, SeekOrigin.Begin);
            symStream?.Seek(0, SeekOrigin.Begin);
            var assembly = context.LoadFromStream(peStream, symStream);
            return new CompiledAssemblyResult(context, assembly);
        }

        var error = PrettyPrintCompileError(emitResult);
        _logger.LogError("Failed to compile applications\n{Error}", error);

        context.Unload();
        // Finally do cleanup and release memory
        GC.Collect();
        GC.WaitForPendingFinalizers();
        throw new InvalidOperationException();
    }

    public void Dispose()
    {
    }

    private CSharpCompilation GetSharpCompilation()
    {
        var syntaxTrees = _syntaxResolver.GetSyntaxTrees();
        var metaDataReference = GetDefaultReferences();

        return CSharpCompilation.Create(
            $"daemon_apps_{Path.GetRandomFileName()}.dll",
            syntaxTrees.ToArray(),
            metaDataReference.ToArray(),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: _useDebug ? OptimizationLevel.Debug : OptimizationLevel.Release,
                assemblyIdentityComparer: DesktopAssemblyIdentityComparer.Default,
                platform: Platform.AnyCpu
            )
        );
    }

    private static IEnumerable<MetadataReference> GetDefaultReferences()
    {
        var metaDataReference = new List<MetadataReference>(10)
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Regex).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(DisplayAttribute).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(INotifyPropertyChanged).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(DynamicExpression).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(NullLogger).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(AssemblyTargetedPatchBandAttribute).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(CSharpArgumentInfo).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(RuntimeBinderException).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Observable).Assembly.Location)
        };

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            if (!assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
                metaDataReference.Add(MetadataReference.CreateFromFile(assembly.Location));

        metaDataReference.Add(MetadataReference.CreateFromFile(Assembly.GetEntryAssembly()?.Location!));

        return metaDataReference;
    }

    private static string PrettyPrintCompileError(EmitResult emitResult)
    {
        var msg = new StringBuilder();

        foreach (var emitResultDiagnostic in emitResult.Diagnostics.Where(emitResultDiagnostic =>
                     emitResultDiagnostic.Severity == DiagnosticSeverity.Error))
            msg.AppendLine(emitResultDiagnostic.ToString());

        return msg.ToString();
    }
}
