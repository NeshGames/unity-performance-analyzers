using System;
using System.IO;
using System.Linq;
using UnityPerformanceAnalyzers.Cli;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    /// <summary>
    /// Covers --init-args: turning what Unity compiled an assembly with into the response file
    /// this tool needs to gate on it.
    /// </summary>
    /// <remarks>
    /// The fixture is a synthetic Unity project rather than the sandbox, because the input is
    /// under <c>Library/</c> and nothing there is committed. What the sandbox proves — that a
    /// real assembly analyzes with zero compile errors — is recorded in the specification; what
    /// these assert is the shape of the file and the partitioning that made it possible.
    /// </remarks>
    public sealed class InitArgsTests : IDisposable
    {
        private const string Probe = @"
using UnityEngine;

public class Probe : MonoBehaviour
{
    void Update()
    {
        var body = GetComponent<Rigidbody>();
    }
}";

        private readonly string _root;
        private readonly string _project;
        private readonly string _unityManaged;

        public InitArgsTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "upa-init-args-" + Guid.NewGuid().ToString("N"));
            _project = Path.Combine(_root, "Project");
            _unityManaged = Path.Combine(_root, "UnityInstall", "Editor", "Data", "Managed", "UnityEngine");
            Directory.CreateDirectory(_unityManaged);
        }

        public void Dispose() => Directory.Delete(_root, recursive: true);

        private string OutputPath => Path.Combine(_root, "upa-args.rsp");

        /// <summary>
        /// Writes a Unity project whose Bee artifacts hold one assembly's compile arguments,
        /// shaped exactly as Unity writes them: quoted paths, one option per line.
        /// </summary>
        private string BuildProject(string assembly = "Assembly-CSharp", string dagName = "abc123.dag")
        {
            var dag = Path.Combine(_project, "Library", "Bee", "artifacts", dagName);
            Directory.CreateDirectory(dag);
            Directory.CreateDirectory(Path.Combine(_project, "Assets"));
            File.WriteAllText(Path.Combine(_project, "Assets", "Probe.cs"), Probe);

            var unityDll = Path.Combine(_unityManaged, "UnityEngine.dll").Replace('\\', '/');
            var shim = Path.Combine(_root, "UnityInstall", "Editor", "Data", "NetStandard",
                "compat", "2.1.0", "shims", "netstandard", "System.dll").Replace('\\', '/');

            var rsp = string.Join(Environment.NewLine, new[]
            {
                "-target:library",
                $"-out:\"Library/Bee/artifacts/{dagName}/{assembly}.dll\"",
                "-langversion:9.0",
                "-define:UNITY_2022_3_OR_NEWER",
                "-define:UPA_TARGET_WEBGL",
                $"-r:\"Library/Bee/artifacts/{dagName}/UniTask.ref.dll\"",
                $"-r:\"{unityDll}\"",
                $"-r:\"{shim}\"",
                "\"Assets/Probe.cs\"",
            });

            File.WriteAllText(Path.Combine(dag, assembly + ".rsp"), rsp);
            return dag;
        }

        private static (int ExitCode, string Stdout, string Stderr) Run(params string[] args)
        {
            var stdout = new StringWriter();
            var stderr = new StringWriter();
            var exitCode = CliEntryPoint.Run(args, stdout, stderr);
            return (exitCode, stdout.ToString(), stderr.ToString());
        }

        private string[] Generate(params string[] extra)
        {
            var args = new[] { "--init-args", OutputPath, "--project", _project }.Concat(extra).ToArray();
            var (exitCode, _, stderr) = Run(args);

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(OutputPath), stderr);
            return File.ReadAllLines(OutputPath);
        }

        // Case 60
        [Fact]
        public void Generates_AndRecordsWhereItCameFrom()
        {
            BuildProject();

            var lines = Generate();

            var header = lines.TakeWhile(l => l.StartsWith("#", StringComparison.Ordinal)).ToArray();
            Assert.Contains(header, l => l.Contains("upa-cli " + AnalyzerCatalog.ToolVersion, StringComparison.Ordinal));
            Assert.Contains(header, l => l.Contains("Assembly-CSharp.rsp", StringComparison.Ordinal));

            // A stale file is only diagnosable if it says when it was made.
            Assert.Contains(header, l => l.Contains("generated 20", StringComparison.Ordinal));
        }

        // Case 61
        [Fact]
        public void CarriesTheArgumentsAGateNeeds()
        {
            BuildProject();

            var lines = Generate();

            Assert.Equal("Assembly-CSharp", After(lines, "--assembly-name"));
            Assert.Contains("--whole-assembly", lines);
            Assert.Contains("UNITY_2022_3_OR_NEWER", Values(lines, "--define"));
            Assert.Contains("UPA_TARGET_WEBGL", Values(lines, "--define"));
            Assert.Contains("Library/Bee/artifacts/abc123.dag/UniTask.ref.dll", Values(lines, "--reference"));

            // The source list is what is left once the options are paired off.
            Assert.Contains("Assets/Probe.cs", lines);
        }

        // Case 62 - the measured reason this partitioning exists: passing Unity's netstandard
        // shims through leaves Roslyn unable to choose a core library, and every file fails
        // with "Predefined type 'System.Void' is not defined".
        [Fact]
        public void FrameworkReferences_AreDropped()
        {
            BuildProject();

            var lines = Generate();

            Assert.DoesNotContain(lines, l => l.Contains("shims", StringComparison.Ordinal));
            Assert.DoesNotContain(lines, l => l.EndsWith("netstandard/System.dll", StringComparison.Ordinal));
        }

        // Case 63
        [Fact]
        public void UnityModules_BecomeOneDirectory()
        {
            BuildProject();

            var lines = Generate();

            var directories = Values(lines, "--unity-dll-dir");
            Assert.Single(directories);
            Assert.EndsWith("Managed/UnityEngine", directories[0].Replace('\\', '/'), StringComparison.Ordinal);

            // Listed as a directory, not as one reference per module: the modules live in the
            // editor installation, so no project-relative path can reach them.
            Assert.DoesNotContain(Values(lines, "--reference"), r => r.Contains("UnityEngine.dll", StringComparison.Ordinal));
        }

        // Case 64 - the acceptance criterion: the generated file is accepted by the tool that
        // generated it, and the run reaches the analyzers rather than dying in setup.
        [Fact]
        public void RoundTrips_ThroughTheToolThatWroteIt()
        {
            var dag = BuildProject();
            var stub = typeof(UnityEngine.MonoBehaviour).Assembly.Location;
            File.Copy(stub, Path.Combine(_unityManaged, "UnityEngine.dll"));

            // Every reference the file names has to be there, or the run refuses: a missing
            // one is a stale argument file, not a package to fake.
            File.Copy(stub, Path.Combine(dag, "UniTask.ref.dll"));

            Generate();

            var previous = Directory.GetCurrentDirectory();
            try
            {
                // The header says to run from the project root, and the paths inside are
                // relative to it. This asserts that instruction is the true one.
                Directory.SetCurrentDirectory(_project);
                var (exitCode, stdout, stderr) = Run("@" + OutputPath, "--format", "json", "--fail-on", "none");

                Assert.Equal(0, exitCode);
                Assert.DoesNotContain("compile error", stderr);
                Assert.Contains("UPA0001", stdout);
            }
            finally
            {
                Directory.SetCurrentDirectory(previous);
            }
        }

        // Case 64b - what the round trip found the first time it ran: a reference path that
        // has gone missing used to be taken for a package name, which satisfies the activation
        // check and resolves nothing. The rules needing that package then report nothing, and
        // a run that analyzed almost none of the code exits 0.
        [Fact]
        public void MissingReferencePath_IsRefusedRatherThanFaked()
        {
            BuildProject();
            File.Copy(
                typeof(UnityEngine.MonoBehaviour).Assembly.Location,
                Path.Combine(_unityManaged, "UnityEngine.dll"));

            Generate();

            var previous = Directory.GetCurrentDirectory();
            try
            {
                Directory.SetCurrentDirectory(_project);
                var (exitCode, _, stderr) = Run("@" + OutputPath, "--fail-on", "none");

                Assert.Equal(2, exitCode);
                Assert.Contains("UniTask.ref.dll", stderr);
                Assert.Contains("regenerate", stderr, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                Directory.SetCurrentDirectory(previous);
            }
        }

        // Case 65
        [Fact]
        public void NoUnityArguments_RefusesAndSaysHow()
        {
            Directory.CreateDirectory(Path.Combine(_project, "Assets"));

            var (exitCode, stdout, stderr) = Run("--init-args", OutputPath, "--project", _project);

            Assert.Equal(2, exitCode);
            Assert.Contains("compile once", stderr);
            Assert.Equal(string.Empty, stdout);
            Assert.False(File.Exists(OutputPath));
        }

        // Case 66
        [Fact]
        public void CombiningModes_IsAUsageError()
        {
            BuildProject();

            var withFiles = Run("--init-args", OutputPath, "--project", _project, "Probe.cs");
            var withCatalog = Run("--init-args", OutputPath, "--project", _project, "--list-rules");

            Assert.Equal(2, withFiles.ExitCode);
            Assert.Equal(2, withCatalog.ExitCode);
            Assert.Contains("--init-args", withFiles.Stderr);
            Assert.Contains("--init-args", withCatalog.Stderr);
        }

        // Case 67 - artifact directories accumulate across editor versions and configurations,
        // and an older one describes a build nobody is running.
        [Fact]
        public void SeveralArtifactDirectories_TakesTheNewest()
        {
            var stale = BuildProject(dagName: "old.dag");
            var current = BuildProject(dagName: "new.dag");

            var staleFile = Path.Combine(stale, "Assembly-CSharp.rsp");
            var currentFile = Path.Combine(current, "Assembly-CSharp.rsp");
            File.SetLastWriteTimeUtc(staleFile, DateTime.UtcNow.AddDays(-3));
            File.SetLastWriteTimeUtc(currentFile, DateTime.UtcNow);

            var lines = Generate();

            Assert.Contains(lines, l => l.Contains("new.dag", StringComparison.Ordinal));
            Assert.DoesNotContain(lines, l => l.Contains("old.dag", StringComparison.Ordinal));
        }

        private static string After(string[] lines, string option)
        {
            var index = Array.IndexOf(lines, option);
            Assert.True(index >= 0 && index + 1 < lines.Length, option + " is not in the generated file");
            return lines[index + 1];
        }

        /// <summary>Every value given to one repeated option, in order.</summary>
        private static string[] Values(string[] lines, string option) =>
            lines.Select((line, index) => (line, index))
                .Where(pair => pair.line == option && pair.index + 1 < lines.Length)
                .Select(pair => lines[pair.index + 1])
                .ToArray();
    }
}
