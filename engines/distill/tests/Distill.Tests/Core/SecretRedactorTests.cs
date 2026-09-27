using Distill.Core.Redaction;

namespace Distill.Tests.Core;

public class SecretRedactorTests
{
    [Fact]
    public void Redact_MasksBuiltInSecretForms()
    {
        var redactor = new SecretRedactor();

        var result = redactor.Redact(
            "password=hunter2 Authorization: Bearer abc123 token:xyz connectionString='Server=db;Password=pw'");

        Assert.DoesNotContain("hunter2", result);
        Assert.DoesNotContain("abc123", result);
        Assert.DoesNotContain("xyz", result);
        Assert.DoesNotContain("Server=db", result);
        Assert.Contains("***", result);
    }

    [Fact]
    public void Redact_MasksCustomPattern()
    {
        var redactor = new SecretRedactor(["MySecret=[^\\s]+"]);

        var result = redactor.Redact("MySecret=custom-value");

        Assert.Equal("***", result);
    }

    [Fact]
    public void Redact_PathologicalCustomPattern_TimesOutAndFailsClosedInsteadOfHanging()
    {
        // 절대 매칭되지 않는 입력과 짝을 이룬 전형적인 치명적 역추적 패턴이다.
        // 매칭 제한 시간이 없으면 사실상 영원히 돌 수 있다.
        var redactor = new SecretRedactor([@"(a+)+$"]);
        var pathologicalInput = new string('a', 40) + "!";

        string result = null!;
        var elapsed = Record.Exception(() =>
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            result = redactor.Redact(pathologicalInput);
            stopwatch.Stop();
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), "Redact should be bounded by the regex match timeout.");
        });

        Assert.Null(elapsed);
        Assert.DoesNotContain("aaaa", result);
    }
}
