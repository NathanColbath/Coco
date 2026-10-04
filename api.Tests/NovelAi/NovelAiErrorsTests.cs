using System.Net;
using ComicStudio.Api.NovelAi;

namespace ComicStudio.Api.Tests.NovelAi;

/// <summary>
/// A free-trial account is told "Recaptcha token is required for trial
/// generations", which reads like a bug in the request. It is really "you need a
/// subscription", and the app must say so.
/// </summary>
public class NovelAiErrorsTests
{
    private const string RecaptchaBody =
        """{"statusCode":400,"message":"StatusCode: 400, Message: Recaptcha token is required for trial generations"}""";

    [Fact]
    public void Recognises_a_trial_recaptcha_demand()
    {
        Assert.True(NovelAiErrors.IsRecaptchaRequired(RecaptchaBody));
        Assert.False(NovelAiErrors.IsRecaptchaRequired("""{"message":"User not found."}"""));
        Assert.False(NovelAiErrors.IsRecaptchaRequired(null));
    }

    [Fact]
    public void Explains_the_subscription_requirement_instead_of_the_recaptcha()
    {
        var message = NovelAiErrors.Describe(HttpStatusCode.BadRequest, RecaptchaBody);

        Assert.Equal(NovelAiErrors.SubscriptionRequired, message);
        Assert.Contains("subscription", message);
        Assert.DoesNotContain("Recaptcha", message);
    }

    [Fact]
    public void Treats_payment_required_as_a_subscription_problem()
    {
        Assert.True(NovelAiErrors.IsSubscriptionRequired(402, null));
        Assert.True(NovelAiErrors.IsSubscriptionRequired(400, RecaptchaBody));
    }

    [Fact]
    public void Does_not_assume_every_bad_request_is_a_subscription_problem()
    {
        // A bare 400 is a malformed request, not a billing problem.
        Assert.False(NovelAiErrors.IsSubscriptionRequired(400, """{"message":"bad width"}"""));
    }

    [Fact]
    public void Keeps_the_token_rejection_reason()
    {
        var message = NovelAiErrors.Describe(
            HttpStatusCode.Unauthorized,
            """{"message":"User not found."}"""
        );

        Assert.Contains("User not found.", message);
    }

    [Fact]
    public void Extracts_novels_message_field()
    {
        Assert.Equal(
            "Invalid Authorization header content.",
            NovelAiErrors.ExtractMessage(
                """{"statusCode":400,"message":"Invalid Authorization header content."}"""
            )
        );
    }

    [Fact]
    public void Falls_back_to_the_raw_body_when_it_is_not_json()
    {
        Assert.Equal("<html>bad gateway</html>", NovelAiErrors.ExtractMessage("<html>bad gateway</html>"));
    }
}
