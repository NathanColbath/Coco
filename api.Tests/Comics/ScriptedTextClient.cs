using System.Runtime.CompilerServices;
using ComicStudio.Api.Domain;
using ComicStudio.Api.Text;

namespace ComicStudio.Api.Tests.Comics;

/// <summary>
/// A transport that replays canned answers, so curator wiring can be tested
/// without a live model. Each call takes the next answer; the last one repeats.
/// </summary>
internal sealed class ScriptedTextClient(params string[] responses) : ITextClient
{
    private int index;

    public string Transport => "scripted";

    public List<TextGenerationRequest> Requests { get; } = [];

    public async IAsyncEnumerable<TextDelta> StreamAsync(
        TextGenerationRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        Requests.Add(request);
        var response = responses[Math.Min(index, responses.Length - 1)];
        index++;
        await Task.Yield();
        yield return TextDelta.Content(response);
    }
}

/// <summary>Resolves every model to the same scripted transport.</summary>
internal sealed class FixedTextResolver(ITextClient client) : ITextClientResolver
{
    public ITextClient Resolve(AiModel model) => client;
}

/// <summary>
/// A transport that replays labelled deltas, so the answer/reasoning split can
/// be tested even before the real transports start labelling reasoning.
/// </summary>
internal sealed class DeltaTextClient(params TextDelta[][] responses) : ITextClient
{
    private int index;

    public string Transport => "scripted-deltas";

    public List<TextGenerationRequest> Requests { get; } = [];

    public async IAsyncEnumerable<TextDelta> StreamAsync(
        TextGenerationRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        Requests.Add(request);
        var deltas = responses[Math.Min(index, responses.Length - 1)];
        index++;
        await Task.Yield();

        foreach (var delta in deltas)
        {
            yield return delta;
        }
    }
}
