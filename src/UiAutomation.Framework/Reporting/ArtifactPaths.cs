namespace UiAutomation.Framework.Reporting;

/// <summary>
/// Resolves artifact locations with the Section 22.4 split: raw diagnostics are written to a
/// <em>restricted, private</em> directory; only policy-approved files are copied into the
/// publish directory. A final redaction pass cannot reliably sanitize a trace/screenshot, so
/// capture prevention and access control are the controls — not scrubbing.
/// </summary>
public sealed class ArtifactPaths
{
    private ArtifactPaths(string rawDirectory, string publishDirectory)
    {
        RawDirectory = rawDirectory;
        PublishDirectory = publishDirectory;
    }

    /// <summary>Private, unpublished raw-capture directory (screenshots, traces before policy approval).</summary>
    public string RawDirectory { get; }

    /// <summary>The published directory: only policy-approved artifacts are copied here.</summary>
    public string PublishDirectory { get; }

    public static ArtifactPaths For(string contentRoot, TestIdentity identity)
    {
        var caseSegment = TestIdentity.Sanitize(identity.CaseId);
        var executionSegment = TestIdentity.Sanitize(identity.ExecutionId);

        // Raw capture lives outside TestResults and is git-ignored; never published as-is.
        var raw = Path.Combine(contentRoot, "raw-artifacts", caseSegment, executionSegment);

        var publish = Path.Combine(
            contentRoot,
            "TestResults",
            TestIdentity.Sanitize(identity.Environment),
            TestIdentity.Sanitize(identity.Profile),
            caseSegment,
            executionSegment);

        return new ArtifactPaths(raw, publish);
    }

    public string RawFile(string fileName)
    {
        Directory.CreateDirectory(RawDirectory);
        return Path.Combine(RawDirectory, fileName);
    }

    public string PublishFile(string fileName)
    {
        Directory.CreateDirectory(PublishDirectory);
        return Path.Combine(PublishDirectory, fileName);
    }

    /// <summary>Promotes a policy-approved raw file into the publish directory (Section 22.4 rule 8).</summary>
    public void PromoteToPublish(string rawFilePath)
    {
        if (!File.Exists(rawFilePath))
        {
            return;
        }

        var target = PublishFile(Path.GetFileName(rawFilePath));
        File.Copy(rawFilePath, target, overwrite: true);
    }
}
