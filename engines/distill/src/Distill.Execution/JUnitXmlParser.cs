using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Distill.Core.Evidence;

namespace Distill.Execution;

/// <summary>
/// JUnit XML의 testcase를 TestCaseEvidence로 읽는다. FQN은 classname.name이고,
/// failure/error는 Failed, skipped는 Skipped, 나머지는 Passed다.
/// </summary>
public static class JUnitXmlParser
{
    public static IReadOnlyList<TestCaseEvidence> Parse(string path, string? project)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null
        };
        using var reader = XmlReader.Create(path, settings);
        var document = XDocument.Load(reader);

        var cases = new List<TestCaseEvidence>();
        foreach (var element in document.Descendants().Where(item => item.Name.LocalName == "testcase"))
        {
            var name = element.Attribute("name")?.Value;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var className = element.Attribute("classname")?.Value;
            var fullyQualifiedName = string.IsNullOrWhiteSpace(className) ? name : className + "." + name;
            var failure = element.Elements().FirstOrDefault(child =>
                child.Name.LocalName is "failure" or "error");
            var skipped = element.Elements().Any(child => child.Name.LocalName == "skipped");
            var outcome = failure is not null ? "Failed" : skipped ? "Skipped" : "Passed";
            var seconds = double.TryParse(
                element.Attribute("time")?.Value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var parsed)
                ? parsed
                : 0;

            cases.Add(new TestCaseEvidence(
                name,
                outcome,
                failure?.Attribute("message")?.Value,
                string.IsNullOrWhiteSpace(failure?.Value) ? null : failure.Value,
                seconds * 1000,
                fullyQualifiedName,
                project));
        }

        return cases;
    }
}
