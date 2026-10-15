<p align="center">
  <img src="assets/logo-transparent.png" alt="SHUNT - Open Buttons Router" width="620"/>
</p>

<p align="center">
  <strong>High-performance, open-source gamepad remapping, virtual controller emulation, and button routing engine.</strong>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white" alt=".NET 8.0" />
  <img src="https://img.shields.io/badge/Platform-Windows-0078D6?logo=windows&logoColor=white" alt="Platform Windows" />
  <img src="https://img.shields.io/badge/Architecture-x64-black" alt="Architecture x64" />
  <img src="https://img.shields.io/badge/License-MIT-green" alt="License MIT" />
</p>

---

## 🎮 Overview

<table>
  <tr>
    <td width="140" align="center" valign="middle">
      <img src="assets/icon.png" alt="App Icon" width="110"/>
    </td>
    <td>
      <strong>SHUNT (Open Buttons Router)</strong> is an extensible, low-latency input remapping and virtual device engine built for PC gaming and accessibility. Designed with full backward compatibility for <code>.rewasd</code> profiles, it gives you complete control over gamepads, keyboards, mice, and custom controllers.
    </td>
  </tr>
</table>

### ✨ Key Features

- **🎮 Virtual Controller Emulation**: Emulate virtual Xbox 360 and DualShock 4 controllers via virtual bus integration (ViGEm).
- **🔀 Dynamic Input Routing & Shifts**: Create multi-layer shift modifiers, toggle states, turbo buttons, and double-press actions.
- **🎯 Stick & Trigger Tuning**: Configure deadzones, linear/exponential response curves, and custom sensitivity zones.
- **🌀 Gyroscope & Motion Aiming**: Route motion sensors from DualSense, DualShock 4, and Switch Pro to analog sticks or high-precision virtual mouse.
- **🕹️ Radial Menus & Key Combos**: On-screen overlay radial menus and macro sequence playback for rapid in-game actions.
- **📂 Profile Compatibility**: Native support for loading and exporting `.rewasd` configuration files.
- **🎨 Modern Dark UI**: Custom-built hardware visualizer, live input inspector, and responsive gamepad view.

---

## 🏗️ Architecture

The solution is divided into modular, decoupled projects:

```text
Shunt.sln
├── src/
│   ├── Shunt.Core/        # Core models, interfaces, HID enumerations, and contract types
│   ├── Shunt.Profiles/    # Profile serializer, validator, and .rewasd schema engine
│   ├── Shunt.Engine/      # Input interception pipeline, device polling, and routing core
│   ├── Shunt.Emulation/   # ViGEm / virtual controller client and bus dispatcher
│   ├── Shunt.UI/          # Modern WPF desktop interface and stage controls
│   └── Shunt.CLI/         # Command-line driver and service installer
├── tests/
│   └── Shunt.Tests/       # Unit tests, profile benchmarks, and compiler validation
└── assets/                 # Vector SVG logos, master icons, and Windows .ico assets
```

---

## 🚀 Getting Started

### Prerequisites

- [Windows 10 / 11 (64-bit)](https://www.microsoft.com/windows)
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [ViGEmBus Driver](https://github.com/nefarius/ViGEmBus) (for virtual Xbox / PlayStation controller emulation)

### Build and Run

1. **Clone the repository:**
   ```bash
   git clone https://github.com/hcn1z1/shunt.git
   cd shunt
   ```

2. **Build the solution:**
   ```bash
   dotnet build Shunt.sln -c Release
   ```

3. **Run the application:**
   ```bash
   dotnet run --project src/Shunt.UI
   ```

4. **Run unit tests:**
   ```bash
   dotnet test tests/Shunt.Tests/Shunt.Tests.csproj
   ```

---

## 🎨 Brand Assets

All branding files are stored in the [`assets/`](assets/) directory:

| Asset | Format | Preview / Link | Description |
| :--- | :--- | :--- | :--- |
| **Full Logo** | PNG | [assets/logo.png](assets/logo.png) | 1024×765 high-res full logo with background gradient |
| **Logo (Transparent)** | PNG | [assets/logo-transparent.png](assets/logo-transparent.png) | 1024×765 transparent full logo |
| **Logo (Vector)** | SVG | [assets/logo.svg](assets/logo.svg) | Scalable vector graphic of the full logo |
| **App Icon** | PNG | [assets/icon.png](assets/icon.png) | 512×512 squircle controller icon |
| **App Icon (Transparent)**| PNG | [assets/icon-transparent.png](assets/icon-transparent.png) | 512×512 controller graphic without background |
| **App Icon (Vector)** | SVG | [assets/icon.svg](assets/icon.svg) | 512×512 scalable vector app icon |
| **Windows Icon** | ICO | [assets/icon.ico](assets/icon.ico) | Multi-resolution Windows app icon (16px to 256px) |

---

## 📄 License

This project is licensed under the [MIT License](LICENSE).
