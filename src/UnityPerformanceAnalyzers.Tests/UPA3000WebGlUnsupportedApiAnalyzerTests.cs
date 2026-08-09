using System.Threading.Tasks;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    public class UPA3000WebGlUnsupportedApiAnalyzerTests
    {
        private static Task VerifyAsync(string source, bool defineWebGlTarget = true)
        {
            // UPA3000~3003 are disabled by default; enable them as a preset would.
            var harness = new RuleHarness
            {
                UnityStubs = false,
                EnabledRules = { "UPA3000", "UPA3001", "UPA3002", "UPA3003" },
            };
            if (defineWebGlTarget)
            {
                harness.Defines.Add(UpaProfile.WebGlDefine);
            }

            return RuleVerifier.VerifyAsync<UPA3000WebGlUnsupportedApiAnalyzer>(source, harness);
        }

        // Test case 1 — UPA3000: construction, static member access, and Task methods
        [Fact]
        public Task Threading_WithDefine_Triggers()
        {
            return VerifyAsync(@"
using System.Threading;
using System.Threading.Tasks;

class C
{
    void M()
    {
        var thread = {|UPA3000:new Thread(() => { })|};
        ThreadPool.{|UPA3000:QueueUserWorkItem|}(_ => { });
        _ = Task.{|UPA3000:Run|}(() => { });
        _ = Task.{|UPA3000:Delay|}(100);
        _ = Task.Factory.{|UPA3000:StartNew|}(() => { });
        Parallel.{|UPA3000:For|}(0, 10, _ => { });
    }
}");
        }

        // Test case 4 — awaiting a plain async method stays allowed
        [Fact]
        public Task PlainAwait_WithDefine_DoesNotTrigger()
        {
            return VerifyAsync(@"
using System.Threading.Tasks;

class C
{
    async Task InnerAsync()
    {
        await Task.CompletedTask;
    }

    async Task M()
    {
        await InnerAsync();
    }
}");
        }

        // Test case 1 — UPA3001: any type in System.Net.Sockets
        [Fact]
        public Task Sockets_WithDefine_Triggers()
        {
            return VerifyAsync(@"
using System.Net.Sockets;

class C
{
    void M()
    {
        var client = {|UPA3001:new TcpClient()|};
        client.{|UPA3001:Connect|}(""localhost"", 80);
    }
}");
        }

        // Test case 1 — UPA3002: File/Directory statics and FileStream construction
        [Fact]
        public Task FileIo_WithDefine_Triggers()
        {
            return VerifyAsync(@"
using System.IO;

class C
{
    void M()
    {
        _ = File.{|UPA3002:ReadAllBytes|}(""save.dat"");
        _ = Directory.{|UPA3002:Exists|}(""saves"");
        var stream = {|UPA3002:new FileStream(""save.dat"", FileMode.Open)|};
        stream.Dispose();
    }
}");
        }

        // Test case 1 — UPA3003
        [Fact]
        public Task Process_WithDefine_Triggers()
        {
            return VerifyAsync(@"
using System.Diagnostics;

class C
{
    void M()
    {
        _ = Process.{|UPA3003:Start|}(""notepad.exe"");
    }
}");
        }

        // Test case 2 — without the define the rules are not registered at all
        [Fact]
        public Task WithoutDefine_DoesNotTrigger()
        {
            return VerifyAsync(@"
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

class C
{
    void M()
    {
        var thread = new Thread(() => { });
        _ = Task.Run(() => { });
        _ = File.ReadAllBytes(""save.dat"");
        _ = Process.Start(""notepad.exe"");
    }
}", defineWebGlTarget: false);
        }

        // Test case 3 — deliberate exclusions stay allowed
        [Fact]
        public Task ExcludedApis_WithDefine_DoNotTrigger()
        {
            return VerifyAsync(@"
using System.IO;
using System.Threading;

class C
{
    int counter;
    readonly object gate = new object();

    void M(CancellationToken token)
    {
        Interlocked.Increment(ref counter);
        lock (gate) { counter++; }
        _ = Path.Combine(""a"", ""b"");
        var stream = new MemoryStream();
        stream.Dispose();
        _ = token.IsCancellationRequested;
    }
}");
        }

        // isEnabledByDefault: false — asserted on the descriptors because the
        // testing framework force-enables disabled-by-default rules when running analyzers.
        [Fact]
        public void Descriptors_AreFourDistinctIds_AllDisabledByDefault()
        {
            var descriptors = new UPA3000WebGlUnsupportedApiAnalyzer().SupportedDiagnostics;
            Assert.Equal(
                new[] { "UPA3000", "UPA3001", "UPA3002", "UPA3003" },
                System.Linq.ImmutableArrayExtensions.Select(descriptors, d => d.Id));
            Assert.All(descriptors, d => Assert.False(d.IsEnabledByDefault));
        }
    }
}
