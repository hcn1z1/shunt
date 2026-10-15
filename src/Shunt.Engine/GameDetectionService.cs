using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Shunt.Engine;

/// <summary>
/// Monitors the focused foreground application and triggers profile auto-switching.
/// </summary>
public class GameDetectionService : IDisposable
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    private CancellationTokenSource? _cts;
    private Task? _monitorTask;
    private string _lastProcessName = string.Empty;

    public event Action<string, string>? ForegroundAppChanged; // (processName, windowTitle)

    public bool IsActive => _monitorTask != null && !_monitorTask.IsCompleted;

    public void Start(int pollIntervalMs = 500)
    {
        if (IsActive) return;

        _cts = new CancellationTokenSource();
        _monitorTask = Task.Run(async () =>
        {
            var token = _cts.Token;
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var hWnd = GetForegroundWindow();
                    if (hWnd != IntPtr.Zero)
                    {
                        GetWindowThreadProcessId(hWnd, out uint pid);
                        if (pid > 0)
                        {
                            try
                            {
                                using var proc = Process.GetProcessById((int)pid);
                                string procName = proc.ProcessName;

                                if (!string.Equals(procName, _lastProcessName, StringComparison.OrdinalIgnoreCase))
                                {
                                    _lastProcessName = procName;

                                    var sb = new StringBuilder(256);
                                    GetWindowText(hWnd, sb, 256);
                                    string title = sb.ToString();

                                    ForegroundAppChanged?.Invoke(procName, title);
                                }
                            }
                            catch
                            {
                                // Access denied on protected processes
                            }
                        }
                    }

                    await Task.Delay(pollIntervalMs, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    // Ignore transient window transition errors
                }
            }
        });
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
        _monitorTask = null;
        _lastProcessName = string.Empty;
    }

    public void Dispose()
    {
        Stop();
    }
}
