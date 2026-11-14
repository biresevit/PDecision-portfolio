using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

// Finds every type in PDecision.dll / DecisionMaking.dll / PokerUtil.dll that has a field or property whose
// type is (directly, or inside List<T>/T[]/Nullable<T>) an enum declared in one of those same three
// assemblies, and reports whether that type is already in Obfuscar.template.xml's skip list. Run this after
// adding a new enum-typed field/property to any of the three assemblies, to find whether its containing type
// needs a <SkipType> entry too. See obfuscation/README.md for why.
//
// Usage: dotnet audit-enum-members.dll <path-to-obf-staging-folder> <path-to-Obfuscar.template.xml-or-generated-xml>
if (args.Length != 2)
{
    Console.Error.WriteLine("usage: audit-enum-members <obf-staging dir> <Obfuscar xml>");
    return 1;
}

string stagingDir = args[0];
string xmlPath = args[1];
var ourAssemblyNames = new HashSet<string> { "PDecision", "DecisionMaking", "PokerUtil" };

// a type is "already skipped" if the xml has a <SkipType name="CONFIGURE_LOCALLY"> for it (with skipFields/skipProperties,
// but this tool doesn't re-check that - it trusts the xml's own convention of always setting all three)
var skipped = new HashSet<string>();
foreach (var line in System.IO.File.ReadAllLines(xmlPath))
{
    var m = System.Text.RegularExpressions.Regex.Match(line, @"<SkipType\s+name=""([^""]+)""");
    if (m.Success)
    {
        skipped.Add(System.Net.WebUtility.HtmlDecode(m.Groups[1].Value));
    }
}

Type Unwrap(Type t)
{
    if (t.IsArray) return Unwrap(t.GetElementType());
    if (t.IsGenericType)
    {
        var def = t.GetGenericTypeDefinition();
        if (def == typeof(Nullable<>) || def == typeof(List<>) || def == typeof(IList<>) || def == typeof(IEnumerable<>))
        {
            return Unwrap(t.GetGenericArguments()[0]);
        }
    }
    return t;
}

bool IsOurs(Type t) => ourAssemblyNames.Contains(t.Assembly.GetName().Name);

Type[] GetLoadableTypes(Assembly asm)
{
    try { return asm.GetTypes(); }
    catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t != null).ToArray(); }
}

var dlls = new[] { "DecisionMaking.dll", "PDecision.dll", "PokerUtil.dll" }
    .Select(f => System.IO.Path.Combine(stagingDir, f)).ToArray();
var loaded = dlls.Select(p => Assembly.LoadFrom(p)).ToArray();

int flagged = 0;
foreach (var target in loaded)
{
    Console.WriteLine($"=== {target.GetName().Name} ===");
    foreach (var type in GetLoadableTypes(target).Where(t => !t.IsEnum).OrderBy(t => t.FullName))
    {
        FieldInfo[] fields; PropertyInfo[] props;
        try
        {
            fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            props = type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        }
        catch { continue; }

        var hits = new List<string>();
        foreach (var f in fields)
        {
            try { var u = Unwrap(f.FieldType); if (u.IsEnum && IsOurs(u)) hits.Add($"field {f.Name} : {u.FullName}"); }
            catch { /* an unrelated member's type couldn't be resolved (e.g. missing WinForms/AspNetCore from this standalone tool) - not an enum concern */ }
        }
        foreach (var p in props)
        {
            try { var u = Unwrap(p.PropertyType); if (u.IsEnum && IsOurs(u)) hits.Add($"property {p.Name} : {u.FullName}"); }
            catch { }
        }
        if (hits.Count == 0) continue;

        string cecilName = type.FullName.Replace("+", "/");
        bool ok = skipped.Contains(cecilName);
        Console.WriteLine($"{(ok ? "OK " : "!!!")} {type.FullName}  [skip-listed: {ok}]");
        foreach (var h in hits) Console.WriteLine("      " + h);
        if (!ok) flagged++;
    }
}
Console.WriteLine();
Console.WriteLine(flagged == 0
    ? "All enum-typed members are in types already on the skip list."
    : $"{flagged} type(s) need a <SkipType> entry added to Obfuscar.template.xml - see the !!! lines above.");
return flagged == 0 ? 0 : 1;
