namespace MtpFailSample;

[TestClass]
public sealed class FailTests
{
    [TestMethod]
    public void AlwaysPasses()
    {
    }

    [TestMethod]
    public void AlwaysFails()
    {
        Assert.Fail("expected failure");
    }
}
