using System;
using System.IO;
using System.Threading.Tasks;
using Shunt.Profiles;

namespace Shunt.Compiler;

internal class Program
{
    private static async Task<int> Main(string[] args)
    {
        string? inputPath = null;
        string? outputPath = null;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].Equals("-i", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                inputPath = args[++i];
            }
            else if (args[i].Equals("-o", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                outputPath = args[++i];
            }
        }

        if (string.IsNullOrEmpty(inputPath))
        {
            Console.WriteLine("Shunt AOT Macro & Profile Compiler (rcompiler replacement)");
            Console.WriteLine("Usage: Shunt.Compiler -i <profile.rewasd> -o <output.bin>");
            return 1;
        }

        if (!File.Exists(inputPath))
        {
            Console.Error.WriteLine($"Error: Input file '{inputPath}' does not exist.");
            return 2;
        }

        outputPath ??= Path.ChangeExtension(inputPath, ".bin");

        try
        {
            Console.WriteLine($"[Compiler] Loading profile '{inputPath}'...");
            var profile = await ProfileLoader.LoadFromFileAsync(inputPath);
            Console.WriteLine($"[Compiler] Compiling profile '{profile.Config?.AppName ?? "Unknown"}' to bytecode...");
            var bytes = BytecodeWriter.CompileToBytes(profile);

            Console.WriteLine($"[Compiler] Writing {bytes.Length} bytes of bytecode to '{outputPath}'...");
            await File.WriteAllBytesAsync(outputPath, bytes);

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[Compiler] Compilation succeeded! Target output: {outputPath}");
            Console.ResetColor();
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Compiler] Compilation failed: {ex.Message}");
            return 3;
        }
    }
}
