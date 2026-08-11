using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using ParcelRoutingSystem.Application.Batches;
using ParcelRoutingSystem.Application.Rules;
using ParcelRoutingSystem.Domain.Routing;

namespace ParcelRoutingSystem.Infrastructure.Persistence;

/// <summary>
/// Defines the PostgreSQL persistence model for immutable decisions, rule
/// versions, approvals, audit events, and durable batch work.
/// </summary>
public sealed class ParcelRoutingDbContext : DbContext
{
    /// <summary>
    /// Creates a scoped persistence context from externally validated options.
    /// </summary>
    /// <param name="options">The configured PostgreSQL context options.</param>
    public ParcelRoutingDbContext(DbContextOptions<ParcelRoutingDbContext> options)
        : base(options)
    {
    }

    internal DbSet<RuleSetEntity> RuleSets => Set<RuleSetEntity>();

    internal DbSet<WeightBandRuleEntity> WeightBandRules => Set<WeightBandRuleEntity>();

    internal DbSet<InsuranceRuleEntity> InsuranceRules => Set<InsuranceRuleEntity>();

    internal DbSet<RoutingDecisionEntity> RoutingDecisions => Set<RoutingDecisionEntity>();

    internal DbSet<InsuranceApprovalEntity> InsuranceApprovals =>
        Set<InsuranceApprovalEntity>();

    internal DbSet<AuditEventEntity> AuditEvents => Set<AuditEventEntity>();

    internal DbSet<BatchEntity> Batches => Set<BatchEntity>();

    internal DbSet<BatchRowEntity> BatchRows => Set<BatchRowEntity>();

