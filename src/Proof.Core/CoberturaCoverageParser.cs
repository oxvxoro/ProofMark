using System.Xml.Linq;

namespace Proof.Core;

/// <summary>
/// 최소의 결정적 Cobertura 리더. <c>packages/classes/methods</c>에서
/// 줄 적중 합이 0보다 큰 실행된 메서드 신원
/// (클래스 이름 + 메서드 이름)을 뽑는다. 출력은 중복 없는 순서
/// 정렬 목록이라 같은 XML은 항상 같은 커버리지 집합을 낸다.
///
/// 어댑터가 아니라 Proof.Core에 둔다. 커버리지
/// 생산자와 이후 제공자가 Distill/CodeMap 엔진
/// 의존 없이 필요하기 때문이다(어댑터 간 참조는
/// MSBuild를 CodeMap 어댑터로 끌어온다).
/// </summary>
public static class CoberturaCoverageParser
{
    public static IReadOnlyList<string> Parse(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            return [];
        }

        XDocument document;
        try
        {
            document = XDocument.Parse(xml);
        }
        catch (System.Xml.XmlException)
        {
            return [];
        }

        var executed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var type in document.Descendants("class"))
        {
            var className = (string?)type.Attribute("name");
            if (string.IsNullOrWhiteSpace(className))
            {
                continue;
            }

            foreach (var method in type.Descendants("method"))
            {
                var methodName = (string?)method.Attribute("name");
                if (string.IsNullOrWhiteSpace(methodName))
                {
                    continue;
                }

                if (!WasExecuted(method))
                {
                    continue;
                }

                executed.Add(className + "." + methodName);
            }
        }

        return executed.OrderBy(value => value, StringComparer.Ordinal).ToArray();
    }

    private static bool WasExecuted(XElement method)
    {
        var lines = method.Element("lines");
        if (lines is null)
        {
            return false;
        }

        var hits = 0;
        foreach (var line in lines.Elements("line"))
        {
            if (int.TryParse((string?)line.Attribute("hits"), out var value) && value > 0)
            {
                hits += value;
            }
        }

        return hits > 0;
    }
}