using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Shunt.Core.Models;
using Shunt.Profiles.Models;

namespace Shunt.Profiles;

public class ProfileLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true
    };

    public static RewasdProfileDocument LoadFromString(string json)
    {
        return JsonSerializer.Deserialize<RewasdProfileDocument>(json, JsonOptions)
            ?? throw new InvalidOperationException("Failed to parse reWASD profile JSON.");
    }

    public static async Task<RewasdProfileDocument> LoadFromFileAsync(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Profile file not found: {filePath}");

        using var stream = File.OpenRead(filePath);
        return await JsonSerializer.DeserializeAsync<RewasdProfileDocument>(stream, JsonOptions)
            ?? throw new InvalidOperationException($"Empty or invalid profile document at: {filePath}");
    }

    public static string SerializeToString(RewasdProfileDocument doc)
    {
        return JsonSerializer.Serialize(doc, JsonOptions);
    }

    public static async Task SaveToFileAsync(string filePath, RewasdProfileDocument doc)
    {
        using var stream = File.Create(filePath);
        await JsonSerializer.SerializeAsync(stream, doc, JsonOptions);
    }

    public static VirtualGamepadType DetermineTargetType(RewasdProfileDocument doc)
    {
        var targetStr = doc.Devices.Virtual?.Gamepads?.Find(g => g.Id == 1)?.Type?.ToLowerInvariant();
        return targetStr switch
        {
            "ds4" or "dualshock" or "dualshock4" => VirtualGamepadType.DualShock4,
            "x360" or "xbox360" or "360" => VirtualGamepadType.Xbox360,
            "xone" or "xboxone" or "xbox_one_bt" => VirtualGamepadType.XboxOne,
            "ds3" or "dualshock3" => VirtualGamepadType.DualShock3,
            "nsw" or "switch" or "switchpro" or "nintendo_switch_pro" => VirtualGamepadType.SwitchPro,
            "kbm" or "keyboard" or "keyboard_and_mouse" => VirtualGamepadType.KeyboardAndMouse,
            _ => VirtualGamepadType.Xbox360
        };
    }
}
