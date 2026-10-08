# Radio Wars

An overhaul mod for **Nuclear Option**.

Radio Wars replaces distance-based radar abstractions with continuous electromagnetic propagation, dynamic radar cross sections, Pulse-Doppler ground clutter filtering, active noise jamming, tiered radar warning receivers, silent active-radar missile guidance, and cooperative tactical datalink networking.

---

## Controls

| Key | System | Description |
|---|---|---|
| `F10` | **Master A/B Toggle** | Hot-swaps between Radio Wars physics and 100% vanilla engine routines. |
| `F9` | **Telemetry HUD** | Displays real-time physical telemetry (\(P_r, P_n, P_c, P_j\), SINR, dynamic RCS, radial velocity, \(R_{\text{burn}}\)). |
| `F8` | **Datalink Visualizer** | Renders 3D sensor mesh lines, donor track vectors, HUD contact reticles, and network telemetry. |
| `F11` | **Raycast Gizmos** | 3D visualizers for radar line-of-sight rays, Doppler projections, and notch crossbars. |

---

## Build from Source

Target framework: .NET Framework 4.8.

```powershell
MSBuild.exe RadioWars.csproj /p:Configuration=Release
```
