using System.Threading.Channels;

namespace Pegaxus.Service.AuditService;

/// <summary>
/// The in-process line of export jobs that the worker reads. The job itself lives in the cache; a restart empties this line,
/// which AU4 notices (a job stuck in QUEUED or PROCESSING is reported as FAILED so the user can ask again).
/// </summary>
public sealed class AuditExportQueue
{
    private readonly Channel<Guid> channel = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions { SingleReader = true });

    public void Enqueue(Guid exportId) => channel.Writer.TryWrite(exportId);

    public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken cancellationToken) => channel.Reader.ReadAllAsync(cancellationToken);
}
