using Xunit;

namespace Milvus.Client.Tests;

public class FieldSchemaAnalyzerTests
{
    // Baseline expectation from TestFieldSchemaTypeParams.test_analyzer_params_dict —
    // analyzer flags are off and params null unless the caller opts in.
    [Fact]
    public void CreateVarchar_disables_analyzer_by_default()
    {
        var field = FieldSchema.CreateVarchar("text", maxLength: 256);

        Assert.False(field.EnableAnalyzer);
        Assert.Null(field.AnalyzerParams);
    }

    // Ports TestFieldSchemaTypeParams.test_analyzer_params_dict (opted-in path, no params).
    [Fact]
    public void CreateVarchar_with_enableAnalyzer_true_sets_flag()
    {
        var field = FieldSchema.CreateVarchar(
            "text",
            maxLength: 256,
            enableAnalyzer: true);

        Assert.True(field.EnableAnalyzer);
        Assert.Null(field.AnalyzerParams);
    }

    // Shape-adapted port of test_analyzer_params_dict — Python tests dict-to-JSON
    // serialisation; C# takes the JSON string verbatim (the SDK does not validate it).
    [Fact]
    public void CreateVarchar_accepts_JSON_analyzer_params_verbatim()
    {
        const string analyzerJson = "{\"tokenizer\":\"standard\"}";
        var field = FieldSchema.CreateVarchar(
            "text",
            maxLength: 256,
            enableAnalyzer: true,
            analyzerParams: analyzerJson);

        Assert.True(field.EnableAnalyzer);
        Assert.Equal(analyzerJson, field.AnalyzerParams);
    }
}
