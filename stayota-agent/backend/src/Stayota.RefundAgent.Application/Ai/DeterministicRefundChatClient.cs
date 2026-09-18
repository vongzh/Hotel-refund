using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Stayota.RefundAgent.Application.Ai;

/// <summary>
/// Offline / demo <see cref="IChatClient"/> that drives tool calling without a remote LLM.
/// Swap this registration for Azure OpenAI / Foundry clients when going live.
/// </summary>
public sealed class DeterministicRefundChatClient : IChatClient
{
    public static AsyncLocal<IReadOnlyList<string>?> PlannedTools { get; } = new();
    public static AsyncLocal<string?> FinalReply { get; } = new();

    public ChatClientMetadata Metadata { get; } = new("stayota-deterministic", new Uri("local://stayota-refund-agent"));

    public void Dispose()
    {
    }

    public object? GetService(Type serviceType, object? key = null)
    {
        if (serviceType == typeof(ChatClientMetadata)) return Metadata;
        if (serviceType == typeof(DeterministicRefundChatClient)) return this;
        return null;
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var list = messages.ToList();
        var completed = list.SelectMany(m => m.Contents).OfType<FunctionResultContent>()
            .Select(f => f.CallId).ToHashSet(StringComparer.Ordinal);
        var planned = PlannedTools.Value ?? [];
        var tools = options?.Tools?.OfType<AIFunction>().ToDictionary(t => t.Name, StringComparer.Ordinal)
                    ?? new Dictionary<string, AIFunction>(StringComparer.Ordinal);

        foreach (var toolName in planned)
        {
            var callId = $"call_{toolName}";
            if (completed.Contains(callId)) continue;
            if (!tools.ContainsKey(toolName) && !tools.Keys.Any(k => k.Equals(toolName, StringComparison.Ordinal)))
                continue;

            return Task.FromResult(new ChatResponse([
                new ChatMessage(ChatRole.Assistant, [
                    new FunctionCallContent(callId, toolName)
                ])
            ]));
        }

        var reply = FinalReply.Value ?? "退款助手已完成本轮决策（确定性 ChatClient，可替换为 Azure OpenAI / Foundry）。";
        return Task.FromResult(new ChatResponse([
            new ChatMessage(ChatRole.Assistant, reply)
        ]));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
        foreach (var message in response.Messages)
        {
            foreach (var content in message.Contents)
            {
                yield return new ChatResponseUpdate(message.Role, [content]);
            }
        }
    }
}
