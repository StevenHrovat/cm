using System.Text.RegularExpressions;

namespace Cm.Dmp;

// DMP-020: every deployment obeys DMP (constitution C25). The check is default-deny and reads the workflows with a real parser
// (WorkflowYaml, which refuses YAML it cannot read), so a key cannot hide from it.
//
// (a) Every workflow is registered. DMP/workflows.txt lists every file of .github/workflows as `check` or `deploy`. An unlisted
//     workflow is refused, so nothing is a deployment "by not being called one".
//     - A `check` workflow cannot ship through GitHub: no `environment:`, no self-hosted runner, no reusable workflow, top-level
//       `permissions:` present and every scope read or none except the comment scopes pull-requests and issues (never checks or
//       statuses, which could post the required status), runs-on one literal GitHub-hosted label (no expression, no group or
//       labels map), and it names no secret except GITHUB_TOKEN and the ones its register line lists.
//     - A `deploy` workflow has a fixed shape. Only known keys at the top, job and step level (no continue-on-error, strategy,
//       defaults, shell, container, services, reusable workflow, top-level env). A gate job runs the same gate as the required
//       check: its runs-on and steps are identical to the `verify` job of verify.yml, and it sets no env of its own. Every other
//       job needs a gate job directly and ships from main only (its if: includes github.ref == 'refs/heads/main', joined only by
//       &&, so a dispatch from an unreviewed branch never ships), every `needs:` names a job of the file, and a job-level `if:` is allow-listed: only
//       comparisons of vars.*, github.ref, github.ref_name or github.event_name with a quoted literal, joined by && or ||. A status
//       function (always(), Always(), ...) therefore never passes, so a red gate always stops the ship. A ship job checks out no other
//       ref, runs no git at all (any case) and uses only checkout, setup and artifact actions, so it ships the commit the gate saw.
//     - The gate itself: the verify job of verify.yml runs `tools/verify` as a plain step (no if:, no continue-on-error), so a PR that
//       edits verify.yml and a deploy workflow together cannot make the gate a gate that checks nothing.
//     - In any workflow, env may not set a GITHUB_ or RUNNER_ variable, and no workflow is triggered by pull_request_target or
//       workflow_run (both run with the base repository's token and secrets on code the gate did not judge).
//     What stays outside a workflow-level check: code a ship step runs (a script that calls git) and the contents of tools/verify.
//     Both are the commit the gate verified, so their own tests and the review cover them.
// (b) No lockout. DMP/routes.txt lists administrator actions with their routes (cli, console) or as a gap with a reason, and a
//     line `cap = N`, the most gaps allowed. Every line must parse and the gaps may not exceed the cap.
// (c) The ratchet. On a pull request (GITHUB_BASE_REF, set by GitHub, not by the workflow) the base is the exact base commit from
//     the event (pull_request.base.sha), which the verify workflow fetches; no step can move a commit id. If it cannot be read, or
//     git fails on it for any reason but an absent file, the rule fails closed. The registers are compared with the base: the cap may not rise, no gap may be added, no action and no route may be removed, and a workflow
//     registered as `deploy` may not become `check`. C25: "the list of known gaps may only shrink".
public static partial class Rules
{
    const string WorkflowDir = ".github/workflows";
    const string RoutesFile = "DMP/routes.txt";
    const string WorkflowsFile = "DMP/workflows.txt";
    static readonly string[] Routes = ["cli", "console"];
    // checks and statuses are not here: with them a workflow could post the green status the branch protection reads.
    static readonly string[] CommentScopes = ["pull-requests", "issues"];
    // A check workflow runs only on GitHub-hosted runners, named literally, so it can never reach the operating organisation network.
    static readonly string[] HostedRunners = ["ubuntu-latest", "ubuntu-24.04", "ubuntu-22.04", "windows-latest", "windows-2025", "windows-2022"];
    const string VerifyWorkflow = ".github/workflows/verify.yml";
    static readonly string[] DeployTopKeys = ["name", "run-name", "on", "permissions", "concurrency", "jobs"];
    static readonly string[] DeployJobKeys = ["name", "runs-on", "needs", "if", "environment", "permissions", "timeout-minutes", "env", "steps", "concurrency"];
    static readonly string[] GateJobKeys = ["name", "runs-on", "needs", "if", "environment", "permissions", "timeout-minutes", "steps", "concurrency"];
    static readonly string[] ShipStepKeys = ["name", "id", "uses", "with", "run", "env", "if", "timeout-minutes", "working-directory"];
    static readonly string[] ShipActions = ["actions/checkout@", "actions/setup-", "actions/upload-artifact@", "actions/download-artifact@"];
    // The only inputs a ship step may give those actions: no ref, repository, token or key, so the checkout stays the gate's commit.
    static readonly string[] ShipInputs = ["fetch-depth", "fetch-tags", "dotnet-version", "global-json-file", "name", "path", "retention-days", "if-no-files-found"];

