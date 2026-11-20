# ReentrySync — Smart Copilot, Full-Flight GNC Autopilot & 2-3 Player Crew Co-op

A BepInEx 5 mod and locally hosted Mission Control web dashboard (`http://localhost:8080/`) for **Reentry - An Orbital Simulator**.

## Features

- **MET-Aware Autonomous Mission Director & Smart Checklist Copilot**
  - Reads live Mission Elapsed Time (MET), T-Minus countdown, Sphere of Influence (`EARTH` / `MOON`), and flight phase to automatically recommend or execute the right Apollo checklists (including scheduled battery charges, waste water drains, and cryo purges).
  - State-aware copilot: verifies switches, circuit breakers, and knobs before actuating, automatically opens protective switch covers, uses crisp DSKY keystroke timing, and waits for AGC program transitions (`P01 -> P02`, etc.).
- **Full-Flight GNC Autopilot & Platform Alignment**
  - 1-click **IMU / GDC / REFSMMAT Alignment** for CSM & LM.
  - Smooth attitude hold (`KILL ROT`, `PROGRADE`, `RETROGRADE`, `NORMAL`, `ANTI-NORMAL`, `RADIAL IN`, `RADIAL OUT`).
  - **`AUTO-DOCK (CSM / LM)`** — aligns docking ports, nulls lateral corridor drift, and closes smoothly to hard capture.
  - **`AUTO-LAND (PDI -> TOUCHDOWN)`** — braking burn, powered approach to your selected lunar landing site, auto gear deployment, hover nulling, and soft touchdown.
  - **`AUTO-ASCENT (LIFTOFF -> ORBIT)`** — descent stage separation, APS ignition, gravity turn to target lunar orbit, and circularization planning.
  - **`AUTO-BURN`** — slews to burn attitude, waits for TIG, fires ullage, ignites SPS/DPS/RCS, integrates dV in real time, and cuts off on target.
- **Custom Lunar/Earth Orbit Burn Planner & Interactive Moon Landing Map**
  - Plan custom target orbits (`Ap x Pe` in NM), 1-click `LOI / Circularize`, `60x60 NM`, `DOI 60x8.5 NM`, or raw course-correction maneuvers directly integrated with Reentry's `ManeuverPlanner` and Burn PADs.
  - Click anywhere on the interactive Moon Map (or select Apollo 11-17, Tycho, Copernicus, or orbital ground-track offset) to lock a custom Lunar Landing Site and align the LM IMU & LPD.
- **True 2-3 Player Crew Co-op Multiplayer (CDR / CMP / LMP / FLIGHT)**
  - Real-time bi-directional sync for **all CSM & LM switches, circuit breakers, rotary knobs, and pushbuttons (including DSKY)**.
  - Full join snapshot syncing every cockpit control plus **entire AGC & LGC computer memory banks**.
  - 10 Hz authoritative orbit, attitude, engine, MET, burn plan, and landing site synchronization with transferable **Flight Control Authority** (`TAKE FLIGHT CONTROL`).
  - Supports **Direct P2P** (LAN / Tailscale / ZeroTier / Port-Forward TCP 7777) and a standalone **Dedicated Relay Server** (`ReentrySyncServer`) with **Room Codes** (no port forwarding on host).
- **Locally Hosted Mission Control Web Dashboard**
  - Open `http://localhost:8080/` in any browser (or second monitor / tablet) for live telemetry, interactive Apollo DSKY, searchable 300+ switchboard, checklist director, autopilot, burn planner, lunar landing map, and crew intercom.

---

## 1-Click Automated Install (BepInEx + ReentrySync)

Open **Windows PowerShell** and run:
```powershell
irm https://raw.githubusercontent.com/OttoApocalypse69/ReentrySync/main/scripts/install.ps1 | iex
```

Or download **`install.bat`** from the [Latest Release](https://github.com/OttoApocalypse69/ReentrySync/releases/latest) and double-click it.

The installer automatically:
1. Auto-detects your Reentry Steam or custom installation folder.
2. Downloads and installs **BepInEx 5.4.23.6 (win_x64)** if not already installed.
3. Downloads the latest **ReentrySync** release (`ReentrySync.dll` + `Fleck.dll`) into `BepInEx\plugins\`.

### Uninstall
```powershell
irm https://raw.githubusercontent.com/OttoApocalypse69/ReentrySync/main/scripts/install.ps1 | Out-File "$env:TEMP\reentry_uninstall.ps1"; & "$env:TEMP\reentry_uninstall.ps1" -Uninstall
```

---

## Hosting & Joining Crew Co-op Multiplayer

### Option A — Direct P2P (LAN / Tailscale / ZeroTier / Port Forward TCP 7777)
1. Everyone installs ReentrySync and loads the same Apollo mission.
2. Open `http://localhost:8080/` -> **`CREW CO-OP`** tab.
3. **Host (CDR):** Enter callsign, select `CDR`, click **`HOST MISSION (P2P)`**.
4. **Crewmates (CMP / LMP):** Enter Host IP + Port `7777`, select role, click **`JOIN CREW (P2P)`**.

### Option B — Dedicated Relay Server (Room Codes, No Port Forwarding Needed on Host)
1. Download `ReentrySyncServer` from [Releases](https://github.com/OttoApocalypse69/ReentrySync/releases/latest) and run it on any Windows or Linux server:
   ```bash
   ./ReentrySyncServer 7777
   ```
2. In **`CREW CO-OP`**: Host enters the Relay IP + Room Code (e.g. `APOLLO11`) and clicks **`HOST VIA RELAY`**.
3. Crewmates enter the same Relay IP + Room Code and click **`JOIN VIA RELAY`**.

---

## Building from Source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
# Build the BepInEx mod (uses stripped metadata-only stubs in lib/ref/)
dotnet build src/ReentrySync/ReentrySync.csproj -c Release

# Build the Dedicated Relay Server
dotnet build src/ReentrySyncServer/ReentrySyncServer.csproj -c Release
```

To regenerate stripped reference stubs after a Reentry game update:
```powershell
.\scripts\generate-refs.ps1 -GameDir "E:\Games\Reentry - A Space Flight Simulator"
```
