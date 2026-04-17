using System.Reflection;
using Xunit;

namespace Milvus.Client.Tests;

/// <summary>
/// Ensures that IMilvusClient and IMilvusCollection stay in sync with their concrete implementations.
/// These tests do not require a running Milvus server and serve as a compile-time drift guard.
/// </summary>
public class InterfaceParityTests
{
    [Fact]
    public void IMilvusCollection_contains_all_public_methods_of_MilvusCollection()
    {
        AssertInterfaceContainsAllPublicMethods(typeof(MilvusCollection), typeof(IMilvusCollection));
    }

    [Fact]
    public void IMilvusClient_contains_all_public_methods_of_MilvusClient()
    {
        AssertInterfaceContainsAllPublicMethods(typeof(MilvusClient), typeof(IMilvusClient));
    }

    private static void AssertInterfaceContainsAllPublicMethods(Type concreteType, Type interfaceType)
    {
        // Collect all interface members (including inherited ones like IDisposable)
        HashSet<string> interfaceSignatures = interfaceType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Concat(interfaceType.GetInterfaces().SelectMany(i => i.GetMethods()))
            .Select(GetMethodSignature)
            .ToHashSet(StringComparer.Ordinal);

        // Collect public instance methods on the concrete type, excluding:
        //  - Methods inherited from System.Object (ToString, Equals, GetHashCode, GetType)
        //  - Compiler-generated / property accessors
        var missing = concreteType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName)                          // skip get_/set_/add_/remove_
            .Where(m => m.DeclaringType != typeof(object))
            .Where(m => !interfaceSignatures.Contains(GetMethodSignature(m)))
            .Select(GetMethodSignature)
            .OrderBy(s => s)
            .ToList();

        Assert.True(
            missing.Count == 0,
            $"The following public methods of {concreteType.Name} are missing from {interfaceType.Name}:\n" +
            string.Join("\n", missing.Select(s => "  - " + s)));
    }

    /// <summary>
    /// Produces a normalised string that uniquely identifies a method signature for parity comparison.
    /// </summary>
    private static string GetMethodSignature(MethodInfo m)
    {
        string generics = m.IsGenericMethod
            ? $"`{m.GetGenericArguments().Length}"
            : string.Empty;

        string parameters = string.Join(", ", m.GetParameters().Select(p =>
        {
            // Normalise generic parameter types so T on the concrete side matches T on the interface side
            string typeName = p.ParameterType.IsGenericParameter
                ? p.ParameterType.Name
                : p.ParameterType.FullName ?? p.ParameterType.Name;
            return typeName;
        }));

        return $"{m.Name}{generics}({parameters})";
    }
}

