using Xunit;

namespace Milvus.Client.Tests;

public class FunctionSchemaTests
{
    // Ports pymilvus tests/orm/test_schema.py:TestFunctionCreation.test_create_function[bm25].
    [Fact]
    public void Create_bm25_function_sets_all_properties()
    {
        var f = new FunctionSchema(
            "bm25_func",
            MilvusFunctionType.Bm25,
            new[] { "text" },
            new[] { "sparse" });

        Assert.Equal("bm25_func", f.Name);
        Assert.Equal(MilvusFunctionType.Bm25, f.Type);
        Assert.Equal(new[] { "text" }, f.InputFieldNames);
        Assert.Equal(new[] { "sparse" }, f.OutputFieldNames);
    }

    // Covers TestFunctionCreation.test_input_as_string + test_output_as_string (shape-adapted:
    // C# static typing wraps single values into single-element arrays via the factory).
    [Fact]
    public void CreateBm25_factory_maps_single_input_and_output()
    {
        var f = FunctionSchema.CreateBm25("f", inputFieldName: "input", outputFieldName: "output");

        Assert.Single(f.InputFieldNames);
        Assert.Equal("input", f.InputFieldNames[0]);
        Assert.Single(f.OutputFieldNames);
        Assert.Equal("output", f.OutputFieldNames[0]);
    }

    // Ports TestFunctionValidation.test_invalid_name_type[none_name].
    [Fact]
    public void Throws_for_null_name()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new FunctionSchema(null!, MilvusFunctionType.Bm25, new[] { "in" }, new[] { "out" }));
    }

    // C#-specific adaptation of test_invalid_name_type[int_name] — Python tests a type-error;
    // C# enforces non-whitespace via Verify.NotNullOrWhiteSpace, which is stricter.
    [Fact]
    public void Throws_for_whitespace_name()
    {
        Assert.Throws<ArgumentException>(() =>
            new FunctionSchema("", MilvusFunctionType.Bm25, new[] { "in" }, new[] { "out" }));
    }

    // Ports part of TestFunctionValidation.test_invalid_input_field_names_type (null variant).
    [Fact]
    public void Throws_for_null_input_field_names()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new FunctionSchema("f", MilvusFunctionType.Bm25, null!, new[] { "out" }));
    }

    // Adaptation of TestFunctionValidation.test_invalid_input_field_names_type (empty variant).
    [Fact]
    public void Throws_for_empty_input_field_names()
    {
        Assert.Throws<ArgumentException>(() =>
            new FunctionSchema("f", MilvusFunctionType.Bm25, Array.Empty<string>(), new[] { "out" }));
    }

    // Ports part of TestFunctionValidation.test_invalid_output_field_names_type (null variant).
    [Fact]
    public void Throws_for_null_output_field_names()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new FunctionSchema("f", MilvusFunctionType.Bm25, new[] { "in" }, null!));
    }

    // Adaptation of TestFunctionValidation.test_invalid_output_field_names_type (empty variant).
    [Fact]
    public void Throws_for_empty_output_field_names()
    {
        Assert.Throws<ArgumentException>(() =>
            new FunctionSchema("f", MilvusFunctionType.Bm25, new[] { "in" }, Array.Empty<string>()));
    }

    // Ports TestFunctionSchema.test_function_schema_with_params.
    [Fact]
    public void Parameters_dictionary_accepts_arbitrary_key_values()
    {
        var f = FunctionSchema.CreateBm25("f", "in", "out");
        f.Parameters["k1"] = "v1";
        f.Parameters["k2"] = "v2";

        Assert.Equal(2, f.Parameters.Count);
        Assert.Equal("v1", f.Parameters["k1"]);
        Assert.Equal("v2", f.Parameters["k2"]);
    }

    // Ports TestFunctionSchema.test_function_schema_basic_init (Description carrier).
    [Fact]
    public void Description_is_preserved_when_set()
    {
        var f = new FunctionSchema(
            "test_function",
            MilvusFunctionType.Bm25,
            new[] { "in" },
            new[] { "out" },
            description: "Test function");

        Assert.Equal("Test function", f.Description);
    }

    // C#-specific invariant introduced by the DescribeAsync round-trip fix.
    // Function id is server-assigned; user-constructed instances default to zero.
    [Fact]
    public void FunctionId_is_zero_when_user_constructed()
    {
        var f = FunctionSchema.CreateBm25("f", "in", "out");
        Assert.Equal(0L, f.FunctionId);
    }
}
