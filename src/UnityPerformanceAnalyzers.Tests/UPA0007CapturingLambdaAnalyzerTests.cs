using System.Threading.Tasks;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    public class UPA0007CapturingLambdaAnalyzerTests
    {
        private static Task VerifyAsync(string source) =>
            RuleVerifier.VerifyAsync<UPA0007CapturingLambdaAnalyzer>(source);

        // UPA0007 test case 1
        [Fact]
        public Task LocalCapture_InUpdate_Triggers()
        {
            return VerifyAsync(@"
using System;
using UnityEngine;

class C : MonoBehaviour
{
    void Update()
    {
        int count = 0;
        Action a = {|UPA0007:() => count++|};
        a();
    }
}");
        }

        // UPA0007 test case 2 — calling an instance member captures this
        [Fact]
        public Task ThisCapture_InUpdate_Triggers()
        {
            return VerifyAsync(@"
using System;
using UnityEngine;

class C : MonoBehaviour
{
    void Update()
    {
        Action a = {|UPA0007:() => Helper()|};
        a();
    }

    void Helper()
    {
    }
}");
        }

        // UPA0007 test case 3 — capture-free lambdas are cached by the compiler
        [Fact]
        public Task NoCapture_InUpdate_DoesNotTrigger()
        {
            return VerifyAsync(@"
using System;
using UnityEngine;

class C : MonoBehaviour
{
    void Update()
    {
        Func<int> f = () => 1 + 1;
        f();
    }
}");
        }

        // UPA0007 test case 4
        [Fact]
        public Task LocalCapture_InStart_DoesNotTrigger()
        {
            return VerifyAsync(@"
using System;
using UnityEngine;

class C : MonoBehaviour
{
    void Start()
    {
        int count = 0;
        Action a = () => count++;
        a();
    }
}");
        }

        // UPA0007 test case 5
        [Fact]
        public Task LocalCapture_InHotPathAttributedMethod_Triggers()
        {
            return VerifyAsync(@"
using System;

class HotPathAttribute : System.Attribute { }

class C
{
    [HotPath]
    void Tick()
    {
        int count = 0;
        Action a = {|UPA0007:() => count++|};
        a();
    }
}");
        }

        // UPA0007 test case 6
        [Fact]
        public Task AnonymousMethodCapture_InUpdate_Triggers()
        {
            return VerifyAsync(@"
using System;
using UnityEngine;

class C : MonoBehaviour
{
    void Update()
    {
        int count = 0;
        Action a = {|UPA0007:delegate { count++; }|};
        a();
    }
}");
        }
    }
}
