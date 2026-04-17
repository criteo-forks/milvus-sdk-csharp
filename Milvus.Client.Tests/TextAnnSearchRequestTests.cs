using Xunit;

namespace Milvus.Client.Tests;

public class TextAnnSearchRequestTests
{
    [Fact]
    public void Stores_query_texts()
    {
        var request = new TextAnnSearchRequest(
            "text_sparse",
            ["white headphones", "quiet and comfortable"],
            limit: 5);

        Assert.Equal("text_sparse", request.VectorFieldName);
        Assert.Equal(SimilarityMetricType.Bm25, request.MetricType);
        Assert.Equal(5, request.Limit);
        Assert.Equal(["white headphones", "quiet and comfortable"], request.QueryTexts);
    }

    [Fact]
    public void Single_query()
    {
        var request = new TextAnnSearchRequest(
            "text_sparse",
            ["search query"],
            limit: 10);

        Assert.Single(request.QueryTexts);
        Assert.Equal("search query", request.QueryTexts[0]);
    }

    [Fact]
    public void Supports_expression_filter()
    {
        var request = new TextAnnSearchRequest(
            "text_sparse",
            ["query"],
            limit: 5)
        {
            Expression = "id > 10"
        };

        Assert.Equal("id > 10", request.Expression);
    }

    [Fact]
    public void Supports_extra_parameters()
    {
        var request = new TextAnnSearchRequest(
            "text_sparse",
            ["query"],
            limit: 5)
        {
            ExtraParameters =
            {
                ["drop_ratio_search"] = "0.2"
            }
        };

        Assert.Equal("0.2", request.ExtraParameters["drop_ratio_search"]);
    }

    [Fact]
    public void Throws_for_null_query_texts()
    {
        Assert.Throws<ArgumentNullException>(() => new TextAnnSearchRequest(
            "text_sparse",
            null!,
            limit: 5));
    }

    [Fact]
    public void Throws_for_empty_query_texts()
    {
        Assert.Throws<ArgumentException>(() => new TextAnnSearchRequest(
            "text_sparse",
            Array.Empty<string>(),
            limit: 5));
    }

    [Fact]
    public void Throws_for_invalid_limit()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TextAnnSearchRequest(
            "text_sparse",
            ["query"],
            limit: 0));
    }

    [Fact]
    public void Throws_for_empty_field_name()
    {
        Assert.Throws<ArgumentException>(() => new TextAnnSearchRequest(
            "",
            ["query"],
            limit: 5));
    }
}
