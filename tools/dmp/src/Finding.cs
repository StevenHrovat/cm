namespace Cm.Dmp;

/// <summary>One thing a rule objects to. <see cref="Path"/> is relative to the repository root, with forward slashes.</summary>
public sealed record Finding(string RuleId, string Path, string Message);

/// <summary>A rule of the dmp verify group: an id that DMP sections cite in "Enforced by:", a title, and the check itself.</summary>
public sealed record Rule(string Id, string Title, Func<RepoView, IEnumerable<Finding>> Check);
