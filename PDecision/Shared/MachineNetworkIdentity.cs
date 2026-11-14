using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;

namespace PokerSuite.Shared.Utilities;

// Linked into Setup, Updater and PDecision so discovery does not depend on
// different versions of the larger PokerSuite.Shared package.
internal static class MachineNetworkIdentity
{
    internal static bool IsCandidate(NetworkInterfaceType type, byte[] address) =>
        (type is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211
            or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetT)
        && address.Length == 6 && address.Any(b => b != 0)
        && (address[0] & 1) == 0;

    internal static string[] GetMacAddresses() => FormatCandidates(
        NetworkInterface.GetAllNetworkInterfaces().Select(n =>
            (n.NetworkInterfaceType, n.GetPhysicalAddress().GetAddressBytes())));

    internal static string[] FormatCandidates(IEnumerable<(NetworkInterfaceType Type, byte[] Address)> adapters) =>
        adapters.Where(n => IsCandidate(n.Type, n.Address))
            .Select(n => string.Join("-", n.Address.Select(b => b.ToString("X2"))))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(mac => mac, StringComparer.Ordinal).Take(8).ToArray();

    internal static bool Matches(string expected, IEnumerable<string> candidates) =>
        candidates.Any(mac => string.Equals(mac.Replace("-", ""),
            (expected ?? "").Replace("-", "").Replace(":", ""), StringComparison.OrdinalIgnoreCase));
}
