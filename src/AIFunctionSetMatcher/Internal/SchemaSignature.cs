using Microsoft.Extensions.AI;
using System.Buffers;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace AIFunctionSetMatcher.Internal;

/// <summary>
/// 为函数声明的 JSON Schema 计算稳定的语义指纹。
/// </summary>
internal static class SchemaSignature
{
    private static readonly HashSet<string> IgnoredAnnotations =
    [
        "title",
        "description",
        "examples",
    ];

    private static readonly HashSet<string> UnorderedValueArrayKeywords =
    [
        "required",
        "enum",
        "type",
    ];

    private static readonly HashSet<string> UnorderedSchemaArrayKeywords =
    [
        "oneOf",
        "anyOf",
        "allOf",
    ];

    private static readonly HashSet<string> SchemaMapKeywords =
    [
        "properties",
        "patternProperties",
        "$defs",
        "definitions",
        "dependentSchemas",
    ];

    private static readonly HashSet<string> SchemaValueKeywords =
    [
        "additionalProperties",
        "unevaluatedProperties",
        "propertyNames",
        "contains",
        "not",
        "if",
        "then",
        "else",
        "items",
    ];

    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = false,
        SkipValidation = true,
    };

    /// <summary>
    /// 计算函数声明输入 Schema 的 SHA-256 语义指纹。
    /// </summary>
    /// <param name="declaration">要计算指纹的函数声明。</param>
    /// <returns>Schema 语义指纹和原始 UTF-8 字节数。</returns>
    public static SchemaSignatureResult Compute(AIFunctionDeclaration declaration)
    {
        using var workspace = new SchemaSignatureWorkspace();
        return Compute(declaration, workspace);
    }

    /// <summary>
    /// 使用可复用工作区计算函数声明的 Schema 语义指纹。
    /// </summary>
    /// <param name="declaration">要计算指纹的函数声明。</param>
    /// <param name="workspace">可复用的 Schema canonical UTF-8 工作区。</param>
    /// <returns>Schema 语义指纹和原始 UTF-8 字节数。</returns>
    public static SchemaSignatureResult Compute(
        AIFunctionDeclaration declaration,
        SchemaSignatureWorkspace workspace)
    {
        var schema = declaration.JsonSchema;
        if (schema.ValueKind != JsonValueKind.Object)
        {
            throw new AIFunctionDeclarationSetParseException(
                $"函数“{declaration.Name}”的 JsonSchema 必须是 JSON 对象。");
        }

        var schemaBytes = JsonMarshal.GetRawUtf8Value(schema).Length;
        workspace.Reset();
        CanonicalizeSchema(workspace.Writer, schema);
        workspace.Writer.Flush();

        Span<byte> digest = stackalloc byte[32];
        SHA256.HashData(workspace.Buffer.WrittenSpan, digest);
        return new SchemaSignatureResult(
            SchemaFingerprint.FromDigest(digest),
            schemaBytes);
    }

    /// <summary>
    /// 将 JSON Schema 写入稳定的语义表示。
    /// </summary>
    /// <param name="writer">接收 canonical UTF-8 的 JSON 写入器。</param>
    /// <param name="schema">要规范化的 Schema 元素。</param>
    private static void CanonicalizeSchema(
        Utf8JsonWriter writer,
        JsonElement schema)
    {
        switch (schema.ValueKind)
        {
            case JsonValueKind.Object:
                CanonicalizeSchemaObject(writer, schema);
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            default:
                throw new AIFunctionDeclarationSetParseException(
                    "JSON Schema 必须是对象或布尔值。");
        }
    }

    /// <summary>
    /// 写入按键排序的 Schema 对象并移除允许忽略的注解。
    /// </summary>
    /// <param name="writer">接收 canonical UTF-8 的 JSON 写入器。</param>
    /// <param name="schema">Schema 对象。</param>
    private static void CanonicalizeSchemaObject(
        Utf8JsonWriter writer,
        JsonElement schema)
    {
        var properties = RentSortedProperties(schema);
        try
        {
            EnsureUniquePropertyNames(properties.Properties, properties.Count);
            writer.WriteStartObject();
            for (var index = 0; index < properties.Count; index++)
            {
                var property = properties.Properties[index];
                if (IgnoredAnnotations.Contains(property.Name))
                {
                    continue;
                }

                writer.WritePropertyName(property.Name);
                CanonicalizeSchemaKeyword(writer, property.Name, property.Value);
            }

            writer.WriteEndObject();
        }
        finally
        {
            ReturnProperties(properties.Properties);
        }
    }

    /// <summary>
    /// 按 JSON Schema 关键字语义写入对应的值。
    /// </summary>
    /// <param name="writer">接收 canonical UTF-8 的 JSON 写入器。</param>
    /// <param name="keyword">Schema 关键字。</param>
    /// <param name="value">关键字对应的值。</param>
    private static void CanonicalizeSchemaKeyword(
        Utf8JsonWriter writer,
        string keyword,
        JsonElement value)
    {
        if (SchemaMapKeywords.Contains(keyword))
        {
            CanonicalizeSchemaMap(writer, value);
            return;
        }

        if (UnorderedSchemaArrayKeywords.Contains(keyword))
        {
            CanonicalizeSchemaArray(writer, value, unordered: true);
            return;
        }

        if (keyword == "prefixItems")
        {
            CanonicalizeSchemaArray(writer, value, unordered: false);
            return;
        }

        if (keyword == "items" && value.ValueKind == JsonValueKind.Array)
        {
            CanonicalizeSchemaArray(writer, value, unordered: false);
            return;
        }

        if (SchemaValueKeywords.Contains(keyword))
        {
            CanonicalizeSchema(writer, value);
            return;
        }

        if (keyword == "dependentRequired")
        {
            CanonicalizeDependentRequired(writer, value);
            return;
        }

        if (UnorderedValueArrayKeywords.Contains(keyword) &&
            value.ValueKind == JsonValueKind.Array)
        {
            CanonicalizeValueArray(writer, value, unordered: true);
            return;
        }

        CanonicalizeValue(writer, value);
    }

    /// <summary>
    /// 写入按键排序的属性名到 Schema 映射对象。
    /// </summary>
    /// <param name="writer">接收 canonical UTF-8 的 JSON 写入器。</param>
    /// <param name="value">映射对象。</param>
    private static void CanonicalizeSchemaMap(
        Utf8JsonWriter writer,
        JsonElement value)
    {
        EnsureObject(value, "JSON Schema 的属性和定义容器必须是对象。");
        var properties = RentSortedProperties(value);
        try
        {
            EnsureUniquePropertyNames(properties.Properties, properties.Count);
            writer.WriteStartObject();
            for (var index = 0; index < properties.Count; index++)
            {
                var property = properties.Properties[index];
                writer.WritePropertyName(property.Name);
                CanonicalizeSchema(writer, property.Value);
            }

            writer.WriteEndObject();
        }
        finally
        {
            ReturnProperties(properties.Properties);
        }
    }

    /// <summary>
    /// 写入 Schema 数组，并按关键字语义决定是否排序。
    /// </summary>
    /// <param name="writer">接收 canonical UTF-8 的 JSON 写入器。</param>
    /// <param name="value">Schema 数组。</param>
    /// <param name="unordered">是否按 canonical 字节排序数组元素。</param>
    private static void CanonicalizeSchemaArray(
        Utf8JsonWriter writer,
        JsonElement value,
        bool unordered)
    {
        EnsureArray(value, "JSON Schema 组合关键字必须是数组。");
        if (!unordered)
        {
            writer.WriteStartArray();
            foreach (var item in value.EnumerateArray())
            {
                CanonicalizeSchema(writer, item);
            }

            writer.WriteEndArray();
            return;
        }

        var buffers = CanonicalizeUnorderedElements(
            value,
            (itemWriter, item) => CanonicalizeSchema(itemWriter, item));
        try
        {
            buffers.Sort(CanonicalBufferComparer.Instance);
            writer.WriteStartArray();
            foreach (var buffer in buffers)
            {
                writer.WriteRawValue(buffer.WrittenSpan, skipInputValidation: true);
            }

            writer.WriteEndArray();
        }
        finally
        {
            DisposeBuffers(buffers);
        }
    }

    /// <summary>
    /// 写入按键排序的 dependentRequired 属性依赖对象。
    /// </summary>
    /// <param name="writer">接收 canonical UTF-8 的 JSON 写入器。</param>
    /// <param name="value">dependentRequired 对象。</param>
    private static void CanonicalizeDependentRequired(
        Utf8JsonWriter writer,
        JsonElement value)
    {
        EnsureObject(value, "JSON Schema 的 dependentRequired 必须是对象。");
        var properties = RentSortedProperties(value);
        try
        {
            EnsureUniquePropertyNames(properties.Properties, properties.Count);
            writer.WriteStartObject();
            for (var index = 0; index < properties.Count; index++)
            {
                var property = properties.Properties[index];
                writer.WritePropertyName(property.Name);
                CanonicalizeValueArray(writer, property.Value, unordered: true);
            }

            writer.WriteEndObject();
        }
        finally
        {
            ReturnProperties(properties.Properties);
        }
    }

    /// <summary>
    /// 写入非 Schema 结构的通用 JSON 值。
    /// </summary>
    /// <param name="writer">接收 canonical UTF-8 的 JSON 写入器。</param>
    /// <param name="value">要规范化的 JSON 值。</param>
    private static void CanonicalizeValue(
        Utf8JsonWriter writer,
        JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                CanonicalizeValueObject(writer, value);
                break;
            case JsonValueKind.Array:
                CanonicalizeValueArray(writer, value, unordered: false);
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(value.GetString());
                break;
            case JsonValueKind.Number:
                writer.WriteRawValue(
                    CanonicalizeNumber(value),
                    skipInputValidation: true);
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;
            default:
                throw new AIFunctionDeclarationSetParseException(
                    "JsonSchema 包含不支持的 JSON 值。");
        }
    }

    /// <summary>
    /// 写入按键排序的通用 JSON 对象。
    /// </summary>
    /// <param name="writer">接收 canonical UTF-8 的 JSON 写入器。</param>
    /// <param name="value">通用 JSON 对象。</param>
    private static void CanonicalizeValueObject(
        Utf8JsonWriter writer,
        JsonElement value)
    {
        var properties = RentSortedProperties(value);
        try
        {
            EnsureUniquePropertyNames(properties.Properties, properties.Count);
            writer.WriteStartObject();
            for (var index = 0; index < properties.Count; index++)
            {
                var property = properties.Properties[index];
                writer.WritePropertyName(property.Name);
                CanonicalizeValue(writer, property.Value);
            }

            writer.WriteEndObject();
        }
        finally
        {
            ReturnProperties(properties.Properties);
        }
    }

    /// <summary>
    /// 写入通用 JSON 数组，并按需排序数组元素。
    /// </summary>
    /// <param name="writer">接收 canonical UTF-8 的 JSON 写入器。</param>
    /// <param name="value">通用 JSON 数组。</param>
    /// <param name="unordered">是否按 canonical 字节排序数组元素。</param>
    private static void CanonicalizeValueArray(
        Utf8JsonWriter writer,
        JsonElement value,
        bool unordered)
    {
        EnsureArray(value, "JSON Schema 关键字必须是数组。");
        if (!unordered)
        {
            writer.WriteStartArray();
            foreach (var item in value.EnumerateArray())
            {
                CanonicalizeValue(writer, item);
            }

            writer.WriteEndArray();
            return;
        }

        var buffers = CanonicalizeUnorderedElements(
            value,
            (itemWriter, item) => CanonicalizeValue(itemWriter, item));
        try
        {
            buffers.Sort(CanonicalBufferComparer.Instance);
            writer.WriteStartArray();
            foreach (var buffer in buffers)
            {
                writer.WriteRawValue(buffer.WrittenSpan, skipInputValidation: true);
            }

            writer.WriteEndArray();
        }
        finally
        {
            DisposeBuffers(buffers);
        }
    }

    /// <summary>
    /// 将无顺序数组元素分别写入独立池化缓冲区。
    /// </summary>
    /// <param name="value">要处理的 JSON 数组。</param>
    /// <param name="writeElement">将单个元素写入指定 JSON 写入器的委托。</param>
    /// <returns>按原数组顺序排列的池化缓冲区。</returns>
    private static List<PooledUtf8BufferWriter> CanonicalizeUnorderedElements(
        JsonElement value,
        Action<Utf8JsonWriter, JsonElement> writeElement)
    {
        var buffers = new List<PooledUtf8BufferWriter>(value.GetArrayLength());
        try
        {
            foreach (var item in value.EnumerateArray())
            {
                var buffer = new PooledUtf8BufferWriter(256);
                buffers.Add(buffer);
                using var writer = new Utf8JsonWriter(buffer, WriterOptions);
                writeElement(writer, item);
            }

            return buffers;
        }
        catch
        {
            DisposeBuffers(buffers);
            throw;
        }
    }

    /// <summary>
    /// 释放无顺序数组元素缓冲区。
    /// </summary>
    /// <param name="buffers">要释放的缓冲区列表。</param>
    private static void DisposeBuffers(List<PooledUtf8BufferWriter> buffers)
    {
        foreach (var buffer in buffers)
        {
            buffer.Dispose();
        }
    }

    /// <summary>
    /// 租用并填充按键排序的 JSON 对象属性数组。
    /// </summary>
    /// <param name="value">要枚举属性的 JSON 对象。</param>
    /// <returns>池化属性数组及其中有效元素数量。</returns>
    private static SortedProperties RentSortedProperties(JsonElement value)
    {
        EnsureObject(value, "JSON 对象不能为 null。");
        var propertyCount = 0;
        foreach (var _ in value.EnumerateObject())
        {
            propertyCount++;
        }

        var properties = ArrayPool<JsonProperty>.Shared.Rent(
            Math.Max(propertyCount, 1));
        var index = 0;
        foreach (var property in value.EnumerateObject())
        {
            properties[index++] = property;
        }

        Array.Sort(
            properties,
            0,
            propertyCount,
            JsonPropertyNameComparer.Instance);
        return new SortedProperties(properties, propertyCount);
    }

    /// <summary>
    /// 将池化 JSON 属性数组归还共享数组池。
    /// </summary>
    /// <param name="properties">要归还的属性数组。</param>
    private static void ReturnProperties(JsonProperty[] properties)
    {
        ArrayPool<JsonProperty>.Shared.Return(properties);
    }

    /// <summary>
    /// 验证排序后的 JSON 对象属性名称不存在重复项。
    /// </summary>
    /// <param name="properties">排序后的属性数组。</param>
    /// <param name="count">有效属性数量。</param>
    private static void EnsureUniquePropertyNames(
        JsonProperty[] properties,
        int count)
    {
        for (var index = 1; index < count; index++)
        {
            if (string.Equals(
                properties[index - 1].Name,
                properties[index].Name,
                StringComparison.Ordinal))
            {
                throw new AIFunctionDeclarationSetParseException(
                    $"JsonSchema 包含重复属性“{properties[index].Name}”。");
            }
        }
    }

    /// <summary>
    /// 验证 JSON 元素是对象。
    /// </summary>
    /// <param name="value">要验证的 JSON 元素。</param>
    /// <param name="message">结构无效时使用的异常消息。</param>
    private static void EnsureObject(JsonElement value, string message)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new AIFunctionDeclarationSetParseException(message);
        }
    }

    /// <summary>
    /// 验证 JSON 元素是数组。
    /// </summary>
    /// <param name="value">要验证的 JSON 元素。</param>
    /// <param name="message">结构无效时使用的异常消息。</param>
    private static void EnsureArray(JsonElement value, string message)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new AIFunctionDeclarationSetParseException(message);
        }
    }

    /// <summary>
    /// 规范化 JSON 数字，使数值等价的写法得到相同文本。
    /// </summary>
    /// <param name="value">JSON 数字。</param>
    /// <returns>规范化后的数字文本。</returns>
    private static string CanonicalizeNumber(JsonElement value)
    {
        if (decimal.TryParse(
            value.GetRawText(),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var number))
        {
            return number.ToString("G29", CultureInfo.InvariantCulture);
        }

        return value.GetRawText();
    }

    /// <summary>
    /// 保存租用的 JSON 属性数组及其有效元素数量。
    /// </summary>
    /// <param name="Properties">池化属性数组。</param>
    /// <param name="Count">有效属性数量。</param>
    private readonly record struct SortedProperties(
        JsonProperty[] Properties,
        int Count);

    /// <summary>
    /// 按属性名称序号比较 JSON 属性。
    /// </summary>
    private sealed class JsonPropertyNameComparer : IComparer<JsonProperty>
    {
        /// <summary>
        /// 获取共享属性名称比较器。
        /// </summary>
        public static JsonPropertyNameComparer Instance { get; } = new();

        /// <summary>
        /// 按序号比较两个 JSON 属性名称。
        /// </summary>
        /// <param name="left">左侧属性。</param>
        /// <param name="right">右侧属性。</param>
        /// <returns>属性名称的序号比较结果。</returns>
        public int Compare(JsonProperty left, JsonProperty right)
        {
            return string.CompareOrdinal(left.Name, right.Name);
        }
    }

    /// <summary>
    /// 按 canonical UTF-8 字节比较无顺序数组元素。
    /// </summary>
    private sealed class CanonicalBufferComparer : IComparer<PooledUtf8BufferWriter>
    {
        /// <summary>
        /// 获取共享 canonical 缓冲区比较器。
        /// </summary>
        public static CanonicalBufferComparer Instance { get; } = new();

        /// <summary>
        /// 比较两个 canonical UTF-8 缓冲区。
        /// </summary>
        /// <param name="left">左侧缓冲区。</param>
        /// <param name="right">右侧缓冲区。</param>
        /// <returns>UTF-8 字节的序号比较结果。</returns>
        public int Compare(
            PooledUtf8BufferWriter? left,
            PooledUtf8BufferWriter? right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left is null)
            {
                return -1;
            }

            if (right is null)
            {
                return 1;
            }

            return left.WrittenSpan.SequenceCompareTo(right.WrittenSpan);
        }
    }
}
