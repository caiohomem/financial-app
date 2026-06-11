using Npgsql;

public sealed class RuleManager
{
    private const int UserRulePriority = 200;

    public async Task<RuleCreationResult> CreateRuleAndRecategorizeAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string pattern,
        string matchType,
        int categoryCanonicalId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var insertCommand = connection.CreateCommand();
            insertCommand.Transaction = transaction;
            insertCommand.CommandText = """
                INSERT INTO rules (pattern, match_type, category_canonical_id, priority)
                VALUES (@pattern, @matchType, @categoryCanonicalId, @priority)
                RETURNING id;
                """;
            insertCommand.Parameters.AddWithValue("pattern", pattern);
            insertCommand.Parameters.AddWithValue("matchType", matchType);
            insertCommand.Parameters.AddWithValue("categoryCanonicalId", categoryCanonicalId);
            insertCommand.Parameters.AddWithValue("priority", UserRulePriority);

            _ = (int)(await insertCommand.ExecuteScalarAsync(cancellationToken))!;
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return RuleCreationResult.Conflict();
        }

        var recategorizedTransactions = await RecategorizeTransactionsAsync(
            connection,
            transaction,
            pattern,
            matchType,
            categoryCanonicalId,
            cancellationToken);

        return RuleCreationResult.Success(recategorizedTransactions);
    }

    public Task<int> RecategorizeByRuleAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string pattern,
        string matchType,
        int categoryCanonicalId,
        CancellationToken cancellationToken) =>
        RecategorizeTransactionsAsync(connection, transaction, pattern, matchType, categoryCanonicalId, cancellationToken);

    private static async Task<int> RecategorizeTransactionsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string pattern,
        string matchType,
        int categoryCanonicalId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.Parameters.AddWithValue("categoryCanonicalId", categoryCanonicalId);
        command.Parameters.AddWithValue("pattern", pattern);

        command.CommandText = matchType switch
        {
            "merchant_eq" => """
                UPDATE transactions
                SET category_canonical_id = @categoryCanonicalId
                WHERE normalized_merchant IS NOT NULL
                  AND normalized_merchant ILIKE @pattern;
                """,
            "contains" => """
                UPDATE transactions
                SET category_canonical_id = @categoryCanonicalId
                WHERE (
                    COALESCE(normalized_merchant, '') ILIKE '%' || @pattern || '%'
                    OR raw_description ILIKE '%' || @pattern || '%'
                );
                """,
            "regex" => """
                UPDATE transactions
                SET category_canonical_id = @categoryCanonicalId
                WHERE COALESCE(normalized_merchant, raw_description) ~* @pattern;
                """,
            _ => throw new InvalidOperationException($"Unsupported match type '{matchType}'.")
        };

        return await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

public sealed record RuleCreationResult(bool Created, int RecategorizedTransactions)
{
    public static RuleCreationResult Success(int recategorizedTransactions) =>
        new(true, recategorizedTransactions);

    public static RuleCreationResult Conflict() =>
        new(false, 0);
}
