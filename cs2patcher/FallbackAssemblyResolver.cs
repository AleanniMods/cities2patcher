using Mono.Cecil;
using System.IO;

namespace Cs2MacPatcher;

// Resolves assemblies from managedDir; silently returns an empty module for unknown ones
// so Mono.Cecil doesn't crash reading custom attributes from Unity assemblies we don't need.
class FallbackAssemblyResolver : DefaultAssemblyResolver
{
    public FallbackAssemblyResolver(string searchDir)
    {
        AddSearchDirectory(searchDir);
    }

    public override AssemblyDefinition Resolve(AssemblyNameReference name, ReaderParameters parameters)
    {
        try { return base.Resolve(name, parameters); }
        catch (AssemblyResolutionException)
        {
            // Return an empty in-memory assembly as a stub for unknown references
            var stub = AssemblyDefinition.CreateAssembly(
                new AssemblyNameDefinition(name.Name, name.Version),
                name.Name,
                ModuleKind.Dll);
            return stub;
        }
    }
}
