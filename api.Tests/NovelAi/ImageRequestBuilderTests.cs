using System.Text.Json.Nodes;
using ComicStudio.Api.NovelAi.Image;

namespace ComicStudio.Api.Tests.NovelAi;

/// <summary>
/// NovelAI's newer image parameters are undocumented, so these tests pin the
/// request shape. If the API moves, this is where it will show up.
/// </summary>
public class ImageRequestBuilderTests
{
    private static ImageGenerationRequest Request(
        int paramsVersion = 3,
        IReadOnlyList<CharacterPrompt>? characters = null,
        IReadOnlyList<ReferenceImage>? references = null,
        bool useReferences = true
    ) =>
        new()
        {
            ModelId = "nai-diffusion-4-5-full",
            BaseCaption = "manga, monochrome, garden at dusk",
            NegativeCaption = "color, text",
            Width = 832,
            Height = 1216,
            Steps = 28,
            Scale = 5,
            Seed = 42,
            ParamsVersion = paramsVersion,
            Characters = characters?.ToList() ?? [],
            References = references?.ToList() ?? [],
            UsePreciseReference = useReferences,
            Token = "pst-test",
        };

    private static JsonNode Parameters(ImageGenerationRequest request) =>
        ImageRequestBuilder.Build(request)["parameters"]!;

    [Fact]
    public void Sends_the_model_action_and_prompt()
    {
        var body = ImageRequestBuilder.Build(Request());

        Assert.Equal("nai-diffusion-4-5-full", body["model"]!.GetValue<string>());
        Assert.Equal("generate", body["action"]!.GetValue<string>());
        Assert.Equal("manga, monochrome, garden at dusk", body["input"]!.GetValue<string>());
    }

