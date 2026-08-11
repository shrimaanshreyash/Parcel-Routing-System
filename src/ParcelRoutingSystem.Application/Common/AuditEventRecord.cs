using System.Collections.ObjectModel;

namespace ParcelRoutingSystem.Application.Common;

/// <summary>
/// Describes one append-only, privacy-safe audit event that must be persisted in
/// the same transaction as its business state change.
/// </summary>
public sealed class AuditEventRecord
{
    private AuditEventRecord(
        Guid id,
        string eventType,
        string subjectType,
        string subjectId,
        string actorId,
        string correlationId,
        string idempotencyKey,
        DateTimeOffset occurredAtUtc,
        IReadOnlyDictionary<string, string> details)
    {
        Id = id;
        EventType = eventType;
        SubjectType = subjectType;
        SubjectId = subjectId;
        ActorId = actorId;
        CorrelationId = correlationId;
        IdempotencyKey = idempotencyKey;
        OccurredAtUtc = occurredAtUtc;
        Details = details;
    }

    /// <summary>Gets the server-generated event identifier.</summary>
    public Guid Id { get; }

    /// <summary>Gets the allow-listed event category.</summary>
    public string EventType { get; }

    /// <summary>Gets the business-record category affected by the event.</summary>
    public string SubjectType { get; }

    /// <summary>Gets the non-personal business-record identifier.</summary>
    public string SubjectId { get; }

    /// <summary>Gets the stable actor subject identifier.</summary>
    public string ActorId { get; }

    /// <summary>Gets the operation correlation identifier.</summary>
    public string CorrelationId { get; }

    /// <summary>Gets the key that prevents duplicate state changes and events.</summary>
    public string IdempotencyKey { get; }

    /// <summary>Gets the event timestamp normalized to UTC.</summary>
    public DateTimeOffset OccurredAtUtc { get; }

    /// <summary>Gets bounded non-personal event facts.</summary>
    public IReadOnlyDictionary<string, string> Details { get; }

    /// <summary>
    /// Creates a bounded append-only audit record without accepting recipient
    /// data or raw request payloads.
    /// </summary>
    /// <param name="id">The server-generated event identifier.</param>
    /// <param name="eventType">The allow-listed event name.</param>
    /// <param name="subjectType">The affected business-record type.</param>
    /// <param name="subjectId">The non-personal affected-record identifier.</param>
    /// <param name="metadata">Validated actor and correlation metadata.</param>
    /// <param name="idempotencyKey">The operation idempotency key.</param>
    /// <param name="occurredAtUtc">The server-owned UTC timestamp.</param>
    /// <param name="details">Optional bounded non-personal facts.</param>
    /// <returns>An immutable audit event ready for transactional persistence.</returns>
    public static AuditEventRecord Create(
        Guid id,
        string eventType,
        string subjectType,
        string subjectId,
        OperationMetadata metadata,
        string idempotencyKey,
        DateTimeOffset occurredAtUtc,
        IReadOnlyDictionary<string, string>? details = null)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        var copiedDetails = new Dictionary<string, string>(StringComparer.Ordinal);
        if (details is not null)
        {
            foreach ((string name, string value) in details)
            {
                string safeName = ApplicationGuard.RequiredText(
                    name,
                    80,
                    ApplicationErrorCodes.OperationMetadataInvalid,
                    "Audit detail name");
                string safeValue = ApplicationGuard.RequiredText(
                    value,
                    200,
                    ApplicationErrorCodes.OperationMetadataInvalid,
                    "Audit detail value");
                copiedDetails.Add(safeName, safeValue);
            }
        }

        return new AuditEventRecord(
            id,
            ApplicationGuard.RequiredText(
                eventType,
                100,
                ApplicationErrorCodes.OperationMetadataInvalid,
                "Audit event type"),
            ApplicationGuard.RequiredText(
                subjectType,
                50,
                ApplicationErrorCodes.OperationMetadataInvalid,
                "Audit subject type"),
            ApplicationGuard.RequiredText(
                subjectId,
                100,
                ApplicationErrorCodes.OperationMetadataInvalid,
                "Audit subject identifier"),
            metadata.ActorId,
            metadata.CorrelationId,
            ApplicationGuard.RequiredText(
                idempotencyKey,
                100,
                ApplicationErrorCodes.IdempotencyKeyInvalid,
                "Idempotency key"),
            occurredAtUtc.ToUniversalTime(),
            new ReadOnlyDictionary<string, string>(copiedDetails));
    }
}
