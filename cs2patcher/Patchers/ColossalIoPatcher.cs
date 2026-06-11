// Patches Colossal.IO.dll — fixes game launch crash on macOS/Wine
//
// Wine returns false from FindNextFile even on success, triggering an error check that
// throws an exception during directory enumeration at startup. Fix: NOP the block
// GetLastWin32Error → GetExceptionFromWin32Error → throw in LongDirectory state machines.

using Mono.Cecil;
using Mono.Cecil.Cil;
using System;
using System.IO;
using System.Linq;

namespace Cs2MacPatcher;

static class ColossalIoPatcher
{
    public static PatchSummary Patch(string managedDir, bool dryRun)
    {
        var dllPath = Path.Combine(managedDir, "Colossal.IO.dll");
        if (!File.Exists(dllPath))
            return PatchSummary.Skipped("Colossal.IO.dll not found");

        var module = ModuleDefinition.ReadModule(dllPath,
            new ReaderParameters { ReadingMode = ReadingMode.Immediate });

        var longDirType = module.Types.FirstOrDefault(t => t.Name == "LongDirectory");
        if (longDirType == null)
        {
            module.Dispose();
            return PatchSummary.Skipped("LongDirectory type not found — wrong DLL?");
        }

        int applied = 0;

        foreach (var nestedType in longDirType.NestedTypes)
        {
            var moveNext = nestedType.Methods.FirstOrDefault(m => m.Name == "MoveNext");
            if (moveNext == null) continue;

            var instructions = moveNext.Body.Instructions.ToList();
            for (int i = 0; i < instructions.Count; i++)
            {
                if (instructions[i].OpCode != OpCodes.Throw) continue;
                if (i < 1 || instructions[i - 1].OpCode != OpCodes.Call) continue;
                var callTarget = instructions[i - 1].Operand as MethodReference;
                if (callTarget == null || !callTarget.Name.Contains("GetExceptionFromWin32Error")) continue;

                int blockStart = -1;
                for (int j = i - 1; j >= Math.Max(0, i - 15); j--)
                {
                    if (instructions[j].OpCode == OpCodes.Call)
                    {
                        var m = instructions[j].Operand as MethodReference;
                        if (m != null && m.Name.Contains("GetLastWin32Error")) { blockStart = j; break; }
                    }
                }
                if (blockStart == -1) continue;

                if (!dryRun)
                    for (int j = blockStart; j <= i; j++)
                    { instructions[j].OpCode = OpCodes.Nop; instructions[j].Operand = null; }
                applied++;
            }
        }

        // ---- FIX 2: LongFile.GetFileHandle — retry Wine CreateFile invalid handle with LastError=0 ----
        // Wine can fail CreateFile for normalized \\?\ paths while leaving GetLastWin32Error() as 0
        // ("Success"). Retry once with the long-path prefix stripped, then let the original error
        // handling run if the handle is still invalid.
        var longFileType = module.Types.FirstOrDefault(t => t.FullName == "System.IO.LongFile");
        if (longFileType != null)
        {
            ApplyLongFileOpenFallback(module, longFileType, dryRun, ref applied);
            ApplyLongFileCreateFileRetry(module, longFileType, dryRun, ref applied);
        }

        if (applied == 0)
        {
            module.Dispose();
            return PatchSummary.AlreadyPatched("Colossal.IO.dll");
        }

        if (!dryRun)
        {
            BackupAndWrite(module, dllPath);
            return new PatchSummary("Colossal.IO.dll", applied, DryRun: false);
        }

        module.Dispose();
        return new PatchSummary("Colossal.IO.dll", applied, DryRun: true);
    }

    static void BackupAndWrite(ModuleDefinition module, string dllPath)
    {
        var backup = dllPath + ".bak";
        if (!File.Exists(backup)) File.Copy(dllPath, backup);
        var tmp = dllPath + ".tmp";
        module.Write(tmp);
        module.Dispose();
        File.Move(tmp, dllPath, overwrite: true);
    }

