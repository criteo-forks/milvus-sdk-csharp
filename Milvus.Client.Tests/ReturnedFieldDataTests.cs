using Xunit;

namespace Milvus.Client.Tests;

public class ReturnedFieldDataTests
{
    [Theory]
    [InlineData(MilvusDataType.Bool)]
    [InlineData(MilvusDataType.Int8)]
    [InlineData(MilvusDataType.Int16)]
    [InlineData(MilvusDataType.Int32)]
    [InlineData(MilvusDataType.Int64)]
    [InlineData(MilvusDataType.Float)]
    [InlineData(MilvusDataType.Double)]
    [InlineData(MilvusDataType.String)]
    [InlineData(MilvusDataType.VarChar)]
    [InlineData(MilvusDataType.Json)]
    [InlineData(MilvusDataType.Array)]
    [InlineData(MilvusDataType.BinaryVector)]
    [InlineData(MilvusDataType.FloatVector)]
    [InlineData(MilvusDataType.Float16Vector)]
    [InlineData(MilvusDataType.SparseFloatVector)]
    public void Search_with_no_hits_and_data_less_field_returns_empty_fields(MilvusDataType type)
    {
        Grpc.SearchResults response = CreateEmptySearchResponse(
            new Grpc.FieldData { FieldName = "field", Type = (Grpc.DataType)type });

        SearchResults results = MilvusCollection.FromGrpcSearchResults(response);

        Assert.Empty(results.FieldsData);
        Assert.Empty(results.Scores);
        Assert.Null(results.Ids.LongIds);
        Assert.Null(results.Ids.StringIds);
    }

    [Fact]
    public void Search_with_no_hits_and_data_less_dynamic_field_returns_empty_fields()
    {
        Grpc.SearchResults response = CreateEmptySearchResponse(
            new Grpc.FieldData
            {
                FieldName = "$meta",
                Type = Grpc.DataType.Json,
                IsDynamic = true
            });

        SearchResults results = MilvusCollection.FromGrpcSearchResults(response);

        Assert.Empty(results.FieldsData);
    }

    [Fact]
    public void Search_with_no_hits_preserves_typed_empty_fields()
    {
        Grpc.SearchResults response = CreateEmptySearchResponse(
            CreateLongField("id"));

        SearchResults results = MilvusCollection.FromGrpcSearchResults(response);

        FieldData field = Assert.Single(results.FieldsData);
        Assert.Equal("id", field.FieldName);
        Assert.Equal(MilvusDataType.Int64, field.DataType);
        Assert.Equal(0, field.RowCount);
    }

    [Fact]
    public void Search_with_no_hits_preserves_typed_empty_vector_field()
    {
        Grpc.SearchResults response = CreateEmptySearchResponse(
            new Grpc.FieldData
            {
                FieldName = "vector",
                Type = Grpc.DataType.FloatVector,
                Vectors = new Grpc.VectorField
                {
                    Dim = 2,
                    FloatVector = new Grpc.FloatArray()
                }
            });

        SearchResults results = MilvusCollection.FromGrpcSearchResults(response);

        FieldData field = Assert.Single(results.FieldsData);
        Assert.Equal(MilvusDataType.FloatVector, field.DataType);
        Assert.Equal(0, field.RowCount);
    }

    [Fact]
    public void Search_with_no_hits_preserves_typed_empty_array_field()
    {
        Grpc.SearchResults response = CreateEmptySearchResponse(
            new Grpc.FieldData
            {
                FieldName = "array",
                Type = Grpc.DataType.Array,
                Scalars = new Grpc.ScalarField
                {
                    ArrayData = new Grpc.ArrayArray { ElementType = Grpc.DataType.Int64 }
                }
            });

        SearchResults results = MilvusCollection.FromGrpcSearchResults(response);

        FieldData field = Assert.Single(results.FieldsData);
        Assert.Equal(MilvusDataType.Array, field.DataType);
        Assert.Equal(0, field.RowCount);
    }

    [Fact]
    public void Search_with_no_hits_and_mixed_data_less_fields_returns_empty_fields()
    {
        Grpc.SearchResults response = CreateEmptySearchResponse(
            CreateLongField("id"),
            new Grpc.FieldData
            {
                FieldName = "vector",
                Type = Grpc.DataType.FloatVector
            });

        SearchResults results = MilvusCollection.FromGrpcSearchResults(response);

        Assert.Empty(results.FieldsData);
    }

    [Fact]
    public void Search_with_hits_and_data_less_field_throws()
    {
        Grpc.SearchResults response = CreateSearchResponseWithOneHit(
            new Grpc.FieldData
            {
                FieldName = "vector",
                Type = Grpc.DataType.FloatVector
            });

        Assert.Throws<NotSupportedException>(() => MilvusCollection.FromGrpcSearchResults(response));
    }

