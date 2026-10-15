using System.Diagnostics;

namespace Shunt.CLI;

internal static class WindowsServiceInstaller
{
    private const string ServiceName = "ShuntEngine";

    public static async Task<int> RunAsync(string[] args)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("Windows Service installation is available only on Windows.");
            return 1;
        }

        if (args.Length == 2 && args[0].Equals("install-service", StringComparison.OrdinalIgnoreCase))
        {
            string executable = Path.GetFullPath(args[1]);
            if (!File.Exists(executable))
            {
                Console.Error.WriteLine($"Service executable was not found: {executable}");
                return 1;
            }
            return await RunScAsync("create", ServiceName, "binPath=", $"\"{executable}\" service", "start=", "auto", "DisplayName=", "Shunt Engine");
        }

        if (args.Length == 1 && args[0].Equals("uninstall-service", StringComparison.OrdinalIgnoreCase))
        {
            _ = await RunScAsync("stop", ServiceName);
            return await RunScAsync("delete", ServiceName);
        }

        return -1;
    }

    private static async Task<int> RunScAsync(params string[] arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("sc.exe")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        foreach (string argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        process.Start();
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        string output = await stdout;
        string error = await stderr;
        if (!string.IsNullOrWhiteSpace(output)) Console.WriteLine(output.Trim());
        if (!string.IsNullOrWhiteSpace(error)) Console.Error.WriteLine(error.Trim());
        return process.ExitCode;
    }
}
