namespace Nexus.QuerySafety;

public sealed record StructuredQueryValidationResult(
    bool IsValid,
    IReadOnlyList<string> Errors,
    int? EffectiveLimit)
{
    public static StructuredQueryValidationResult Success(int effectiveLimit) =>
        new(true, [], effectiveLimit);

    public static StructuredQueryValidationResult Failure(IReadOnlyList<string> errors) =>
        new(false, errors, null);
}
