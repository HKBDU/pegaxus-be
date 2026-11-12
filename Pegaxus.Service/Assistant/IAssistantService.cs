namespace Pegaxus.Service.Assistant;

public interface IAssistantService
{
    Task<ChatResponse> ChatAsync(Guid accountId, ChatRequest request, CancellationToken cancellationToken);
}
