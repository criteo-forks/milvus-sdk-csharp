using Xunit;

namespace Milvus.Client.Tests;

[Collection("Milvus")]
public class TextMatchTests(MilvusFixture milvusFixture) : IDisposable
{
    private readonly MilvusClient Client = milvusFixture.CreateClient();

    [Fact]
    public async Task EnableMatch_survives_describe_and_allows_TEXT_MATCH()
    {
        MilvusCollection collection = await CreateBrandCollection(
            nameof(EnableMatch_survives_describe_and_allows_TEXT_MATCH), enableMatch: true);

        MilvusCollectionDescription description = await collection.DescribeAsync();
        FieldSchema brand = description.Schema.Fields.Single(f => f.Name == "brand");
        Assert.True(brand.EnableAnalyzer);
        Assert.True(brand.EnableMatch);

        // The standard analyzer lowercases, so "zarvelo" matches "Zarvelo" and "ZARVELO Shoes".
        Assert.Equal(new[] { 1L, 2L }, await QueryIds(collection, "TEXT_MATCH(brand, 'zarvelo')"));
        // Word match, not substring: "north" and "face" are both words of "The North Face" only.
        Assert.Equal(new[] { 4L }, await QueryIds(
            collection, "TEXT_MATCH(brand, 'north') and TEXT_MATCH(brand, 'face')"));
    }

    [Fact]
    public async Task TEXT_MATCH_is_rejected_without_EnableMatch()
    {
        MilvusCollection collection = await CreateBrandCollection(
            nameof(TEXT_MATCH_is_rejected_without_EnableMatch), enableMatch: false);

        FieldSchema brand = (await collection.DescribeAsync()).Schema.Fields.Single(f => f.Name == "brand");
        Assert.False(brand.EnableMatch);

        await Assert.ThrowsAsync<MilvusException>(() => QueryIds(collection, "TEXT_MATCH(brand, 'zarvelo')"));
    }

    private async Task<MilvusCollection> CreateBrandCollection(string name, bool enableMatch)
    {
        MilvusCollection collection = Client.GetCollection(name);
        await collection.DropAsync();

        await Client.CreateCollectionAsync(
            name,
            new[]
            {
                FieldSchema.Create<long>("id", isPrimaryKey: true),
                FieldSchema.CreateFloatVector("float_vector", 2),
                FieldSchema.CreateVarchar("brand", maxLength: 200, enableAnalyzer: true, enableMatch: enableMatch),
            });

        await collection.CreateIndexAsync("float_vector", IndexType.Flat, SimilarityMetricType.L2);
        await collection.InsertAsync(
            new FieldData[]
            {
                FieldData.Create("id", new[] { 1L, 2L, 3L, 4L }),
                FieldData.CreateFloatVector("float_vector", new ReadOnlyMemory<float>[]
                {
                    new[] { 1f, 2f }, new[] { 3f, 4f }, new[] { 5f, 6f }, new[] { 7f, 8f }
                }),
                FieldData.CreateVarChar("brand", new[] { "Zarvelo", "ZARVELO Shoes", "Nike", "The North Face" }),
            });
        await collection.FlushAsync();
        await collection.LoadAsync();
        await collection.WaitForCollectionLoadAsync(
            waitingInterval: TimeSpan.FromMilliseconds(100), timeout: TimeSpan.FromMinutes(1));
        return collection;
    }

    private static async Task<long[]> QueryIds(MilvusCollection collection, string expression)
    {
        IReadOnlyList<FieldData> fields = await collection.QueryAsync(
            expression,
            new QueryParameters { OutputFields = { "id" }, ConsistencyLevel = ConsistencyLevel.Strong });
        return ((FieldData<long>)fields.Single(f => f.FieldName == "id")).Data.OrderBy(id => id).ToArray();
    }

    public void Dispose() => Client.Dispose();
}