    static void ApplyLongFileOpenFallback(ModuleDefinition module, TypeDefinition longFileType, bool dryRun, ref int applied)
    {
        var open = longFileType.Methods.FirstOrDefault(m =>
            m.Name == "Open" &&
            m.HasBody &&
            m.Parameters.Count == 7 &&
            m.Parameters[0].ParameterType.MetadataType == MetadataType.String &&
            m.ReturnType.FullName == "System.IO.FileStream");
        if (open == null) return;

        var existingHelper = longFileType.Methods.FirstOrDefault(m => m.Name == "__Cs2MacPatcher_OpenWineFallback");
        if (existingHelper != null &&
            IsOpenWineFallbackHelperCurrent(existingHelper) &&
            open.Body.Instructions.Any(i =>
                i.Operand is MethodReference mr &&
                mr.Name == "__Cs2MacPatcher_OpenWineFallback"))
            return;

        if (!dryRun)
        {
            if (existingHelper != null)
                longFileType.Methods.Remove(existingHelper);

            var helper = EnsureOpenWineFallbackHelper(module, longFileType);
            open.Body.Instructions.Clear();
            open.Body.ExceptionHandlers.Clear();
            open.Body.Variables.Clear();
            open.Body.InitLocals = false;
            open.Body.MaxStackSize = 8;

            var il = open.Body.GetILProcessor();
            for (int i = 0; i < open.Parameters.Count; i++)
                il.Append(il.Create(OpCodes.Ldarg, i));
            il.Append(il.Create(OpCodes.Call, helper));
            il.Append(il.Create(OpCodes.Ret));
        }

        applied++;
    }

    static bool IsOpenWineFallbackHelperCurrent(MethodDefinition method)
    {
        var instructions = method.Body?.Instructions;
        return instructions is { Count: >= 2 } &&
               instructions[^2].OpCode == OpCodes.Ldnull &&
               instructions[^1].OpCode == OpCodes.Ret;
    }

