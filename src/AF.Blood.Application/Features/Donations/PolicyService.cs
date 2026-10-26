using AF.Blood.Application.Abstractions;
using AF.Blood.Application.Domain;
using AF.Blood.Contracts;
using Dapper;
using Npgsql;

namespace AF.Blood.Application.Features.Donations;

/// <summary>Reads approved deferral policy versions. Policy values are provided
/// by approved policy rows only — this code never hard-codes intervals.</summary>
public sealed class PolicyService(NpgsqlDataSource db)
{
    public const string WholeBlood = "whole_blood";

    /// <summary>The active policy for a component, readable inside a caller's transaction.</summary>
    public async Task<DeferralPolicy?> GetActiveAsync(
        NpgsqlConnection conn, NpgsqlTransaction? tx, string componentType = WholeBlood, CancellationToken ct = default)
    {
        return await conn.QuerySingleOrDefaultAsync<DeferralPolicy>(new CommandDefinition("""
            select id, version, deferral_days as deferraldays, provisional_days as provisionaldays,
                   effective_from as effectivefrom
              from deferral_policy_versions
             where is_active and component_type = @componentType
            """,
            new { componentType },
            tx,
            cancellationToken: ct));
    }

    public async Task<DeferralPolicy?> GetByIdAsync(
        NpgsqlConnection conn, NpgsqlTransaction? tx, Guid policyVersionId, CancellationToken ct = default)
    {
        return await conn.QuerySingleOrDefaultAsync<DeferralPolicy>(new CommandDefinition("""
            select id, version, deferral_days as deferraldays, provisional_days as provisionaldays,
                   effective_from as effectivefrom
              from deferral_policy_versions
             where id = @policyVersionId
            """,
            new { policyVersionId },
            tx,
            cancellationToken: ct));
    }

    public async Task<IReadOnlyList<PolicyVersionDto>> ListAsync(CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<PolicyRow>(new CommandDefinition("""
            select id, version, component_type, deferral_days, provisional_days, effective_from,
                   is_active, approved_by, approval_reference, notes, created_at_utc
              from deferral_policy_versions
             order by created_at_utc desc
            """,
            cancellationToken: ct));
        return rows.Select(Map).ToList();
    }

    public async Task<PolicyVersionDto> CreateAsync(
        Guid adminUserId, CreatePolicyVersionRequest request, string? correlationId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Version) || request.Version.Length > 100)
        {
            throw AppException.Invalid("Policy version label is required (max 100 characters).");
        }

        if (request.DeferralDays is < 0 or > 365 || request.ProvisionalDays is < 0 or > 365)
        {
            throw AppException.Invalid("Deferral days must be between 0 and 365.");
        }

        if (string.IsNullOrWhiteSpace(request.ApprovedBy) || string.IsNullOrWhiteSpace(request.ApprovalReference))
        {
            throw AppException.Invalid(
                "An approval reference and approver are mandatory: policies must be approved by a qualified transfusion professional.");
        }

        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        try
        {
            var id = await conn.ExecuteScalarAsync<Guid>(new CommandDefinition("""
                insert into deferral_policy_versions
                    (version, component_type, deferral_days, provisional_days, effective_from,
                     is_active, approved_by, approval_reference, notes)
                values (@version, @componentType, @deferralDays, @provisionalDays, @effectiveFrom,
                        false, @approvedBy, @approvalReference, @notes)
                returning id
                """,
                new
                {
                    request.Version,
                    componentType = WholeBlood,
                    request.DeferralDays,
                    request.ProvisionalDays,
                    request.EffectiveFrom,
                    approvedBy = request.ApprovedBy.Trim(),
                    approvalReference = request.ApprovalReference.Trim(),
                    request.Notes,
                },
                tx,
                cancellationToken: ct));

            await tx.CommitAsync(ct);
            return (await ListAsync(ct)).First(p => p.Id == id);
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            await tx.RollbackAsync(ct);
            throw AppException.Conflict("A policy version with this label already exists.");
        }
    }

    /// <summary>Activates a version (deactivating the previously active one for
    /// the same component). Applies to *new* donation reports only — existing
    /// records keep the policy that was applied to them.</summary>
    public async Task<PolicyVersionDto> ActivateAsync(
        Guid adminUserId, Guid policyVersionId, string? correlationId, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var target = await conn.QuerySingleOrDefaultAsync<(Guid Id, string ComponentType)>(
            new CommandDefinition("select id, component_type from deferral_policy_versions where id = @id for update",
                new { id = policyVersionId }, tx, cancellationToken: ct));

        if (target == default)
        {
            throw AppException.NotFound("Policy version not found.");
        }

        await conn.ExecuteAsync(new CommandDefinition("""
            update deferral_policy_versions set is_active = false
             where component_type = @componentType and is_active
            """,
            new { target.ComponentType }, tx, cancellationToken: ct));

        await conn.ExecuteAsync(new CommandDefinition(
            "update deferral_policy_versions set is_active = true where id = @id",
            new { id = policyVersionId }, tx, cancellationToken: ct));

        await tx.CommitAsync(ct);
        return (await ListAsync(ct)).First(p => p.Id == policyVersionId);
    }

    public static PolicyVersionDto Map(PolicyRow r) => new(
        r.Id, r.Version, r.Component_type, r.Deferral_days, r.Provisional_days,
        r.Effective_from, r.Is_active, r.Approved_by, r.Approval_reference, r.Notes, r.Created_at_utc);

    public sealed class PolicyRow
    {
        public Guid Id { get; init; }
        public string Version { get; init; } = null!;
        public string Component_type { get; init; } = null!;
        public int Deferral_days { get; init; }
        public int Provisional_days { get; init; }
        public DateOnly Effective_from { get; init; }
        public bool Is_active { get; init; }
        public string Approved_by { get; init; } = null!;
        public string Approval_reference { get; init; } = null!;
        public string? Notes { get; init; }
        public DateTimeOffset Created_at_utc { get; init; }
    }
}
