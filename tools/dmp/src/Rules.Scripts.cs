using System.Text.RegularExpressions;

namespace Cm.Dmp;

// DMP-011: the safe-script standard (chapter 10, "Scripts an administrator runs"). The guard is tools/lib/Guard.psm1; the written standard is
// docs/process/owner-scripts.md. The PowerShell side (the guard's self-test, the 5.1 parse) runs as the scripts step of tools/verify.
public static partial class Rules
{
    const string ScriptExemptions = "tools/lib/script-exemptions.txt";
    const int MaxGuardExemptions = 6; // the numbers only go down: fix a script, then lower this

    static readonly Regex GuardImport = new(@"Import-Module\s+\(Join-Path\s+\$PSScriptRoot\s+'(?<path>[^']+)'\)\s+-Force\s+-ErrorAction\s+Stop", RegexOptions.Compiled);

    // Names that differ on German Windows: resolve principals by their well-known SID (Get-Sid, S-1-5-32-544), never by name.
    const string LocalizedNames =
        @"Administrators|Users|Guests|Power Users|Backup Operators|Remote Desktop Users|Remote Management Users|Performance Monitor Users|Performance Log Users|Event Log Readers|Hyper-V Administrators|Network Configuration Operators|Distributed COM Users|Cryptographic Operators|Print Operators|Server Operators|Account Operators|Replicator|Everyone|Authenticated Users|Domain Admins|Domain Users|Domain Computers|Domain Controllers|Domain Guests|Enterprise Admins|Schema Admins|Group Policy Creator Owners|Cert Publishers|"
        + @"Administratoren|Benutzer|G.ste|Hauptbenutzer|Sicherungs-Operatoren|Remotedesktopbenutzer|Remoteverwaltungsbenutzer|Jeder|Authentifizierte Benutzer|Dom.nen-Admins|Dom.nen-Benutzer|Organisations-Admins|Schema-Admins";

