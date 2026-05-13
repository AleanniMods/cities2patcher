namespace Cs2MacPatcher;

record PatchSummary(string DllName, int FixesApplied, bool DryRun, string? SkipReason = null)
{
    public bool IsSkipped => SkipReason != null;
    public bool AlreadyOk => FixesApplied == 0 && SkipReason == null;

    public static PatchSummary Skipped(string reason) =>
        new("", 0, false, reason);

    public static PatchSummary AlreadyPatched(string dll) =>
        new(dll, 0, false);
}
