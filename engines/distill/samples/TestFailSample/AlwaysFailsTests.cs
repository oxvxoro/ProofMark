namespace TestFailSample;

public class AlwaysFailsTests
{
    [Fact]
    public void AlwaysFails()
    {
        Assert.True(false, "Expected failure for Distill test spike.");
    }

    [Fact]
    public void Passes()
    {
        Assert.True(true);
    }
}
