using System.Reflection;

namespace PingWatch.UnitTests;

public class ArchitectureTests
{
    [Fact]
    public void Domain_references_only_the_base_class_library()
    {
        var references = Assembly.Load("PingWatch.Domain")
            .GetReferencedAssemblies()
            .Select(a => a.Name!);

        references.Should().OnlyContain(name => name.StartsWith("System"));
    }
}