    [GeneratedRegex(@"^cap\s*=\s*(\d+)$")]
    private static partial Regex GapCap();

    // One comparison a job-level if: may make. GitHub reads context and function names without regard to case, so the regex does too.
    const string Comparison = @"(vars\.[A-Za-z0-9_]+|github\.(ref|ref_name|event_name))\s*(==|!=)\s*'[^']*'";

    [GeneratedRegex(@"^\s*" + Comparison + @"(\s*(&&|\|\|)\s*" + Comparison + @")*\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex PlainCondition();

    [GeneratedRegex(@"(^|&&)\s*github\.ref\s*==\s*'refs/heads/main'\s*($|&&)", RegexOptions.IgnoreCase)]
    private static partial Regex MainOnly();

    [GeneratedRegex(@"\bgit\b", RegexOptions.IgnoreCase)]
    private static partial Regex AnyGit();

    static readonly string[] UnjudgedTriggers = ["pull_request_target", "workflow_run"];

    [GeneratedRegex(@"\$\{\{(.*?)\}\}", RegexOptions.Singleline)]
    private static partial Regex Expression();

    // GitHub reads context names without regard to case (SECRETS.X is secrets.X), and so does this.
    [GeneratedRegex(@"\bsecrets\b(?<name>\s*\.\s*[A-Za-z0-9_]+\b)?", RegexOptions.IgnoreCase)]
    private static partial Regex SecretUse();

    [GeneratedRegex(@"^(?<file>[A-Za-z0-9_.\-]+\.ya?ml)\s*:\s*(?<kind>check|deploy)(\s+secrets\s+(?<secrets>[A-Z0-9_, ]+?))?\s+-\s+(?<reason>\S.*)$")]
    private static partial Regex WorkflowLine();

    sealed record RouteEntry(bool IsGap, HashSet<string> Routes);

    static IEnumerable<Finding> DeploymentGateRule(RepoView repo)
    {
        var findings = new List<Finding>();
        void Add(string path, string message) => findings.Add(new("DMP-020", path, message));

        // ---- (a) workflows -------------------------------------------------------------------------------------------------
        var register = ParseWorkflowRegister(repo.ReadText(WorkflowsFile), Add);
        YMap? verifyJob = null;
        try { verifyJob = ((WorkflowYaml.Parse(repo.ReadText(VerifyWorkflow) ?? "") as YMap)?["jobs"] as YMap)?["verify"] as YMap; }
        catch (YamlUnsupported) { }
        if (verifyJob is null) Add(VerifyWorkflow, "the required check needs a job 'verify' the deployment gate can be compared with (C25)");
        else if (verifyJob["continue-on-error"] is not null || verifyJob["if"] is not null
                 || !Steps(verifyJob).Any(s => Text(s["run"]) == "tools/verify" && s["if"] is null && s["continue-on-error"] is null))
            Add(VerifyWorkflow, "the verify job must run 'tools/verify' as a plain step, with no if: and no continue-on-error on the step or the job; it is the gate every deployment runs (C25)");
        var files = repo.Files(WorkflowDir).Where(f => f.EndsWith(".yml", StringComparison.Ordinal) || f.EndsWith(".yaml", StringComparison.Ordinal)).ToList();
        foreach (var listed in register.Keys.Where(k => !files.Any(f => Path.GetFileName(f) == k)))
            Add(WorkflowsFile, $"lists '{listed}', which is not a workflow in {WorkflowDir}");
        foreach (var file in files)
        {
            if (!register.TryGetValue(Path.GetFileName(file), out var entry))
            {
                Add(file, $"is not registered in {WorkflowsFile} as check or deploy (C25: every deployment obeys DMP, so none is left unnamed)");
                continue;
            }
            var text = repo.ReadText(file) ?? "";
            YMap root;
            try
            {
                root = WorkflowYaml.Parse(text) as YMap ?? throw new YamlUnsupported(1, "the top level is not a mapping");
            }
            catch (YamlUnsupported e)
            {
                Add(file, $"cannot be read by the deployment check ({e.Message}); write it in plain block YAML");
                continue;
            }
            var jobs = (root["jobs"] as YMap)?.Entries.Select(e => (Name: e.Key, Job: e.Value as YMap)).ToList() ?? [];
            if (jobs.Count == 0 || jobs.Any(j => j.Job is null)) { Add(file, "has no jobs the check can read"); continue; }
            foreach (var reserved in EnvKeys(root).Where(k => k.StartsWith("GITHUB_", StringComparison.Ordinal) || k.StartsWith("RUNNER_", StringComparison.Ordinal)).Distinct())
                Add(file, $"sets env '{reserved}', a variable GitHub sets; a workflow may not override it");
            foreach (var trigger in Triggers(root["on"]).Where(UnjudgedTriggers.Contains))
                Add(file, $"is triggered by {trigger}, which runs with the base repository's token and secrets on code the gate did not judge");
            if (entry.Kind == "check") CheckOnlyWorkflow(file, text, root, jobs!, entry.Secrets, Add);
            else DeployWorkflow(file, root, jobs!, verifyJob, Add);
        }

        // ---- (b) routes ----------------------------------------------------------------------------------------------------
        var routes = ParseRoutes(repo.ReadText(RoutesFile), RoutesFile, Add, out var cap);

        // ---- (c) ratchet against the base ----------------------------------------------------------------------------------
        if (repo.BaseError is { } baseError) Add(RoutesFile, $"the base of this change cannot be read ({baseError}), so the ratchet of C25 cannot be checked");
        else if (repo.ReadBaseText is { } readBase)
        {
            try
            {
                if (routes is not null && readBase(RoutesFile) is { } baseRoutesText)
                {
                    var baseRoutes = ParseRoutes(baseRoutesText, RoutesFile + " (base)", (_, _) => { }, out var baseCap) ?? [];
                    if (cap > baseCap) Add(RoutesFile, $"the cap rose from {baseCap} to {cap}; C25: the list of known gaps may only shrink");
                    foreach (var (action, before) in baseRoutes)
                    {
                        if (!routes.TryGetValue(action, out var now)) { Add(RoutesFile, $"action '{action}' was removed; an action keeps its line (C25)"); continue; }
                        foreach (var lost in before.Routes.Except(now.Routes)) Add(RoutesFile, $"action '{action}' lost its route '{lost}' (C25: no deployment locks us out)");
                    }
                    foreach (var (action, now) in routes.Where(r => r.Value.IsGap))
                        if (!baseRoutes.TryGetValue(action, out var before) || !before.IsGap) Add(RoutesFile, $"'{action}' is a new gap; C25: the list of known gaps may only shrink");
                }
                if (readBase(WorkflowsFile) is { } baseWorkflowsText)
                    foreach (var (file, before) in ParseWorkflowRegister(baseWorkflowsText, (_, _) => { }))
                        if (before.Kind == "deploy" && register.TryGetValue(file, out var now) && now.Kind != "deploy")
                            Add(WorkflowsFile, $"'{file}' was registered as deploy and may not become {now.Kind}");
            }
            catch (BaseUnreadable e) { Add(RoutesFile, $"the base of this change cannot be read ({e.Message}), so the ratchet of C25 cannot be checked"); }
        }
        return findings;
    }

    static Dictionary<string, (string Kind, string[] Secrets)> ParseWorkflowRegister(string? text, Action<string, string> add)
    {
        var register = new Dictionary<string, (string, string[])>(StringComparer.Ordinal);
        if (text is null) { add(WorkflowsFile, "missing: the register of workflows (check or deploy) (C25)"); return register; }
        foreach (var raw in text.Split('\n').Select(l => l.Trim()))
        {
            if (raw.Length == 0 || raw.StartsWith('#')) continue;
            var m = WorkflowLine().Match(raw);
            if (!m.Success) { add(WorkflowsFile, $"line '{raw}' is not 'file.yml: check|deploy [secrets A, B] - reason'"); continue; }
            var secrets = m.Groups["secrets"].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (!register.TryAdd(m.Groups["file"].Value, (m.Groups["kind"].Value, secrets))) add(WorkflowsFile, $"'{m.Groups["file"].Value}' is listed twice");
        }
        return register;
    }

    static Dictionary<string, RouteEntry>? ParseRoutes(string? text, string path, Action<string, string> add, out int? cap)
    {
        cap = null;
        if (text is null) { add(path, "missing: the register of administrator actions and their routes (C25)"); return null; }
        var routes = new Dictionary<string, RouteEntry>(StringComparer.Ordinal);
        foreach (var raw in text.Split('\n').Select(l => l.Trim()))
        {
            if (raw.Length == 0 || raw.StartsWith('#')) continue;
            if (GapCap().Match(raw) is { Success: true } capLine) { cap = int.Parse(capLine.Groups[1].Value); continue; }
            var colon = raw.IndexOf(':');
            if (colon <= 0) { add(path, $"line '{raw}' is not 'action: route[, route]' or 'action: gap - reason'"); continue; }
            var action = raw[..colon].Trim();
            var value = raw[(colon + 1)..].Trim();
            RouteEntry entry;
            if (value.StartsWith("gap", StringComparison.Ordinal))
            {
                if (value[3..].Trim(' ', '-').Length == 0) add(path, $"gap '{action}' needs a reason");
                entry = new(true, []);
            }
            else
            {
                var named = value.Split(',', StringSplitOptions.TrimEntries).ToHashSet(StringComparer.Ordinal);
                foreach (var route in named.Where(r => !Routes.Contains(r))) add(path, $"action '{action}' names an unknown route '{route}' (cli, console)");
                entry = new(false, named);
            }
            if (!routes.TryAdd(action, entry)) add(path, $"action '{action}' is listed twice");
        }
        var gaps = routes.Values.Count(r => r.IsGap);
        if (cap is null) add(path, "missing the line 'cap = N', the most known gaps allowed (C25)");
        else if (gaps > cap) add(path, $"{gaps} known gaps, but the cap is {cap}; C25: the list of known gaps may only shrink");
        return routes;
    }

    static void CheckOnlyWorkflow(string file, string text, YMap root, List<(string Name, YMap Job)> jobs, string[] allowedSecrets, Action<string, string> add)
    {
        if (root["permissions"] is null) add(file, "a check workflow must set top-level permissions (read or none), so its token cannot write");
        CheckPermissions(file, "the workflow", root["permissions"], add);
        foreach (var (name, job) in jobs)
        {
            if (job["environment"] is not null) add(file, $"job '{name}' has an environment, so it is a deployment: register the workflow as deploy");
            if (job["uses"] is not null) add(file, $"job '{name}' calls a reusable workflow, which a check workflow may not do");
            if (job["runs-on"] is not YScalar { Value: var runner } || !HostedRunners.Contains(runner))
                add(file, $"job '{name}' runs-on '{Text(job["runs-on"])}'; a check workflow names one GitHub-hosted runner literally ({string.Join(", ", HostedRunners)}), anything else may reach company systems: register the workflow as deploy");
            CheckPermissions(file, $"job '{name}'", job["permissions"], add);
        }
        foreach (Match expression in Expression().Matches(text))
            foreach (Match use in SecretUse().Matches(expression.Groups[1].Value))
            {
                if (!use.Groups["name"].Success) { add(file, $"reads secrets other than by name ('{expression.Value.Trim()}'); a check workflow names each secret it uses"); continue; }
                var secret = use.Groups["name"].Value.Trim().TrimStart('.').Trim();
                if (!secret.Equals("GITHUB_TOKEN", StringComparison.OrdinalIgnoreCase) && !allowedSecrets.Contains(secret, StringComparer.OrdinalIgnoreCase)) add(file, $"uses secret '{secret}', which its line in {WorkflowsFile} does not list");
            }
    }

    static void CheckPermissions(string file, string where, YNode? permissions, Action<string, string> add)
    {
        switch (permissions)
        {
            case null: break;
            case YScalar s when s.Value is "read-all" or "": break;
            case YScalar s: add(file, $"{where} sets permissions '{s.Value}'; a check workflow may only read"); break;
            case YMap m:
                foreach (var (scope, value) in m.Entries.Select(e => (e.Key, (e.Value as YScalar)?.Value ?? "")))
                    if (value is not ("read" or "none") && !(value == "write" && CommentScopes.Contains(scope)))
                        add(file, $"{where} grants '{scope}: {value}'; a check workflow may only read, or write comments");
                break;
            default: add(file, $"{where} has permissions the check cannot read"); break;
        }
    }

    static void DeployWorkflow(string file, YMap root, List<(string Name, YMap Job)> jobs, YMap? verifyJob, Action<string, string> add)
    {
        foreach (var key in root.Keys.Where(k => !DeployTopKeys.Contains(k))) add(file, $"top-level key '{key}' is not allowed in a deployment workflow");
        var names = jobs.Select(j => j.Name).ToHashSet(StringComparer.Ordinal);
        var gateJobs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (name, job) in jobs)
        {
            foreach (var key in job.Keys.Where(k => !DeployJobKeys.Contains(k))) add(file, $"job '{name}': key '{key}' is not allowed in a deployment job (it could let the job run past a red gate or change how it runs)");
            if (job["if"] is { } condition && !PlainCondition().IsMatch(Unwrap(Text(condition))))
                add(file, $"job '{name}': its if: may only compare vars.*, github.ref, github.ref_name or github.event_name with a quoted literal; anything else (a status function such as always()) could run it past a red gate");
            var needs = Flatten(job["needs"]).ToList();
            foreach (var need in needs.Where(n => !names.Contains(n))) add(file, $"job '{name}' needs '{need}', which is not a job of this workflow");
            if (verifyJob is not null && Same(job["steps"], verifyJob["steps"]) && Same(job["runs-on"], verifyJob["runs-on"]))
            {
                foreach (var key in job.Keys.Where(k => !GateJobKeys.Contains(k))) add(file, $"gate job '{name}': key '{key}' is not allowed, the gate runs exactly as the verify check does");
                gateJobs.Add(name);
            }
        }
        foreach (var (name, job) in jobs.Where(j => !gateJobs.Contains(j.Name)))
        {
            if (!Flatten(job["needs"]).Any(gateJobs.Contains))
                add(file, $"job '{name}' must need a gate job: a job with the runs-on and steps of the verify job in verify.yml (C25)");
            var shipCondition = Unwrap(Text(job["if"]));
            if (!MainOnly().IsMatch(shipCondition) || shipCondition.Contains("||", StringComparison.Ordinal))
                add(file, $"job '{name}' must ship from main only: its if: must include github.ref == 'refs/heads/main', joined only by &&, so nothing ships that skipped the pull request's gate and ratchet (C25)");
            foreach (var (step, index) in Steps(job).Select((s, i) => (s, i)))
            {
                foreach (var key in step.Keys.Where(k => !ShipStepKeys.Contains(k))) add(file, $"job '{name}' step {index + 1}: key '{key}' is not allowed in a deployment");
                // Action inputs match without regard to case (Ref is ref), so the allow-list does too.
                if (step["with"] is YMap with)
                    foreach (var input in with.Keys.Where(k => !ShipInputs.Contains(k, StringComparer.OrdinalIgnoreCase)))
                        add(file, $"job '{name}' step {index + 1} gives the input '{input}'; a ship step may only give {string.Join(", ", ShipInputs)}, so it ships the commit the gate saw");
                else if (step["with"] is not null) add(file, $"job '{name}' step {index + 1} has a with: the check cannot read");
                if (step["run"] is { } run && AnyGit().IsMatch(Text(run))) add(file, $"job '{name}' step {index + 1} runs git; a deployment ships the commit the gate saw and does not touch the checkout");
                if (step["uses"] is { } uses && !ShipActions.Any(a => Text(uses).StartsWith(a, StringComparison.Ordinal))) add(file, $"job '{name}' step {index + 1} uses '{Text(uses)}'; a deployment uses only checkout, setup and artifact actions");
            }
        }
    }

    static bool Same(YNode? a, YNode? b) => (a, b) switch
    {
        (YScalar x, YScalar y) => x.Value == y.Value,
        (YSeq x, YSeq y) => x.Items.Count == y.Items.Count && x.Items.Zip(y.Items).All(p => Same(p.First, p.Second)),
        (YMap x, YMap y) => x.Entries.Count == y.Entries.Count && x.Entries.Zip(y.Entries).All(p => p.First.Key == p.Second.Key && Same(p.First.Value, p.Second.Value)),
        (null, null) => true,
        _ => false,
    };

    // "${{ x }}" and "x" mean the same in an if:.
    static string Unwrap(string condition)
    {
        var t = condition.Trim();
        return t.StartsWith("${{", StringComparison.Ordinal) && t.EndsWith("}}", StringComparison.Ordinal) ? t[3..^2] : t;
    }

    static IEnumerable<string> Triggers(YNode? on) => on switch
    {
        YScalar s => [s.Value],
        YSeq seq => seq.Items.OfType<YScalar>().Select(i => i.Value),
        YMap map => map.Keys,
        _ => [],
    };

    static IEnumerable<string> EnvKeys(YNode? node) => node switch
    {
        YMap map => map.Entries.SelectMany(e => e.Key == "env" && e.Value is YMap env ? env.Keys.Concat(EnvKeys(env)) : EnvKeys(e.Value)),
        YSeq seq => seq.Items.SelectMany(EnvKeys),
        _ => [],
    };

    static IEnumerable<YMap> Steps(YMap job) => (job["steps"] as YSeq)?.Items.Select(i => i as YMap ?? new YMap([new("?", i)])) ?? [];

    static string Text(YNode? node) => node is YScalar s ? s.Value : node is null ? "" : "<structure>";

    static IEnumerable<string> Flatten(YNode? node) => node switch
    {
        YScalar s when s.Value.Length > 0 => [s.Value],
        YSeq seq => seq.Items.SelectMany(Flatten),
        YMap map => map.Entries.SelectMany(e => Flatten(e.Value)),
        _ => [],
    };
}
