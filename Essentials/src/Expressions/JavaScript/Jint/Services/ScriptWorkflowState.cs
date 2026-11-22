using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Exceptions;
using Elsa.Expressions.JavaScript.Jint.Contracts;
using Jint;
using Jint.Native;
using Jint.Runtime.Descriptors;
using Jint.Runtime.Interop;

namespace Elsa.Expressions.JavaScript.Jint.Services;

/// <summary>Installs host functions, never CLR proxies, into one body evaluation.</summary>
internal static class ScriptWorkflowState
{
    internal static void Install(Engine engine, IScriptValueEvaluation values, IScriptWorkflowState state,
        IJavaScriptPrecisionPolicy precision)
    {
        var root = engine.Intrinsics.Object.Construct([]);
        var readNumber = 0;
        void Refuse(string reason) => state.Refuse("VF-STATE-001", reason);
        void Arity(JsValue[] args, int expected)
        {
            if (args.Length != expected) Refuse("Workflow-state methods require their exact argument count.");
        }
        string Name(JsValue value)
        {
            if (!value.IsString() || string.IsNullOrWhiteSpace(value.AsString()))
                Refuse("An output name must be a nonblank string.");
            return value.AsString();
        }
        string? Identity(JsValue value)
        {
            if (value.IsNull()) return null;
            if (!value.IsString()) Refuse("An identity requires a string or explicit null.");
            return value.AsString();
        }
        void Method(string name, Func<JsValue[], JsValue> call)
        {
            var function = new ClrFunction(engine, name, (_, args) =>
            {
                using var scriptWork = EvaluationLimits.CountScriptWork(engine);
                return call(args);
            });
            root.DefineOwnProperty(name, new PropertyDescriptor(function, writable: false, enumerable: true, configurable: false));
        }
        Method("getOutput", args =>
        {
            Arity(args, 1);
            var name = Name(args[0]);
            return state.TryGetOutput(name, out var json)
                ? values.Enter($"workflowState.output:{++readNumber}", json)
                : JsValue.Undefined;
        });
        Method("setOutput", args =>
        {
            Arity(args, 2);
            var name = Name(args[0]);
            try
            {
                // The bridge runs toJSON/getters once, under the same evaluation limits and precision policy.
                var written = JavaScriptValueWrites.Apply(values.Write(args[1], "workflow output"), "workflow output", precision);
                if (written.Value is not { } json)
                    Refuse("An output requires an explicit serializable JSON value.");
                else state.SetOutput(name, json);
            }
            catch (JavaScriptPrecisionException exception)
            {
                state.Refuse(exception.Code, exception.ValueFreeMessage);
                throw;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                state.Refuse("VF-STATE-001", "The workflow output could not be encoded as an explicit JSON value.");
                throw;
            }
            return JsValue.Undefined;
        });
        Method("getCorrelationId", args => { Arity(args, 0); return state.CorrelationId is { } value ? value : JsValue.Null; });
        Method("getInstanceName", args => { Arity(args, 0); return state.InstanceName is { } value ? value : JsValue.Null; });
        Method("setCorrelationId", args => { Arity(args, 1); state.CorrelationId = Identity(args[0]); return JsValue.Undefined; });
        Method("setInstanceName", args => { Arity(args, 1); state.InstanceName = Identity(args[0]); return JsValue.Undefined; });
        Method("requireExactIdentity", args =>
        {
            Arity(args, 1);
            var value = Identity(args[0]);
            if (value is not null && string.IsNullOrWhiteSpace(value))
                state.Refuse("VF-STATE-005", "Identity normalization would change this value. Use explicit null or a nonblank identity.");
            return args[0];
        });
        root.PreventExtensions();
        engine.Global.DefineOwnProperty("workflowState", new PropertyDescriptor(root, writable: false, enumerable: false, configurable: false));
    }
}