    static readonly Regex[] PrincipalNames =
    [
        new($@"(?<q>['""])\s*(?:(?:BUILTIN|NT AUTHORITY|NT-AUTORIT.T|VORDEFINIERT)\\)?(?:{LocalizedNames})\s*\k<q>", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"(BUILTIN|NT AUTHORITY)\\[A-Za-z]", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new($@"-(Group|Identity|Principal|Trustee)\s+(?:{LocalizedNames})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new($@"-eq\s+'(?:{LocalizedNames})'", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\bnet(\.exe)?\s+localgroup\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new($@"(?<![\w\\*])(?:{LocalizedNames}|SYSTEM)\s*:\s*\(", RegexOptions.IgnoreCase | RegexOptions.Compiled),
    ];

    // "A name must not lie": a read-only script changes nothing, an install or upgrade sits behind the apply switch and is proven afterwards (case: an upgrade run under the name -Check removed Git from a laptop).
    static readonly string[] AllowedLikeMutators = ["Set-StrictMode", "Set-Location", "Set-Variable", "Set-Content", "Add-Content", "Set-PSDebug", "Add-Type", "New-Object", "New-Guid", "New-TimeSpan", "New-CimSession", "New-CimSessionOption", "New-PSSession", "New-PSSessionOption", "New-PSSessionConfigurationFile", "Remove-PSSession", "Remove-CimSession", "Remove-Variable", "Remove-Module", "Remove-PSDrive", "Start-Sleep"];

    static readonly Regex MutatingCmdlet = new(@"(?<![\w\-.$])(?:(?:Set|Remove|Install|Uninstall|Enable|Disable|Grant|Revoke|Register|Unregister|Restart|Move|Rename|Update|Publish|Reset|Suspend|Resume|Block|Unblock|Lock|Unlock|Add|New|Copy|Stop|Start)-[A-Z][A-Za-z0-9]+)(?![\w\-])", RegexOptions.Compiled);

    static readonly Regex MutatingNative = new(@"(?<![\w\-.])(?:winget(?:\.exe)?\s+(?:install|upgrade|uninstall|import)|choco(?:\.exe)?\s+(?:install|upgrade|uninstall)|git(?:\.exe)?\s+(?:commit|push|pull|checkout|switch|reset|merge|rebase|clean|add|clone|stash|apply)|sc(?:\.exe)?\s+(?:create|config|delete|start|stop|sdset)|net(?:\.exe)?\s+(?:user|accounts)|setspn(?:\.exe)?\s+-[SAD]\b|reg(?:\.exe)?\s+(?:add|delete|import)|schtasks(?:\.exe)?\s+/(?:create|delete|change)|msiexec(?:\.exe)?\s)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    static readonly Regex InstallCommand = new(@"(?<![\w\-.$])(?:(?:Install|Update)-(?:Module|Package|Script|PSResource|WindowsFeature|WindowsCapability)|Add-WindowsFeature|winget(?:\.exe)?\s+(?:install|upgrade|import)|choco(?:\.exe)?\s+(?:install|upgrade)|msiexec(?:\.exe)?\s)(?![\w\-])", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    static readonly Regex InstallThroughHelper = new(@"'(?:winget|choco)(?:\.exe)?'\s*[,(@ ]+\(?@?\(?\s*'(?:install|upgrade|import)'", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    static readonly Regex GateHeader = new(@"\$Apply\b|ShouldProcess|WhatIfPreference|-not\s+\$(?:Check|DryRun|WhatIf)\b|!\s*\$(?:Check|DryRun|WhatIf)\b|\bInvoke-Change\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // shapes/claude-code-config/ is skipped on purpose: its scripts are copies of the configuration repository's own install and upgrade scripts, kept
    // byte-compatible and replaced by that repository's scripts at the cut-over (shapes/claude-code-config/README.md). Exactly this folder, nothing else under shapes/.
    const string SkippedShapeFolder = "shapes/claude-code-config/";

    static bool IsShapeScript(string f) => f.EndsWith(".ps1", StringComparison.Ordinal) && !f.StartsWith(SkippedShapeFolder, StringComparison.Ordinal);

    // Scripts an administrator runs live under tools/ and, since shapes/ holds deployable shapes with their own owner scripts, under shapes/ (same standard).
    static IEnumerable<string> AdminScripts(RepoView repo) =>
        repo.Files("tools").Where(f => f.EndsWith(".ps1", StringComparison.Ordinal) && !f.StartsWith("tools/lib/", StringComparison.Ordinal) && f != "tools/verify.ps1")
            .Concat(repo.Files("shapes").Where(IsShapeScript));

    // Where the guard module is, seen from the script: tools/<area>/x.ps1 -> ..\lib\Guard.psm1; shapes/<shape>/x.ps1 -> ..\..\tools\lib\Guard.psm1.
    static string ExpectedGuardImport(string rel)
    {
        var slashes = rel.Count(c => c == '/');
        return rel.StartsWith("shapes/", StringComparison.Ordinal)
            ? string.Concat(Enumerable.Repeat(@"..\", slashes)) + @"tools\lib\Guard.psm1"
            : string.Concat(Enumerable.Repeat(@"..\", slashes - 1)) + @"lib\Guard.psm1";
    }

    static IEnumerable<Finding> ScriptGuard(RepoView repo)
    {
        if (!repo.Exists("tools/lib/Guard.psm1")) { yield return new("DMP-011", "tools/lib/Guard.psm1", "missing: the shared script guard"); yield break; }

        var exemptions = (repo.ReadText(ScriptExemptions) ?? "").Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0 && l[0] != '#').Select(l => l.Split('\t')).ToList();
        var exempt = exemptions.Select(e => e[0]).ToHashSet(StringComparer.Ordinal);
        var all = AdminScripts(repo).ToList();
        foreach (var e in exemptions)
        {
            if (!all.Contains(e[0])) yield return new("DMP-011", ScriptExemptions, $"'{e[0]}' is not a script under tools/ or shapes/ (delete the line)");
            else if (e.Length < 2 || e[1].Length < 12) yield return new("DMP-011", ScriptExemptions, $"'{e[0]}' needs a reason");
            else if ((repo.ReadText(e[0]) ?? "").Contains("Assert-Script", StringComparison.Ordinal)) yield return new("DMP-011", ScriptExemptions, $"'{e[0]}' calls the guard now; delete its exemption");
        }
        if (exemptions.Count > MaxGuardExemptions) yield return new("DMP-011", ScriptExemptions, $"more than {MaxGuardExemptions} exemptions; fix a script instead of adding a line");

        foreach (var rel in all.Where(f => !exempt.Contains(f)))
        {
            var code = PowerShellScan.StripComments(repo.ReadText(rel) ?? "");
            var expected = ExpectedGuardImport(rel);
            var import = GuardImport.Match(code);
            if (!import.Success) { yield return new("DMP-011", rel, $"does not import the guard: Import-Module (Join-Path $PSScriptRoot '{expected}') -Force -ErrorAction Stop"); continue; }
            if (import.Groups["path"].Value != expected) yield return new("DMP-011", rel, $"imports '{import.Groups["path"].Value}', expected '{expected}'");

            var before = PowerShellScan.RemoveParamBlock(code[..import.Index]);
            before = Regex.Replace(before, @"\[CmdletBinding\([^\]]*\)\]|\[CmdletBinding\]|\$ErrorActionPreference\s*=\s*'\w+'|Set-StrictMode[^\r\n]*", "");
            if (before.Trim().Length > 0) yield return new("DMP-011", rel, "something runs before the guard (only param, [CmdletBinding], $ErrorActionPreference and Set-StrictMode may come first)");

            var calls = Regex.Matches(code, @"Assert-Script\b");
            if (calls.Count != 1) { yield return new("DMP-011", rel, $"calls Assert-Script {calls.Count} times, must be exactly once"); continue; }
            var call = Regex.Match(code[calls[0].Index..], @"^[^\r\n]*(?:`\r?\n[^\r\n]*)*").Value.Replace("`\r\n", " ").Replace("`\n", " ");
            if (!Regex.IsMatch(code[..calls[0].Index], @"\$\w+\s*=\s*$")) yield return new("DMP-011", rel, "the call must be '$null = Assert-Script ...'");
            if (import.Index > calls[0].Index) yield return new("DMP-011", rel, "calls the guard before importing it");
            if (!Regex.IsMatch(call, @"-ScriptPath\s+\$PSCommandPath\b")) yield return new("DMP-011", rel, "the guard call needs -ScriptPath $PSCommandPath");
            if (!Regex.IsMatch(call, @"-Elevation\s+(Required|Forbidden|Either)\b")) yield return new("DMP-011", rel, "the guard call needs -Elevation Required, Forbidden or Either");
            var kind = Regex.Match(call, @"-Kind\s+(?<k>ReadOnly|Changes)\b");
            if (!kind.Success) { yield return new("DMP-011", rel, "the guard call needs -Kind ReadOnly or -Kind Changes"); continue; }

            // Roles, not values: the expected host is a mandatory parameter of the script, so the command block supplies it; a literal name never enters the tree.
            var byParameter = Regex.IsMatch(call, @"-ExpectedHost\s+\$ExpectedHost\b") && Regex.IsMatch(code, @"\[Parameter\(Mandatory\)\]\s*\[string\]\s*\$ExpectedHost\b");
            var anyHost = Regex.IsMatch(call, @"-AnyHostBecause\s+'");
            if (byParameter == anyHost) yield return new("DMP-011", rel, "the guard call needs exactly one of -ExpectedHost $ExpectedHost (with a mandatory [string]$ExpectedHost parameter) or -AnyHostBecause '<reason>'");
            foreach (Match reason in Regex.Matches(call, @"-(\w*(?:Any|NotRequired)\w*Because)\s+'(?<r>[^']*)'"))
                if (reason.Groups["r"].Value.Trim().Length < 12) yield return new("DMP-011", rel, $"{reason.Groups[1].Value} needs a real reason (at least 12 characters)");
            if (Regex.IsMatch(call, @"-Facts\b|-Policy\b")) yield return new("DMP-011", rel, "the guard call may not pass -Facts or -Policy");

            var hasApply = Regex.IsMatch(code, @"\[switch\]\s*\$Apply\b");
            var hasShouldProcess = code.Contains("SupportsShouldProcess", StringComparison.Ordinal);
            if (kind.Groups["k"].Value == "ReadOnly")
            {
                if (hasApply || hasShouldProcess || call.Contains("-Applying", StringComparison.Ordinal)) yield return new("DMP-011", rel, "is Kind ReadOnly but has -Apply / SupportsShouldProcess: either it is read only or it changes something");
            }
            else
            {
                var applying = Regex.Match(call, @"-Applying:(?<e>\$Apply\b|\(-not \$WhatIfPreference\))");
                if (!applying.Success) yield return new("DMP-011", rel, "changes something, so it needs a dry run and -Applying:$Apply (a [switch]$Apply) or -Applying:(-not $WhatIfPreference) (SupportsShouldProcess)");
                else if (applying.Groups["e"].Value == "$Apply" && !hasApply) yield return new("DMP-011", rel, "passes -Applying:$Apply but declares no [switch]$Apply");
                else if (applying.Groups["e"].Value != "$Apply" && !hasShouldProcess) yield return new("DMP-011", rel, "passes -Applying:(-not $WhatIfPreference) but is not [CmdletBinding(SupportsShouldProcess)]");
            }
            if (Regex.IsMatch(code, @"\$env:CM_(ALLOW_BRANCH|EXPECT_COMMIT)\s*=")) yield return new("DMP-011", rel, "sets CM_ALLOW_BRANCH / CM_EXPECT_COMMIT itself; only the person who runs the script may");
        }

        // A name must not lie (read-only scripts change nothing; installs are gated by the apply switch and proven afterwards).
        foreach (var rel in all.Where(f => !exempt.Contains(f)))
        {
            var rawText = repo.ReadText(rel) ?? "";
            var rawLines = rawText.Split('\n');
            var stripped = PowerShellScan.StripComments(rawText);
            var blank = PowerShellScan.BlankStrings(stripped);
            var lines = blank.Split('\n');
            var kind = Regex.Match(stripped, @"Assert-Script[^\r\n]*-Kind\s+(?<k>ReadOnly|Changes)\b").Groups["k"].Value;
            if (kind == "ReadOnly")
                for (var n = 0; n < lines.Length && n < rawLines.Length; n++)
                {
                    if (rawLines[n].Contains("lint-ok: mutates", StringComparison.Ordinal)) continue;
                    var hit = MutatingCmdlet.Matches(lines[n]).Select(m => m.Value).FirstOrDefault(v => !AllowedLikeMutators.Contains(v, StringComparer.OrdinalIgnoreCase))
                              ?? (MutatingNative.Match(lines[n]) is { Success: true } nat ? nat.Value.Trim() : null);
                    if (hit is not null) yield return new("DMP-011", rel, $"line {n + 1}: a Kind ReadOnly script calls '{hit}', which changes something; use Kind Changes with a dry run. Only a script's own scratch file: end the line with '# lint-ok: mutates <why>'");
                }
            var installs = InstallCommand.Matches(blank).Select(m => m.Index).Concat(InstallThroughHelper.Matches(stripped).Select(m => m.Index)).ToList();
            var needsVerify = false;
            foreach (var index in installs)
            {
                var line = blank.Take(index).Count(c => c == '\n');
                if (line < rawLines.Length && rawLines[line].Contains("lint-ok: install", StringComparison.Ordinal)) continue;
                var text = (line < lines.Length ? lines[line] : "").Trim();
                if (!PowerShellScan.EnclosingHeaders(blank, index).Any(h => GateHeader.IsMatch(h)) && !GateHeader.IsMatch(text))
                    yield return new("DMP-011", rel, $"line {line + 1}: an install or upgrade is not inside an 'if ($Apply)' / ShouldProcess block: a dry run must never install");
                needsVerify |= !Regex.IsMatch(text, @"Install-Module|Update-Module|WindowsFeature|WindowsCapability|Install-Script|Install-PSResource|Install-Package", RegexOptions.IgnoreCase);
            }
            if (needsVerify && !stripped.Contains("Assert-ToolWorks", StringComparison.Ordinal))
                yield return new("DMP-011", rel, "installs or upgrades a program but never calls Assert-ToolWorks -Name <tool> -Before $before afterwards");
        }

        // No localized Windows principal names in any script (including the exempt ones and the guard).
        foreach (var rel in repo.Files("tools").Concat(repo.Files("shapes").Where(f => !f.StartsWith(SkippedShapeFolder, StringComparison.Ordinal))).Where(f => f.EndsWith(".ps1", StringComparison.Ordinal) || f.EndsWith(".psm1", StringComparison.Ordinal)))
        {
            var raw = (repo.ReadText(rel) ?? "").Split('\n');
            var stripped = PowerShellScan.StripComments(string.Join('\n', raw)).Split('\n');
            for (var n = 0; n < stripped.Length && n < raw.Length; n++)
            {
                if (raw[n].Contains("lint-ok: principal-name", StringComparison.Ordinal)) continue;
                var hit = PrincipalNames.Select(p => p.Match(stripped[n])).FirstOrDefault(m => m.Success);
                if (hit is not null)
                    yield return new("DMP-011", rel, $"line {n + 1}: '{hit.Value.Trim()}' is a name that differs on German Windows; use the well-known SID (Get-Sid, S-1-5-32-544). A label: end the line with '# lint-ok: principal-name <why>'");
            }
        }
    }
}
