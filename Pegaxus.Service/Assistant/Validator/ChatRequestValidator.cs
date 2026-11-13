using FluentValidation;

namespace Pegaxus.Service.Assistant.Validator;

public sealed class ChatRequestValidator : AbstractValidator<ChatRequest>
{
    public ChatRequestValidator()
    {
        RuleFor(request => request.Messages)
            .Must(messages => messages is { Count: >= 1 and <= 20 })
            .WithMessage("Messages must contain between 1 and 20 items.");

        RuleForEach(request => request.Messages).SetValidator(new ChatMessageRequestValidator());

        RuleFor(request => request.BookingId)
            .Must(value => value is null || (Guid.TryParse(value, out var id) && id != Guid.Empty))
            .WithMessage("BookingId must be a valid UUID.");
    }
}

public sealed class ChatMessageRequestValidator : AbstractValidator<ChatMessageRequest>
{
    public ChatMessageRequestValidator()
    {
        RuleFor(message => message.Role)
            .Must(role => role is "user" or "model")
            .WithMessage("Role must be user or model.");

        RuleFor(message => message.Content)
            .Must(content => !string.IsNullOrWhiteSpace(content))
            .WithMessage("Content must not be empty.")
            .Must(content => content is null || content.Length <= 1000)
            .WithMessage("Content must not exceed 1000 characters.");
    }
}
