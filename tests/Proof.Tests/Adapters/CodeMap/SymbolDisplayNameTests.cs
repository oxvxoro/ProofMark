using CodeMap.Core.Models;
using Proof.Adapters.CodeMap;
using Proof.Core;

namespace Proof.Tests;

public sealed class SymbolDisplayNameTests
{
    [Theory]
    [InlineData(NodeKind.Method, "Cancel", "App.OrderService.Cancel", "Cancel(string)", "App.OrderService.Cancel(string)")]
    [InlineData(NodeKind.Method, "Map", "App.Mapper.Map", "Map<T>(T)", "App.Mapper.Map<T>(T)")]
    [InlineData(NodeKind.Constructor, ".ctor", "App.OrderService::.ctor", ".ctor(BillingService)", "App.OrderService::.ctor(BillingService)")]
    [InlineData(NodeKind.Class, "OrderService", "App.OrderService", null, "App.OrderService")]
    [InlineData(NodeKind.Field, "_billing", "App.OrderService._billing", "_billing", "App.OrderService._billing")]
    public void For_DoesNotRepeatTheMemberName(NodeKind kind, string name, string qualifiedName, string? signature, string expected)
    {
        var symbol = new IndexedSymbol("id", "App", "file", "App/OrderService.cs", kind, name, qualifiedName, signature, 1, 2, "public", "csharp");

        Assert.Equal(expected, SymbolDisplayName.For(symbol));
    }

    [Fact]
    public void For_KeepsCodeMapDisplayName_WhenSignatureDoesNotStartWithTheName()
    {
        var symbol = new IndexedSymbol("id", "App", "file", "App/OrderService.cs", NodeKind.Method, "Cancel", "App.OrderService.Cancel", "(string)", 1, 2, "public", "csharp");

        Assert.Equal(symbol.DisplayName, SymbolDisplayName.For(symbol));
    }

    [Fact]
    public void For_TrimmedDisplayName_MatchesTheTestCaseFullyQualifiedName()
    {
        var symbol = new IndexedSymbol(
            "sym://M:SimpleService.Tests.OrderServiceTests.Cancel_RefundsPayment", "Tests", "file", "Tests/OrderServiceTests.cs",
            NodeKind.Method, "Cancel_RefundsPayment", "SimpleService.Tests.OrderServiceTests.Cancel_RefundsPayment",
            "Cancel_RefundsPayment()", 7, 12, "public", "csharp");

        Assert.Equal(
            "SimpleService.Tests.OrderServiceTests.Cancel_RefundsPayment",
            SubjectIdentityMatcher.TrimSignature(SymbolDisplayName.For(symbol)));
    }
}