    static MethodReference EnsureOpenWineFallbackHelper(ModuleDefinition module, TypeDefinition longFileType)
    {
        const string helperName = "__Cs2MacPatcher_OpenWineFallback";

        var existing = longFileType.Methods.FirstOrDefault(m => m.Name == helperName);
        if (existing != null) return existing;

        var mscorlib = module.AssemblyReferences.First(r => r.Name == "mscorlib");
        var open = longFileType.Methods.First(m =>
            m.Name == "Open" &&
            m.Parameters.Count == 7 &&
            m.Parameters[0].ParameterType.MetadataType == MetadataType.String &&
            m.ReturnType.FullName == "System.IO.FileStream");
        var boolType = module.TypeSystem.Boolean;
        var intType = module.TypeSystem.Int32;
        var stringType = module.TypeSystem.String;
        var ioExceptionType = new TypeReference("System.IO", "IOException", module, mscorlib);
        var exceptionType = new TypeReference("System", "Exception", module, mscorlib);

        var longPathType = module.Types.FirstOrDefault(t => t.FullName == "System.IO.LongPath");
        var normalizeLongPath = longPathType?.Methods.FirstOrDefault(m =>
            m.Name == "NormalizeLongPath" && m.Parameters.Count == 1);
        var getFileHandle = longFileType.Methods.FirstOrDefault(m =>
            m.Name == "GetFileHandle" && m.Parameters.Count == 6);
        var fileStreamWithHandleCtor = longFileType.NestedTypes
            .FirstOrDefault(t => t.Name == "FileStreamWithHandle")
            ?.Methods.FirstOrDefault(m => m.Name == ".ctor" && m.Parameters.Count == 6);
        var fileStreamWithDisposeCallbackCtor = longFileType.NestedTypes
            .FirstOrDefault(t => t.Name == "FileStreamWithDisposeCallback")
            ?.Methods.FirstOrDefault(m => m.Name == ".ctor" && m.Parameters.Count == 8);

        if (normalizeLongPath == null ||
            getFileHandle == null ||
            fileStreamWithHandleCtor == null ||
            fileStreamWithDisposeCallbackCtor == null)
            throw new InvalidOperationException("Could not find LongFile members needed for Wine Open fallback.");

        var fileStreamType = open.ReturnType;
        var fileModeType = open.Parameters[1].ParameterType;
        var fileAccessType = open.Parameters[2].ParameterType;
        var fileShareType = open.Parameters[3].ParameterType;
        var fileOptionsType = open.Parameters[5].ParameterType;
        var actionType = open.Parameters[6].ParameterType;
        var guidType = getFileHandle.Parameters[1].ParameterType;
        var safeFileHandleType = getFileHandle.ReturnType;

        var newGuid = new MethodReference("NewGuid", guidType, guidType);
        var getMessage = new MethodReference("get_Message", stringType, exceptionType) { HasThis = true };
        var contains = new MethodReference("Contains", boolType, stringType) { HasThis = true };
        contains.Parameters.Add(new ParameterDefinition(stringType));
        var startsWith = new MethodReference("StartsWith", boolType, stringType) { HasThis = true };
        startsWith.Parameters.Add(new ParameterDefinition(stringType));
        var substring = new MethodReference("Substring", stringType, stringType) { HasThis = true };
        substring.Parameters.Add(new ParameterDefinition(intType));

        var method = new MethodDefinition(
            helperName,
            MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.HideBySig,
            fileStreamType);

        method.Parameters.Add(new ParameterDefinition("path", ParameterAttributes.None, stringType));
        method.Parameters.Add(new ParameterDefinition("mode", ParameterAttributes.None, fileModeType));
        method.Parameters.Add(new ParameterDefinition("access", ParameterAttributes.None, fileAccessType));
        method.Parameters.Add(new ParameterDefinition("share", ParameterAttributes.None, fileShareType));
        method.Parameters.Add(new ParameterDefinition("bufferSize", ParameterAttributes.None, intType));
        method.Parameters.Add(new ParameterDefinition("options", ParameterAttributes.None, fileOptionsType));
        method.Parameters.Add(new ParameterDefinition("disposeCallback", ParameterAttributes.None, actionType));

        method.Body.InitLocals = true;
        method.Body.MaxStackSize = 8;
        var guidLocal = new VariableDefinition(guidType);
        var normalizedPathLocal = new VariableDefinition(stringType);
        var handleLocal = new VariableDefinition(safeFileHandleType);
        var exceptionLocal = new VariableDefinition(ioExceptionType);
        method.Body.Variables.Add(guidLocal);
        method.Body.Variables.Add(normalizedPathLocal);
        method.Body.Variables.Add(handleLocal);
        method.Body.Variables.Add(exceptionLocal);

        var il = method.Body.GetILProcessor();
        var setDefaultBuffer = il.Create(OpCodes.Ldc_I4, 4096);
        var afterBufferCheck = il.Create(OpCodes.Ldarg_0);
        var tryStart = il.Create(OpCodes.Ldloc, normalizedPathLocal);
        var handlerStart = il.Create(OpCodes.Stloc, exceptionLocal);
        var fallback = il.Create(OpCodes.Ldarg_0);
        var useOriginalPath = il.Create(OpCodes.Ldarg_0);
        var gotFallbackPath = il.Create(OpCodes.Ldloc, guidLocal);
        var handlerEnd = il.Create(OpCodes.Ldnull);

        il.Append(il.Create(OpCodes.Call, newGuid));
        il.Append(il.Create(OpCodes.Stloc, guidLocal));
        il.Append(il.Create(OpCodes.Ldarg_S, method.Parameters[4]));
        il.Append(il.Create(OpCodes.Brfalse_S, setDefaultBuffer));
        il.Append(il.Create(OpCodes.Br_S, afterBufferCheck));
        il.Append(setDefaultBuffer);
        il.Append(il.Create(OpCodes.Starg_S, method.Parameters[4]));
        il.Append(afterBufferCheck);
        il.Append(il.Create(OpCodes.Call, normalizeLongPath));
        il.Append(il.Create(OpCodes.Stloc, normalizedPathLocal));

        il.Append(tryStart);
        il.Append(il.Create(OpCodes.Ldloc, guidLocal));
        il.Append(il.Create(OpCodes.Ldarg_1));
        il.Append(il.Create(OpCodes.Ldarg_2));
        il.Append(il.Create(OpCodes.Ldarg_3));
        il.Append(il.Create(OpCodes.Ldarg_S, method.Parameters[5]));
        il.Append(il.Create(OpCodes.Call, getFileHandle));
        il.Append(il.Create(OpCodes.Stloc, handleLocal));
        il.Append(il.Create(OpCodes.Ldloc, handleLocal));
        il.Append(il.Create(OpCodes.Ldloc, guidLocal));
        il.Append(il.Create(OpCodes.Ldarg_2));
        il.Append(il.Create(OpCodes.Ldarg_S, method.Parameters[4]));
        il.Append(il.Create(OpCodes.Ldarg_S, method.Parameters[5]));
        il.Append(il.Create(OpCodes.Ldc_I4, (int)FileOptions.Asynchronous));
        il.Append(il.Create(OpCodes.And));
        il.Append(il.Create(OpCodes.Ldc_I4, (int)FileOptions.Asynchronous));
        il.Append(il.Create(OpCodes.Ceq));
        il.Append(il.Create(OpCodes.Ldarg_S, method.Parameters[6]));
        il.Append(il.Create(OpCodes.Newobj, fileStreamWithHandleCtor));
        il.Append(il.Create(OpCodes.Ret));

        il.Append(handlerStart);
        il.Append(il.Create(OpCodes.Ldloc, exceptionLocal));
        il.Append(il.Create(OpCodes.Callvirt, getMessage));
        il.Append(il.Create(OpCodes.Ldstr, "Success"));
        il.Append(il.Create(OpCodes.Callvirt, contains));
        il.Append(il.Create(OpCodes.Brtrue_S, fallback));
        il.Append(il.Create(OpCodes.Rethrow));
        il.Append(fallback);
        il.Append(il.Create(OpCodes.Ldstr, "\\\\?\\"));
        il.Append(il.Create(OpCodes.Callvirt, startsWith));
        il.Append(il.Create(OpCodes.Brfalse_S, useOriginalPath));
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldc_I4_4));
        il.Append(il.Create(OpCodes.Callvirt, substring));
        il.Append(il.Create(OpCodes.Br_S, gotFallbackPath));
        il.Append(useOriginalPath);
        il.Append(gotFallbackPath);
        il.Append(il.Create(OpCodes.Ldarg_1));
        il.Append(il.Create(OpCodes.Ldarg_2));
        il.Append(il.Create(OpCodes.Ldarg_3));
        il.Append(il.Create(OpCodes.Ldarg_S, method.Parameters[4]));
        il.Append(il.Create(OpCodes.Ldarg_S, method.Parameters[5]));
        il.Append(il.Create(OpCodes.Ldc_I4, (int)FileOptions.Asynchronous));
        il.Append(il.Create(OpCodes.And));
        il.Append(il.Create(OpCodes.Ldc_I4, (int)FileOptions.Asynchronous));
        il.Append(il.Create(OpCodes.Ceq));
        il.Append(il.Create(OpCodes.Ldarg_S, method.Parameters[6]));
        il.Append(il.Create(OpCodes.Newobj, fileStreamWithDisposeCallbackCtor));
        il.Append(il.Create(OpCodes.Ret));
        il.Append(handlerEnd);
        il.Append(il.Create(OpCodes.Ret));

        method.Body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Catch)
        {
            TryStart = tryStart,
            TryEnd = handlerStart,
            HandlerStart = handlerStart,
            HandlerEnd = handlerEnd,
            CatchType = ioExceptionType
        });

        longFileType.Methods.Add(method);
        return method;
    }

    static void ApplyLongFileCreateFileRetry(ModuleDefinition module, TypeDefinition longFileType, bool dryRun, ref int applied)
    {
        var getFileHandle = longFileType.Methods.FirstOrDefault(m =>
            m.Name == "GetFileHandle" && m.HasBody && m.Parameters.Count == 6);
        if (getFileHandle == null) return;

        var createFileCall = getFileHandle.Body.Instructions.FirstOrDefault(i =>
            i.OpCode == OpCodes.Call &&
            i.Operand is MethodReference mr &&
            mr.Name == "CreateFile" &&
            mr.DeclaringType.Name == "NativeMethods");
        if (createFileCall == null) return;

        var createFileRef = (MethodReference)createFileCall.Operand;
        if (!dryRun)
            createFileCall.Operand = EnsureCreateFileWineRetryHelper(module, longFileType, createFileRef);
        applied++;
    }

    static MethodReference EnsureCreateFileWineRetryHelper(
        ModuleDefinition module,
        TypeDefinition longFileType,
        MethodReference createFileRef)
    {
        const string helperName = "__Cs2MacPatcher_CreateFileWineRetry";

        var existing = longFileType.Methods.FirstOrDefault(m => m.Name == helperName);
        if (existing != null) return existing;

        var mscorlib = module.AssemblyReferences.First(r => r.Name == "mscorlib");
        var boolType = module.TypeSystem.Boolean;
        var intType = module.TypeSystem.Int32;
        var stringType = module.TypeSystem.String;
        var marshalType = new TypeReference("System.Runtime.InteropServices", "Marshal", module, mscorlib);
        var safeHandleType = new TypeReference("System.Runtime.InteropServices", "SafeHandle", module, mscorlib);

        var getLastWin32Error = new MethodReference("GetLastWin32Error", intType, marshalType);
        var getIsInvalid = new MethodReference("get_IsInvalid", boolType, safeHandleType) { HasThis = true };
        var startsWith = new MethodReference("StartsWith", boolType, stringType) { HasThis = true };
        startsWith.Parameters.Add(new ParameterDefinition(stringType));
        var substring = new MethodReference("Substring", stringType, stringType) { HasThis = true };
        substring.Parameters.Add(new ParameterDefinition(intType));

        var method = new MethodDefinition(
            helperName,
            MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.HideBySig,
            createFileRef.ReturnType);

        foreach (var p in createFileRef.Parameters)
            method.Parameters.Add(new ParameterDefinition(p.Name, p.Attributes, p.ParameterType));

        method.Body.InitLocals = true;
        method.Body.MaxStackSize = 8;
        var handleLocal = new VariableDefinition(createFileRef.ReturnType);
        var errorLocal = new VariableDefinition(intType);
        method.Body.Variables.Add(handleLocal);
        method.Body.Variables.Add(errorLocal);

        var il = method.Body.GetILProcessor();
        var returnHandle = il.Create(OpCodes.Ldloc, handleLocal);
        var retry = il.Create(OpCodes.Ldarg_0);
        var retryCall = il.Create(OpCodes.Call, createFileRef);

        for (int i = 0; i < createFileRef.Parameters.Count; i++)
            il.Append(il.Create(OpCodes.Ldarg, i));
        il.Append(il.Create(OpCodes.Call, createFileRef));
        il.Append(il.Create(OpCodes.Stloc, handleLocal));
        il.Append(il.Create(OpCodes.Call, getLastWin32Error));
        il.Append(il.Create(OpCodes.Stloc, errorLocal));
        il.Append(il.Create(OpCodes.Ldloc, handleLocal));
        il.Append(il.Create(OpCodes.Callvirt, getIsInvalid));
        il.Append(il.Create(OpCodes.Brfalse_S, returnHandle));
        il.Append(il.Create(OpCodes.Ldloc, errorLocal));
        il.Append(il.Create(OpCodes.Brtrue_S, returnHandle));
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldstr, "\\\\?\\"));
        il.Append(il.Create(OpCodes.Callvirt, startsWith));
        il.Append(il.Create(OpCodes.Brtrue_S, retry));
        il.Append(il.Create(OpCodes.Br_S, returnHandle));
        il.Append(retry);
        il.Append(il.Create(OpCodes.Ldc_I4_4));
        il.Append(il.Create(OpCodes.Callvirt, substring));
        for (int i = 1; i < createFileRef.Parameters.Count; i++)
            il.Append(il.Create(OpCodes.Ldarg, i));
        il.Append(retryCall);
        il.Append(il.Create(OpCodes.Stloc, handleLocal));
        il.Append(returnHandle);
        il.Append(il.Create(OpCodes.Ret));

        longFileType.Methods.Add(method);
        return method;
    }
}
