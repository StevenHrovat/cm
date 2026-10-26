using Xunit;

namespace Cm.Dmp.Tests;

/// <summary>Each rule is proved twice: green on the repository itself, red on a copy with one deliberate breach.</summary>
public sealed class RuleTests : IDisposable
{
    readonly string clone = Path.Combine(Path.GetTempPath(), "cm-dmp-" + Guid.NewGuid().ToString("N"));

    public RuleTests()
    {
        Copy(RepositoryRoot(), clone);
        SeedSampleScripts();
    }

    public void Dispose() => Directory.Delete(clone, recursive: true);

    static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CM.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("CM.slnx not found above the test binaries");
    }

    static void Copy(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from)) File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
        foreach (var dir in Directory.GetDirectories(from))
        {
            var name = Path.GetFileName(dir);
            if (name is ".git" or "bin" or "obj" or "TestResults") continue;
            Copy(dir, Path.Combine(to, name));
        }
    }

    // The script lint needs scripts to breach. The repository's own scripts are too few and too large to edit by string, so the clone gets three small
    // synthetic ones under tools/sample/ (never in the repository itself). freeze-replay-set.ps1 carries one registered unguarded write.
    const string SampleScript = "<#\n.SYNOPSIS\nSynthetic script for the lint tests.\n#>\n[CmdletBinding()]\nparam([switch]$Apply, [Parameter(Mandatory)][string]$ExpectedHost)\n$ErrorActionPreference = 'Stop'\n"
        + "# The guard first\nImport-Module (Join-Path $PSScriptRoot '..\\lib\\Guard.psm1') -Force -ErrorAction Stop\n"
        + "$null = Assert-Script -ScriptPath $PSCommandPath -Kind Changes -Applying:$Apply -ExpectedHost $ExpectedHost -Elevation Forbidden\nWrite-Host 'ok'\n";

    void SeedSampleScripts()
    {
        foreach (var name in new[] { "setup-node", "harden-node" }) Write($"tools/sample/{name}.ps1", SampleScript);
        Write("tools/sample/freeze-replay-set.ps1", SampleScript + "New-Item -ItemType Directory -Path $scratch | Out-Null\n");
        Edit("tools/lib/unguarded-writes.txt", t => t + "tools/sample/freeze-replay-set.ps1\t1\n");
    }

    void Write(string relative, string text)
    {
        var path = Path.Combine(clone, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    void Edit(string relative, Func<string, string> change) => Write(relative, change(File.ReadAllText(Path.Combine(clone, relative))));

    IReadOnlyList<Finding> Run(string id) => Rules.Run(new RepoView(clone), id);

    [Fact]
    public void The_repository_itself_is_compliant()
    {
        var repo = new RepoView(RepositoryRoot());
        foreach (var rule in Rules.All) Assert.Empty(rule.Check(repo));
    }

    [Fact]
    public void Every_rule_id_is_unique_and_numbered()
    {
        Assert.Equal(Rules.All.Count, Rules.Ids.Count);
        Assert.All(Rules.All, r => Assert.Matches(@"^DMP-\d{3}$", r.Id));
    }

    [Fact]
    public void Licence_must_be_apache()
    {
        Write("LICENSE", "All rights reserved.");
        Assert.NotEmpty(Run("DMP-001"));
    }

    [Fact]
    public void Charter_must_come_first()
    {
        Edit("CLAUDE.md", t => t.Replace("## Defensive security charter", "## Something else"));
        Assert.NotEmpty(Run("DMP-002"));
    }

    [Fact]
    public void Knowledge_rule_must_be_present()
    {
        Edit("CLAUDE.md", t => t.Replace("Knowledge goes back into Tier 1", "Optional"));
        Assert.NotEmpty(Run("DMP-002"));
    }

    [Fact]
    public void Knowledge_ask_first_rule_must_be_present()
    {
        Edit("CLAUDE.md", t => t.Replace("Ask the nearest tier first", "Optional"));
        Assert.NotEmpty(Run("DMP-002"));
    }

    [Fact]
    public void Combine_every_path_rule_must_be_present()
    {
        Edit("CLAUDE.md", t => t.Replace("Combine every path", "Optional"));
        Assert.NotEmpty(Run("DMP-002"));
    }

    [Fact]
    public void Look_it_up_rule_must_be_present()
    {
        Edit("CLAUDE.md", t => t.Replace("Look it up, don't guess", "Optional"));
        Assert.NotEmpty(Run("DMP-002"));
    }

    [Fact]
    public void Prove_forward_and_back_rule_must_be_present()
    {
        Edit("CLAUDE.md", t => t.Replace("Prove forward and back", "Optional"));
        Assert.NotEmpty(Run("DMP-002"));
    }

    [Fact]
    public void Thesis_rule_must_be_present()
    {
        Edit("CLAUDE.md", t => t.Replace("What is not fully trusted is ejected", "Optional"));
        Assert.NotEmpty(Run("DMP-002"));
    }

    [Fact]
    public void Deployment_rule_must_be_present()
    {
        Edit("CLAUDE.md", t => t.Replace("Every deployment obeys DMP", "Optional"));
        Assert.NotEmpty(Run("DMP-002"));
    }

    // A deployment in the shape DMP-020 accepts: a gate job identical to the verify job of verify.yml, then a ship job that needs it.
    string Deploy
    {
        get
        {
            var verify = File.ReadAllText(Path.Combine(clone, ".github/workflows/verify.yml"));
            return "name: ship\non: workflow_dispatch\npermissions:\n  contents: read\njobs:\n  gate:\n    runs-on: ubuntu-latest\n" + verify[verify.IndexOf("    steps:\n", StringComparison.Ordinal)..] +
                "  ship:\n    needs: gate\n    if: github.ref == 'refs/heads/main'\n    environment: stable\n    runs-on: ubuntu-latest\n    steps:\n      - uses: actions/checkout@v4\n      - run: ./ship.sh\n";
        }
    }

    void Register(string line) => Edit("DMP/workflows.txt", t => t + line + "\n");

    IReadOnlyList<Finding> RunWithBase(string id, Dictionary<string, string> baseFiles) =>
        Rules.Run(new RepoView(clone) { ReadBaseText = p => baseFiles.GetValueOrDefault(p) }, id);

    [Fact]
    public void An_unregistered_workflow_is_refused_whatever_its_name()
    {
        Write(".github/workflows/rollout.yml", Deploy);
        Assert.Contains(Run("DMP-020"), f => f.Path.EndsWith("rollout.yml", StringComparison.Ordinal) && f.Message.Contains("not registered", StringComparison.Ordinal));
    }

    [Fact]
    public void A_registered_deployment_with_a_gate_job_passes()
    {
        Write(".github/workflows/ship.yml", Deploy);
        Register("ship.yml: deploy - test");
        Assert.Empty(Run("DMP-020"));
    }

    [Fact]
    public void A_deployment_without_a_gate_job_is_refused()
    {
        Write(".github/workflows/ship.yml", Deploy.Replace("    needs: gate\n", ""));
        Register("ship.yml: deploy - test");
        Assert.Contains(Run("DMP-020"), f => f.Message.Contains("must need a gate job", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("tools/verify || true")]
    [InlineData("tools/verify; true")]
    [InlineData("tools/verify | tee log")]
    [InlineData("tools/verify &")]
    [InlineData("tools/verifyx")]
    [InlineData("|\n          ./ship.sh\n          tools/verify")]
    [InlineData("|\n          set +e\n          tools/verify")]
    public void A_softened_gate_command_is_not_a_gate(string command)
    {
        Write(".github/workflows/ship.yml", Deploy.Replace("        run: tools/verify\n", $"        run: {command}\n"));
        Register("ship.yml: deploy - test");
        Assert.NotEmpty(Run("DMP-020"));
    }

    [Theory]
    [InlineData("        run: tools/verify\n", "        run: tools/verify\n        continue-on-error: true\n")]
    [InlineData("        run: tools/verify\n", "        run: tools/verify\n        shell: bash {0} || true\n")]
    [InlineData("        run: tools/verify\n", "        run: tools/verify\n        if: false\n")]
    [InlineData("        run: tools/verify\n", "        run: tools/verify\n        env:\n          BASH_ENV: ./evil.sh\n")]
    [InlineData("  gate:\n    runs-on: ubuntu-latest\n", "  gate:\n    continue-on-error: true\n    runs-on: ubuntu-latest\n")]
    [InlineData("    needs: gate\n", "    needs: gate\n    if: always()\n")]
    [InlineData("    needs: gate\n", "    needs: gate\n    if: ${{ failure() || success() }}\n")]
    [InlineData("    needs: gate\n", "    needs: gate\n    if: Always()\n")]
    [InlineData("    needs: gate\n", "    needs: gate\n    if: ${{ ALWAYS() }}\n")]
    [InlineData("    needs: gate\n", "    needs: gate\n    if: !Cancelled()\n")]
    [InlineData("    needs: gate\n", "    needs: gate\n    if: vars.X == 'true' || always()\n")]
    [InlineData("    needs: gate\n", "    needs: [gate, nowhere]\n")]
    [InlineData("    if: github.ref == 'refs/heads/main'\n", "")]
    [InlineData("    if: github.ref == 'refs/heads/main'\n", "    if: vars.SHIP == 'true'\n")]
    [InlineData("    if: github.ref == 'refs/heads/main'\n", "    if: github.ref == 'refs/heads/main' || github.ref == 'refs/heads/other'\n")]
    [InlineData("permissions:\n", "defaults:\n  run:\n    shell: bash\npermissions:\n")]
    public void A_gate_that_can_be_bypassed_is_refused(string from, string to)
    {
        Write(".github/workflows/ship.yml", Deploy.Replace(from, to));
        Register("ship.yml: deploy - test");
        Assert.NotEmpty(Run("DMP-020"));
    }

    [Theory]
    [InlineData("      - uses: actions/checkout@v4\n      - uses: actions/setup-dotnet", "      - uses: actions/checkout@v4\n        with:\n          ref: other\n      - uses: actions/setup-dotnet")]
    [InlineData("      - run: ./ship.sh\n", "      - uses: actions/checkout@v4\n        with:\n          ref: other\n      - run: ./ship.sh\n")]
    [InlineData("      - run: ./ship.sh\n", "      - run: git fetch origin other && git checkout FETCH_HEAD && ./ship.sh\n")]
    [InlineData("      - run: ./ship.sh\n", "      - run: GIT fetch origin other && Git checkout FETCH_HEAD && ./ship.sh\n")]
    [InlineData("      - uses: actions/checkout@v4\n      - uses: actions/setup-dotnet", "      - uses: actions/checkout@v4\n        with: { ref: other }\n      - uses: actions/setup-dotnet")]
    [InlineData("      - run: ./ship.sh\n", "      - uses: actions/checkout@v4\n        with:\n          Ref: other\n      - run: ./ship.sh\n")]
    [InlineData("      - run: ./ship.sh\n", "      - uses: actions/checkout@v4\n        with:\n          REPOSITORY: someone/else\n      - run: ./ship.sh\n")]
    [InlineData("      - run: ./ship.sh\n", "      - uses: actions/checkout@v4\n        with:\n          token: other\n      - run: ./ship.sh\n")]
    [InlineData("      - run: ./ship.sh\n", "      - uses: actions/checkout@v4\n        with:\n          ssh-key: other\n      - run: ./ship.sh\n")]
    [InlineData("        run: tools/verify\n", "        run: tools/verify\n      - run: ./ship.sh\n")]
    public void A_deployment_must_ship_the_commit_the_gate_saw(string from, string to)
    {
        Write(".github/workflows/ship.yml", Deploy.Replace(from, to));
        Register("ship.yml: deploy - test");
        Assert.NotEmpty(Run("DMP-020"));
    }

    [Theory]
    [InlineData("  ship:\n    needs: gate\n    environment: stable\n", "  ship:\n    needs: gate\n    environment: stable\n")]
    [InlineData("    runs-on: ubuntu-latest\n    steps:\n      - uses: actions/checkout@v4\n      - run: ./ship.sh\n", "    runs-on: [self-hosted, windows]\n    steps:\n      - uses: actions/checkout@v4\n      - run: ./ship.sh\n")]
    [InlineData("permissions:\n  contents: read\n", "permissions:\n  contents: write\n")]
    [InlineData("permissions:\n  contents: read\n", "permissions:\n  contents: read\n  statuses: write\n")]
    [InlineData("permissions:\n  contents: read\n", "permissions:\n  contents: read\n  checks: write\n")]
    [InlineData("    runs-on: ubuntu-latest\n    steps:\n      - uses: actions/checkout@v4\n      - run: ./ship.sh\n", "    runs-on: ${{ vars.RUNNER }}\n    steps:\n      - uses: actions/checkout@v4\n      - run: ./ship.sh\n")]
    [InlineData("    runs-on: ubuntu-latest\n    steps:\n      - uses: actions/checkout@v4\n      - run: ./ship.sh\n", "    runs-on:\n      group: company\n    steps:\n      - uses: actions/checkout@v4\n      - run: ./ship.sh\n")]
    [InlineData("    runs-on: ubuntu-latest\n    steps:\n      - uses: actions/checkout@v4\n      - run: ./ship.sh\n", "    runs-on: [ubuntu-latest, company]\n    steps:\n      - uses: actions/checkout@v4\n      - run: ./ship.sh\n")]
    [InlineData("      - run: ./ship.sh\n", "      - run: ./ship.sh ${{ secrets.DEPLOY_KEY }}\n")]
    [InlineData("      - run: ./ship.sh\n", "      - run: ./ship.sh ${{ secrets['DEPLOY_KEY'] }}\n")]
    [InlineData("      - run: ./ship.sh\n", "      - run: echo '${{ toJSON(secrets) }}'\n")]
    [InlineData("      - run: ./ship.sh\n", "      - run: ./ship.sh ${{ SECRETS.DEPLOY_KEY }}\n")]
    [InlineData("      - run: ./ship.sh\n", "      - run: echo '${{ toJSON(Secrets) }}'\n")]
    public void A_check_workflow_cannot_ship(string from, string to)
    {
        Write(".github/workflows/ship.yml", Deploy.Replace(from, to).Replace("    environment: stable\n", from.Contains("environment", StringComparison.Ordinal) ? "    environment: stable\n" : ""));
        Register("ship.yml: check - claims to only check");
        Assert.NotEmpty(Run("DMP-020"));
    }

    [Fact]
    public void A_job_condition_on_a_repository_variable_is_allowed()
    {
        Write(".github/workflows/ship.yml", Deploy.Replace("    if: github.ref == 'refs/heads/main'\n", "    if: ${{ vars.SHIP_ENABLED == 'true' && github.ref == 'refs/heads/main' }}\n"));
        Register("ship.yml: deploy - test");
        Assert.Empty(Run("DMP-020"));
    }

    [Theory]
    [InlineData("        run: tools/verify\n", "        run: echo fine\n")]
    [InlineData("        run: tools/verify\n", "        run: tools/verify\n        continue-on-error: true\n")]
    [InlineData("    runs-on: ubuntu-latest\n    steps:\n", "    runs-on: ubuntu-latest\n    continue-on-error: true\n    steps:\n")]
    public void A_verify_job_that_stops_running_the_gate_is_refused(string from, string to)
    {
        // The gate is defined as the verify job, so weakening both together must still go red.
        Edit(".github/workflows/verify.yml", t => t.Replace(from, to));
        Write(".github/workflows/ship.yml", Deploy);
        Register("ship.yml: deploy - test");
        Assert.Contains(Run("DMP-020"), f => f.Path == ".github/workflows/verify.yml" && f.Message.Contains("plain step", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("on: workflow_dispatch\n", "on: pull_request_target\n", "deploy")]
    [InlineData("on: workflow_dispatch\n", "on: [push, workflow_run]\n", "deploy")]
    [InlineData("on: workflow_dispatch\n", "on:\n  pull_request_target:\n    types: [opened]\n", "check")]
    public void A_workflow_triggered_on_code_the_gate_did_not_judge_is_refused(string from, string to, string kind)
    {
        Write(".github/workflows/ship.yml", Deploy.Replace(from, to));
        Register($"ship.yml: {kind} - test");
        Assert.Contains(Run("DMP-020"), f => f.Message.Contains("did not judge", StringComparison.Ordinal));
    }

    [Fact]
    public void A_workflow_may_not_override_a_github_variable()
    {
        Write(".github/workflows/ship.yml", Deploy.Replace("    environment: stable\n", "    environment: stable\n    env:\n      GITHUB_BASE_REF: \"\"\n"));
        Register("ship.yml: deploy - test");
        Assert.Contains(Run("DMP-020"), f => f.Message.Contains("GITHUB_BASE_REF", StringComparison.Ordinal));
    }

    [Fact]
    public void A_ship_step_uses_only_checkout_setup_and_artifact_actions()
    {
        Write(".github/workflows/ship.yml", Deploy.Replace("      - run: ./ship.sh\n", "      - uses: someone/fetch-other-ref@v1\n"));
        Register("ship.yml: deploy - test");
        Assert.Contains(Run("DMP-020"), f => f.Message.Contains("someone/fetch-other-ref", StringComparison.Ordinal));
    }

    [Fact]
    public void A_workflow_the_reader_cannot_read_is_refused()
    {
        Write(".github/workflows/ship.yml", Deploy.Replace("jobs:\n", "x: &anchor 1\njobs:\n"));
        Register("ship.yml: deploy - test");
        Assert.Contains(Run("DMP-020"), f => f.Message.Contains("cannot be read", StringComparison.Ordinal));
    }

    [Fact]
    public void Route_gaps_may_not_exceed_the_cap()
    {
        Edit("DMP/routes.txt", t => t + "new action: gap - not yet\n");
        Assert.Contains(Run("DMP-020"), f => f.Path == "DMP/routes.txt" && f.Message.Contains("only shrink", StringComparison.Ordinal));
    }

    [Fact]
    public void The_routes_register_needs_its_cap()
    {
        Edit("DMP/routes.txt", t => t.Replace("cap = 0\n", ""));
        Assert.Contains(Run("DMP-020"), f => f.Message.Contains("cap = N", StringComparison.Ordinal));
    }

    [Fact]
    public void A_route_gap_needs_a_reason()
    {
        Write("DMP/routes.txt", "cap = 3\nschedules update: gap\n");
        Assert.Contains(Run("DMP-020"), f => f.Message.Contains("needs a reason", StringComparison.Ordinal));
    }

    [Fact]
    public void A_route_must_be_cli_or_console()
    {
        Edit("DMP/routes.txt", t => t + "users list: telepathy\n");
        Assert.Contains(Run("DMP-020"), f => f.Message.Contains("telepathy", StringComparison.Ordinal));
    }

    [Fact]
    public void The_ratchet_passes_against_an_unchanged_base()
    {
        var routes = File.ReadAllText(Path.Combine(clone, "DMP/routes.txt"));
        var workflows = File.ReadAllText(Path.Combine(clone, "DMP/workflows.txt"));
        Assert.Empty(RunWithBase("DMP-020", new() { ["DMP/routes.txt"] = routes, ["DMP/workflows.txt"] = workflows }));
    }

    [Theory]
    [InlineData("cap = 0", "cap = 1", "", "cap rose")]
    [InlineData("cap = 0", "cap = 1", "users list: gap - later\n", "new gap")]
    [InlineData("schedules update: gap", "schedules update: gap", "", "")]
    public void The_ratchet_refuses_a_raised_cap_or_a_new_gap(string from, string to, string append, string expected)
    {
        var baseRoutes = File.ReadAllText(Path.Combine(clone, "DMP/routes.txt"));
        Edit("DMP/routes.txt", t => t.Replace(from, to) + append);
        var findings = RunWithBase("DMP-020", new() { ["DMP/routes.txt"] = baseRoutes });
        if (expected.Length == 0) Assert.Empty(findings);
        else Assert.Contains(findings, f => f.Message.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void The_ratchet_refuses_a_removed_route()
    {
        var baseRoutes = File.ReadAllText(Path.Combine(clone, "DMP/routes.txt")) + "users list: cli, console\n";
        Edit("DMP/routes.txt", t => t + "users list: console\n");
        Assert.Contains(RunWithBase("DMP-020", new() { ["DMP/routes.txt"] = baseRoutes }), f => f.Message.Contains("lost its route 'cli'", StringComparison.Ordinal));
    }

    [Fact]
    public void The_ratchet_refuses_a_deploy_workflow_turned_check()
    {
        var baseWorkflows = File.ReadAllText(Path.Combine(clone, "DMP/workflows.txt")) + "mirror.yml: deploy - test\n";
        Edit("DMP/workflows.txt", t => t + "mirror.yml: check - test\n");
        Assert.Contains(RunWithBase("DMP-020", new() { ["DMP/workflows.txt"] = baseWorkflows }), f => f.Message.Contains("may not become check", StringComparison.Ordinal));
    }

    [Fact]
    public void A_git_error_on_the_base_is_not_an_absent_file()
    {
        var findings = Rules.Run(new RepoView(clone) { ReadBaseText = _ => throw new BaseUnreadable("test") }, "DMP-020");
        Assert.Contains(findings, f => f.Message.Contains("cannot be read", StringComparison.Ordinal));
    }

    [Fact]
    public void The_base_is_the_exact_commit_the_pull_request_event_names()
    {
        var repo = Path.Combine(Path.GetTempPath(), "dmp-base-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(repo, "DMP"));
        try
        {
            string Git(params string[] a)
            {
                var start = new System.Diagnostics.ProcessStartInfo("git") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
                foreach (var x in new[] { "-C", repo, "-c", "user.name=t", "-c", "user.email=t@t", "-c", "commit.gpgsign=false" }.Concat(a)) start.ArgumentList.Add(x);
                using var p = System.Diagnostics.Process.Start(start)!;
                var o = p.StandardOutput.ReadToEnd();
                p.StandardError.ReadToEnd();
                p.WaitForExit();
                return o.Trim();
            }
            Git("init", "-q");
            File.WriteAllText(Path.Combine(repo, "DMP/routes.txt"), "cap = 0\n");
            Git("add", "-A");
            Git("commit", "-q", "-m", "base");
            var sha = Git("rev-parse", "HEAD");
            var eventFile = Path.Combine(repo, "event.json");

            File.WriteAllText(eventFile, "{}");
            Assert.NotNull(RepoView.WithGitBase(repo, null, "main", eventFile).BaseError);
            File.WriteAllText(eventFile, $"{{\"pull_request\":{{\"base\":{{\"sha\":\"{new string('a', 40)}\"}}}}}}");
            Assert.NotNull(RepoView.WithGitBase(repo, null, "main", eventFile).BaseError);
            File.WriteAllText(eventFile, $"{{\"pull_request\":{{\"base\":{{\"sha\":\"{sha}\"}}}}}}");
            var view = RepoView.WithGitBase(repo, null, "main", eventFile);
            Assert.Null(view.BaseError);
            Assert.Equal("cap = 0\n", view.ReadBaseText!("DMP/routes.txt"));
            Assert.Null(view.ReadBaseText!("DMP/workflows.txt"));
            Assert.Null(RepoView.WithGitBase(repo, null, null, null).ReadBaseText);
        }
        finally
        {
            foreach (var f in Directory.EnumerateFiles(repo, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(repo, true);
        }
    }

    [Fact]
    public void An_unreadable_base_fails_closed()
    {
        var findings = Rules.Run(new RepoView(clone) { BaseError = "test" }, "DMP-020");
        Assert.Contains(findings, f => f.Message.Contains("cannot be read", StringComparison.Ordinal));
    }

    [Fact]
    public void Derivation_as_shipped_passes() => Assert.Empty(Run("DMP-021"));

    [Fact]
    public void A_theorem_citing_a_later_section_is_refused()
    {
        Edit("vendor/dmp/01-constitution.md", t => t.Replace("Derived from: Axiom 2, Axiom 1.", "Derived from: Axiom 2, Axiom 1, C22."));
        Assert.Contains(Run("DMP-021"), f => f.Message.Contains("'C22'", StringComparison.Ordinal));
    }

    [Fact]
    public void A_theorem_citing_itself_is_refused()
    {
        Edit("vendor/dmp/01-constitution.md", t => t.Replace("Derived from: Axiom 2, Axiom 1.", "Derived from: Axiom 2, T0."));
        Assert.Contains(Run("DMP-021"), f => f.Message.Contains("cites itself", StringComparison.Ordinal));
    }

    [Fact]
    public void A_theorem_without_a_derivation_is_refused()
    {
        Edit("vendor/dmp/01-constitution.md", t => t.Replace("Derived from: Axiom 2, Axiom 1.\n", ""));
        Assert.Contains(Run("DMP-021"), f => f.Message.Contains("T0", StringComparison.Ordinal));
    }

    [Fact]
    public void An_unlisted_axiom_is_refused()
    {
        Edit("vendor/dmp/01-constitution.md", t => t.Replace("## G0 · The ground", "## Axiom 5 · Smuggled\n\nStatus: stable since DMP 0.9.0\nOwner: CM coordinator\nEnforced by: none yet\n\n## G0 · The ground"));
        Assert.Contains(Run("DMP-021"), f => f.Message.Contains("axiom sections", StringComparison.Ordinal));
    }

    [Fact]
    public void Stopgap_as_shipped_passes() => Assert.Empty(Run("DMP-017"));

    [Fact]
    public void Section_after_the_stopgap_is_refused()
    {
        Edit("vendor/dmp/01-constitution.md", t => t + "\n## C99 · Appended below the stopgap\n\nStatus: stable since DMP 0.1.0\nOwner: CM coordinator\nEnforced by: none yet\n");
        Assert.NotEmpty(Run("DMP-017"));
    }

    [Fact]
    public void Stopgap_without_its_thesis_is_refused()
    {
        Edit("vendor/dmp/01-constitution.md", t => t.Replace("Everything that is not fully trusted is automatically ejected", "Something else"));
        Assert.NotEmpty(Run("DMP-017"));
    }

    [Fact]
    public void Version_on_a_package_reference_is_refused()
    {
        Edit("cm.io/src/cm.io.csproj", t => t.Replace("</Project>", "<ItemGroup><PackageReference Include=\"Some.Package\" Version=\"1.0.0\" /></ItemGroup></Project>"));
        Assert.Contains(Run("DMP-003"), f => f.Path == "cm.io/src/cm.io.csproj");
    }

    [Fact]
    public void Folder_outside_the_tree_is_refused()
    {
        Write("CM.Unlisted/readme.md", "x");
        Assert.Contains(Run("DMP-004"), f => f.Path == "CM.Unlisted");
    }

    [Fact]
    public void Node_without_tests_is_refused()
    {
        File.Delete(Path.Combine(clone, "CM.Pictor/tests/CM.Pictor.Tests.csproj"));
        Assert.NotEmpty(Run("DMP-004"));
    }

    [Fact]
    public void Namespace_outside_the_folder_is_refused()
    {
        Write("cm.io/src/Stray.cs", "namespace Elsewhere;\npublic static class Stray { }\n");
        Assert.Contains(Run("DMP-004"), f => f.Path == "cm.io/src/Stray.cs");
    }

    [Fact]
    public void Foreign_language_in_a_product_folder_is_refused()
    {
        Write("cm.os/src/helper.py", "print('x')\n");
        Assert.Contains(Run("DMP-005"), f => f.Path == "cm.os/src/helper.py");
    }

    [Fact]
    public void Other_target_framework_is_refused()
    {
        Edit("CM.Pictor/src/CM.Pictor.csproj", t => t.Replace("</Project>", "<PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>"));
        Assert.NotEmpty(Run("DMP-005"));
    }

    [Fact]
    public void A_secret_shape_is_refused()
    {
        Write("docs/oops.md", "-----BEGIN " + "RSA PRIVATE KEY-----\n");
        Assert.Contains(Run("DMP-006"), f => f.Path == "docs/oops.md");
    }

    [Fact]
    public void A_github_installation_token_is_refused_in_the_old_and_the_stateless_shape()
    {
        // Made-up values, split so this file does not trip the rule itself.
        Write("docs/old.md", "token gh" + "s_" + new string('a', 36) + "\n");
        Assert.Contains(Run("DMP-006"), f => f.Path == "docs/old.md");
        Write("docs/old.md", "nothing here\n");
        Write("docs/new.md", "token gh" + "s_" + "eyJhbGciOiJFUzI1NiJ9" + "." + new string('b', 300) + "." + new string('c', 80) + "\n");
        Assert.Contains(Run("DMP-006"), f => f.Path == "docs/new.md");
    }

    [Fact]
    public void An_address_a_mailbox_and_a_share_are_refused_but_placeholders_pass()
    {
        Write("docs/values.md", "host 10.20." + "30.40\nmail someone@" + "corp.test\nshare \\\\" + "fileserver\\" + "data\n");
        Assert.Equal(3, Run("DMP-006").Count);
        Write("docs/values.md", "host 192.0.2.10 and 127.0.0.1\nmail role@example.com\n");
        Assert.Empty(Run("DMP-006"));
    }

    [Fact]
    public void A_scrub_list_value_is_refused_and_never_echoed()
    {
        var list = Path.Combine(clone, "scrub.txt");
        File.WriteAllText(list, "Zebra" + "Corp\n");
        Write("docs/note.md", "we run on zebracorp systems\n");
        var findings = Rules.Run(new RepoView(clone, list), "DMP-006");
        Assert.Contains(findings, f => f.Path == "docs/note.md");
        Assert.DoesNotContain(findings, f => f.Message.Contains("Zebra", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Section_without_a_mark_is_refused()
    {
        Edit("vendor/dmp/10-code-quality.md", t => t + "\n## Unmarked\n\nText.\n");
        Assert.NotEmpty(Run("DMP-007"));
    }

    [Fact]
    public void Stable_section_citing_an_unknown_rule_is_refused()
    {
        Edit("vendor/dmp/10-code-quality.md", t => t.Replace("Enforced by: DMP-010", "Enforced by: DMP-999"));
        Assert.NotEmpty(Run("DMP-007"));
    }

    [Fact]
    public void Stable_section_without_an_owner_is_refused()
    {
        Edit("vendor/dmp/14-process.md", t => t.Replace("Owner: CM coordinator\nEnforced by: none yet\n\n## Every pull request", "Enforced by: none yet\n\n## Every pull request"));
        Assert.NotEmpty(Run("DMP-007"));
    }

    [Fact]
    public void Draft_section_in_the_constitution_is_refused()
    {
        Edit("vendor/dmp/01-constitution.md", t => t.Replace("Status: stable since DMP 0.1.0", "Status: draft"));
        Assert.NotEmpty(Run("DMP-007"));
    }

    [Fact]
    public void Constellation_referencing_a_constellation_is_refused()
    {
        Edit("CM.Pictor/src/CM.Pictor.csproj", t => t.Replace("</Project>", "<ItemGroup><ProjectReference Include=\"../../CM.MilkyWay/src/CM.MilkyWay.csproj\" /></ItemGroup></Project>"));
        Assert.Contains(Run("DMP-008"), f => f.Path == "CM.Pictor/src/CM.Pictor.csproj");
    }

    [Fact]
    public void Brick_referencing_os_is_refused()
    {
        Edit("cm.io/src/cm.io.csproj", t => t.Replace("</Project>", "<ItemGroup><ProjectReference Include=\"../../cm.os/src/cm.os.csproj\" /></ItemGroup></Project>"));
        Assert.NotEmpty(Run("DMP-008"));
    }

    [Fact]
    public void Changed_authority_grant_is_refused()
    {
        Edit("vendor/dmp/01-constitution.md", t => t.Replace("on their own authority", "on anyone's authority"));
        Assert.NotEmpty(Run("DMP-009"));
    }

    [Fact]
    public void Grant_hash_ignores_layout_and_typographic_quotes()
    {
        Assert.Equal(Rules.GrantHash("> It’s  here\n> now"), Rules.GrantHash("It's here now"));
    }

    [Fact]
    public void Wall_clock_in_a_test_is_refused()
    {
        Write("cm.io/tests/Clocky.cs", "namespace cm.io.Tests;\npublic class Clocky { public object T() => " + "DateTime" + ".UtcNow; }\n");
        Assert.Contains(Run("DMP-010"), f => f.Path == "cm.io/tests/Clocky.cs");
    }

    [Fact]
    public void Script_without_the_guard_is_refused()
    {
        Edit("tools/sample/setup-node.ps1", t => t.Replace("Import-Module (Join-Path $PSScriptRoot '..\\lib\\Guard.psm1') -Force -ErrorAction Stop", ""));
        Assert.Contains(Run("DMP-011"), f => f.Path == "tools/sample/setup-node.ps1");
    }

    [Fact]
    public void Script_with_a_literal_host_name_is_refused()
    {
        Edit("tools/sample/setup-node.ps1", t => t.Replace("-ExpectedHost $ExpectedHost", "-ExpectedHost 'SOME-HOST'"));
        Assert.Contains(Run("DMP-011"), f => f.Path == "tools/sample/setup-node.ps1");
    }

    [Fact]
    public void Script_that_changes_something_without_a_dry_run_flag_is_refused()
    {
        Edit("tools/sample/harden-node.ps1", t => t.Replace(" -Applying:$Apply", ""));
        Assert.Contains(Run("DMP-011"), f => f.Path == "tools/sample/harden-node.ps1");
    }

    [Fact]
    public void Read_only_script_that_changes_something_is_refused()
    {
        Edit("tools/sample/harden-node.ps1", t => t.Replace("-Kind Changes", "-Kind ReadOnly") + "\nSet-ADUser -Identity x -Enabled $false\n");
        Assert.Contains(Run("DMP-011"), f => f.Path == "tools/sample/harden-node.ps1" && f.Message.Contains("Set-ADUser"));
    }

    [Fact]
    public void Install_outside_the_apply_switch_is_refused()
    {
        Edit("tools/sample/harden-node.ps1", t => t + "\nwinget upgrade --id Git.Git -e\n");
        var found = Run("DMP-011").Where(f => f.Path == "tools/sample/harden-node.ps1").ToList();
        Assert.Contains(found, f => f.Message.Contains("install or upgrade"));
        Assert.Contains(found, f => f.Message.Contains("Assert-ToolWorks"));
    }

    [Fact]
    public void Quotes_inside_a_subexpression_of_a_string_do_not_hide_a_command()
    {
        Edit("tools/sample/harden-node.ps1", t => t + "\n$r = Invoke-Thing \"where x = '$($role.Replace(\"'\", \"''\"))'\"\nwinget upgrade --id Git.Git -e\n");
        Assert.Contains(Run("DMP-011"), f => f.Path == "tools/sample/harden-node.ps1" && f.Message.Contains("install or upgrade"));
    }

    [Fact]
    public void Statement_before_the_guard_is_refused()
    {
        Edit("tools/sample/freeze-replay-set.ps1", t => t.Replace("# The guard first", "Write-Host 'hello'\n# The guard first"));
        Assert.Contains(Run("DMP-011"), f => f.Path == "tools/sample/freeze-replay-set.ps1");
    }

    [Fact]
    public void Localized_group_name_in_a_script_is_refused()
    {
        Edit("tools/sample/harden-node.ps1", t => t + "\nAdd-LocalGroupMember -Group 'Administrators' -Member x\n");
        Assert.Contains(Run("DMP-011"), f => f.Path == "tools/sample/harden-node.ps1");
    }

    [Fact]
    public void Principal_name_with_a_lint_ok_marker_is_accepted()
    {
        Edit("tools/sample/harden-node.ps1", t => t + "\n$label = 'Administrators'   # lint-ok: principal-name a label, the SID is used\n");
        Assert.Empty(Run("DMP-011"));
    }

    [Fact]
    public void New_script_is_not_exempt_by_default()
    {
        Write("tools/sample/new-thing.ps1", "param([switch]$Apply)\nWrite-Host 'x'\n");
        Assert.Contains(Run("DMP-011"), f => f.Path == "tools/sample/new-thing.ps1");
    }

    const string GoodShapeScript = "[CmdletBinding()]\nparam([switch]$Apply, [Parameter(Mandatory)][string]$ExpectedHost)\n$ErrorActionPreference = 'Stop'\nImport-Module (Join-Path $PSScriptRoot '..\\..\\tools\\lib\\Guard.psm1') -Force -ErrorAction Stop\n$null = Assert-Script -ScriptPath $PSCommandPath -Kind Changes -Applying:$Apply -ExpectedHost $ExpectedHost -Elevation Forbidden\nWrite-Host 'ok'\n";

    [Fact]
    public void Shape_script_without_the_guard_is_refused()
    {
        Write("shapes/new-shape/deploy.ps1", "param([switch]$Apply)\nWrite-Host 'x'\n");
        Assert.Contains(Run("DMP-011"), f => f.Path == "shapes/new-shape/deploy.ps1");
    }

    [Fact]
    public void Shape_script_with_the_guard_at_the_shape_path_is_accepted()
    {
        Write("shapes/new-shape/deploy.ps1", GoodShapeScript);
        Assert.DoesNotContain(Run("DMP-011"), f => f.Path == "shapes/new-shape/deploy.ps1");
        Assert.DoesNotContain(Run("DMP-012"), f => f.Path == "shapes/new-shape/deploy.ps1");
    }

    [Fact]
    public void Shape_script_importing_the_guard_by_the_tools_path_is_refused()
    {
        Write("shapes/new-shape/deploy.ps1", GoodShapeScript.Replace("..\\..\\tools\\lib\\", "..\\lib\\"));
        Assert.Contains(Run("DMP-011"), f => f.Path == "shapes/new-shape/deploy.ps1" && f.Message.Contains("expected"));
    }

    [Fact]
    public void Shape_script_with_an_unguarded_write_is_refused()
    {
        Write("shapes/new-shape/deploy.ps1", GoodShapeScript + "Copy-Item a b\n");
        Assert.Contains(Run("DMP-012"), f => f.Path == "shapes/new-shape/deploy.ps1");
    }

    [Fact]
    public void Shape_script_with_a_localized_group_name_is_refused()
    {
        Write("shapes/new-shape/deploy.ps1", GoodShapeScript + "$g = 'Administrators'\n");
        Assert.Contains(Run("DMP-011"), f => f.Path == "shapes/new-shape/deploy.ps1" && f.Message.Contains("German Windows"));
    }

    [Fact]
    public void Only_the_claude_code_config_shape_folder_is_skipped_by_the_script_lint()
    {
        // The skipped folder: a script without the guard there is not scanned (this is what lets the copied configuration scripts stay as they are).
        Write("shapes/claude-code-config/extra.ps1", "param([switch]$Apply)\nWrite-Host 'x'\n");
        Assert.DoesNotContain(Run("DMP-011"), f => f.Path == "shapes/claude-code-config/extra.ps1");
        Assert.DoesNotContain(Run("DMP-012"), f => f.Path == "shapes/claude-code-config/extra.ps1");
        // Nothing else is skipped: a sibling folder, a folder whose name only starts the same, and a nested folder of another shape are all scanned.
        foreach (var path in new[] { "shapes/claude-code-config2/x.ps1", "shapes/claude-code-configuration/x.ps1", "shapes/other/claude-code-config/x.ps1", "shapes/other-shape/x.ps1" })
        {
            Write(path, "param([switch]$Apply)\nWrite-Host 'x'\n");
            Assert.Contains(Run("DMP-011"), f => f.Path == path);
        }
    }

    [Fact]
    public void Stale_exemption_is_refused()
    {
        Edit("tools/lib/script-exemptions.txt", t => t + "tools/sample/setup-node.ps1\tcalls the guard already, so this line is stale\n");
        Assert.NotEmpty(Run("DMP-011"));
    }

    const string GuardedScript = "tools/sample/freeze-replay-set.ps1";

    const string GoodGuardedWrite = "\nInvoke-GuardedWrite -What 'Disable service S' `\n    -Read   { (Get-Service -Name S).StartType } `\n    -Expect { param($s) $s -eq 'Automatic' } `\n    -Verify { param($s) $s -eq 'Disabled' } `\n    -DependentSteps @() `\n    -Write  { param($s) Set-Service -Name S -StartupType Disabled } `\n    -Apply:$Apply\n";

    [Fact]
    public void New_unguarded_write_is_refused()
    {
        Edit(GuardedScript, t => t + "\nSet-Service -Name S -StartupType Disabled\n");
        Assert.Contains(Run("DMP-012"), f => f.Path == GuardedScript);
    }

    [Fact]
    public void Write_inside_a_guarded_write_is_accepted()
    {
        Edit(GuardedScript, t => t + GoodGuardedWrite);
        Assert.Empty(Run("DMP-012"));
    }

    [Fact]
    public void Unguarded_write_with_a_lint_ok_marker_is_accepted()
    {
        Edit(GuardedScript, t => t + "\nNew-Item -ItemType Directory -Path $scratch   # lint-ok: unguarded the script's own scratch folder\n");
        Assert.Empty(Run("DMP-012"));
    }

    [Fact]
    public void Guarded_write_without_a_read_back_is_refused()
    {
        Edit(GuardedScript, t => t + GoodGuardedWrite.Replace("    -Verify { param($s) $s -eq 'Disabled' } `\n", ""));
        Assert.Contains(Run("DMP-012"), f => f.Path == GuardedScript && f.Message.Contains("-Verify or -Done"));
    }

    [Fact]
    public void Guarded_write_with_a_cached_read_is_refused()
    {
        Edit(GuardedScript, t => t + GoodGuardedWrite.Replace("(Get-Service -Name S).StartType", "$startType"));
        Assert.Contains(Run("DMP-012"), f => f.Path == GuardedScript && f.Message.Contains("live"));
    }

    [Fact]
    public void Tenant_connection_without_a_tenant_assert_is_refused()
    {
        Edit(GuardedScript, t => t + "\nConnect-MgGraph -Scopes 'Device.Read.All'\n");
        Assert.Contains(Run("DMP-012"), f => f.Path == GuardedScript && f.Message.Contains("Assert-Tenant"));
        Edit(GuardedScript, t => t + "$null = Assert-Tenant -ExpectedTenantId $TenantId -Service Graph\n");
        Assert.Empty(Run("DMP-012"));
    }

    [Fact]
    public void Unguarded_write_ratchet_only_goes_down()
    {
        Edit("tools/lib/unguarded-writes.txt", t => t.Replace($"{GuardedScript}\t1", $"{GuardedScript}\t2"));
        Assert.Contains(Run("DMP-012"), f => f.Path == "tools/lib/unguarded-writes.txt");
    }

    const string DataRegisterFile = "DMP/data-features.txt";
    const string FromTemplate = "<!doctype html><title>x</title><style>/* Map template of the Canis Major suite (templates/map/README.md). */</style>";

    // A synthetic feature that reaches company data (a data brick of cm.io used outside cm.io), inside a node that exists in this repository.
    const string DataCode = "CM.Pictor/src/Datafeed";
    const string DataDocs = "CM.Pictor/docs/data/example";

    void AddDataCode() => Write(DataCode + "/Datafeed.cs", "using cm.io.Database;\nnamespace CM.Pictor;\n");

    void MoveBillingToGeneral()
    {
        AddDataCode();
        Edit(DataRegisterFile, t => t + $"example-feature\tgeneral\t{DataCode}\t{DataDocs}/handling.html\t{DataDocs}/grb-report.html\n");
    }

    [Fact]
    public void Code_that_reaches_company_data_must_be_registered()
    {
        AddDataCode();
        Assert.Contains(Run("DMP-013"), f => f.Path.StartsWith(DataCode, StringComparison.Ordinal));
    }

    [Fact]
    public void General_testing_without_the_document_pair_is_refused()
    {
        MoveBillingToGeneral();
        Assert.Contains(Run("DMP-013"), f => f.Message.Contains("handling document is missing"));
    }

    [Fact]
    public void General_testing_with_the_document_pair_is_accepted()
    {
        MoveBillingToGeneral();
        Write(DataDocs + "/handling.html", FromTemplate);
        Write(DataDocs + "/grb-report.html", FromTemplate + "<p>Scope file: GRB-0001-sandbox-gate.md</p>");
        Assert.Empty(Run("DMP-013"));
    }

    [Fact]
    public void Document_not_built_from_the_template_is_refused()
    {
        MoveBillingToGeneral();
        Write(DataDocs + "/handling.html", "<!doctype html><title>own design</title>");
        Write(DataDocs + "/grb-report.html", FromTemplate + "<p>Scope file: GRB-0001-sandbox-gate.md</p>");
        Assert.Contains(Run("DMP-013"), f => f.Path == DataDocs + "/handling.html");
    }

    [Fact]
    public void Grb_report_without_a_scope_file_is_refused()
    {
        MoveBillingToGeneral();
        Write(DataDocs + "/handling.html", FromTemplate);
        Write(DataDocs + "/grb-report.html", FromTemplate);
        Assert.Contains(Run("DMP-013"), f => f.Message.Contains("scope file"));
    }

    [Fact]
    public void Feature_in_another_repository_is_not_checked_for_paths_here()
    {
        Edit(DataRegisterFile, t => t + "some-feature\tit\tother-repo#1,store:knowledge/x\t-\t-\n");
        Assert.Empty(Run("DMP-013"));
        Edit(DataRegisterFile, t => t + "local-feature\tit\tCM.Nowhere\t-\t-\n");
        Assert.Contains(Run("DMP-013"), f => f.Message.Contains("CM.Nowhere"));
    }

    void LockImVersionOne()
    {
        Write("cm.os/tests/Im/corpus/v1/heartbeat.bin", "IMM1 heartbeat");
        var hash = Rules.CorpusHash(new RepoView(clone), "cm.os/tests/Im/corpus/v1");
        Edit("DMP/im-lock.txt", t => t + $"1\towner in the operations master project, 2026-12-01, first productive system\tcm.os/tests/Im/corpus/v1\t{hash}\n");
    }

    [Fact]
    public void Locked_im_version_with_its_corpus_and_replay_test_is_accepted()
    {
        LockImVersionOne();
        Write("cm.os/tests/Im/ImLockReplayTests.cs", "namespace cm.os.Tests.Im;\n");
        Assert.Empty(Run("DMP-015"));
    }

    [Fact]
    public void Locked_im_corpus_that_changed_is_refused()
    {
        LockImVersionOne();
        Write("cm.os/tests/Im/ImLockReplayTests.cs", "namespace cm.os.Tests.Im;\n");
        Write("cm.os/tests/Im/corpus/v1/heartbeat.bin", "IMM1 heartbeat, edited to fit new code");
        Assert.Contains(Run("DMP-015"), f => f.Path == "cm.os/tests/Im/corpus/v1");
    }

    [Fact]
    public void Locked_im_version_without_the_replay_test_is_refused()
    {
        LockImVersionOne();
        Assert.Contains(Run("DMP-015"), f => f.Path == "cm.os/tests/Im/ImLockReplayTests.cs");
    }

    [Fact]
    public void Warehouse_register_as_shipped_passes()
    {
        Assert.Empty(Run("DMP-016"));
    }

    [Fact]
    public void Warehouse_that_uses_im_while_registered_as_not_serving_is_refused()
    {
        Edit("DMP/warehouses.txt", t => t + "Sample\tCM.Pictor\tno\n");
        Write("CM.Pictor/src/Serve.cs", "using Im;\nnamespace CM.Pictor;\n");
        Assert.Contains(Run("DMP-016"), f => f.Path == "CM.Pictor/src/Serve.cs");
    }

    [Fact]
    public void Warehouse_that_serves_im_needs_its_dual_signature_test()
    {
        Edit("DMP/warehouses.txt", t => t + "Sample\tCM.Pictor\tyes\n");
        Assert.Contains(Run("DMP-016"), f => f.Path == "CM.Pictor/tests/Im/DualSignatureTests.cs");
        Write("CM.Pictor/tests/Im/DualSignatureTests.cs", "namespace CM.Pictor.Tests.Im;\n");
        Assert.Empty(Run("DMP-016"));
    }

    [Fact]
    public void Warehouse_section_missing_from_the_im_spec_is_refused()
    {
        Edit("vendor/dmp/04-im.md", t => t.Replace("## Serving at a warehouse", "## Serving somewhere"));
        Assert.Contains(Run("DMP-016"), f => f.Path == "vendor/dmp/04-im.md");
    }

    const string C2Laws = "CM.BlackHole/gravity/C02/LAWS.bend";

    [Fact]
    public void Locked_laws_as_shipped_pass()
    {
        Assert.Empty(Run("DMP-018"));
    }

    [Fact]
    public void Laws_file_changed_without_the_constitution_is_refused()
    {
        Edit(C2Laws, t => t.Replace("law no_replay:", "law no_replay_relaxed:"));
        Assert.Contains(Run("DMP-018"), f => f.Path == C2Laws && f.Message.Contains("sha256"));
    }

    [Fact]
    public void Law_sentence_that_drifts_from_its_quote_is_refused()
    {
        Edit("vendor/dmp/01-constitution.md", t => t.Replace("is verified at most once", "is verified at most twice"));
        Assert.Contains(Run("DMP-018"), f => f.Path == C2Laws && f.Message.Contains("C2.L3 is quoted differently"));
    }

    [Fact]
    public void Law_quoted_in_the_file_but_missing_from_the_section_is_refused()
    {
        Edit("vendor/dmp/01-constitution.md", t => t.Replace("- C2.L4 The permission table.", "C2.L4 The permission table."));
        Assert.Contains(Run("DMP-018"), f => f.Message.Contains("quotes law C2.L4"));
    }

    [Fact]
    public void Law_block_without_its_lock_is_refused()
    {
        Edit("vendor/dmp/01-constitution.md", t => t.Replace("Laws file: CM.BlackHole/gravity/C02/LAWS.bend sha256", "Laws at: CM.BlackHole/gravity/C02/LAWS.bend sha256"));
        Assert.Contains(Run("DMP-018"), f => f.Path == "vendor/dmp/01-constitution.md" && f.Message.Contains("no 'Laws file:"));
    }

    [Fact]
    public void Section_with_laws_must_be_enforced_by_the_law_rule()
    {
        Edit("vendor/dmp/01-constitution.md", t => t.Replace("Owner: Milky Way coordinator\nEnforced by: DMP-018", "Owner: Milky Way coordinator\nEnforced by: DMP-007"));
        Assert.Contains(Run("DMP-018"), f => f.Message.Contains("must cite DMP-018"));
    }

    [Fact]
    public void Laws_folder_without_its_proof_is_refused()
    {
        File.Delete(Path.Combine(clone, "CM.BlackHole/gravity/C02/PROOF.bend"));
        Assert.Contains(Run("DMP-018"), f => f.Path == "CM.BlackHole/gravity/C02/PROOF.bend");
    }

    [Fact]
    public void Verify_that_stops_proving_the_laws_is_refused()
    {
        Edit("tools/verify", t => t.Replace("tools/gravity", "tools/nothing"));
        Assert.Contains(Run("DMP-018"), f => f.Path == "tools/verify");
    }

    [Fact]
    public void Level_one_node_without_approval_is_refused()
    {
        Edit("DMP/tree.txt", t => t + "CM.Newthing\n");
        Assert.Contains(Run("DMP-014"), f => f.Path == "DMP/tree-approvals.txt" && f.Message.Contains("CM.Newthing"));
        Edit("DMP/tree-approvals.txt", t => t + "CM.Newthing\tThe Administrator in the operations master project, 2026-10-25 09:00Z; LHC pending\n");
        Assert.DoesNotContain(Run("DMP-014"), f => f.Message.Contains("CM.Newthing"));
    }
    [Fact]
    public void Level_one_approval_without_lhc_is_refused()
    {
        Edit("DMP/tree.txt", t => t + "CM.Newthing\n");
        Edit("DMP/tree-approvals.txt", t => t + "CM.Newthing\tThe Administrator in the operations master project, 2026-10-25 09:00Z\n");
        Assert.Contains(Run("DMP-014"), f => f.Message.Contains("LHC"));
    }
    [Fact]
    public void Protocol_library_outside_cm_io_is_refused()
    {
        Edit("CM.Pictor/src/CM.Pictor.csproj", t => t.Replace("</Project>", "<ItemGroup><PackageReference Include=\"SSH.NET\" /></ItemGroup></Project>"));
        Assert.Contains(Run("DMP-014"), f => f.Path == "CM.Pictor/src/CM.Pictor.csproj");
    }
    [Fact]
    public void Stale_protocol_exception_is_refused()
    {
        const string Project = "CM.Pictor/src/CM.Pictor.csproj";
        const string Reference = "<ItemGroup><PackageReference Include=\"Npgsql\" /></ItemGroup>";
        Edit(Project, t => t.Replace("</Project>", Reference + "</Project>"));
        Edit("DMP/protocol-exceptions.txt", t => t + Project + "\tNpgsql\tmoves into its brick when the database brick exists\n");
        Assert.DoesNotContain(Run("DMP-014"), f => f.Path == "DMP/protocol-exceptions.txt");
        Edit(Project, t => t.Replace(Reference, ""));
        Assert.Contains(Run("DMP-014"), f => f.Path == "DMP/protocol-exceptions.txt");
    }
    [Fact]
    public void Foundation_part_without_a_scope_is_refused()
    {
        Write("cm.io/src/Ssh/SshClient.cs", "namespace cm.io.Ssh;\npublic static class SshClient { }\n");
        Assert.Contains(Run("DMP-014"), f => f.Path == "cm.io/src/Ssh");
        Write("cm.io/src/Ssh/README.md", "# cm.io.Ssh\n\nScope: SSH, the whole protocol.\n");
        Assert.Empty(Run("DMP-014"));
    }
}
