namespace PokeTokenBar.Tests;

internal sealed class TemporaryDirectory : IDisposable
{
    private static readonly string TestRoot = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(),
        "PokeTokenBar.Tests");

    public TemporaryDirectory(bool create = true)
    {
        Path = System.IO.Path.Combine(TestRoot, Guid.NewGuid().ToString("N"));
        if (create)
        {
            Directory.CreateDirectory(Path);
        }
    }

    public string Path { get; }

    public void Dispose()
    {
        var resolvedPath = System.IO.Path.GetFullPath(Path);
        var resolvedRoot = System.IO.Path.GetFullPath(TestRoot) + System.IO.Path.DirectorySeparatorChar;
        if (!resolvedPath.StartsWith(resolvedRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Refusing to delete a directory outside the test root.");
        }

        if (Directory.Exists(resolvedPath))
        {
            Directory.Delete(resolvedPath, recursive: true);
        }
    }
}
