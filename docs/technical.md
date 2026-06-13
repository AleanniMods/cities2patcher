# Technical Reference — CS2 macOS Patcher

This document explains every Wine-specific bug patched by this tool, how they manifest, why they happen, and what the IL-level fix does.

All patches are applied to .NET assemblies using [Mono.Cecil](https://github.com/jbevain/cecil) — a library for reading and rewriting .NET IL bytecode without access to source code.

---

## Colossal.IO.dll — Game launch crash

### Bug: FindNextFile error check throws on valid results

**Symptom:** Game crashes immediately on launch before reaching the main menu.

**Cause:** `Colossal.IO.LongDirectory` uses P/Invoke to call Win32 `FindNextFile`. On Wine, this function returns `false` (and sets `GetLastError()` to `ERROR_NO_MORE_FILES`) even when enumeration succeeded. The error-checking code unconditionally calls `GetExceptionFromWin32Error` and throws.

**Fix:** NOP the block `GetLastWin32Error → GetExceptionFromWin32Error → throw` in all `LongDirectory` state machine `MoveNext` methods. The return value from `FindNextFile` is ignored; the game continues normally.

---

### Bug: LongFile.Open throws IOException: Success on Wine long paths

**Symptom:** The game opens, but settings/mod startup can fail with `System.IO.IOException: Success` while opening files through `System.IO.LongFile.Open`. When the file is opened through the regular .NET path APIs instead of the normalized `\\?\` long-path form, the same file can be read successfully.

**Root cause:** `LongFile.Open` normalizes every path through `LongPath.NormalizeLongPath`, then calls `LongFile.GetFileHandle`. On Wine, the normalized `\\?\C:\...` path can fail in the Win32 `CreateFile` path while the failure still reports error code `0` (`"Success"`). The original implementation treats that as a real `IOException` and never attempts the equivalent non-`\\?\` path.

**Fix:** Replace `LongFile.Open` with a helper that preserves the original behavior first, then falls back only for Wine's spurious `"Success"` IOException:

```csharp
var guid = Guid.NewGuid();

try {
    var normalizedPath = LongPath.NormalizeLongPath(path);
    var handle = GetFileHandle(normalizedPath, guid, mode, access, share, options);
    return new FileStreamWithHandle(handle, guid, access, bufferSize, async, disposeCallback);
}
catch (IOException ex) when (ex.Message.Contains("Success")) {
    var fallbackPath = path.StartsWith(@"\\?\") ? path.Substring(4) : path;
    return new FileStreamWithDisposeCallback(
        fallbackPath, guid, mode, access, share, bufferSize, async, disposeCallback);
}
```

Implementation detail: the fallback helper is injected into `System.IO.LongFile` as `__Cs2MacPatcher_OpenWineFallback`. The patcher also detects and replaces an older bad helper body that ended with a bare `nop`, because Mono rejected it at runtime with `InvalidProgramException`. The current helper ends with:

```il
ldnull
ret
```

---

### Bug: CreateFile returns invalid handle with LastError=0 for normalized paths

**Symptom:** Long-path file opens can fail even before the fallback `FileStreamWithDisposeCallback` path is reached. The failure path shows an invalid file handle while `Marshal.GetLastWin32Error()` reports `0`.

**Root cause:** `LongFile.GetFileHandle` calls the internal `NativeMethods.CreateFile` with the normalized `\\?\` path. Under Wine, this can return an invalid handle while leaving `LastError` at `0`. The surrounding .NET code interprets the invalid handle as a failure, but there is no useful Win32 error code to distinguish it from Wine's long-path prefix bug.

**Fix:** Replace the direct `NativeMethods.CreateFile` call inside `LongFile.GetFileHandle` with an injected retry helper. The helper:

1. Calls the original `CreateFile`.
2. Captures `Marshal.GetLastWin32Error()`.
3. If the handle is valid, returns it.
4. If the handle is invalid and the error code is non-zero, returns it unchanged so the original error handling runs.
5. If the handle is invalid, the error code is `0`, and the path starts with `\\?\`, retries once with the prefix stripped.

Resulting behavior:

```csharp
var handle = CreateFile(path, ...);
var error = Marshal.GetLastWin32Error();

if (!handle.IsInvalid || error != 0 || !path.StartsWith(@"\\?\"))
    return handle;

return CreateFile(path.Substring(4), ...);
```

This stays narrow: it only changes Wine's invalid-handle/`LastError=0` case and leaves real Win32 failures on the original path.

---

## Colossal.IO.AssetDatabase.dll — Asset loading crash

### Bug: File.Exists returns true for non-existent files

**Symptom:** Mods fail to load. Log shows "Failed to add Mod" or similar asset errors.

**Root cause:** Wine's `GetFileAttributesW` (called by .NET `File.Exists`) returns `S_OK` (success) instead of `ERROR_FILE_NOT_FOUND` when the file doesn't exist but its parent directory does. So `File.Exists(".priority")` returns `true` even when no `.priority` file was created.

**Effect:** `FileSystemDataSource.PopulateFromDirectory` checks for a `.priority` file to sort mods. Finding it "exists", it calls `File.ReadAllLines(".priority")` which throws `FileNotFoundException`.

**Fix:** In `PopulateFromDirectory`, NOP the `ldstr ".priority"` + `call File::Exists` instructions and change `brfalse` to unconditional `br` — always skipping the `.priority` block.

---

## PDX.SDK.dll — Paradox Mods downloads

`PDX.SDK.dll` is the Paradox mod download SDK. It contains 17 Wine-specific bugs addressed by this patcher. The root-cause fixes are FIX 15 (download freeze), FIX 16 (subsequent-download deadlock), and FIX 17 (fresh-install IOException). FIX 7 and FIX 12 also include a fallback path that triggers an `InvalidProgramException` if not handled correctly.

### FIX 1–4: P/Invoke error checks and PathExists lies (DiskIODefaultWindows)

`DiskIODefaultWindows` wraps Win32 file operations. Under Wine:
- `DeleteLongPathFile`, `DeleteLongPathDirectory`, `CreateLongPathDirectory`, `LongPathMove` throw `IOException` even when the operation succeeded — NOP'd.
- Short-path equivalents (`DeleteFile`, `DeleteDirectory`, `CreateDirectory`, `Move`) receive spurious `IOException`s from the BCL — wrapped in try-catch that swallows them.
- `CreateLongPathDirectory` and `CreateDirectory` call `PathExists` before creating, which returns `true` for non-existent paths under Wine — the early-exit branch is NOP'd.

### FIX 5: CreateWriteStream — always create parent directory

`FileIO.CreateWriteStream` checks `PathExists` before creating the parent directory. Wine lies — NOP'd; the directory is always created.

### FIX 6: GetLongPath — slash direction

`DiskIODefaultWindows.GetLongPath` calls `Replace('/', DirectorySeparatorChar)` — on Wine the separator is `\` (`92`) but the literal `47` (`/`) stays in both calls. Changed both `ldc.i4.s 47` to `ldc.i4.s 92`.

### FIX 7: CancellationToken checks — broad safety net

Every `get_IsCancellationRequested` call in the DLL is replaced with `ldc.i4.0` (always `false`). This prevents any download operation from being cancelled by a spuriously-cancelled token. **Note:** FIX 15 is the targeted root-cause fix; FIX 7 is a belt-and-suspenders safety net for older versions.

**Wholesale-replace fallback (added after a `get_IsPaused` regression):** the surgical NOP approach fails when the instruction preceding `call get_IsCancellationRequested` is itself a `ret` from an earlier branch. `ModsDownloadProgressController.get_IsPaused` has exactly this shape:
```il
ldarg.0; ldfld tokenSource; dup; brtrue.s Exists
pop; ldc.i4.0; ret                 ← null branch returns false here
Exists:
call CancellationTokenSource::get_IsCancellationRequested   ← FIX 7 target
ret
```
The "else" branch of FIX 7 (when `prev` isn't `ldflda`) NOPs `il[i-1]`, which here is the early-return `ret`. Execution then falls through into our `ldc.i4.0` push, leaving the stack imbalanced at the final `ret`. Mono's verifier rejects the resulting body with `InvalidProgramException: Invalid IL code in ... get_IsPaused (): IL_000d: ret`.

When the patcher detects `prev.OpCode == Ret` on a `bool`-returning method, it replaces the entire method body with `ldc.i4.0; ret` instead of attempting surgical patching. Same fallback is wired into FIX 12.

### FIX 8: CreateLongPathFileStream — invalid handle IOException

`CreateLongPathFileStream` throws `IOException` on invalid handle even when Wine returned a valid handle. NOP'd.

### FIX 9: DownloadFilesInManifest — always re-download

In `RemoteRepository.<DownloadFilesInManifest>d__N.MoveNext`, the `FileAlreadyDownloaded` result (`bool`) is popped and replaced with an unconditional branch to the download path. Belt-and-suspenders for versions where FIX 14/16 pattern matching fails.

### FIX 10: InstallToFolder — GetInstalledVersion error bypass

`Executor.<InstallToFolder>d__13.MoveNext` calls `GetInstalledVersion` which returns an error result under Wine. The `get_Success` check and error-return block are NOP'd so installation proceeds unconditionally.

### FIX 11: TaskCanceledException — treat as regular I/O error

`ResultFactory.CreateFileIoResultFromException` has a special branch for `TaskCanceledException` that propagates cancellation state up the call stack. Under Wine, cancellations are spurious, not real user intent. The `isinst TaskCanceledException` + `brfalse` pattern is changed to always branch past the special handling.

### FIX 12: IsCancelledOperation — force false

`IsCancelledOperation` extension method checks are replaced with `ldc.i4.0` (always `false`) across all types in the DLL, preventing spurious cancellation propagation.

### FIX 13: PerformDownload PathExists — always create new file

`FileDownloader.<PerformDownload>d__N.MoveNext` calls `PathExists` to decide whether to append to an existing download or create a new file. Wine returns `true` for the non-existent `.downloading/*.zip` file, so the code tries to append to nothing. NOP the PathExists block and branch unconditionally to the "create new file" path.

### FIX 14: FileAlreadyDownloaded — always return false

`RemoteRepository.FileAlreadyDownloaded` body is replaced with:
```il
ldc.i4.0
call Task::FromResult<bool>
ret
```
This prevents `CheckIntegrity` from ever running, which would otherwise acquire a reader lock on the non-existent download file and leak it (see FIX 16). Kept for older SDK versions; superseded by FIX 16 on v1.5.8f1+.

---

### FIX 15: GetLockToken — premature Win32 timer cancellation (root cause, v1.5.8f1)

**Symptom:** Downloads start but freeze at 0% with no progress. `AcquireWriterLock` is never entered.

**Root cause:** `FileIO.GetLockToken` creates a `CancellationTokenSource` with a 10-second timeout:
```csharp
var cts = CancellationTokenSource.CreateLinkedTokenSource(
    cancellationToken,
    new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token);
return cts.Token;
```
`CancellationTokenSource(TimeSpan)` uses a Win32 waitable timer internally. Under Wine on macOS, this timer fires in **milliseconds**, not 10 seconds. Every call to `AcquireWriterLock` receives a token that is already cancelled before the semaphore can be acquired.

**Fix:** Replace the entire method body with:
```il
ldarg.1    // push cancellationToken parameter
ret        // return it unchanged
```
The outer download cancellation token is still passed through normally — user-initiated cancels work. Only the spurious 10-second timer is removed.

---

### FIX 16: CreateFileStream lock leak — reader lock not disposed on FileNotFoundException (root cause, v1.5.8f1)

**Symptom:** First download may succeed, but all subsequent downloads hang forever. Exact same symptom as FIX 15 if both are active, so FIX 15 must be applied first to observe FIX 16's failure mode in isolation.

**Root cause:**

Before each download, `FileIntegrityVerifier.CheckIntegrity` calls `FileIO.CreateFileStream` (read mode) to verify the file's integrity. The state machine `FileIO.<CreateFileStream>d__25.MoveNext` works like this:

```
1. Call FileLocks.FetchLock(path)          → gets AsyncReaderWriterLock for this path
2. Call lock.AcquireReaderLock(token)      → acquires reader lock (_readSemaphore.Wait)
3. Store result in local V_5 (AcquireLockResult)
4. Call DiskIO.PathExists(path)            → returns TRUE (Wine lies)
5. Open file for reading                   → throws FileNotFoundException (file doesn't exist)
6. [IOException catch block]
7.   Call ResultFactory.CreateIoResultFromException(ex)
8.   stloc.2  (store error result)
9.   *** leave.s IL_01f6  ← exits WITHOUT calling V_5.Dispose() ***
```

`AcquireLockResult.Dispose()` calls the release action (`ReleaseReaderLock`), which releases `_readSemaphore`. Without it, `_readSemaphore` stays at `0`. When the downloader then calls `FileIO.CreateWriteStream` for the same path, `AcquireWriterLock` calls `_readSemaphore.WaitAsync()` — which blocks forever because no one will ever release it.

`AsyncReaderWriterLock` is per-path: `FileLocks` uses `ConcurrentDictionary<string, AsyncReaderWriterLock>`. So only downloads for the same file path block each other; different mods download independently until they happen to retry the same path.

**Fix:** Insert before every `leave`/`leave.s` in the `IOException` catch block:
```il
ldloc.s V_5                              // push AcquireLockResult
callvirt AcquireLockResult::Dispose()    // release reader lock
```
`AcquireLockResult.Dispose()` is null-safe (checks `_releaseAction != null`) so calling it on a failed/unacquired lock is safe.

**Why this is cleaner than FIX 14:** FIX 14 prevents the code path from running at all (by always returning `false` from `FileAlreadyDownloaded`), which means the file is re-downloaded on every game launch even if it was already downloaded. FIX 16 fixes the actual bug so the integrity check can run normally.

---

### FIX 17: ListFiles / ListDirectories / ListFilesRecursive — IOException: Success on fresh install

**Symptom:** Downloads fail immediately at ~2% with "Preparing", UI shows "Failed". Only reproducible on a clean profile — users who already have the per-mod `.downloading/<modId>/` folders from previous attempts don't see it.

**Log evidence:**
```
[ERROR] [P06:04][ModsPatching.PrepareFolderForPatching][PerformDiskOperationAndCatch]
System.IO.IOException: Success : 'C:\users\crossover\AppData\LocalLow\Colossal Order\
  Cities Skylines II\.cache\Mods\pdx_mods\.downloading\143483_1'
  at System.IO.Enumeration.FileSystemEnumerator`1[TResult].CreateDirectoryHandle (...)
  at PDX.SDK.Internal.Util.IO.DiskIODefaultWindows.ListFiles (System.String path)
```

**Root cause:** `DiskIODefaultWindows.ListFiles` (and `ListDirectories`, `ListFilesRecursive`) is defined as:
```csharp
public List<string> ListFiles(string path) {
    if (!PathExists(path)) return new List<string>();
    if (IsLongPath(path)) return GetLongPathFiles(path);
    return Directory.GetFiles(path).ToList();
}
```
The `!PathExists` guard is meant to handle missing directories — but Wine's `PathExists` lies (returns `true` for non-existent paths, see FIX 4/5/13), so execution always falls through. When the actual `Directory.GetFiles(path)` is called on a non-existent directory, Wine's `GetFileAttributes` returns success with an invalid handle, and the .NET `FileSystemEnumerator` constructor throws `IOException` with the system error message for error code 0 (`"Success"`).

**Fix:** Wrap each method's entire body in `try { ... } catch (IOException) { return new List<string>(); }`. Implementation rewrites the IL in place:
1. Add a local variable `V_0` of the method's return type (`List<string>`).
2. Mutate every existing `ret` to `stloc V_0; leave End` (mutated in place — preserves branch target references).
3. Append the catch handler: `pop; newobj List<string>::.ctor(); stloc V_0; leave End`.
4. Append the join point: `End: ldloc V_0; ret`.
5. Register an `ExceptionHandler(Catch IOException)` covering `[firstInstr..catchPop)`.

The `newobj List<string>::.ctor()` reference is reused from the original method body — the `!PathExists` early-return uses exactly that constructor, so we don't need to import a new method reference.

Result for `ListFiles`:
```il
.try {
    ldarg.0; ldarg.1; callvirt PathExists
    brtrue.s Exists
    newobj List<string>::.ctor()
    stloc V_0; leave End

  Exists:
    ldarg.0; ldarg.1; call IsLongPath
    brtrue.s LongPath
    ldarg.1; call Directory::GetFiles
    call Enumerable::ToList
    stloc V_0; leave End

  LongPath:
    ldarg.0; ldarg.1; call GetLongPathFiles
    stloc V_0; leave End
}
.catch IOException {
    pop
    newobj List<string>::.ctor()
    stloc V_0; leave End
}
End:
    ldloc V_0
    ret
```

Why we keep the (useless) `PathExists` check: it costs nothing on Wine (always falls through) and remains correct on Windows where `PathExists` returns the real answer.

---

## IL comparison: FIX 15 + FIX 16 verified against v1.5.8f1

After applying both fixes, the patched methods produce identical IL to the reference working binary (verified with Mono.Cecil disassembly):

**GetLockToken:**
```il
ldarg.1
ret
```

**CreateFileStream IOException catch (final instructions):**
```il
...
call   ResultFactory::CreateIoResultFromException
stloc.2
ldloc.s V_5                              ← FIX 16 inserted
callvirt AcquireLockResult::Dispose()    ← FIX 16 inserted
leave.s IL_01f6
```
