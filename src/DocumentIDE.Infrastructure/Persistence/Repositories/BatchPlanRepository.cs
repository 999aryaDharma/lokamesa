using System;
using System.Collections.Generic;
using System.Data;
using System.Text.Json;
using System.Threading.Tasks;
using DocumentIDE.Core.Batch;
using DocumentIDE.Core.Domain.Repositories;
using Microsoft.Data.Sqlite;

namespace DocumentIDE.Infrastructure.Persistence.Repositories;

public class BatchPlanRepository : IBatchPlanRepository
{
    private readonly AppDb _appDb;

    public BatchPlanRepository(AppDb appDb)
    {
        _appDb = appDb ?? throw new ArgumentNullException(nameof(appDb));
    }

    public async Task SavePlanAsync(BatchOperationPlan plan)
    {
        if (plan == null) throw new ArgumentNullException(nameof(plan));

        using var connection = _appDb.CreateConnection();
        await connection.OpenAsync();
        using var tx = connection.BeginTransaction();

        var planSql = @"
            INSERT INTO batch_operation_plans (
                id, workspace_id, operation_type, scope_json, parameters_json, status, created_at, expires_at, idempotency_key
            ) VALUES (
                @id, @workspaceId, @opType, @scopeJson, @parametersJson, @status, @createdAt, @expiresAt, @idempotencyKey
            )
            ON CONFLICT(id) DO UPDATE SET
                workspace_id = excluded.workspace_id,
                operation_type = excluded.operation_type,
                scope_json = excluded.scope_json,
                parameters_json = excluded.parameters_json,
                status = excluded.status,
                created_at = excluded.created_at,
                expires_at = excluded.expires_at,
                idempotency_key = excluded.idempotency_key;
        ";

        using (var planCmd = connection.CreateCommand())
        {
            planCmd.Transaction = tx;
            planCmd.CommandText = planSql;
            planCmd.Parameters.AddWithValue("@id", plan.Id);
            planCmd.Parameters.AddWithValue("@workspaceId", plan.WorkspaceId);
            planCmd.Parameters.AddWithValue("@opType", plan.OperationType.ToString());
            planCmd.Parameters.AddWithValue("@scopeJson", plan.ScopeJson ?? string.Empty);
            planCmd.Parameters.AddWithValue("@parametersJson", plan.ParametersJson ?? string.Empty);
            planCmd.Parameters.AddWithValue("@status", plan.Status.ToString());
            planCmd.Parameters.AddWithValue("@createdAt", plan.CreatedAt.ToString("O"));
            planCmd.Parameters.AddWithValue("@expiresAt", plan.ExpiresAt.ToString("O"));
            planCmd.Parameters.AddWithValue("@idempotencyKey", (object?)plan.IdempotencyKey ?? DBNull.Value);
            await planCmd.ExecuteNonQueryAsync();
        }

        // Replace files for this plan
        using (var deleteFilesCmd = connection.CreateCommand())
        {
            deleteFilesCmd.Transaction = tx;
            deleteFilesCmd.CommandText = "DELETE FROM batch_operation_plan_files WHERE plan_id = @planId;";
            deleteFilesCmd.Parameters.AddWithValue("@planId", plan.Id);
            await deleteFilesCmd.ExecuteNonQueryAsync();
        }

        if (plan.Files != null && plan.Files.Count > 0)
        {
            var fileSql = @"
                INSERT INTO batch_operation_plan_files (
                    plan_id, document_id, expected_fingerprint, match_count, selected, selected_change_ids_json, plan_payload_json
                ) VALUES (
                    @planId, @documentId, @expectedFingerprint, @matchCount, @selected, @selectedChangeIdsJson, @planPayloadJson
                );
            ";

            foreach (var file in plan.Files)
            {
                file.PlanId = plan.Id;
                using var fileCmd = connection.CreateCommand();
                fileCmd.Transaction = tx;
                fileCmd.CommandText = fileSql;
                fileCmd.Parameters.AddWithValue("@planId", file.PlanId);
                fileCmd.Parameters.AddWithValue("@documentId", file.DocumentId);
                fileCmd.Parameters.AddWithValue("@expectedFingerprint", file.ExpectedFingerprint ?? string.Empty);
                fileCmd.Parameters.AddWithValue("@matchCount", file.MatchCount);
                fileCmd.Parameters.AddWithValue("@selected", file.Selected ? 1 : 0);
                fileCmd.Parameters.AddWithValue("@selectedChangeIdsJson", JsonSerializer.Serialize(file.SelectedChangeIds ?? new List<string>()));
                fileCmd.Parameters.AddWithValue("@planPayloadJson", (object?)file.PlanPayloadJson ?? DBNull.Value);
                await fileCmd.ExecuteNonQueryAsync();
            }
        }

        await tx.CommitAsync();
    }

