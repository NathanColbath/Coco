using ComicStudio.Api.Domain;

namespace ComicStudio.Api.Text;

public interface ITextClientResolver
{
    ITextClient Resolve(AiModel model);
}

public class TextClientResolver(IEnumerable<ITextClient> clients) : ITextClientResolver
{
    private readonly Dictionary<string, ITextClient> byTransport = clients.ToDictionary(
        client => client.Transport,
        StringComparer.OrdinalIgnoreCase
    );

    public ITextClient Resolve(AiModel model)
    {
        var transport = model.Capabilities.Transport;

        if (transport is not null && byTransport.TryGetValue(transport, out var client))
        {
            return client;
        }

        throw new NovelAiTextException(
            $"{model.DisplayName} uses the “{transport ?? "unknown"}” transport, "
                + "which this build does not implement yet."
        );
    }
}
