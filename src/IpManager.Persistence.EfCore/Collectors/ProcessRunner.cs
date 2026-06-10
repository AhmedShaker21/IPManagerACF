using System.Diagnostics;

namespace IpManager.Persistence.EfCore.Collectors;

/// <summary>Runs a child process and captures stdout. Used by ARP and DHCP collectors.</summary>
internal static class ProcessRunner
{
    public static async Task<string> RunAsync(string file, string args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(file, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var p = Process.Start(psi);
        if (p is null) return "";
        var stdout = await p.StandardOutput.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        return stdout;
    }
}