    public async Task<BatchOperationPlan?> GetByIdAsync(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;

        using var connection = _appDb.CreateConnection();
        await connection.OpenAsync();

        BatchOperationPlan? plan = null;
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT id, workspace_id, operation_type, scope_json, parameters_json, status, created_at, expires_at, idempotency_key
                FROM batch_operation_plans
                WHERE id = @id;
            ";
            cmd.Parameters.AddWithValue("@id", id);

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                plan = MapReaderToPlan(reader);
            }
        }

        if (plan != null)
        {
            plan.Files = await LoadFilesForPlanAsync(connection, plan.Id);
        }

        return plan;
    }

    public async Task<BatchOperationPlan?> GetByIdempotencyKeyAsync(string workspaceId, string idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(workspaceId) || string.IsNullOrWhiteSpace(idempotencyKey)) return null;

        using var connection = _appDb.CreateConnection();
        await connection.OpenAsync();

        BatchOperationPlan? plan = null;
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT id, workspace_id, operation_type, scope_json, parameters_json, status, created_at, expires_at, idempotency_key
                FROM batch_operation_plans
                WHERE workspace_id = @workspaceId AND idempotency_key = @key;
            ";
            cmd.Parameters.AddWithValue("@workspaceId", workspaceId);
            cmd.Parameters.AddWithValue("@key", idempotencyKey);

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                plan = MapReaderToPlan(reader);
            }
        }

        if (plan != null)
        {
            plan.Files = await LoadFilesForPlanAsync(connection, plan.Id);
        }

        return plan;
    }

    public async Task<IReadOnlyList<BatchOperationPlan>> GetByWorkspaceAsync(string workspaceId)
    {
        if (string.IsNullOrWhiteSpace(workspaceId)) return Array.Empty<BatchOperationPlan>();

        using var connection = _appDb.CreateConnection();
        await connection.OpenAsync();

        var plans = new List<BatchOperationPlan>();
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT id, workspace_id, operation_type, scope_json, parameters_json, status, created_at, expires_at, idempotency_key
                FROM batch_operation_plans
                WHERE workspace_id = @workspaceId
                ORDER BY created_at DESC;
            ";
            cmd.Parameters.AddWithValue("@workspaceId", workspaceId);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                plans.Add(MapReaderToPlan(reader));
            }
        }

        foreach (var plan in plans)
        {
            plan.Files = await LoadFilesForPlanAsync(connection, plan.Id);
        }

        return plans;
    }

    public async Task UpdateStatusAsync(string planId, BatchPlanStatus newStatus)
    {
        if (string.IsNullOrWhiteSpace(planId)) return;

        using var connection = _appDb.CreateConnection();
        await connection.OpenAsync();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE batch_operation_plans SET status = @status WHERE id = @id;";
        cmd.Parameters.AddWithValue("@status", newStatus.ToString());
        cmd.Parameters.AddWithValue("@id", planId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeleteAsync(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return;

        using var connection = _appDb.CreateConnection();
        await connection.OpenAsync();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM batch_operation_plans WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    private static BatchOperationPlan MapReaderToPlan(SqliteDataReader reader)
    {
        Enum.TryParse<BatchOperationType>(reader.GetString(2), out var opType);
        Enum.TryParse<BatchPlanStatus>(reader.GetString(5), out var status);

        return new BatchOperationPlan
        {
            Id = reader.GetString(0),
            WorkspaceId = reader.GetString(1),
            OperationType = opType,
            ScopeJson = reader.GetString(3),
            ParametersJson = reader.GetString(4),
            Status = status,
            CreatedAt = DateTimeOffset.Parse(reader.GetString(6)),
            ExpiresAt = DateTimeOffset.Parse(reader.GetString(7)),
            IdempotencyKey = reader.IsDBNull(8) ? null : reader.GetString(8)
        };
    }

    private static async Task<List<BatchOperationPlanFile>> LoadFilesForPlanAsync(SqliteConnection connection, string planId)
    {
        var files = new List<BatchOperationPlanFile>();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT plan_id, document_id, expected_fingerprint, match_count, selected, selected_change_ids_json, plan_payload_json
            FROM batch_operation_plan_files
            WHERE plan_id = @planId
            ORDER BY document_id ASC;
        ";
        cmd.Parameters.AddWithValue("@planId", planId);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var selectedChangeIdsJson = reader.IsDBNull(5) ? null : reader.GetString(5);
            List<string> selectedChangeIds;
            if (!string.IsNullOrEmpty(selectedChangeIdsJson))
            {
                try
                {
                    selectedChangeIds = JsonSerializer.Deserialize<List<string>>(selectedChangeIdsJson) ?? new List<string>();
                }
                catch
                {
                    selectedChangeIds = new List<string>();
                }
            }
            else
            {
                selectedChangeIds = new List<string>();
            }

            files.Add(new BatchOperationPlanFile
            {
                PlanId = reader.GetString(0),
                DocumentId = reader.GetString(1),
                ExpectedFingerprint = reader.GetString(2),
                MatchCount = reader.GetInt32(3),
                Selected = reader.GetInt32(4) != 0,
                SelectedChangeIds = selectedChangeIds,
                PlanPayloadJson = reader.IsDBNull(6) ? null : reader.GetString(6)
            });
        }

        return files;
    }
}
