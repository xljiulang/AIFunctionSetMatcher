using Microsoft.Extensions.AI;
using System.Text;
using System.Text.Json;
using AIFunctionSetMatcher.Internal;

namespace AIFunctionSetMatcher;

/// <summary>
/// 表示不可变的 AI 函数声明集合。
/// </summary>
public sealed class AIFunctionDeclarationSet
    : IReadOnlyCollection<AIFunctionDeclaration>
{
    private const int MaximumDeclarationCount = 256;
    private const int MaximumPageCount = 64;
    private const int MaximumDocumentBytes = 4 * 1024 * 1024;
    private readonly AIFunctionDeclaration[] _declarations;

    /// <summary>
    /// 初始化函数声明集合。
    /// </summary>
    /// <param name="declarations">已校验且去重的函数声明。</param>
    private AIFunctionDeclarationSet(AIFunctionDeclaration[] declarations)
    {
        _declarations = declarations;
    }

    /// <summary>
    /// 获取集合中的声明数量。
    /// </summary>
    public int Count => _declarations.Length;

    /// <summary>
    /// 从函数声明序列创建集合。
    /// </summary>
    /// <param name="declarations">要包含的函数声明。</param>
    /// <returns>新的函数声明集合。</returns>
    public static AIFunctionDeclarationSet From(
        IEnumerable<AIFunctionDeclaration> declarations)
    {
        ArgumentNullException.ThrowIfNull(declarations);
        var normalized = declarations.Select(
            declaration =>
            {
                if (declaration is null)
                {
                    throw new AIFunctionDeclarationSetParseException(
                        "函数声明集合不能包含 null。");
                }

                return declaration is AIFunction function
                    ? function.AsDeclarationOnly()
                    : declaration;
            }).ToArray();
        if (normalized.Length > MaximumDeclarationCount)
        {
            throw new AIFunctionDeclarationSetParseException(
                "函数声明数量不能超过 256。");
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var declaration in normalized)
        {
            if (!names.Add(declaration.Name))
            {
                throw new AIFunctionDeclarationSetParseException(
                    $"函数声明名称重复：“{declaration.Name}”。");
            }
        }

        return new AIFunctionDeclarationSet(normalized);
    }

    /// <summary>
    /// 从支持的函数工具或 MCP 工具 JSON 文档创建集合。
    /// </summary>
    /// <param name="json">要解析的 JSON 文档。</param>
    /// <returns>新的函数声明集合。</returns>
    public static AIFunctionDeclarationSet From(JsonElement json)
    {
        var page = ParsePage(json);
        if (page.NextCursor is not null)
        {
            throw new AIFunctionDeclarationSetParseException(
                "MCP tools/list 页面仍包含 nextCursor，分页数据不完整。");
        }

        return From(page.Declarations);
    }

    /// <summary>
    /// 从按顺序排列的 MCP tools/list 分页响应创建集合。
    /// </summary>
    /// <param name="pages">MCP tools/list 分页响应。</param>
    /// <returns>包含所有分页声明的新集合。</returns>
    public static AIFunctionDeclarationSet From(
        IReadOnlyList<JsonElement> pages)
    {
        ArgumentNullException.ThrowIfNull(pages);
        if (pages.Count > MaximumPageCount)
        {
            throw new AIFunctionDeclarationSetParseException(
                "MCP tools/list 分页数量不能超过 64。");
        }

        var declarations = new List<AIFunctionDeclaration>();
        var cursors = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < pages.Count; index++)
        {
            var page = ParsePage(pages[index]);
            var hasNext = page.NextCursor is not null;
            if (index < pages.Count - 1 && !hasNext)
            {
                throw new AIFunctionDeclarationSetParseException(
                    $"MCP tools/list 第 {index + 1} 页缺少 nextCursor。");
            }

            if (hasNext && !cursors.Add(page.NextCursor!))
            {
                throw new AIFunctionDeclarationSetParseException(
                    $"MCP tools/list 的 nextCursor 重复：“{page.NextCursor}”。");
            }

            if (index == pages.Count - 1 && hasNext)
            {
                throw new AIFunctionDeclarationSetParseException(
                    "MCP tools/list 最后一页仍包含 nextCursor，分页数据不完整。");
            }

            declarations.AddRange(page.Declarations);
        }

        return From(declarations);
    }

    /// <summary>
    /// 返回包含当前集合和一个或多个其他集合中全部唯一声明的新集合。
    /// </summary>
    /// <param name="others">要并入的函数声明集合。</param>
    /// <returns>并集后的新函数声明集合。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="others"/> 为 <see langword="null"/> 或包含 <see langword="null"/>。</exception>
    /// <exception cref="AIFunctionDeclarationSetParseException">并集包含同名函数声明。</exception>
    public AIFunctionDeclarationSet Union(
        params AIFunctionDeclarationSet[] others)
    {
        ArgumentNullException.ThrowIfNull(others);
        return From(
            _declarations.Concat(
                others.SelectMany(
                    other => other ?? throw new ArgumentNullException(nameof(others)))));
    }

    /// <summary>
    /// 返回当前集合中无法与另一个集合建立匹配的声明。
    /// </summary>
    /// <param name="other">用于比较的函数声明集合。</param>
    /// <param name="options">匹配选项；为 <see langword="null"/> 时使用默认值。</param>
    /// <returns>包含当前集合中未匹配声明的新集合。</returns>
    public AIFunctionDeclarationSet Except(
        AIFunctionDeclarationSet other,
        AIFunctionMatchOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(other);
        options ??= AIFunctionMatchOptions.Default;
        options.Validate();
        return From(
            DeclarationMatcher.FindUnmatched(
                _declarations,
                other._declarations,
                options));
    }

    /// <summary>
    /// 返回当前集合中能够与另一个集合建立匹配的声明。
    /// </summary>
    /// <param name="other">用于比较的函数声明集合。</param>
    /// <param name="options">匹配选项；为 <see langword="null"/> 时使用默认值。</param>
    /// <returns>包含当前集合中匹配声明的新集合。</returns>
    public AIFunctionDeclarationSet Intersect(
        AIFunctionDeclarationSet other,
        AIFunctionMatchOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(other);
        options ??= AIFunctionMatchOptions.Default;
        options.Validate();
        return From(
            DeclarationMatcher.FindMatched(
                _declarations,
                other._declarations,
                options));
    }

    /// <summary>
    /// 解析单个函数工具、MCP tools/list 响应或 Chat Completions 工具文档。
    /// </summary>
    /// <param name="json">要解析的 JSON 元素。</param>
    /// <returns>解析后的函数声明和下一页游标。</returns>
    private static ParsedPage ParsePage(JsonElement json)
    {
        if (Encoding.UTF8.GetByteCount(json.GetRawText()) > MaximumDocumentBytes)
        {
            throw new AIFunctionDeclarationSetParseException(
                "JSON 文档不能超过 4 MiB。");
        }

        JsonDocumentValidator.EnsureNoDuplicateProperties(json);

        if (json.ValueKind == JsonValueKind.Object &&
            json.TryGetProperty("error", out _))
        {
            throw new AIFunctionDeclarationSetParseException(
                "MCP tools/list 响应包含 JSON-RPC 错误。");
        }

        var pageContainer = json;
        var hasResultEnvelope = false;
        if (json.ValueKind == JsonValueKind.Object &&
            json.TryGetProperty("result", out var result))
        {
            pageContainer = result;
            hasResultEnvelope = true;
        }

        if (pageContainer.ValueKind == JsonValueKind.Object &&
            pageContainer.TryGetProperty("resultType", out var resultType) &&
            (resultType.ValueKind != JsonValueKind.String ||
             !string.Equals(
                 resultType.GetString(),
                 "complete",
                 StringComparison.Ordinal)))
        {
            throw new AIFunctionDeclarationSetParseException(
                "MCP tools/list 的 resultType 必须为 complete。");
        }

        var tools = json.ValueKind == JsonValueKind.Array
            ? json
            : GetRequiredProperty(
                pageContainer,
                "tools",
                hasResultEnvelope ? "result" : "JSON 文档");
        if (tools.ValueKind != JsonValueKind.Array)
        {
            throw new AIFunctionDeclarationSetParseException(
                "MCP tools/list 的 tools 属性必须是数组。");
        }

        var declarations = tools.EnumerateArray()
            .Select(ParseTool)
            .ToArray();

        string? nextCursor = null;
        if (pageContainer.ValueKind == JsonValueKind.Object &&
            pageContainer.TryGetProperty("nextCursor", out var cursor))
        {
            if (cursor.ValueKind is not JsonValueKind.String and not JsonValueKind.Null)
            {
                throw new AIFunctionDeclarationSetParseException(
                    "MCP tools/list 的 nextCursor 必须是字符串或 null。");
            }

            nextCursor = cursor.ValueKind == JsonValueKind.Null
                ? null
                : cursor.GetString();
        }

        return new ParsedPage(declarations, nextCursor);
    }

    /// <summary>
    /// 返回用于枚举函数声明的迭代器。
    /// </summary>
    /// <returns>函数声明迭代器。</returns>
    public IEnumerator<AIFunctionDeclaration> GetEnumerator()
    {
        return ((IEnumerable<AIFunctionDeclaration>)_declarations).GetEnumerator();
    }

    /// <summary>
    /// 返回非泛型枚举器。
    /// </summary>
    /// <returns>非泛型函数声明枚举器。</returns>
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    /// <summary>
    /// 根据工具 JSON 结构解析 MCP 或 OpenAI 函数声明。
    /// </summary>
    /// <param name="tool">单个工具 JSON 对象。</param>
    /// <returns>解析后的函数声明。</returns>
    private static AIFunctionDeclaration ParseTool(JsonElement tool)
    {
        if (tool.ValueKind != JsonValueKind.Object)
        {
            throw new AIFunctionDeclarationSetParseException(
                "工具声明必须是 JSON 对象。");
        }

        if (tool.TryGetProperty("type", out var type))
        {
            if (type.ValueKind != JsonValueKind.String ||
                !string.Equals(
                type.GetString(),
                "function",
                StringComparison.OrdinalIgnoreCase))
            {
                throw new AIFunctionDeclarationSetParseException(
                    "仅支持 type 为 function 的 Chat 工具。");
            }

            if (!tool.TryGetProperty("function", out var function))
            {
                throw new AIFunctionDeclarationSetParseException(
                    "type 为 function 的 Chat 工具缺少 function 对象。");
            }

            return ParseOpenAiTool(function);
        }

        return tool.TryGetProperty("function", out var outerFunction)
            ? ParseOpenAiTool(outerFunction)
            : ParseMcpTool(tool);
    }

    /// <summary>
    /// 解析 OpenAI Chat Completions 的 function 对象。
    /// </summary>
    /// <param name="function">function 属性对应的 JSON 对象。</param>
    /// <returns>解析后的函数声明。</returns>
    private static AIFunctionDeclaration ParseOpenAiTool(JsonElement function)
    {
        var schema = GetRequiredProperty(function, "parameters", "function");
        EnsureSchemaObject(schema, "parameters");

        return AIFunctionFactory.CreateDeclaration(
            GetRequiredString(function, "name", "function"),
            GetOptionalString(function, "description", "function"),
            schema.Clone());
    }

    /// <summary>
    /// 解析 MCP tools/list 中的单个工具对象。
    /// </summary>
    /// <param name="tool">MCP 工具 JSON 对象。</param>
    /// <returns>解析后的函数声明。</returns>
    private static AIFunctionDeclaration ParseMcpTool(JsonElement tool)
    {
        var schema = GetRequiredProperty(tool, "inputSchema", "MCP tool");
        EnsureSchemaObject(schema, "inputSchema");

        return AIFunctionFactory.CreateDeclaration(
            GetRequiredString(tool, "name", "MCP tool"),
            GetOptionalString(tool, "description", "MCP tool"),
            schema.Clone());
    }

    /// <summary>
    /// 获取 JSON 对象中的必需属性。
    /// </summary>
    /// <param name="element">要读取的 JSON 对象。</param>
    /// <param name="propertyName">属性名称。</param>
    /// <param name="context">用于错误消息的上下文说明。</param>
    /// <returns>属性值。</returns>
    private static JsonElement GetRequiredProperty(
        JsonElement element,
        string propertyName,
        string context)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new AIFunctionDeclarationSetParseException(
                $"{context} 必须是 JSON 对象。");
        }

        if (!element.TryGetProperty(propertyName, out var property))
        {
            throw new AIFunctionDeclarationSetParseException(
                $"{context} 缺少必需属性“{propertyName}”。");
        }

        return property;
    }

    /// <summary>
    /// 获取 JSON 对象中的必需非空字符串属性。
    /// </summary>
    /// <param name="element">要读取的 JSON 对象。</param>
    /// <param name="propertyName">属性名称。</param>
    /// <param name="context">用于错误消息的上下文说明。</param>
    /// <returns>非空字符串属性值。</returns>
    private static string GetRequiredString(
        JsonElement element,
        string propertyName,
        string context)
    {
        var property = GetRequiredProperty(element, propertyName, context);
        if (property.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(property.GetString()))
        {
            throw new AIFunctionDeclarationSetParseException(
                $"{context} 的属性“{propertyName}”必须是非空字符串。");
        }

        return property.GetString()!;
    }

    /// <summary>
    /// 获取可选的字符串属性；缺失或为 null 时返回空字符串。
    /// </summary>
    /// <param name="element">要读取的 JSON 对象。</param>
    /// <param name="propertyName">属性名称。</param>
    /// <param name="context">用于错误消息的上下文说明。</param>
    /// <returns>字符串属性值或空字符串。</returns>
    private static string GetOptionalString(
        JsonElement element,
        string propertyName,
        string context)
    {
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind == JsonValueKind.Null)
        {
            return string.Empty;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            throw new AIFunctionDeclarationSetParseException(
                $"{context} 的属性“{propertyName}”必须是字符串或 null。");
        }

        return property.GetString() ?? string.Empty;
    }

    /// <summary>
    /// 验证 Schema 属性是 JSON 对象。
    /// </summary>
    /// <param name="schema">要验证的 Schema 元素。</param>
    /// <param name="propertyName">用于错误消息的属性名称。</param>
    private static void EnsureSchemaObject(
        JsonElement schema,
        string propertyName)
    {
        if (schema.ValueKind != JsonValueKind.Object)
        {
            throw new AIFunctionDeclarationSetParseException(
                $"属性“{propertyName}”必须是 JSON Schema 对象。");
        }
    }

    /// <summary>
    /// 表示单个 MCP tools/list 或工具文档的解析结果。
    /// </summary>
    /// <param name="Declarations">本页包含的函数声明。</param>
    /// <param name="NextCursor">下一页游标；不存在时为 null。</param>
    private sealed record ParsedPage(
        AIFunctionDeclaration[] Declarations,
        string? NextCursor);
}
