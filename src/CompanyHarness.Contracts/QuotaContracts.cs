namespace CompanyHarness.Contracts;

public sealed record QuotaSummary(
    decimal MonthlyBudgetCny,
    decimal UsedThisMonthCny,
    int RequestsPerMinute,
    int TokensPerMinute,
    int MaxConcurrentRequests,
    DateTimeOffset ResetsAt);
