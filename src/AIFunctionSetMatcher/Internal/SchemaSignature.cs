using Microsoft.Extensions.AI;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AIFunctionSetMatcher.Internal;

/// <summary>
/// 为函数声明的 JSON Schema 计算稳定的语义签名。
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

    /// <summary>
    /// 计算函数声明输入 Schema 的 SHA-256 语义签名。
    /// </summary>
    /// <param name="declaration">要计算签名的函数声明。</param>
    /// <returns>Schema 签名和原始 UTF-8 字节数。</returns>
    public static SchemaSignatureResult Compute(AIFunctionDeclaration declaration)
    {
        var schema = declaration.JsonSchema;
        if (schema.ValueKind != JsonValueKind.Object)
        {
            throw new AIFunctionDeclarationSetParseException(
                $"函数“{declaration.Name}”的 JsonSchema 必须是 JSON 对象。");
        }

        var schemaBytes = Encoding.UTF8.GetByteCount(schema.GetRawText());
        var canonical = CanonicalizeSchema(schema);
        return new SchemaSignatureResult(
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))),
            schemaBytes);
    }

    /// <summary>
    /// 将 JSON Schema 转换为稳定的语义表示。
    /// </summary>
    /// <param name="schema">要规范化的 Schema 元素。</param>
    /// <returns>规范化后的 Schema 文本。</returns>
    private static string CanonicalizeSchema(JsonElement schema)
    {
        return schema.ValueKind switch
        {
            JsonValueKind.Object => CanonicalizeSchemaObject(schema),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => throw new AIFunctionDeclarationSetParseException(
                "JSON Schema 必须是对象或布尔值。"),
        };
    }

    /// <summary>
    /// 规范化 Schema 对象并移除允许忽略的注解。
    /// </summary>
    /// <param name="schema">Schema 对象。</param>
    /// <returns>规范化后的 Schema 对象文本。</returns>
    private static string CanonicalizeSchemaObject(JsonElement schema)
    {
        var properties = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in schema.EnumerateObject())
        {
            if (!seen.Add(property.Name))
            {
                throw new AIFunctionDeclarationSetParseException(
                    $"JsonSchema 包含重复属性“{property.Name}”。");
            }

            if (IgnoredAnnotations.Contains(property.Name))
            {
                continue;
            }

            properties.Add(
                property.Name,
                CanonicalizeSchemaKeyword(property.Name, property.Value));
        }

        return "{" + string.Join(
            ",",
            properties.Select(
                property =>
                    $"{JsonSerializer.Serialize(property.Key)}:{property.Value}")) + "}";
    }

    /// <summary>
    /// 按 JSON Schema 关键字语义规范化对应的值。
    /// </summary>
    /// <param name="keyword">Schema 关键字。</param>
    /// <param name="value">关键字对应的值。</param>
    /// <returns>规范化后的关键字值文本。</returns>
    private static string CanonicalizeSchemaKeyword(
        string keyword,
        JsonElement value)
    {
        if (SchemaMapKeywords.Contains(keyword))
        {
            return CanonicalizeSchemaMap(value);
        }

        if (UnorderedSchemaArrayKeywords.Contains(keyword))
        {
            return CanonicalizeSchemaArray(value, unordered: true);
        }

        if (keyword == "prefixItems")
        {
            return CanonicalizeSchemaArray(value, unordered: false);
        }

        if (keyword == "items" && value.ValueKind == JsonValueKind.Array)
        {
            return CanonicalizeSchemaArray(value, unordered: false);
        }

        if (SchemaValueKeywords.Contains(keyword))
        {
            return CanonicalizeSchema(value);
        }

        if (keyword == "dependentRequired")
        {
            return CanonicalizeDependentRequired(value);
        }

        if (UnorderedValueArrayKeywords.Contains(keyword) &&
            value.ValueKind == JsonValueKind.Array)
        {
            return CanonicalizeValueArray(value, unordered: true);
        }

        return CanonicalizeValue(value);
    }

    /// <summary>
    /// 规范化属性名到 Schema 的映射对象。
    /// </summary>
    /// <param name="value">映射对象。</param>
    /// <returns>按键排序后的映射文本。</returns>
    private static string CanonicalizeSchemaMap(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new AIFunctionDeclarationSetParseException(
                "JSON Schema 的属性和定义容器必须是对象。");
        }

        var properties = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            properties.Add(property.Name, CanonicalizeSchema(property.Value));
        }

        return "{" + string.Join(
            ",",
            properties.Select(
                property =>
                    $"{JsonSerializer.Serialize(property.Key)}:{property.Value}")) + "}";
    }

    /// <summary>
    /// 规范化 Schema 数组。
    /// </summary>
    /// <param name="value">Schema 数组。</param>
    /// <param name="unordered">是否按规范化文本排序数组元素。</param>
    /// <returns>规范化后的数组文本。</returns>
    private static string CanonicalizeSchemaArray(
        JsonElement value,
        bool unordered)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new AIFunctionDeclarationSetParseException(
                "JSON Schema 组合关键字必须是数组。");
        }

        var items = value.EnumerateArray()
            .Select(CanonicalizeSchema)
            .ToList();
        if (unordered)
        {
            items.Sort(StringComparer.Ordinal);
        }

        return "[" + string.Join(",", items) + "]";
    }

    /// <summary>
    /// 规范化 dependentRequired 属性依赖对象。
    /// </summary>
    /// <param name="value">dependentRequired 对象。</param>
    /// <returns>规范化后的依赖对象文本。</returns>
    private static string CanonicalizeDependentRequired(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new AIFunctionDeclarationSetParseException(
                "JSON Schema 的 dependentRequired 必须是对象。");
        }

        var properties = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            properties.Add(
                property.Name,
                CanonicalizeValueArray(property.Value, unordered: true));
        }

        return "{" + string.Join(
            ",",
            properties.Select(
                property =>
                    $"{JsonSerializer.Serialize(property.Key)}:{property.Value}")) + "}";
    }

    /// <summary>
    /// 规范化非 Schema 结构的通用 JSON 值。
    /// </summary>
    /// <param name="value">要规范化的 JSON 值。</param>
    /// <returns>规范化后的 JSON 文本。</returns>
    private static string CanonicalizeValue(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.Object => CanonicalizeValueObject(value),
            JsonValueKind.Array => CanonicalizeValueArray(value, unordered: false),
            JsonValueKind.String => JsonSerializer.Serialize(value.GetString()),
            JsonValueKind.Number => CanonicalizeNumber(value),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => "null",
            _ => throw new AIFunctionDeclarationSetParseException(
                "JsonSchema 包含不支持的 JSON 值。"),
        };
    }

    /// <summary>
    /// 规范化通用 JSON 对象并按键排序。
    /// </summary>
    /// <param name="value">通用 JSON 对象。</param>
    /// <returns>规范化后的对象文本。</returns>
    private static string CanonicalizeValueObject(JsonElement value)
    {
        var properties = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            properties.Add(property.Name, CanonicalizeValue(property.Value));
        }

        return "{" + string.Join(
            ",",
            properties.Select(
                property =>
                    $"{JsonSerializer.Serialize(property.Key)}:{property.Value}")) + "}";
    }

    /// <summary>
    /// 规范化通用 JSON 数组。
    /// </summary>
    /// <param name="value">通用 JSON 数组。</param>
    /// <param name="unordered">是否按规范化文本排序数组元素。</param>
    /// <returns>规范化后的数组文本。</returns>
    private static string CanonicalizeValueArray(
        JsonElement value,
        bool unordered)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new AIFunctionDeclarationSetParseException(
                "JSON Schema 关键字必须是数组。");
        }

        var items = value.EnumerateArray()
            .Select(CanonicalizeValue)
            .ToList();
        if (unordered)
        {
            items.Sort(StringComparer.Ordinal);
        }

        return "[" + string.Join(",", items) + "]";
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
}
