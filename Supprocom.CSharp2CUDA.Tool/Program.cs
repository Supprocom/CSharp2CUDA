namespace Supprocom.CSharp2CUDA.Tool;

internal static class Program
{
    public static Task<int> Main(string[] args) =>
        ToolApplication.RunAsync(args, Console.Out, Console.Error, CancellationToken.None);
}
