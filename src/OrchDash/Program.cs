using OrchDash.App;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;

namespace OrchDash;

/// <summary>Entry point: <c>OrchDash [repo]</c>. See <see cref="AppRunner.Run"/> for the exit codes.</summary>
public static class Program
{
    public static int Main(string[] args) =>
        AppRunner.Run(args, Environment.CurrentDirectory, Console.Error, (root, onUpdate) => Terminal.Run(root, onUpdate));
}
