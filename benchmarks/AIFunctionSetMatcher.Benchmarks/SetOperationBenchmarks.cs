using System.Text.Json;
using AIFunctionSetMatcher;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using Microsoft.Extensions.AI;

namespace AIFunctionSetMatcher.Benchmarks;

/// <summary>
/// 测量集合并集、交集和差集操作的性能与内存分配。
/// </summary>
[MemoryDiagnoser]
public class SetOperationBenchmarks
{
    private readonly Consumer consumer = new();
    private AIFunctionDeclaration[] exactRightDeclarations = null!;
    private AIFunctionDeclarationSet left = null!;
    private AIFunctionDeclarationSet exactRight = null!;
    private AIFunctionDeclarationSet renamedRight = null!;
    private AIFunctionDeclarationSet rejectedRight = null!;
    private AIFunctionDeclarationSet disjointRight = null!;

    /// <summary>
    /// 获取或设置每个集合中的函数数量。
    /// </summary>
    [Params(1, 8, 32, 128)]
    public int Count { get; set; }

    /// <summary>
    /// 准备确定性的集合输入。
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        using var schema = JsonDocument.Parse(
            """
            {
              "type": "object",
              "properties": {
                "query": {
                  "type": "string",
                  "description": "Query text."
                }
              },
              "required": ["query"]
            }
            """);
        var schemaElement = schema.RootElement.Clone();

        left = CreateSet(
            index => CreateDeclaration($"tool_{index:D3}", index, schemaElement));
        exactRightDeclarations = CreateDeclarations(
            index => CreateDeclaration($"tool_{index:D3}", index, schemaElement));
        exactRight = AIFunctionDeclarationSet.From(exactRightDeclarations);
        renamedRight = CreateSet(
            index => CreateDeclaration(
                $"mcp__benchmark__tool_{index:D3}",
                index,
                schemaElement));
        rejectedRight = CreateSet(
            index => AIFunctionFactory.CreateDeclaration(
                $"tool_{index:D3}",
                "This tool is enabled, do not call it.",
                schemaElement));
        disjointRight = CreateSet(
            index => CreateDeclaration($"other_{index:D3}", index, schemaElement));
    }

    /// <summary>
    /// 测量两个不相交集合的严格并集。
    /// </summary>
    [Benchmark]
    public void Union_Disjoint()
    {
        consumer.Consume(left.Union(disjointRight));
    }

    /// <summary>
    /// 测量名称和 Schema 完全匹配时的交集。
    /// </summary>
    [Benchmark]
    public void Intersect_Exact()
    {
        consumer.Consume(left.Intersect(exactRight));
    }

    /// <summary>
    /// 测量 MCP 命名空间名称改写时的交集。
    /// </summary>
    [Benchmark]
    public void Intersect_NamespaceRenamed()
    {
        consumer.Consume(left.Intersect(renamedRight));
    }

    /// <summary>
    /// 测量所有声明均匹配时的差集。
    /// </summary>
    [Benchmark]
    public void Except_Exact()
    {
        consumer.Consume(left.Except(exactRight));
    }

    /// <summary>
    /// 测量描述安全门禁拒绝全部候选时的差集。
    /// </summary>
    [Benchmark]
    public void Except_DescriptionMismatch()
    {
        consumer.Consume(left.Except(rejectedRight));
    }

    /// <summary>
    /// 测量复用服务端集合缓存并创建新请求集合时的交集。
    /// </summary>
    [Benchmark]
    public void Intersect_RequestSet()
    {
        var requestSet = AIFunctionDeclarationSet.From(exactRightDeclarations);
        consumer.Consume(left.Intersect(requestSet));
    }

    /// <summary>
    /// 测量复用服务端集合缓存并创建新请求集合时的差集。
    /// </summary>
    [Benchmark]
    public void Except_RequestSet()
    {
        var requestSet = AIFunctionDeclarationSet.From(exactRightDeclarations);
        consumer.Consume(left.Except(requestSet));
    }

    private AIFunctionDeclarationSet CreateSet(
        Func<int, AIFunctionDeclaration> createDeclaration)
    {
        return AIFunctionDeclarationSet.From(CreateDeclarations(createDeclaration));
    }

    private AIFunctionDeclaration[] CreateDeclarations(
        Func<int, AIFunctionDeclaration> createDeclaration)
    {
        return Enumerable.Range(0, Count).Select(createDeclaration).ToArray();
    }

    private static AIFunctionDeclaration CreateDeclaration(
        string name,
        int index,
        JsonElement schema)
    {
        return AIFunctionFactory.CreateDeclaration(
            name,
            $"Process benchmark records for item {index:D3}.",
            schema);
    }
}
