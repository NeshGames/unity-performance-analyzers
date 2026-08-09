using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace UnityPerformanceAnalyzers
{
    /// <summary>
    /// Questions about an operation that several rules ask, with the answer written down once.
    /// </summary>
    internal static class OperationFacts
    {
        /// <summary>
        /// The operation is the target of a plain <c>=</c>: its old value is replaced and never
        /// read. Rules that report a costly <em>read</em> use this to stay quiet on writes —
        /// and only on writes, because <c>x.name += y</c> reads the property before storing,
        /// and so still costs what the rule is about.
        /// </summary>
        public static bool IsOverwritten(IOperation operation)
            => operation.Parent is ISimpleAssignmentOperation assignment &&
                ReferenceEquals(assignment.Target, operation);

        /// <summary>
        /// The operation is the target of any assignment, compound included. Rules that report
        /// the <em>write</em> itself use this: <c>x.text += y</c> stores a new value and costs
        /// whatever storing costs, exactly like <c>x.text = y</c>.
        /// </summary>
        public static bool IsWritten(IOperation operation)
            => operation.Parent is IAssignmentOperation assignment &&
                ReferenceEquals(assignment.Target, operation);

        /// <summary>
        /// The operation with implicit conversions stripped. A literal passed where an
        /// interface or a wider type is expected arrives wrapped, and the rules care about
        /// what was written, not what it was converted to.
        /// </summary>
        public static IOperation Unwrap(IOperation operation)
        {
            var current = operation;
            while (current is IConversionOperation conversion)
            {
                current = conversion.Operand;
            }

            return current;
        }

        /// <summary>
        /// Where to report a member: on its name, not on the whole expression. Chained calls
        /// nest syntactically, so an outer call's span covers everything before it and a
        /// diagnostic on the expression would underline the entire chain.
        /// </summary>
        /// <remarks>
        /// The bare member-access case is not decoration: UPA3004 reports properties
        /// (<c>task.Result</c>) as well as calls, and the three private copies this replaced
        /// were not in fact identical — only that one had the second branch.
        /// </remarks>
        public static Location MemberNameLocation(SyntaxNode syntax)
        {
            switch (syntax)
            {
                case InvocationExpressionSyntax invocation
                    when invocation.Expression is MemberAccessExpressionSyntax invoked:
                    return invoked.Name.GetLocation();
                case MemberAccessExpressionSyntax memberAccess:
                    return memberAccess.Name.GetLocation();
                default:
                    return syntax.GetLocation();
            }
        }
    }
}
