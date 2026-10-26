using System.Buffers.Binary;
using System.Globalization;
using System.Text.Json;

namespace Milvus.Client;

internal static class RestResultReader
{
    internal static SearchResults ReadSearch(JsonElement root, MilvusCollectionDescription description,
        int numQueries, IReadOnlyList<string>? outputFields, int? hybridLimit = null)
    {
        JsonElement rows = root.GetProperty("data");
        int count = rows.GetArrayLength();
        long[] limits = root.TryGetProperty("topks", out JsonElement topks)
            ? topks.EnumerateArray().Select(value => value.GetInt64()).ToArray()
            : numQueries == 1 ? [count]
            : throw new MilvusException("REST omitted multi-query result counts; an empty reply can hide earlier queries' hits.");
        if (limits.Length != numQueries || limits.Any(value => value < 0) || limits.Sum() != count)
            throw new MilvusException("REST search returned inconsistent per-query result counts.");
        FieldSchema primaryKey = description.Schema.Fields.Single(field => field.IsPrimaryKey);
        MilvusIds ids = count == 0 ? default : primaryKey.DataType == MilvusDataType.Int64
            ? MilvusIds.FromIds(rows.EnumerateArray().Select(row => ReadInt64(row.GetProperty(primaryKey.Name))).ToArray())
            : MilvusIds.FromIds(rows.EnumerateArray().Select(row => row.GetProperty(primaryKey.Name).GetString()!).ToArray());
        return new SearchResults
        {
            CollectionName = description.CollectionName,
            NumQueries = numQueries,
            // The ordinary Milvus reducer exposes the last query's actual hit count as TopK.
            Limit = hybridLimit ?? limits.LastOrDefault(),
            Limits = limits,
            Ids = ids,
            Scores = rows.EnumerateArray().Select(row => row.GetProperty("distance").GetSingle()).ToArray(),
            FieldsData = ReadFields(rows, description, outputFields, isSearch: true)
        };
    }

    internal static IReadOnlyList<FieldData> ReadFields(JsonElement data,
        MilvusCollectionDescription description, IReadOnlyList<string>? searchOutputFields, bool isSearch)
    {
        JsonElement[] rows = data.EnumerateArray().ToArray();
        if (rows.Length == 0) return [];
        FieldSchema? primaryKey = description.Schema.Fields.FirstOrDefault(field => field.IsPrimaryKey);
        var names = new HashSet<string>(rows.SelectMany(row => row.EnumerateObject().Select(field => field.Name)), StringComparer.Ordinal);
        if (isSearch)
        {
            names.Remove("distance");
            if (primaryKey is not null && searchOutputFields?.Contains(primaryKey.Name) != true
                && searchOutputFields?.Contains("*") != true) names.Remove(primaryKey.Name);
        }
        var fields = new List<FieldData>();
        foreach (FieldSchema schema in description.Schema.Fields)
        {
            if (!names.Remove(schema.Name)) continue;
            JsonElement[] values = rows.Select(row => row.TryGetProperty(schema.Name, out JsonElement value) ? value : default).ToArray();
            fields.Add(ReadField(schema, values));
        }
        foreach (string name in names)
        {
            JsonElement[] values = rows.Select(row => row.TryGetProperty(name, out JsonElement value) ? value : default).ToArray();
            if (name == "count(*)") fields.Add(FieldData.Create(name, values.Select(ReadInt64).ToArray()));
            else fields.Add(ReadDynamicField(name, values));
        }
        return fields;
    }

    private static FieldData ReadField(FieldSchema schema, JsonElement[] values) => schema.DataType switch
    {
        MilvusDataType.Bool => ReadScalar(schema, values, value => value.GetBoolean()),
        MilvusDataType.Int8 or MilvusDataType.Int16 or MilvusDataType.Int32 => ReadScalar(schema, values, value => value.GetInt32()),
        MilvusDataType.Int64 => ReadScalar(schema, values, ReadInt64),
        MilvusDataType.Float => ReadScalar(schema, values, value => value.GetSingle()),
        MilvusDataType.Double => ReadScalar(schema, values, value => value.GetDouble()),
        MilvusDataType.String or MilvusDataType.VarChar => FieldData.CreateVarChar(schema.Name,
            values.Select(value => IsNull(value) ? null : value.GetString()).ToArray()),
        MilvusDataType.Json => FieldData.CreateJson(schema.Name, values.Select(value =>
            value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText()).ToArray()),
        MilvusDataType.FloatVector => FieldData.CreateFloatVector(schema.Name,
            values.Select(value => (ReadOnlyMemory<float>)value.EnumerateArray().Select(item => item.GetSingle()).ToArray()).ToArray()),
        MilvusDataType.BinaryVector => FieldData.CreateBinaryVectors(schema.Name,
            values.Select(value => (ReadOnlyMemory<byte>)value.GetBytesFromBase64()).ToArray()),
#if NET8_0_OR_GREATER
        MilvusDataType.Float16Vector => FieldData.CreateFloat16Vector(schema.Name, values.Select(ReadHalfVector).ToArray()),
#endif
        MilvusDataType.SparseFloatVector => FieldData.CreateSparseFloatVector(schema.Name, values.Select(ReadSparseVector).ToArray()),
        MilvusDataType.Array => ReadArrayField(schema, values),
        _ => throw new NotSupportedException($"REST result type {schema.DataType} is not supported.")
    };