    [Fact]
    public void Search_with_no_hits_and_populated_field_throws()
    {
        Grpc.SearchResults response = CreateEmptySearchResponse(
            FieldData.Create("id", new[] { 1L }).ToGrpcFieldData());

        Assert.Throws<MilvusException>(() => MilvusCollection.FromGrpcSearchResults(response));
    }

    [Fact]
    public void Search_with_inconsistent_score_and_id_counts_throws()
    {
        Grpc.SearchResults response = CreateSearchResponseWithOneHit();
        response.Results.Ids.IntId.Data.Clear();

        Assert.Throws<MilvusException>(() => MilvusCollection.FromGrpcSearchResults(response));
    }

    [Fact]
    public void Search_with_inconsistent_topks_throws()
    {
        Grpc.SearchResults response = CreateSearchResponseWithOneHit();
        response.Results.Topks[0] = 2;

        Assert.Throws<MilvusException>(() => MilvusCollection.FromGrpcSearchResults(response));
    }

    [Fact]
    public void Query_with_only_data_less_fields_returns_empty_fields()
    {
        Grpc.QueryResults response = new();
        response.FieldsData.Add(new Grpc.FieldData
        {
            FieldName = "id",
            Type = Grpc.DataType.Int64
        });
        response.FieldsData.Add(new Grpc.FieldData
        {
            FieldName = "vector",
            Type = Grpc.DataType.FloatVector
        });

        IReadOnlyList<FieldData> fields = MilvusCollection.FromGrpcQueryResults(response);

        Assert.Empty(fields);
    }

    [Fact]
    public void Query_with_mixed_typed_and_data_less_fields_throws()
    {
        Grpc.QueryResults response = new();
        response.FieldsData.Add(CreateLongField("id"));
        response.FieldsData.Add(new Grpc.FieldData
        {
            FieldName = "vector",
            Type = Grpc.DataType.FloatVector
        });

        Assert.Throws<NotSupportedException>(() => MilvusCollection.FromGrpcQueryResults(response));
    }

    [Fact]
    public void Query_with_inconsistent_field_row_counts_throws()
    {
        Grpc.QueryResults response = new();
        response.FieldsData.Add(FieldData.Create("id", new[] { 1L }).ToGrpcFieldData());
        response.FieldsData.Add(CreateStringField("name"));

        Assert.Throws<MilvusException>(() => MilvusCollection.FromGrpcQueryResults(response));
    }

    [Fact]
    public void Query_with_consistent_field_row_counts_returns_fields()
    {
        Grpc.QueryResults response = new();
        response.FieldsData.Add(FieldData.Create("id", new[] { 1L }).ToGrpcFieldData());
        response.FieldsData.Add(FieldData.CreateVarChar("name", new[] { "one" }).ToGrpcFieldData());

        IReadOnlyList<FieldData> fields = MilvusCollection.FromGrpcQueryResults(response);

        Assert.Equal(2, fields.Count);
        Assert.All(fields, field => Assert.Equal(1, field.RowCount));
    }

    [Fact]
    public void Direct_data_less_field_conversion_remains_strict()
    {
        Grpc.FieldData field = new()
        {
            FieldName = "field",
            Type = Grpc.DataType.VarChar
        };

        Assert.Throws<NotSupportedException>(() => FieldData.FromGrpcFieldData(field));
    }

    private static Grpc.SearchResults CreateEmptySearchResponse(params Grpc.FieldData[] fields)
    {
        Grpc.SearchResults response = new()
        {
            CollectionName = "collection",
            Results = new Grpc.SearchResultData
            {
                NumQueries = 1,
                TopK = 2
            }
        };

        response.Results.Topks.Add(0);
        response.Results.FieldsData.AddRange(fields);
        return response;
    }

    private static Grpc.FieldData CreateLongField(string name, params long[] values)
    {
        Grpc.FieldData field = new()
        {
            FieldName = name,
            Type = Grpc.DataType.Int64,
            Scalars = new Grpc.ScalarField { LongData = new Grpc.LongArray() }
        };

        field.Scalars.LongData.Data.AddRange(values);
        return field;
    }

    private static Grpc.FieldData CreateStringField(string name, params string[] values)
    {
        Grpc.FieldData field = new()
        {
            FieldName = name,
            Type = Grpc.DataType.VarChar,
            Scalars = new Grpc.ScalarField { StringData = new Grpc.StringArray() }
        };

        field.Scalars.StringData.Data.AddRange(values);
        return field;
    }

    private static Grpc.SearchResults CreateSearchResponseWithOneHit(params Grpc.FieldData[] fields)
    {
        Grpc.SearchResults response = new()
        {
            CollectionName = "collection",
            Results = new Grpc.SearchResultData
            {
                NumQueries = 1,
                TopK = 1,
                Ids = new Grpc.IDs { IntId = new Grpc.LongArray() }
            }
        };

        response.Results.Scores.Add(0.5f);
        response.Results.Ids.IntId.Data.Add(1);
        response.Results.Topks.Add(1);
        response.Results.FieldsData.AddRange(fields);
        return response;
    }
}
