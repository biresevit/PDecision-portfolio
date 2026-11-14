using System.Net.NetworkInformation;
using PokerSuite.Shared.Utilities;

var cases = new (NetworkInterfaceType Type, string Mac, bool Expected)[] {
    (NetworkInterfaceType.Wireless80211, "02901D038565", true),
    (NetworkInterfaceType.Wireless80211, "12901D038565", true),
    (NetworkInterfaceType.Ethernet, "00F48DD21306", true),
    (NetworkInterfaceType.Ethernet, "021122334455", true),
    (NetworkInterfaceType.Ethernet, "000000000000", false),
    (NetworkInterfaceType.Ethernet, "FFFFFFFFFFFF", false),
    (NetworkInterfaceType.Ethernet, "011122334455", false),
    (NetworkInterfaceType.Ethernet, "0011", false),
    (NetworkInterfaceType.Loopback, "021122334455", false),
    (NetworkInterfaceType.Tunnel, "021122334455", false),
};
foreach (var c in cases)
    if (MachineNetworkIdentity.IsCandidate(c.Type, Convert.FromHexString(c.Mac)) != c.Expected)
        throw new Exception($"Adapter policy failed for {c.Type} / {c.Mac}");
Console.WriteLine($"Passed {cases.Length} adapter registration cases.");
var adapters = cases.Select(c => (c.Type, Convert.FromHexString(c.Mac)));
var macs = MachineNetworkIdentity.FormatCandidates(adapters.Concat(adapters).Reverse());
if (macs.Length != 4 || !macs.SequenceEqual(MachineNetworkIdentity.FormatCandidates(adapters)))
    throw new Exception("Discovery must be deterministic and deduplicate adapters.");
if (!MachineNetworkIdentity.Matches("02:90:1d:03:85:65", macs)
    || MachineNetworkIdentity.Matches("02-90-1D-03-85-66", macs))
    throw new Exception("Enrollment matching must use the same discovered candidates.");
Console.WriteLine("Shared discovery and enrollment matching passed.");
