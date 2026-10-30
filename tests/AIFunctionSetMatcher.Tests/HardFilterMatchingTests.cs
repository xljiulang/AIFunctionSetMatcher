using Microsoft.Extensions.AI;
using System.Text.Json;
using Xunit;

namespace AIFunctionSetMatcher.Tests;

/// <summary>
/// 验证 Schema 和描述硬过滤路径与全局匹配语义一致。
/// </summary>
public sealed class HardFilterMatchingTests
{
    /// <summary>
    /// 验证 256 个唯一工具保持原始左侧顺序。
    /// </summary>
    [Fact]
    public void Intersect_preserves_unique_matches_at_the_maximum_size()
    {
        var leftDeclarations = CreateDeclarations(256);
        var rightDeclarations = CreateDeclarations(256);
        var left = AIFunctionDeclarationSet.From(leftDeclarations);
        var right = AIFunctionDeclarationSet.From(rightDeclarations);

        var intersection = left.Intersect(right);

        Assert.Equal(leftDeclarations, intersection);
    }

    /// <summary>
    /// 验证无描述工具使用名称相似度选择唯一最佳候选。
    /// </summary>
    [Fact]
    public void Intersect_uses_name_similarity_to_choose_the_best_candidate()
    {
        var left = CreateSet(
            Declaration("get_user_profile", string.Empty),
            Declaration("list_team_members", string.Empty));
        var right = CreateSet(
            Declaration("mcp__get_user_profile", string.Empty));

        var intersection = left.Intersect(right);

        Assert.Equal(
            ["get_user_profile"],
            intersection.Select(declaration => declaration.Name));
    }

    /// <summary>
    /// 验证大型无描述分组保持全局匹配语义。
    /// </summary>
    [Fact]
    public void Intersect_matches_name_only_groups()
    {
        var leftDeclarations = Enumerable.Range(0, 32)
            .Select(index => Declaration($"tool_{index:D3}", string.Empty))
            .ToArray();
        var rightDeclarations = Enumerable.Range(0, 32)
            .Select(index => Declaration($"mcp__tool_{index:D3}", string.Empty))
            .ToArray();
        var left = CreateSet(leftDeclarations);
        var right = CreateSet(rightDeclarations);

        var intersection = left.Intersect(right);

        Assert.Equal(leftDeclarations, intersection);
    }

    /// <summary>
    /// 验证相同描述产生多个候选时仍使用一对一全局分配。
    /// </summary>
    [Fact]
    public void Intersect_uses_global_assignment_when_descriptions_are_shared()
    {
        var left = CreateSet(
            Declaration("get_user_record", "Perform records operation."),
            Declaration("get_user", "Perform records operation."));
        var right = CreateSet(
            Declaration("get_user", "Perform records operation."),
            Declaration("get_record_copy", "Perform records operation."));

        var intersection = left.Intersect(right);

        Assert.Equal(
            ["get_user_record", "get_user"],
            intersection.Select(declaration => declaration.Name));
    }

    /// <summary>
    /// 验证请求中额外 Schema 组不会改变共同 Schema 组的匹配结果。
    /// </summary>
    [Fact]
    public void Intersect_ignores_request_declarations_in_additional_schema_groups()
    {
        const string matchingSchema = """{ "type": "object" }""";
        const string extraSchema =
            """
            {
              "type": "object",
              "properties": {
                "id": { "type": "integer" }
              }
            }
            """;
        var matching = CreateDeclaration("get_weather", "Get weather.", matchingSchema);
        var left = AIFunctionDeclarationSet.From([matching]);
        var right = AIFunctionDeclarationSet.From(
            [
                CreateDeclaration("get_weather", "Get weather.", matchingSchema),
                .. Enumerable.Range(0, 32)
                    .Select(index => CreateDeclaration(
                        $"extra_{index:D3}",
                        $"Extra operation {index:D3}.",
                        extraSchema)),
            ]);

        var intersection = left.Intersect(right);

        Assert.Same(matching, Assert.Single(intersection));
    }

    /// <summary>
    /// 创建包含指定声明的工具集合。
    /// </summary>
    /// <param name="declarations">集合中的声明。</param>
    /// <returns>新的函数声明集合。</returns>
    private static AIFunctionDeclarationSet CreateSet(
        params AIFunctionDeclaration[] declarations)
    {
        return AIFunctionDeclarationSet.From(declarations);
    }

    /// <summary>
    /// 创建指定数量的唯一工具声明。
    /// </summary>
    /// <param name="count">声明数量。</param>
    /// <returns>按名称排序的唯一声明数组。</returns>
    private static AIFunctionDeclaration[] CreateDeclarations(int count)
    {
        return Enumerable.Range(0, count)
            .Select(index => CreateDeclaration(
                $"tool_{index:D3}",
                $"Process benchmark records for item {index:D3}.",
                """{ "type": "object" }"""))
            .ToArray();
    }

    /// <summary>
    /// 创建单个函数声明。
    /// </summary>
    /// <param name="name">函数名称。</param>
    /// <param name="description">函数描述。</param>
    /// <param name="schemaJson">输入 Schema。</param>
    /// <returns>函数声明。</returns>
    private static AIFunctionDeclaration CreateDeclaration(
        string name,
        string description,
        string schemaJson = """{ "type": "object" }""")
    {
        using var schema = JsonDocument.Parse(schemaJson);
        return AIFunctionFactory.CreateDeclaration(
            name,
            description,
            schema.RootElement.Clone());
    }

    /// <summary>
    /// 使用默认 Schema 创建单个函数声明。
    /// </summary>
    /// <param name="name">函数名称。</param>
    /// <param name="description">函数描述。</param>
    /// <returns>函数声明。</returns>
    private static AIFunctionDeclaration Declaration(string name, string description)
    {
        return CreateDeclaration(name, description);
    }
}
