using System.Reflection;
using System.Text.Json.Nodes;
using Kkdev92.StackChan.Gateway.Abstractions;
using Microsoft.Extensions.AI;

namespace Kkdev92.StackChan.Gateway.AgentFramework.Tools;

/// <summary>Projects capabilities to Agent Framework tools.</summary>
/// <remarks>
/// This adapter keeps the abstraction layer independent of Agent Framework. It uses reflection to
/// find public instance methods marked with <see cref="CapabilityActionAttribute"/>.
/// </remarks>
internal static class CapabilityToolProjector
{
    /// <summary>Represents projected tools and prefetch triggers.</summary>
    /// <param name="Tools">The tools passed to the model.</param>
    /// <param name="Triggers">Trigger phrases by tool name. Tools without declared triggers are omitted.</param>
    public sealed record Projection(
        IReadOnlyList<AITool> Tools,
        IReadOnlyDictionary<string, IReadOnlyList<string>> Triggers);

    /// <summary>Projects capabilities to Agent Framework tools.</summary>
    /// <param name="capabilities">The capabilities to project.</param>
    /// <exception cref="InvalidOperationException">
    /// A tool name is duplicated or a method has an unsupported signature.
    /// </exception>
    public static Projection Project(IEnumerable<ICapability> capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);

        var tools = new List<AITool>();
        var triggers = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var capability in capabilities)
        {
            var type = capability.GetType();

            // Include static methods so a misplaced attribute is reported as a validation error.
            foreach (var method in type.GetMethods(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                var action = method.GetCustomAttribute<CapabilityActionAttribute>();

                if (action is null)
                {
                    continue;
                }

                Validate(type, method, action);

                if (!names.Add(action.Name))
                {
                    throw new InvalidOperationException(
                        $"Capability action '{action.Name}' is declared more than once.");
                }

                tools.Add(AIFunctionFactory.Create(
                    method,
                    capability,
                    new AIFunctionFactoryOptions
                    {
                        Name = action.Name,
                        Description = action.Description,
                        JsonSchemaCreateOptions = LocalModelSchema,
                    }));

                // Prefetch is driven only by words in the utterance, so restrict it to side-effect-free
                // capabilities. The model can still invoke write operations as regular tools.
                if (action.Triggers.Length > 0 && action.IsReadOnly)
                {
                    triggers[action.Name] = [.. action.Triggers];
                }
            }
        }

        return new Projection(tools, triggers);
    }

    private static readonly AIJsonSchemaCreateOptions LocalModelSchema = new()
    {
        TransformSchemaNode = CollapseNullableType,
    };

    /// <summary>Converts <c>"type": ["string","null"]</c> to a single type.</summary>
    /// <remarks>
    /// Some llama.cpp-based endpoints reject schemas whose <c>type</c> is an array. This removes only
    /// <c>"null"</c>; the presence or absence of <c>required</c> continues to represent optionality.
    /// </remarks>
    private static JsonNode CollapseNullableType(AIJsonSchemaCreateContext context, JsonNode node)
    {
        if (node is not JsonObject schema ||
            !schema.TryGetPropertyValue("type", out var type) ||
            type is not JsonArray union)
        {
            return node;
        }

        var single = union
            .OfType<JsonValue>()
            .Select(value => value.GetValue<string>())
            .FirstOrDefault(value => !string.Equals(value, "null", StringComparison.Ordinal));

        if (single is not null)
        {
            schema["type"] = single;
        }

        return node;
    }

    /// <summary>Validates that a method has a signature supported for tool calls.</summary>
    /// <remarks>
    /// Configuration problems are detected at application startup instead of during the first tool call.
    /// </remarks>
    private static void Validate(Type type, MethodInfo method, CapabilityActionAttribute action)
    {
        var where = $"{type.Name}.{method.Name}";

        if (method.IsStatic)
        {
            throw new InvalidOperationException(
                $"{where} is static. Capability actions must be instance methods.");
        }

        if (method.IsGenericMethodDefinition)
        {
            throw new InvalidOperationException(
                $"{where} is generic. Capability actions must not be generic.");
        }

        if (string.IsNullOrWhiteSpace(action.Description))
        {
            throw new InvalidOperationException($"{where} has no description.");
        }

        var parameters = method.GetParameters();

        for (var index = 0; index < parameters.Length; index++)
        {
            var parameter = parameters[index];

            if (parameter.ParameterType.IsByRef)
            {
                throw new InvalidOperationException(
                    $"{where} has a ref or out parameter ({parameter.Name}).");
            }

            if (parameter.ParameterType == typeof(CancellationToken) &&
                index != parameters.Length - 1)
            {
                throw new InvalidOperationException(
                    $"{where} takes a cancellation token that is not the last parameter.");
            }
        }
    }
}
