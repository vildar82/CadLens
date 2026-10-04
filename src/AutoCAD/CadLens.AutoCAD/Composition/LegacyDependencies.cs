#if NETFRAMEWORK
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace CadLens.AutoCAD;

internal static class LegacyDependencies
{
    private static readonly string Directory = Path.GetDirectoryName(typeof(LegacyDependencies).Assembly.Location)!;

    [ModuleInitializer]
    internal static void Register() => AppDomain.CurrentDomain.AssemblyResolve += Resolve;

    private static Assembly? Resolve(object? sender, ResolveEventArgs args)
    {
        var requested = new AssemblyName(args.Name);

        if (requested.Name is not (
                "System.Memory" or
                "System.Buffers" or
                "System.Runtime.CompilerServices.Unsafe" or
                "System.Numerics.Vectors"))
            return null;

        if (args.RequestingAssembly is {} requester
                ? !IsPayloadAssembly(requester)
                : !HasPayloadCaller())
            return null;

        var path = Path.Combine(Directory, requested.Name + ".dll");

        if (!File.Exists(path))
            return null;

        var candidate = AssemblyName.GetAssemblyName(path);

        if (!string.Equals(candidate.Name, requested.Name, StringComparison.Ordinal) ||
            !string.Equals(candidate.CultureName, requested.CultureName, StringComparison.OrdinalIgnoreCase) ||
            candidate.Version is null || requested.Version is null || candidate.Version < requested.Version ||
            candidate.GetPublicKeyToken() is not {Length: > 0} token ||
            !token.SequenceEqual(requested.GetPublicKeyToken() ?? []))
            return null;

        return Assembly.LoadFrom(path);
    }

    private static bool HasPayloadCaller() => new StackTrace().GetFrames()?.Any(frame =>
        frame.GetMethod()?.DeclaringType is {} type &&
        type != typeof(LegacyDependencies) &&
        IsPayloadAssembly(type.Assembly)) == true;

    private static bool IsPayloadAssembly(Assembly assembly) => !assembly.IsDynamic && string.Equals(
        Path.GetDirectoryName(assembly.Location),
        Directory,
        StringComparison.OrdinalIgnoreCase);
}
#endif