    [Fact]
    public void Always_sends_both_v4_prompt_objects()
    {
        // Dropping either one makes NovelAI answer 500 rather than 400.
        var parameters = Parameters(Request());

        Assert.NotNull(parameters["v4_prompt"]);
        Assert.NotNull(parameters["v4_negative_prompt"]);
        Assert.Equal(
            "manga, monochrome, garden at dusk",
            parameters["v4_prompt"]!["caption"]!["base_caption"]!.GetValue<string>()
        );
        Assert.Equal(
            "color, text",
            parameters["v4_negative_prompt"]!["caption"]!["base_caption"]!.GetValue<string>()
        );
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public void Carries_the_params_version(int version)
    {
        Assert.Equal(version, Parameters(Request(paramsVersion: version))["params_version"]!.GetValue<int>());
    }

    [Fact]
    public void Character_and_negative_caption_arrays_stay_the_same_length()
    {
        var request = Request(
            characters:
            [
                new CharacterPrompt("1girl, auburn hair", 0.3, 0.5, "blonde hair"),
                new CharacterPrompt("1boy, dark hair", 0.7, 0.5),
            ]
        );

        var parameters = Parameters(request);
        var positive = parameters["v4_prompt"]!["caption"]!["char_captions"]!.AsArray();
        var negative = parameters["v4_negative_prompt"]!["caption"]!["char_captions"]!.AsArray();

        Assert.Equal(2, positive.Count);
        Assert.Equal(2, negative.Count);
        Assert.Equal("1girl, auburn hair", positive[0]!["char_caption"]!.GetValue<string>());
        Assert.Equal("blonde hair", negative[0]!["char_caption"]!.GetValue<string>());
        Assert.Equal(string.Empty, negative[1]!["char_caption"]!.GetValue<string>());
    }

    [Fact]
    public void Places_characters_with_coordinates()
    {
        var request = Request(
            characters: [new CharacterPrompt("1girl", 0.25, 0.75)]
        );

        var caption = Parameters(request)["v4_prompt"]!;
        Assert.True(caption["use_coords"]!.GetValue<bool>());

        var center = caption["caption"]!["char_captions"]![0]!["centers"]![0]!;
        Assert.Equal(0.25, center["x"]!.GetValue<double>(), 3);
        Assert.Equal(0.75, center["y"]!.GetValue<double>(), 3);
    }

    [Fact]
    public void Omits_coordinates_when_there_are_no_characters()
    {
        var caption = Parameters(Request())["v4_prompt"]!;

        Assert.False(caption["use_coords"]!.GetValue<bool>());
        Assert.Empty(caption["caption"]!["char_captions"]!.AsArray());
    }

    [Fact]
    public void Reference_arrays_are_parallel_and_inverted_fidelity()
    {
        var reference = new ReferenceImage(TestImages.Png(8, 8), ReferenceKind.Character, 0.8, 0.25);

        var parameters = Parameters(Request(references: [reference]));

        Assert.Single(parameters["director_reference_images"]!.AsArray());
        Assert.Single(parameters["director_reference_descriptions"]!.AsArray());
        Assert.Equal(0.8, parameters["director_reference_strength_values"]![0]!.GetValue<double>(), 3);
        // Secondary strength is 1 - fidelity.
        Assert.Equal(0.75, parameters["director_reference_secondary_strength_values"]![0]!.GetValue<double>(), 3);
        Assert.Equal(1.0, parameters["director_reference_information_extracted"]![0]!.GetValue<double>(), 3);
        Assert.Equal(
            "character",
            parameters["director_reference_descriptions"]![0]!["caption"]!["base_caption"]!.GetValue<string>()
        );
    }

    [Theory]
    [InlineData(ReferenceKind.Character, "character")]
    [InlineData(ReferenceKind.Style, "style")]
    [InlineData(ReferenceKind.CharacterAndStyle, "character&style")]
    public void Maps_reference_kinds(ReferenceKind kind, string expected)
    {
        var parameters = Parameters(
            Request(references: [new ReferenceImage(TestImages.Png(8, 8), kind)])
        );

        Assert.Equal(
            expected,
            parameters["director_reference_descriptions"]![0]!["caption"]!["base_caption"]!.GetValue<string>()
        );
    }

    [Fact]
    public void Sends_empty_reference_arrays_when_none_are_used()
    {
        var parameters = Parameters(Request(references: [], useReferences: true));

        Assert.Empty(parameters["director_reference_images"]!.AsArray());
        Assert.Empty(parameters["director_reference_descriptions"]!.AsArray());
    }

    [Fact]
    public void Normalises_reference_images_to_a_permitted_canvas()
    {
        // A full page render - the shape that used to make NovelAI answer 500.
        var reference = new ReferenceImage(TestImages.Png(1472, 2048));

        var parameters = Parameters(Request(references: [reference]));
        var sent = Convert.FromBase64String(
            parameters["director_reference_images"]![0]!.GetValue<string>()
        );

        Assert.Equal((1024, 1536), PngInfo.ReadDimensions(sent));
    }

    [Fact]
    public void Sends_an_already_permitted_reference_byte_identical()
    {
        var source = TestImages.Png(1024, 1536);

        var parameters = Parameters(Request(references: [new ReferenceImage(source)]));
        var sent = Convert.FromBase64String(
            parameters["director_reference_images"]![0]!.GetValue<string>()
        );

        Assert.Equal(source, sent);
    }

    [Fact]
    public void An_undecodable_reference_fails_the_build_instead_of_being_dropped()
    {
        var request = Request(references: [new ReferenceImage([1, 2, 3])]);

        var exception = Assert.Throws<ReferenceImageNormalizationException>(() =>
            ImageRequestBuilder.Build(request)
        );

        Assert.Contains("Director reference 1", exception.Message);
    }

    [Fact]
    public void Always_sends_n_samples_of_one()
    {
        // Anything else breaks the free-generation envelope.
        Assert.Equal(1, Parameters(Request())["n_samples"]!.GetValue<int>());
    }
}