    /// <summary>
    /// Configures explicit PostgreSQL table shapes, constraints, indexes, and the
    /// immutable default rule-set seed used before administration exists.
    /// </summary>
    /// <param name="modelBuilder">The EF Core relational model builder.</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureRuleSets(modelBuilder);
        ConfigureRoutingDecisions(modelBuilder);
        ConfigureApprovals(modelBuilder);
        ConfigureAuditEvents(modelBuilder);
        ConfigureBatches(modelBuilder);
        SeedDefaultRuleSet(modelBuilder);
    }

    /// <summary>
    /// Configures immutable rule versions and constrained child rules, including
    /// database uniqueness for identifiers, priorities, and the active version.
    /// </summary>
    /// <param name="modelBuilder">The relational model builder.</param>
    private static void ConfigureRuleSets(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RuleSetEntity>(
            entity =>
            {
                entity.ToTable("routing_rule_sets");
                entity.HasKey(item => item.Version);
                entity.Property(item => item.Version).HasColumnName("version");
                entity.Property(item => item.Status)
                    .HasColumnName("status")
                    .HasConversion<string>()
                    .HasMaxLength(20);
                entity.Property(item => item.CreatedAtUtc)
                    .HasColumnName("created_at_utc");
                entity.Property(item => item.CreatedBy)
                    .HasColumnName("created_by")
                    .HasMaxLength(100);
                entity.Property(item => item.ActivatedAtUtc)
                    .HasColumnName("activated_at_utc");
                entity.HasIndex(item => item.Status)
                    .IsUnique()
                    .HasFilter("status = 'Active'");
            });

        modelBuilder.Entity<WeightBandRuleEntity>(
            entity =>
            {
                entity.ToTable("routing_weight_band_rules");
                entity.HasKey(item => item.Id);
                entity.Property(item => item.Id).HasColumnName("id");
                entity.Property(item => item.RuleSetVersion)
                    .HasColumnName("rule_set_version");
                entity.Property(item => item.RuleId)
                    .HasColumnName("rule_id")
                    .HasMaxLength(100);
                entity.Property(item => item.Priority).HasColumnName("priority");
                entity.Property(item => item.LowerBoundExclusive)
                    .HasColumnName("lower_bound_exclusive")
                    .HasPrecision(29, 12);
                entity.Property(item => item.UpperBoundInclusive)
                    .HasColumnName("upper_bound_inclusive")
                    .HasPrecision(29, 12);
                entity.Property(item => item.Department)
                    .HasColumnName("department")
                    .HasConversion<string>()
                    .HasMaxLength(20);
                entity.HasIndex(item => new { item.RuleSetVersion, item.RuleId })
                    .IsUnique();
                entity.HasIndex(item => new { item.RuleSetVersion, item.Priority })
                    .IsUnique();
                entity.HasOne(item => item.RuleSet)
                    .WithMany(item => item.WeightBands)
                    .HasForeignKey(item => item.RuleSetVersion)
                    .OnDelete(DeleteBehavior.Restrict);
            });

        modelBuilder.Entity<InsuranceRuleEntity>(
            entity =>
            {
                entity.ToTable("routing_insurance_rules");
                entity.HasKey(item => item.RuleSetVersion);
                entity.Property(item => item.RuleSetVersion)
                    .HasColumnName("rule_set_version");
                entity.Property(item => item.RuleId)
                    .HasColumnName("rule_id")
                    .HasMaxLength(100);
                entity.Property(item => item.Priority).HasColumnName("priority");
                entity.Property(item => item.ThresholdExclusiveEuros)
                    .HasColumnName("threshold_exclusive_euros")
                    .HasPrecision(29, 12);
                entity.HasOne(item => item.RuleSet)
                    .WithOne(item => item.InsuranceRule)
                    .HasForeignKey<InsuranceRuleEntity>(item => item.RuleSetVersion)
                    .OnDelete(DeleteBehavior.Restrict);
            });
    }

    /// <summary>
    /// Configures immutable decision storage with unique request and batch-row
    /// identities and PostgreSQL text arrays for rule identifiers and reasons.
    /// </summary>
    /// <param name="modelBuilder">The relational model builder.</param>
    private static void ConfigureRoutingDecisions(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RoutingDecisionEntity>(
            entity =>
            {
                entity.ToTable("routing_decisions");
                entity.HasKey(item => item.Id);
                entity.Property(item => item.Id).HasColumnName("id");
                entity.Property(item => item.IdempotencyKey)
                    .HasColumnName("idempotency_key")
                    .HasMaxLength(100);
                entity.Property(item => item.RequestFingerprint)
                    .HasColumnName("request_fingerprint")
                    .HasMaxLength(64)
                    .IsFixedLength();
                entity.Property(item => item.WeightKilograms)
                    .HasColumnName("weight_kilograms")
                    .HasPrecision(29, 12);
                entity.Property(item => item.DeclaredValueEuros)
                    .HasColumnName("declared_value_euros")
                    .HasPrecision(29, 12);
                entity.Property(item => item.DestinationCountry)
                    .HasColumnName("destination_country")
                    .HasMaxLength(2)
                    .IsFixedLength();
                entity.Property(item => item.IntendedDepartment)
                    .HasColumnName("intended_department")
                    .HasConversion<string>()
                    .HasMaxLength(20);
                entity.Property(item => item.ApprovalState)
                    .HasColumnName("approval_state")
                    .HasConversion<string>()
                    .HasMaxLength(40);
                entity.Property(item => item.RuleSetVersion)
                    .HasColumnName("rule_set_version");
                entity.Property(item => item.MatchedRuleIds)
                    .HasColumnName("matched_rule_ids")
                    .HasColumnType("text[]");
                entity.Property(item => item.Reasons)
                    .HasColumnName("reasons")
                    .HasColumnType("text[]");
                entity.Property(item => item.DecidedAtUtc)
                    .HasColumnName("decided_at_utc");
                entity.Property(item => item.CorrelationId)
                    .HasColumnName("correlation_id")
                    .HasMaxLength(100);
                entity.Property(item => item.BatchId).HasColumnName("batch_id");
                entity.Property(item => item.BatchRowId).HasColumnName("batch_row_id");
                entity.HasIndex(item => item.IdempotencyKey).IsUnique();
                entity.HasIndex(item => item.DecidedAtUtc);
                entity.HasIndex(item => new { item.ApprovalState, item.DecidedAtUtc });
                entity.HasIndex(item => item.BatchRowId)
                    .IsUnique()
                    .HasFilter("batch_row_id IS NOT NULL");
                entity.HasOne<RuleSetEntity>()
                    .WithMany()
                    .HasForeignKey(item => item.RuleSetVersion)
                    .OnDelete(DeleteBehavior.Restrict);
            });
    }

    /// <summary>
    /// Configures append-only approvals with one approval per decision and one
    /// durable outcome per idempotency key.
    /// </summary>
    /// <param name="modelBuilder">The relational model builder.</param>
    private static void ConfigureApprovals(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<InsuranceApprovalEntity>(
            entity =>
            {
                entity.ToTable("insurance_approvals");
                entity.HasKey(item => item.Id);
                entity.Property(item => item.Id).HasColumnName("id");
                entity.Property(item => item.DecisionId).HasColumnName("decision_id");
                entity.Property(item => item.IdempotencyKey)
                    .HasColumnName("idempotency_key")
                    .HasMaxLength(100);
                entity.Property(item => item.ApprovedBy)
                    .HasColumnName("approved_by")
                    .HasMaxLength(100);
                entity.Property(item => item.ApprovedAtUtc)
                    .HasColumnName("approved_at_utc");
                entity.Property(item => item.CorrelationId)
                    .HasColumnName("correlation_id")
                    .HasMaxLength(100);
                entity.HasIndex(item => item.DecisionId).IsUnique();
                entity.HasIndex(item => item.IdempotencyKey).IsUnique();
                entity.HasOne<RoutingDecisionEntity>()
                    .WithOne()
                    .HasForeignKey<InsuranceApprovalEntity>(item => item.DecisionId)
                    .OnDelete(DeleteBehavior.Restrict);
            });
    }

    /// <summary>
    /// Configures append-only privacy-safe audit storage and operation-level
    /// uniqueness for idempotent state transitions.
    /// </summary>
    /// <param name="modelBuilder">The relational model builder.</param>
    private static void ConfigureAuditEvents(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AuditEventEntity>(
            entity =>
            {
                entity.ToTable("audit_events");
                entity.HasKey(item => item.Id);
                entity.Property(item => item.Id).HasColumnName("id");
                entity.Property(item => item.EventType)
                    .HasColumnName("event_type")
                    .HasMaxLength(100);
                entity.Property(item => item.SubjectType)
                    .HasColumnName("subject_type")
                    .HasMaxLength(50);
                entity.Property(item => item.SubjectId)
                    .HasColumnName("subject_id")
                    .HasMaxLength(100);
                entity.Property(item => item.ActorId)
                    .HasColumnName("actor_id")
                    .HasMaxLength(100);
                entity.Property(item => item.CorrelationId)
                    .HasColumnName("correlation_id")
                    .HasMaxLength(100);
                entity.Property(item => item.IdempotencyKey)
                    .HasColumnName("idempotency_key")
                    .HasMaxLength(100);
                entity.Property(item => item.OccurredAtUtc)
                    .HasColumnName("occurred_at_utc");
                entity.Property(item => item.DetailsJson)
                    .HasColumnName("details")
                    .HasColumnType("jsonb");
                entity.HasIndex(item => new { item.EventType, item.IdempotencyKey })
                    .IsUnique();
                entity.HasIndex(item => new { item.SubjectType, item.SubjectId });
                entity.HasIndex(item => item.OccurredAtUtc);
            });
    }

    /// <summary>
    /// Configures durable batches and lease-protected rows with unique source
    /// positions and efficient claim lookup.
    /// </summary>
    /// <param name="modelBuilder">The relational model builder.</param>
    private static void ConfigureBatches(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BatchEntity>(
            entity =>
            {
                entity.ToTable("parcel_batches");
                entity.HasKey(item => item.Id);
                entity.Property(item => item.Id).HasColumnName("id");
                entity.Property(item => item.IdempotencyKey)
                    .HasColumnName("idempotency_key")
                    .HasMaxLength(100);
                entity.Property(item => item.RequestFingerprint)
                    .HasColumnName("request_fingerprint")
                    .HasMaxLength(64)
                    .IsFixedLength();
                entity.Property(item => item.FallbackDestinationCountry)
                    .HasColumnName("destination_country")
                    .HasMaxLength(2)
                    .IsFixedLength();
                entity.Property(item => item.Status)
                    .HasColumnName("status")
                    .HasConversion<string>()
                    .HasMaxLength(30);
                entity.Property(item => item.TotalRows).HasColumnName("total_rows");
                entity.Property(item => item.CompletedRows)
                    .HasColumnName("completed_rows");
                entity.Property(item => item.FailedRows).HasColumnName("failed_rows");
                entity.Property(item => item.CreatedAtUtc)
                    .HasColumnName("created_at_utc");
                entity.Property(item => item.CreatedBy)
                    .HasColumnName("created_by")
                    .HasMaxLength(100);
                entity.HasIndex(item => item.IdempotencyKey).IsUnique();
            });

        modelBuilder.Entity<BatchRowEntity>(
            entity =>
            {
                entity.ToTable("parcel_batch_rows");
                entity.HasKey(item => item.Id);
                entity.Property(item => item.Id).HasColumnName("id");
                entity.Property(item => item.BatchId).HasColumnName("batch_id");
                entity.Property(item => item.RowNumber).HasColumnName("row_number");
                entity.Property(item => item.WeightKilograms)
                    .HasColumnName("weight_kilograms")
                    .HasPrecision(29, 12);
                entity.Property(item => item.DeclaredValueEuros)
                    .HasColumnName("declared_value_euros")
                    .HasPrecision(29, 12);
                entity.Property(item => item.DestinationCountry)
                    .HasColumnName("destination_country")
                    .HasMaxLength(2)
                    .IsFixedLength();
                entity.Property(item => item.CountrySource)
                    .HasColumnName("country_source")
                    .HasConversion<string>()
                    .HasMaxLength(30);
                entity.Property(item => item.Status)
                    .HasColumnName("status")
                    .HasConversion<string>()
                    .HasMaxLength(30);
                entity.Property(item => item.ErrorCode)
                    .HasColumnName("error_code")
                    .HasMaxLength(100);
                entity.Property(item => item.ErrorMessage)
                    .HasColumnName("error_message")
                    .HasMaxLength(500);
                entity.Property(item => item.AttemptCount)
                    .HasColumnName("attempt_count");
                entity.Property(item => item.DecisionId).HasColumnName("decision_id");
                entity.Property(item => item.ClaimToken).HasColumnName("claim_token");
                entity.Property(item => item.LeaseExpiresAtUtc)
                    .HasColumnName("lease_expires_at_utc");
                entity.HasIndex(item => new { item.BatchId, item.RowNumber })
                    .IsUnique();
                entity.HasIndex(item => new { item.Status, item.LeaseExpiresAtUtc });
                entity.HasOne(item => item.Batch)
                    .WithMany(item => item.Rows)
                    .HasForeignKey(item => item.BatchId)
                    .OnDelete(DeleteBehavior.Cascade);
            });
    }

    /// <summary>
    /// Seeds the reviewed default version so the first deployment can route
    /// safely before a rule-administration interface exists.
    /// </summary>
    /// <param name="modelBuilder">The relational model builder.</param>
    private static void SeedDefaultRuleSet(ModelBuilder modelBuilder)
    {
        DateTimeOffset seededAtUtc =
            new(2026, 7, 28, 0, 0, 0, TimeSpan.Zero);
        modelBuilder.Entity<RuleSetEntity>().HasData(
            new
            {
                Version = 1,
                Status = RuleSetLifecycleStatus.Active,
                CreatedAtUtc = seededAtUtc,
                CreatedBy = "system",
                ActivatedAtUtc = (DateTimeOffset?)seededAtUtc,
            });
        modelBuilder.Entity<WeightBandRuleEntity>().HasData(
            new
            {
                Id = 1L,
                RuleSetVersion = 1,
                RuleId = DefaultRoutingRuleIds.MailWeight.Value,
                Priority = 100,
                LowerBoundExclusive = 0m,
                UpperBoundInclusive = (decimal?)1m,
                Department = RoutingDepartment.Mail,
            },
            new
            {
                Id = 2L,
                RuleSetVersion = 1,
                RuleId = DefaultRoutingRuleIds.RegularWeight.Value,
                Priority = 200,
                LowerBoundExclusive = 1m,
                UpperBoundInclusive = (decimal?)10m,
                Department = RoutingDepartment.Regular,
            },
            new
            {
                Id = 3L,
                RuleSetVersion = 1,
                RuleId = DefaultRoutingRuleIds.HeavyWeight.Value,
                Priority = 300,
                LowerBoundExclusive = 10m,
                UpperBoundInclusive = (decimal?)null,
                Department = RoutingDepartment.Heavy,
            });
        modelBuilder.Entity<InsuranceRuleEntity>().HasData(
            new
            {
                RuleSetVersion = 1,
                RuleId = DefaultRoutingRuleIds.InsuranceValue.Value,
                Priority = 1_000,
                ThresholdExclusiveEuros = 1_000m,
            });
    }
}

/// <summary>
/// Supplies EF migration tooling with a non-secret local design-time context.
/// Runtime configuration remains the responsibility of a later API composition
/// phase.
/// </summary>
public sealed class ParcelRoutingDesignTimeDbContextFactory :
    IDesignTimeDbContextFactory<ParcelRoutingDbContext>
{
    /// <summary>
    /// Creates the design-time context used only to generate and inspect
    /// migrations; it does not connect while building the model.
    /// </summary>
    /// <param name="args">Unused EF tooling arguments.</param>
    /// <returns>A PostgreSQL-configured migration context.</returns>
    public ParcelRoutingDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ParcelRoutingDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=parcel_routing;Username=postgres")
            .Options;

        return new ParcelRoutingDbContext(options);
    }
}
