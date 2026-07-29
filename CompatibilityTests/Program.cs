using System.Reflection;
using System.Runtime.Loader;
using HarmonyLib;

if (args.Length != 2)
{
    throw new ArgumentException(
        "Usage: TDBank.BinaryCompatibilitySmokeTests <candidate-mods-root> <game-data-root>");
}

string candidateRoot = Path.GetFullPath(args[0]);
string gameDataRoot = Path.GetFullPath(args[1]);
string tdLibPath = Path.Combine(candidateRoot, "TDLib", "TDLib.dll");
string tdBankPath = Path.Combine(candidateRoot, "TDBank", "TDBank.dll");
if (!File.Exists(tdLibPath) || !File.Exists(tdBankPath))
{
    throw new FileNotFoundException(
        $"Candidate TDLib or TD Bank DLL is missing under {candidateRoot}.");
}

AssemblyLoadContext.Default.Resolving += ResolveDependency;

Assembly tdLib = Assembly.LoadFrom(tdLibPath);
Assembly tdBank = Assembly.LoadFrom(tdBankPath);
_ = tdLib.GetTypes();
_ = tdBank.GetTypes();

AssemblyName[] references = tdBank.GetReferencedAssemblies();
if (!references.Any(reference => reference.Name == "TDLib")
    || references.Any(reference => reference.Name == "BaseLib"))
{
    throw new InvalidOperationException(
        "The candidate dependency boundary is invalid.");
}

VerifyPatchMatrix(tdLib, "TDBank.BinaryCompatibility.TDLib", 5);
VerifyPatchMatrix(tdBank, "TDBank.BinaryCompatibility.Bank", 30);

Console.WriteLine(
    $"Binary compatibility smoke test passed against {typeof(MegaCrit.Sts2.Core.Modding.Mod).Assembly.GetName().Version}.");
return;

Assembly? ResolveDependency(
    AssemblyLoadContext context,
    AssemblyName name)
{
    foreach (string directory in new[]
             {
                 Path.GetDirectoryName(tdBankPath)!,
                 Path.GetDirectoryName(tdLibPath)!,
                 gameDataRoot,
             })
    {
        string path = Path.Combine(directory, $"{name.Name}.dll");
        if (File.Exists(path))
        {
            return context.LoadFromAssemblyPath(path);
        }
    }

    return null;
}

static void VerifyPatchMatrix(
    Assembly assembly,
    string owner,
    int minimumTargets)
{
    var harmony = new Harmony(owner);
    try
    {
        harmony.PatchAll(assembly);
        int targets = Harmony.GetAllPatchedMethods().Count(method =>
        {
            Patches? patches = Harmony.GetPatchInfo(method);
            return patches is not null
                && (patches.Prefixes.Any(patch => patch.owner == owner)
                    || patches.Postfixes.Any(patch => patch.owner == owner)
                    || patches.Transpilers.Any(patch => patch.owner == owner)
                    || patches.Finalizers.Any(patch => patch.owner == owner));
        });
        if (targets < minimumTargets)
        {
            throw new InvalidOperationException(
                $"{assembly.GetName().Name} patched only {targets} targets.");
        }
    }
    finally
    {
        harmony.UnpatchAll(owner);
    }
}
