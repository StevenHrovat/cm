using System.Text.Json;

namespace Cm.Gravity;

/// <summary>One text change of the model: <see cref="Find"/> must occur exactly once.</summary>
sealed record MutantEdit(string Find, string Replace);

/// <summary>
/// A named mutant (gravity.md, "How a section gets laws", step 4): a plausible bug in the model, the law it breaks, the edits that make
/// it, and a witness: a Bend <c>main</c> of type Bool over the model that is <c>False{}</c> on the real model and <c>True{}</c> on the
/// mutant, so the law is shown broken on a concrete input, not only its proof.
/// </summary>
sealed record Mutant(string Id, string Says, string Breaks, IReadOnlyList<MutantEdit> Edits, string Witness)
{
    static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static IReadOnlyList<Mutant> Load(string file) =>
        JsonSerializer.Deserialize<List<Mutant>>(File.ReadAllText(file), Options) ?? throw new InvalidDataException(file + " holds no mutants");

    /// <summary>The model with the edits applied, or why they do not apply.</summary>
    public string Apply(string model)
    {
        foreach (var edit in Edits)
        {
            var at = model.IndexOf(edit.Find, StringComparison.Ordinal);
            if (at < 0 || model.IndexOf(edit.Find, at + 1, StringComparison.Ordinal) >= 0)
                throw new InvalidDataException($"{Id}: an edit's text does not occur exactly once in model.bend: {edit.Find.Split('\n')[0]}");
            model = string.Concat(model.AsSpan(0, at), edit.Replace, model.AsSpan(at + edit.Find.Length));
        }
        return model;
    }

    public string WitnessFile() => "import Base\nimport ./model.bend as M\n\n" + Witness + "\n";
}

/// <summary><c>gravity check</c>: per section, the proofs with the kernel, then every mutant.</summary>
static class Check
{
    public static bool Section(Bend bend, string section, TextWriter output)
    {
        var name = Path.GetFileName(section);
        var proofs = bend.Run(section, "PROOF.bend", "--verdict");
        output.WriteLine($"{name}  proofs   {(proofs.AllProofsCheck ? "ALL PROOFS CHECK (kernel)" : "FAILED")}");
        if (!proofs.AllProofsCheck) output.WriteLine(Indent(proofs.Output));
        var ok = proofs.AllProofsCheck;

        var file = Path.Combine(section, "mutants.json");
        if (!File.Exists(file)) { output.WriteLine($"{name}  mutants  none named (a section with laws needs them)"); return false; }
        var model = File.ReadAllText(Path.Combine(section, "model.bend"));
        var real = Scratch(section, model);
        try
        {
            foreach (var mutant in Mutant.Load(file))
            {
                var why = Mutate(bend, section, model, real, mutant);
                ok &= why is null;
                output.WriteLine($"{name}  {mutant.Id,-7}  {(why is null ? "caught" : "NOT CAUGHT: " + why)}  ({mutant.Breaks}: {mutant.Says})");
            }
        }
        finally { Directory.Delete(real, recursive: true); }
        return ok;
    }

    /// <summary>Null when the mutant builds, breaks a proof, and its witness tells it from the real model; else why not.</summary>
    static string? Mutate(Bend bend, string section, string model, string real, Mutant mutant)
    {
        string changed;
        try { changed = mutant.Apply(model); }
        catch (InvalidDataException e) { return e.Message; }
        var dir = Scratch(section, changed);
        try
        {
            if (bend.Run(dir, "model.bend") is { Exit: not 0 } build) return "the mutant does not build: " + First(build.Output);
            if (bend.Run(dir, "PROOF.bend").AllProofsCheck) return "every proof still checks";
            File.WriteAllText(Path.Combine(dir, "witness.bend"), mutant.WitnessFile());
            File.WriteAllText(Path.Combine(real, "witness.bend"), mutant.WitnessFile());
            var onMutant = bend.Run(dir, "witness.bend").Output;
            var onModel = bend.Run(real, "witness.bend").Output;
            return onMutant == "True{}" && onModel == "False{}" ? null : $"the witness says {First(onModel)} on the model and {First(onMutant)} on the mutant";
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    /// <summary>A private copy of the section's Bend files with <paramref name="model"/> as its model.</summary>
    static string Scratch(string section, string model)
    {
        var dir = Directory.CreateTempSubdirectory("cm-gravity-").FullName;
        foreach (var f in Directory.GetFiles(section, "*.bend")) File.Copy(f, Path.Combine(dir, Path.GetFileName(f)));
        File.WriteAllText(Path.Combine(dir, "model.bend"), model);
        return dir;
    }

    static string First(string text) => text.Split('\n')[0];

    static string Indent(string text) => string.Join('\n', text.Split('\n').Take(20).Select(l => "    " + l));
}
