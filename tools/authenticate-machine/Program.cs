using System;
using System.IO;
using PokerSuite.Shared.Helpers;

// One-time provisioning tool: creates C:\PokerSuite\Authentication.txt on a machine that doesn't have one yet.
// Any PokerSuite.Shared-based service (PDecision included) refuses to even construct its logger without this
// file - see PokerSuite.Shared.Helpers.LogHelpers.GetLogger, SettingsHelpers.LoadAppSettings and
// SQLDataService's constructor, which all call AuthenticationHelpers.ReadAuthenticationData and throw
// "Failed to authenticate" if it's missing (unless the machine is detected as an actual Azure VM). This is
// a separate, PokerSuite-wide per-machine gate, unrelated to PDecision's own dbo.CustomerMachine/MAC
// whitelist - a machine needs both to run PDecision.
//
// Usage: authenticate-machine <ComputerId> [ComputerName]
// ComputerId: a short not already used by another machine (35 is reserved to mean "Azure"); ComputerName
// defaults to this machine's own hostname. Refuses to run if C:\PokerSuite\Authentication.txt already exists.
if (args.Length < 1 || !short.TryParse(args[0], out short computerId))
{
    Console.Error.WriteLine("usage: authenticate-machine <ComputerId> [ComputerName]");
    return 1;
}
string computerName = args.Length >= 2 ? args[1] : Environment.MachineName;
string token = Guid.NewGuid().ToString();

// WriteAuthenticationDataToFile calls EnsureFileDoesNotExist internally but still deletes and overwrites an
// existing file regardless (confirmed the hard way) - guard it ourselves so this can never clobber a
// machine's already-working Authentication.txt.
const string path = "C:\\PokerSuite\\Authentication.txt";
if (File.Exists(path))
{
    Console.Error.WriteLine($"{path} already exists - refusing to overwrite it. Delete it yourself first if you really want to replace it.");
    return 1;
}

var result = AuthenticationHelpers.WriteAuthenticationDataToFile(token, computerName, computerId);
if (result.IsSuccess)
{
    Console.WriteLine($"Wrote C:\\PokerSuite\\Authentication.txt for ComputerId={computerId}, ComputerName={computerName}");
    return 0;
}

Console.Error.WriteLine("FAILED: " + result.GenerateReport());
return 1;
