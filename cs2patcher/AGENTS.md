# Cities: Skylines 2 — macOS / Wine Patcher

A patching tool for Cities: Skylines 2 running under CrossOver/Wine on macOS.

## Summary

Cities: Skylines 2 does not fully behave correctly under CrossOver/Wine on macOS. Some Windows filesystem and platform calls return unexpected results, which can cause crashes, failed asset loading, broken mod behaviour, or Paradox Mods issues.

This tool patches selected Cities: Skylines 2 managed assemblies to improve compatibility when running the game through CrossOver on macOS.

Current focus:

- Fix crashes caused by Wine-specific filesystem behaviour.
- Improve asset and mod loading reliability.
- Enable Paradox Mods functionality where Wine compatibility issues interfere.
- Keep patches targeted, minimal, and reversible where practical.

The primary goal is to make Cities: Skylines 2 more stable and usable on macOS via CrossOver without changing gameplay behaviour.

---

# Development Standards

## General

1. Follow existing project architecture and conventions unless explicitly instructed otherwise.
2. Prefer readability, maintainability, and patch safety over clever or overly broad solutions.
3. Keep changes focused on the requested patch. Avoid unrelated refactors.
4. All control-flow statements must use braces:
   - if
   - else
   - for
   - foreach
   - while
   - do
   - switch

   Braces are required even for single-line statements.

5. Naming conventions:
   - Private fields: _camelCase
   - Properties: PascalCase
   - Methods: PascalCase
   - Classes: PascalCase
   - Interfaces: IInterfaceName

6. Use descriptive names. Avoid abbreviations unless they are already established within the project or .NET ecosystem.

7. Remove unused code, variables, and imports when modifying a file.

8. Avoid introducing new dependencies unless specifically approved.

9. Keep patch logic deterministic and conservative.

10. Target framework:
   - .NET
   - Mono.Cecil for assembly inspection and rewriting
   - CrossOver/Wine compatibility on macOS

11. Workflow requirements:
   - Do not make code changes immediately.
   - First present the proposed changes.
   - Wait for approval before generating modified code.
   - Approval may be explicit acceptance or a requested revision.

12. Follow framework, platform, and patching best practices whenever appropriate.

---

## Patch Semantics

### Patch Scope

1. Patches must target only the minimum required instructions, methods, or assemblies.
2. Avoid broad rewrites unless there is no safe targeted alternative.
3. Do not change gameplay logic unless explicitly required for compatibility.
4. Prefer NOP-based or branch-targeted patches when they are safer than reconstructing larger method bodies.
5. Each patch should clearly document:
   - The affected assembly.
   - The affected method or state machine.
   - The Wine/macOS-specific issue.
   - The expected behavioural change.

### Safety

1. Always validate that the expected IL pattern exists before patching.
2. If a required pattern cannot be found, skip the patch and report the reason.
3. Avoid applying the same patch multiple times.
4. Prefer idempotent patching where practical.
5. Do not silently ignore failed patches.

### File Handling

1. Check that target assemblies exist before attempting to patch them.
2. Avoid destructive writes unless the patch has been validated.
3. Preserve or create backups when appropriate.
4. Report skipped, successful, failed, and already-applied patches clearly.

---

## Code Semantics

### Access Modifiers

1. Methods, properties, fields, classes, and structs should always use the minimum required visibility.
2. Do not expose members publicly unless there is a clear requirement.

### Constants

1. Constant names must use ALL_CAPS_UNDERSCORE formatting.

Example:

csharp private const int MAX_PATCH_COUNT = 10; 

### Member Ordering

Order fields as follows:

1. Constants
2. Private fields
3. Protected fields
4. Public fields

Example:

csharp private const string TARGET_ASSEMBLY = "Colossal.IO.dll";  private readonly string _managedDirectory;  protected bool _isDryRun;  public string DisplayName; 

### Method Ordering

Methods should be ordered as follows:

1. Public methods
2. Protected methods
3. Private methods

---

## Documentation & References

### Mono.Cecil

https://github.com/jbevain/cecil

### CrossOver

https://www.codeweavers.com/crossover

### Cities: Skylines 2

https://www.paradoxinteractive.com/games/cities-skylines-ii/about

---

## AI Collaboration Guidelines

1. Never apply code changes immediately.
   - Present the proposed solution first.
   - Explain the approach.
   - Wait for approval before producing modified code.

2. When proposing changes:
   - Explain what will change.
   - Explain why the change is needed.
   - Explain the expected outcome or benefit.
   - Identify any risks or side effects.

3. Prefer providing complete classes rather than isolated methods to ensure full context is available during review.

4. If multiple implementation approaches exist:
   - Present the available options.
   - Explain the trade-offs.
   - Recommend the preferred solution and justify the recommendation.

5. Assume explanations should be explicit and structured.
   - Avoid skipping reasoning steps.
   - Prioritize clarity over brevity.
:::`