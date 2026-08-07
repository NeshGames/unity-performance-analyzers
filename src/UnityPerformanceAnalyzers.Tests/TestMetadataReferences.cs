using System.IO;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    internal static class TestMetadataReferences
    {
        /// <summary>
        /// Emits an empty in-memory assembly with the given name. Package presence is detected
        /// by assembly name alone, so an empty assembly with the right name is
        /// enough to flip an UpaProfile flag.
        /// </summary>
        public static MetadataReference EmptyAssembly(string assemblyName)
        {
            var compilation = CSharpCompilation.Create(
                assemblyName,
                references: new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            using (var stream = new MemoryStream())
            {
                var result = compilation.Emit(stream);
                Assert.True(result.Success);
                return MetadataReference.CreateFromImage(stream.ToArray());
            }
        }
    }
}
