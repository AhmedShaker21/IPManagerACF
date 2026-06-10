# Integration Guide — the two hard parts

This system can show every IP, who holds it, conflicts, and per‑device internet usage. Two pieces
of that are genuinely hard because of how networks work, not because of code. This guide explains
*why* they are hard and gives you a concrete, step‑by‑step path for each, wired to the collectors
that ship in `IpManager.Persistence.EfCore`.

---

## Part 1 — Seeing MAC addresses across subnets / VLANs

### Why it's hard

A MAC address is a **layer‑2** identifier. It only exists inside a single broadcast domain
(one subnet / VLAN). When a packet crosses from `192.168.1.0/24` to `192.168.5.0/24`, the router
rewrites the layer‑2 header: the source MAC becomes the router's MAC. So a server sitting in
subnet 1 that runs ARP or a ping sweep can learn IP↔MAC for **its own subnet only**. For any other
subnet it can learn that an IP responds to ping, but never the device's real MAC.

There is exactly one device that knows the MACs of every subnet: the **router / layer‑3 switch**
that connects them. It keeps an ARP table per interface. The whole job is to read that table.

You have two ways to read it. Use either or both; the app merges them.

### Option A — SNMP walk of the router ARP table (works with any L3 device)

The router exposes its ARP cache through the SNMP MIB‑II table `ipNetToMediaTable`. The column we
need is `ipNetToMediaPhysAddress`:

```
OID 1.3.6.1.2.1.4.22.1.2
```

Each row's OID suffix is `<ifIndex>.<a>.<b>.<c>.<d>` (the last four numbers are the IPv4 address)
and the row's value is the 6‑byte MAC. Walking that table on each gateway yields IP→MAC for every
directly connected subnet. This is implemented in `Collectors/SnmpArpReader.cs`.

**Steps**

1. **Enable SNMP read‑only on each router / L3 switch**, scoped to the collector's IP.

   *Cisco IOS (SNMP v2c, read‑only, ACL‑restricted):*
   ```
   ip access-list standard SNMP-RO
    permit host <collector-ip>
   snmp-server community AcfReadOnly RO SNMP-RO
   ```

   *Prefer SNMP v3* (no clear‑text community) in production:
   ```
   snmp-server group ACF v3 priv
   snmp-server user acfmon ACF v3 auth sha <authpass> priv aes 128 <privpass>
   ```
   (If you use v3, switch `VersionCode.V2` to `VersionCode.V3` in `SnmpArpReader` and supply the
   user/auth/priv — SharpSnmpLib supports it; the v2c path is included for the common case.)

2. **Open UDP 161** from the collector to each gateway.

3. **Configure** `appsettings.json`:
   ```json
   "Snmp": {
     "Enabled": true,
     "RouterHosts": [ "192.168.1.1", "192.168.5.1" ],
     "Community": "AcfReadOnly",
     "Port": 161,
     "TimeoutMs": 3000
   }
   ```

4. **Verify from the collector** before trusting the app:
   ```
   snmpwalk -v2c -c AcfReadOnly 192.168.1.1 1.3.6.1.2.1.4.22.1.2
   ```
   You should see one line per known host, ending in the IP, with a hex MAC value. If this is empty,
   the app will be empty too — fix SNMP first.

5. **Run the app in Live mode** (see README). `NetworkScanWorker` calls `SnmpArpReader` every scan
   cycle, tags each result `BindingSource.Snmp`, and feeds it through the same pipeline as local ARP.

**Caveats.** The router only has an ARP entry for a host it has talked to recently; idle hosts age
out. Run the scan on the router's ARP timeout cadence (often ~4 hours, sometimes shorter). To force
freshness you can ping‑sweep each subnet first (the app's `PingSweepScanner` does this for subnets
the collector can route to), which makes hosts reappear in the router ARP table.

### Option B — Read the DHCP server (richest data, no SNMP)

If your subnets are DHCP‑served by Windows Server, the DHCP server already knows IP + MAC + hostname
+ lease expiry for **every scope it serves**, including subnets the collector isn't attached to.
This is implemented in `Collectors/WindowsDhcpLeaseReader.cs` + `tools/get-leases.ps1`.

**Steps**

1. Put `tools/get-leases.ps1` on the DHCP server (or run it remotely with `-ComputerName` and RSAT
   `DhcpServer` module installed on the collector).
2. Configure:
   ```json
   "Dhcp": {
     "Enabled": true,
     "ScriptPath": "tools/get-leases.ps1",
     "ServerHost": "DC01"
   }
   ```
3. The script calls `Get-DhcpServerv4Scope` + `Get-DhcpServerv4Lease` and emits JSON; the reader maps
   it to bindings tagged `BindingSource.Dhcp`. `DhcpSyncWorker` polls it every
   `DhcpIntervalSeconds`.

**Statically addressed devices** (printers, servers) won't appear in DHCP — cover those with SNMP
(Option A) or a `BindingSource.Static`/`Manual` seed.

