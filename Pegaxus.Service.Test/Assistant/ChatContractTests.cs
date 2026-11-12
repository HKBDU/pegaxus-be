using Pegaxus.Service.Assistant;
using Pegaxus.Service.Assistant.Validator;

namespace Pegaxus.Service.Test.Assistant;

public class ChatContractTests
{
    private readonly ChatRequestValidator _validator = new();

    [Theory]
    [InlineData(1)]
    [InlineData(20)]
    public void Request_AcceptsMessageCountBoundaries(int count)
    {
        var request = new ChatRequest
        {
            Messages = Enumerable.Range(0, count).Select(_ => new ChatMessageRequest
            {
                Role = "user",
                Content = "question"
            }).ToArray()
        };

        Assert.True(_validator.Validate(request).IsValid);
    }

    [Fact]
    public void Request_RejectsEmptyAndTooManyMessages()
    {
        Assert.False(_validator.Validate(new ChatRequest { Messages = [] }).IsValid);
        Assert.False(_validator.Validate(new ChatRequest
        {
            Messages = Enumerable.Range(0, 21).Select(_ => new ChatMessageRequest { Role = "user", Content = "x" }).ToArray()
        }).IsValid);
    }

    [Theory]
    [InlineData(1000, true)]
    [InlineData(1001, false)]
    public void Request_EnforcesContentLength(int length, bool expected)
    {
        var result = _validator.Validate(new ChatRequest
        {
            Messages = [new ChatMessageRequest { Role = "user", Content = new string('x', length) }]
        });

        Assert.Equal(expected, result.IsValid);
    }

    [Theory]
    [InlineData("system")]
    [InlineData("")]
    public void Request_RejectsInvalidRole(string role)
    {
        var result = _validator.Validate(new ChatRequest
        {
            Messages = [new ChatMessageRequest { Role = role, Content = "question" }]
        });

        Assert.Contains(result.Errors, error => error.PropertyName == "Messages[0].Role");
    }

    [Fact]
    public void Request_RejectsWhitespaceContentAndInvalidBookingId()
    {
        var result = _validator.Validate(new ChatRequest
        {
            Messages = [new ChatMessageRequest { Role = "user", Content = " \t " }],
            BookingId = "not-a-uuid"
        });

        Assert.Contains(result.Errors, error => error.PropertyName == "Messages[0].Content");
        Assert.Contains(result.Errors, error => error.PropertyName == "BookingId");
    }

    [Fact]
    public void Response_ProjectsOnlyValidServerReferencesAndCapsAtThree()
    {
        var validId = Guid.NewGuid();
        var response = ChatResponse.FromToolResults("reply", [
            new("booking", validId.ToString(), " Booking ", DateTimeOffset.UtcNow),
            new("admin", Guid.NewGuid().ToString(), "invalid type", null),
            new("dossier", "bad-id", "invalid id", null),
            new("tracking", Guid.NewGuid().ToString(), "third", null),
            new("booking", Guid.NewGuid().ToString(), "fourth", null),
            new("booking", validId.ToString(), "duplicate", null)
        ]);

        Assert.Equal("reply", response.Reply);
        Assert.Equal(3, response.References.Count);
        Assert.Equal(validId, response.References[0].Id);
        Assert.Equal("Booking", response.References[0].Label);
        Assert.DoesNotContain(response.References, reference => reference.Type == "admin");
        Assert.DoesNotContain(response.References, reference => reference.Label == "invalid id");
        Assert.Equal(5, typeof(AssistantReference).GetProperties().Length);
        Assert.DoesNotContain(typeof(AssistantReference).GetProperties(), property => property.Name.Contains("Url", StringComparison.OrdinalIgnoreCase));
    }
}
