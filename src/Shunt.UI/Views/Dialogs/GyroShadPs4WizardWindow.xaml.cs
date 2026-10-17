using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Shunt.Engine.Hardware;

namespace Shunt.UI.Views.Dialogs;

public partial class GyroShadPs4WizardWindow : Window
{
    private static readonly HttpClient EngineClient = new() { BaseAddress = new Uri("http://127.0.0.1:35474/"), Timeout = TimeSpan.FromSeconds(5) };
    private readonly GyroShadPs4TestRunner _runner = new();
    private HardwareImuDevice[] _devices = [];
    private CancellationTokenSource? _testCancellation;

    public GyroShadPs4WizardWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closed += (_, _) => _testCancellation?.Cancel();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _devices = HardwareImuDiscovery.Enumerate().ToArray();
            DeviceCombo.ItemsSource = _devices;
            if (_devices.Length > 0) DeviceCombo.SelectedIndex = 0;
            else StatusText.Text = "No supported HID motion sensor was found. Connect a compatible controller and scan again.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Sensor discovery failed: {ex.Message}";
        }
    }

    private async void TestButton_Click(object sender, RoutedEventArgs e)
    {
        if (DeviceCombo.SelectedItem is not HardwareImuDevice device) return;
        TestButton.IsEnabled = false;
        EnableShuntButton.IsEnabled = false;
        DeviceCombo.IsEnabled = false;
        _testCancellation?.Dispose();
        _testCancellation = new CancellationTokenSource();
        ProgressBar.Value = 0;
        StatusText.Text = "Keep the controller still briefly, then tilt it in all directions.";
        StageText.Text = "Listening for motion…";
        RateText.Text = "Polling rate: —";
        MotionText.Text = "Angular range: —";
        GravityText.Text = "Gravity: —";
        NoiseText.Text = "Noise floor: —";

        await using var poller = device.CreatePoller();
        var progress = new Progress<GyroShadPs4TestProgress>(sample =>
        {
            ProgressBar.Value = sample.FractionComplete * 100;
            StageText.Text = sample.IsBaselineStage ? "Hold still to measure sensor noise…" : "Tilt and rotate the controller gently…";
        });

        try
        {
            var result = await _runner.RunAsync(poller, progress, _testCancellation.Token);
            RateText.Text = $"Polling rate: {result.PollingRateHz:F0} Hz";
            MotionText.Text = $"Angular range: {result.AngularVelocityDeltaDegreesPerSecond:F1} °/s";
            GravityText.Text = $"Gravity: {result.GravityMagnitudeG:F2} g";
            NoiseText.Text = double.IsFinite(result.NoiseFloorDegreesPerSecond)
                ? $"Noise floor: {result.NoiseFloorDegreesPerSecond:F2} °/s"
                : "Noise floor: unavailable";
            StatusText.Text = result.Message;
            StatusText.Foreground = result.Passed ? System.Windows.Media.Brushes.LightGreen : System.Windows.Media.Brushes.Orange;
            StageText.Text = result.Passed ? "Gyroscope verified" : "Test did not pass";
            EnableShuntButton.IsEnabled = result.Passed;
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "The motion test was cancelled.";
            StageText.Text = "Cancelled";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"The motion test could not run: {ex.Message}";
            StageText.Text = "Test failed";
        }
        finally
        {
            _testCancellation?.Dispose();
            _testCancellation = null;
            TestButton.IsEnabled = true;
            DeviceCombo.IsEnabled = true;
        }
    }

    private void DeviceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        TestButton.IsEnabled = DeviceCombo.SelectedItem is HardwareImuDevice;
        StatusText.Foreground = System.Windows.Media.Brushes.White;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private async void EnableShuntButton_Click(object sender, RoutedEventArgs e)
    {
        if (DeviceCombo.SelectedItem is not HardwareImuDevice device) return;
        EnableShuntButton.IsEnabled = false;
        try
        {
            var activationMode = ActivationModeCombo.SelectedIndex == 1 ? "hold" : "toggle";
            using var response = await EngineClient.PostAsJsonAsync("api/shunt/enable", new { device.DevicePath, ActivationMode = activationMode });
            if (!response.IsSuccessStatusCode)
            {
                var detail = await response.Content.ReadAsStringAsync();
                StatusText.Text = $"Shunt Mode could not start: {detail}";
                EnableShuntButton.IsEnabled = true;
                return;
            }

            StatusText.Text = "Shunt Mode is active. L3 + R3 controls gyro streaming.";
            StageText.Text = "Shunt Mode active";
            Close();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not connect to the Shunt Engine: {ex.Message}";
            EnableShuntButton.IsEnabled = true;
        }
    }
}