### Which to use

| Situation | Use |
|---|---|
| Mixed vendors, static + dynamic hosts | SNMP (A) |
| Windows‑DHCP shop, want hostnames + lease times | DHCP (B) |
| Want the most complete picture | Both — the store merges by IP and MAC |

---

## Part 2 — Per‑device internet usage

### Why it's hard

A device's internet traffic path is **device → gateway → internet**. It never passes through this
application's server, so the app cannot observe usage directly — there is no packet to see. The only
component in the path that can report usage is the box doing the forwarding/NAT: the
**firewall / gateway / proxy**. The app has to be *told* by that box.

Every such box can emit a per‑session record (syslog, NetFlow/IPFIX, or proxy log) containing at
minimum: source IP, destination IP, and byte counts. The strategy is:

1. Receive those session records.
2. Keep only sessions whose **destination is outside** your internal ranges → that's internet usage.
3. Emit an event keyed by the **internal source IP**.
4. Let the store attribute it to **whichever device held that IP at that moment**
   (IP → active binding at the session timestamp → device). This is why short DHCP leases still
   resolve to the right machine — attribution is time‑aware, not just "current owner".

Steps 1–3 are in `Collectors/SyslogInternetActivityReader.cs`; step 4 is `ApplyInternetEvent` in the
store.

### Option A — Firewall / gateway syslog (most common)

The reader listens on UDP and parses `src`, `dst`, and byte fields out of each line. It's
vendor‑tolerant (field aliases for FortiGate / pfSense / Palo Alto are built in).

**Steps**

1. **Point your firewall's traffic log at the collector** over syslog UDP.

   *FortiGate:*
   ```
   config log syslogd setting
     set status enable
     set server <collector-ip>
     set port 5514
   end
   ```
   *pfSense:* Status → System Logs → Settings → Remote Logging → enable, set the collector
   `ip:5514`, and tick **Firewall events**.

2. **Make sure the log carries bytes.** FortiGate `sentbyte`/`rcvdbyte` are on by default; on
   pfSense enable per‑rule logging. Records without byte fields still attribute the *session*
   (bytes show as 0).

3. **Configure**:
   ```json
   "Syslog": {
     "Enabled": true,
     "UdpPort": 5514,
     "InternalRanges": [ "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16" ]
   }
   ```
   `InternalRanges` is the definition of "not internet" — anything destined inside these is ignored.

4. **Run in Live mode.** `InternetActivityWorker` consumes the reader's stream and calls
   `ApplyInternetEventAsync`, which raises **InternetAccessStarted** the first time a device goes
   out, and records every session under the device's "Internet activity" tab.

5. **Tune the parser if needed.** If your firewall uses different field names, adjust the four
   regexes at the top of `SyslogInternetActivityReader` (`SrcIp`, `DstIp`, `SentB`, `RcvdB`). They are
   isolated for exactly this reason.

### Option B — NetFlow / IPFIX (higher fidelity, more setup)

NetFlow gives clean, structured flow records (no text parsing). Export flows from the router/firewall
to the collector, decode them, and emit the same `InternetEvent`. To use it, implement a second
`IInternetActivityReader` that decodes NetFlow v9/IPFIX (e.g. with a flow‑collector library) and
register it instead of the syslog reader — the rest of the pipeline is unchanged because everything
downstream depends only on the `IInternetActivityReader` interface and the `InternetEvent` shape.

### Option C — Proxy / DNS logs (usage + the actual sites)

If clients go through a forward proxy (Squid) or a filtering DNS resolver, its access log already has
internal IP + destination host + bytes. Tail that log, emit `InternetEvent` per line. Same interface,
same attribution.

### The attribution chain (important)

```
InternetEvent.InternalIp ──► IpAddress row
                              └─► IpBinding active at InternetEvent.At  (LeaseStart ≤ At ≤ LeaseEnd)
                                   └─► Device                            ◄── usage is recorded here
```

Because attribution uses the binding that was active **at the event's timestamp**, a session is
credited to the device that actually held the IP then — even if DHCP has since handed that IP to a
different machine.

---

## Putting it together

In `Program.cs`, the switch from demo to a fully live, persisted system is three lines (and a project
reference) — see the README "Production" section. Once live:

- **Same‑subnet** hosts → `PingSweepScanner` + `ArpTableReader`
- **Other subnets / VLANs** → `SnmpArpReader` (and/or `WindowsDhcpLeaseReader`)
- **Internet usage** → `SyslogInternetActivityReader` (or a NetFlow/proxy reader you add)

All of them feed the **same** store methods the demo already exercises, so the dashboard, search,
conflict detection, and the four notifications behave identically — only the data source changes.

> **Authorization:** scanning, SNMP polling, and log collection touch production infrastructure.
> Get sign‑off from network operations before pointing this at the AOI / مصنع الطائرات network, and
> restrict SNMP and syslog to the collector's address.
