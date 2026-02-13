using System.Diagnostics;
using System.Net.NetworkInformation;
using Serilog;

namespace Cliparino.Core.Utilities;

public static class PortUtilities {
    public static bool IsPortAvailable(int port) {
        try {
            var ipGlobalProperties = IPGlobalProperties.GetIPGlobalProperties();
            var tcpListeners = ipGlobalProperties.GetActiveTcpListeners();

            return tcpListeners.All(x => x.Port != port);
        } catch (Exception ex) {
            Log.Warning(ex, "Error checking if port {Port} is available", port);

            return false;
        }
    }

    public static string? GetProcessUsingPort(int port) {
        try {
            var ipGlobalProperties = IPGlobalProperties.GetIPGlobalProperties();
            var tcpConnections = ipGlobalProperties.GetActiveTcpConnections();
            var tcpListeners = ipGlobalProperties.GetActiveTcpListeners();

            if (tcpListeners.All(x => x.Port != port)) return null;

            var processes = Process.GetProcesses();

            foreach (var process in processes)
                try {
                    var processConnections = tcpConnections
                        .Where(c => c.LocalEndPoint.Port == port)
                        .ToList();

                    if (processConnections.Count != 0) return process.ProcessName;
                } catch (Exception ex) {
                    Log.Debug(ex, "Error checking process {ProcessName} for port {Port}", process.ProcessName, port);
                }

            return null;
        } catch (Exception ex) {
            Log.Warning(ex, "Error getting process using port {Port}", port);

            return null;
        }
    }

    public static int FindNextAvailablePort(int startPort, int maxAttempts = 10) {
        for (var i = 0; i < maxAttempts; i++) {
            var candidatePort = startPort + i;

            if (IsPortAvailable(candidatePort)) return candidatePort;
        }

        return -1;
    }
}