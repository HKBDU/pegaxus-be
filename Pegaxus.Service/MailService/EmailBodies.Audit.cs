using System.Globalization;

namespace Pegaxus.Service.MailService;

public static partial class EmailBodies
{
    /// <summary>
    /// A large audit package is not downloaded on screen: the manager gets this link instead. The link itself is the button,
    /// so it must be an absolute https URL.
    /// </summary>
    public static string AuditPackageReadyBody(string managerName, string transportCode, string fileName, long sizeBytes, string? downloadUrl, DateTime expiresAt) =>
        Notice("Audit package ready", $"The audit package of {transportCode} is ready", transportCode, managerName,
            "The audit package you requested is ready. It is large, so it is sent as a download link instead of a file.", Tone.Success, "Ready",
            [Row("Transport", Accent(transportCode)), Row("File", Encode(fileName)),
             Row("Size", Encode((sizeBytes / 1_048_576.0).ToString("#,##0.#", CultureInfo.InvariantCulture) + " MB")), Row("Link valid until", Accent(FmtDateTime(expiresAt)))],
            null, "Note", downloadUrl, "Download package");
}
