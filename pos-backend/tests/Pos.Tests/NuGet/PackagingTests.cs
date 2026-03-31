using System.Diagnostics;

namespace Pos.Tests.NuGet;

public sealed class PackagingTests
{
    private static readonly string[] ExpectedPackages =
    [
        "Pos.SharedKernel",
        "Pos.Infrastructure.Postgres",
        "Pos.Orders.Contracts",
        "Pos.Orders.Core",
        "Pos.Payments.Contracts",
        "Pos.Payments.Core",
        "Pos.Products.Contracts",
        "Pos.Products.Core"
    ];

    [Fact]
    public void DotnetPack_ShouldGenerate8Packages()
    {
        var solutionDir = FindSolutionDirectory();
        var outputDir = Path.Combine(solutionDir, "artifacts", "nupkg-test");

        if (Directory.Exists(outputDir))
            Directory.Delete(outputDir, recursive: true);

        var process = Process.Start(new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"pack -c Release -o \"{outputDir}\" -p:Version=0.0.1-test",
            WorkingDirectory = solutionDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        });

        process.Should().NotBeNull();
        process!.WaitForExit(TimeSpan.FromMinutes(2));
        process.ExitCode.Should().Be(0, "dotnet pack should succeed");

        var nupkgFiles = Directory.GetFiles(outputDir, "*.nupkg");
        nupkgFiles.Should().HaveCount(ExpectedPackages.Length,
            $"should generate exactly {ExpectedPackages.Length} packages");

        foreach (var expected in ExpectedPackages)
        {
            nupkgFiles.Should().Contain(f => Path.GetFileName(f).StartsWith(expected),
                $"package {expected} should be generated");
        }

        // Cleanup
        Directory.Delete(outputDir, recursive: true);
    }

    private static string FindSolutionDirectory()
    {
        var dir = Directory.GetCurrentDirectory();
        while (dir is not null && !File.Exists(Path.Combine(dir, "pos-backend.sln")))
        {
            dir = Directory.GetParent(dir)?.FullName;
        }
        return dir ?? throw new InvalidOperationException("Could not find solution directory");
    }
}
