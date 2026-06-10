# Aircraft Factory — IP Management System

IP address management (IPAM) for **مصنع الطائرات / AOI**: discover which addresses are in use vs free
across the LAN, identify the device behind each IP (MAC, hostname, type, department, location),
detect IP conflicts, attribute per‑device internet usage, and watch it all update live.

Built on **ASP.NET Core 8 MVC**, with **SignalR** for real‑time updates and **EF Core + SQL Server**
for persistence.

---

## What it does (requirement checklist)

- **Used vs available IPs**, auto‑detected from the network — a live cell‑per‑address scope grid.
- **Device behind each IP**: MAC, hostname, type, department, switch/port location, history.
- **Cross‑subnet / VLAN MAC detection** via SNMP router ARP walk and/or Windows DHCP leases —
  see `docs/INTEGRATION-GUIDE.md`, Part 1.
- **Per‑device internet usage** via firewall/gateway syslog (or NetFlow/proxy) — Part 2 of the guide.
- **IP conflict detection** — any address claimed by more than one device is flagged, with both
  devices shown, and auto‑resolves when the clash clears.
- **Numeric IP sorting** — `192.168.1.9` sorts before `192.168.1.10` (a `bigint` sort key, not text).
- **Partial search** by IP fragment *or* MAC fragment.
- **Four notifications**: IP used, IP freed, conflict detected, device started using the internet —
  persisted and pushed live via SignalR (toasts + a notification drawer).
- **Real‑time dashboard** — stats, scope grid, and table all refresh on change with no page reload.

---

## Run it now (Demo mode — zero setup)

No database, no network access, nothing to configure. The app boots with an in‑memory store driven by
a self‑driving simulator that generates a realistic factory network through the **real** pipeline, so
every feature above is live and visible immediately.

```bash
cd src/IpManager.Web
dotnet run
```

Open the URL it prints (e.g. `http://localhost:5080`). In Visual Studio, just press **F5** on
`IpManager.Web`. `Network:Mode` is `Demo` by default in `appsettings.json`.

You'll see ~500 addresses across two subnets, devices coming and going, a couple of live conflicts,
and the four notifications firing as the simulation runs.

---

## Run it for real (Production — SQL Server + live network)

The production layer lives in `IpManager.Persistence.EfCore` (EF Core store + the real collectors).
It's kept as a separate project so the demo builds and runs anywhere; switching over is small.

1. **Reference the persistence project from the web app** (once):
   ```bash
   dotnet add src/IpManager.Web reference src/IpManager.Persistence.EfCore
   ```

2. **Wire it in `Program.cs`** — add these two lines *before* `AddIpManagerCore()`:
   ```csharp
   builder.Services.AddEfCoreStore(builder.Configuration);    // SQL Server store
   builder.Services.AddLiveCollectors(builder.Configuration); // ARP / SNMP / DHCP / syslog
   ```
   (Because Core registers the in‑memory store with `TryAdd`, the EF store registered here wins.)

3. **Set the connection string** in `appsettings.json` → `ConnectionStrings:Default`, then create the
   database:
   ```bash
   dotnet tool install --global dotnet-ef          # if needed
   dotnet ef migrations add InitialCreate -p src/IpManager.Persistence.EfCore -s src/IpManager.Web
   dotnet ef database update          -p src/IpManager.Persistence.EfCore -s src/IpManager.Web
   ```

4. **Switch to Live mode and enable the collectors** in `appsettings.json`:
   ```json
   "Network": {
     "Mode": "Live",
     "Subnets": [ "192.168.1.0/24", "192.168.5.0/24" ],
     "Snmp":   { "Enabled": true,  "RouterHosts": [ "192.168.1.1", "192.168.5.1" ], "Community": "AcfReadOnly" },
     "Dhcp":   { "Enabled": false, "ScriptPath": "tools/get-leases.ps1" },
     "Syslog": { "Enabled": true,  "UdpPort": 5514 }
   }
   ```
   Each collector is independent — enable only what your environment supports. Full per‑collector
   setup (SNMP OIDs, firewall syslog config, DHCP script) is in **`docs/INTEGRATION-GUIDE.md`**.

5. `dotnet run`. The collectors now feed the same pipeline the demo used, so the UI is identical —
   only the data is real.

> **Note on NuGet:** the persistence project restores `Microsoft.EntityFrameworkCore.SqlServer`,
> `...Design`, and `Lextm.SharpSnmpLib`. Build it on a machine with NuGet access (your Windows dev box).

---

## Project layout

```
AircraftFactory.IpManager/
├─ src/
│  ├─ IpManager.Core/                 # entities, DTOs, domain logic, interfaces, demo simulator
│  │  ├─ Storage/InMemoryNetworkStore.cs   # default store (the domain "brain")
│  │  └─ Demo/NetworkSimulator.cs          # self-driving sample network
│  ├─ IpManager.Web/                  # MVC + SignalR + the dashboard UI
│  │  ├─ Controllers/ Views/ wwwroot/      # avionics "glass cockpit" design system
│  │  └─ Workers/                          # Demo worker, and the Live scan/DHCP/internet workers
│  └─ IpManager.Persistence.EfCore/   # SQL Server store + real collectors (production drop-in)
│     ├─ Services/EfNetworkStore.cs        # same logic as in-memory, on EF Core
│     └─ Collectors/                       # PingSweep, ARP, SNMP (cross-subnet), DHCP, Syslog (internet)
└─ docs/INTEGRATION-GUIDE.md          # the two hard parts, step by step
```

### How the swap works

Everything above the data layer depends only on `INetworkStore`. The in‑memory store and the EF store
implement it identically, so the dashboard, search, conflict logic, and notifications don't know or
care which one is active — `Demo` uses the simulator + in‑memory store; `Live` uses real collectors +
SQL Server.

---

## Tech

ASP.NET Core 8 MVC · SignalR · EF Core 8 (SQL Server) · SharpSnmpLib (SNMP) · vanilla JS front end.
