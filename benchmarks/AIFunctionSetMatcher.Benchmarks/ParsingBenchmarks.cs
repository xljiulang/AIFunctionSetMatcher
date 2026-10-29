using System.Buffers;
using System.Text.Json;
using AIFunctionSetMatcher;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;

namespace AIFunctionSetMatcher.Benchmarks;

/// <summary>
/// 测量从 OpenAI tools JSON 和 MCP tools/list JSON 创建声明集合的性能。
/// </summary>
[MemoryDiagnoser]
public class ParsingBenchmarks
{
    private readonly Consumer consumer = new();
    private JsonDocument openAiDocument = null!;
    private JsonDocument mcpDocument = null!;

    /// <summary>
    /// 获取或设置每个 JSON 文档中的函数数量。
    /// </summary>
    [Params(1, 8, 32, 128)]
    public int Count { get; set; }

    /// <summary>
    /// 准备基准输入。
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        openAiDocument = CreateOpenAiDocument(Count);
        mcpDocument = CreateMcpDocument(Count);
    }

    /// <summary>
    /// 释放基准输入。
    /// </summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        openAiDocument.Dispose();
        mcpDocument.Dispose();
    }

    /// <summary>
    /// 测量解析裸 OpenAI function tools 数组。
    /// </summary>
    [Benchmark]
    public void From_OpenAi()
    {
        consumer.Consume(AIFunctionDeclarationSet.From(openAiDocument.RootElement));
    }

    /// <summary>
    /// 测量解析 MCP tools/list JSON-RPC 响应。
    /// </summary>
    [Benchmark]
    public void From_Mcp()
    {
        consumer.Consume(AIFunctionDeclarationSet.From(mcpDocument.RootElement));
    }

    private static JsonDocument CreateOpenAiDocument(int count)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartArray();
            for (var index = 0; index < count; index++)
            {
                writer.WriteStartObject();
                writer.WriteString("type", "function");
                writer.WritePropertyName("function");
                writer.WriteStartObject();
                WriteIdentity(writer, index);
                writer.WritePropertyName("parameters");
                WriteSchema(writer);
                writer.WriteEndObject();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.Flush();
        }

        return JsonDocument.Parse(buffer.WrittenMemory);
    }

    private static JsonDocument CreateMcpDocument(int count)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("jsonrpc", "2.0");
            writer.WriteNumber("id", 1);
            writer.WritePropertyName("result");
            writer.WriteStartObject();
            writer.WritePropertyName("tools");
            writer.WriteStartArray();
            for (var index = 0; index < count; index++)
            {
                writer.WriteStartObject();
                WriteIdentity(writer, index);
                writer.WritePropertyName("inputSchema");
                WriteSchema(writer);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.Flush();
        }

        return JsonDocument.Parse(buffer.WrittenMemory);
    }

    private static void WriteIdentity(Utf8JsonWriter writer, int index)
    {
        writer.WriteString("name", $"tool_{index:D3}");
        writer.WriteString("description", $"Process benchmark records for item {index:D3}.");
    }

    private static void WriteSchema(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteString("type", "object");
        writer.WritePropertyName("properties");
        writer.WriteStartObject();
        writer.WritePropertyName("query");
        writer.WriteStartObject();
        writer.WriteString("type", "string");
        writer.WriteString("description", "Query text.");
        writer.WriteEndObject();
        writer.WriteEndObject();
        writer.WritePropertyName("required");
        writer.WriteStartArray();
        writer.WriteStringValue("query");
        writer.WriteEndArray();
        writer.WriteEndObject();
    }
}