    private static FieldData ReadScalar<T>(FieldSchema schema, JsonElement[] values, Func<JsonElement, T> read) where T : struct
        => schema.Nullable
            ? FieldData.Create(schema.Name, values.Select(value => IsNull(value) ? (T?)null : read(value)).ToArray())
            : FieldData.Create(schema.Name, values.Select(read).ToArray());

    private static FieldData ReadDynamicField(string name, JsonElement[] values)
    {
        JsonElement sample = values.FirstOrDefault(value => !IsNull(value));
        return sample.ValueKind switch
        {
            JsonValueKind.String => FieldData.CreateVarChar(name,
                values.Select(value => IsNull(value) ? null : value.GetString()).ToArray(), isDynamic: true),
            JsonValueKind.Number => FieldData.Create(name,
                values.Select(value => IsNull(value) ? 0 : ReadInt64(value)).ToArray(), isDynamic: true),
            JsonValueKind.True or JsonValueKind.False => FieldData.Create(name,
                values.Select(value => !IsNull(value) && value.GetBoolean()).ToArray(), isDynamic: true),
            _ => throw new NotSupportedException($"REST dynamic field '{name}' has an unsupported type.")
        };
    }

    private static FieldData ReadArrayField(FieldSchema schema, JsonElement[] values) => schema.ElementDataType switch
    {
        MilvusDataType.Bool => ReadArray(schema.Name, values, "BoolData", value => value.GetBoolean()),
        MilvusDataType.Int8 or MilvusDataType.Int16 or MilvusDataType.Int32 => ReadArray(schema.Name, values, "IntData", value => value.GetInt32()),
        MilvusDataType.Int64 => ReadArray(schema.Name, values, "LongData", ReadInt64),
        MilvusDataType.Float => ReadArray(schema.Name, values, "FloatData", value => value.GetSingle()),
        MilvusDataType.Double => ReadArray(schema.Name, values, "DoubleData", value => value.GetDouble()),
        MilvusDataType.String or MilvusDataType.VarChar => ReadArray(schema.Name, values, "StringData", value => value.GetString()!),
        _ => throw new NotSupportedException($"REST array type {schema.ElementDataType} is not supported.")
    };

    private static ArrayFieldData<T> ReadArray<T>(string name, JsonElement[] values, string member, Func<JsonElement, T> read)
    {
        IReadOnlyList<T>?[] arrays = values.Select(value => IsNull(value) ? null
            : (IReadOnlyList<T>)ArrayValues(value, member).Select(read).ToArray()).ToArray();
        return new ArrayFieldData<T>(name, arrays, isDynamic: false);
    }

    private static IEnumerable<JsonElement> ArrayValues(JsonElement value, string member)
    {
        if (value.ValueKind == JsonValueKind.Array) return value.EnumerateArray();
        // Milvus 2.6 also emits arrays as Go's ScalarField oneof wrapper.
        if (value.TryGetProperty("Data", out JsonElement union)) value = union;
        if (IsNull(value)) return [];
        string camelMember = char.ToLowerInvariant(member[0]) + member.Substring(1);
        if (value.TryGetProperty(member, out JsonElement field) || value.TryGetProperty(camelMember, out field))
            value = field;
        if (value.TryGetProperty("data", out JsonElement data)) return data.EnumerateArray();
        throw new MilvusException("REST array field has an invalid representation.");
    }

#if NET8_0_OR_GREATER
    private static ReadOnlyMemory<Half> ReadHalfVector(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Array)
            return value.EnumerateArray().Select(item => (Half)item.GetSingle()).ToArray();
        byte[] bytes = value.GetBytesFromBase64();
        if (bytes.Length % 2 != 0) throw new MilvusException("REST float16 vector has an odd byte count.");
        var vector = new Half[bytes.Length / 2];
        for (int i = 0; i < vector.Length; i++)
            vector[i] = BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(i * 2)));
        return vector;
    }
#endif

    private static MilvusSparseVector<float> ReadSparseVector(JsonElement value)
    {
        var entries = value.EnumerateObject().Select(item =>
            (Index: int.Parse(item.Name, CultureInfo.InvariantCulture), Value: item.Value.GetSingle())).OrderBy(item => item.Index).ToArray();
        return new MilvusSparseVector<float>(entries.Select(item => item.Index).ToArray(), entries.Select(item => item.Value).ToArray());
    }

    private static bool IsNull(JsonElement value) => value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined;
    private static long ReadInt64(JsonElement value) => value.ValueKind == JsonValueKind.String
        ? long.Parse(value.GetString()!, CultureInfo.InvariantCulture) : value.GetInt64();
}
