using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Distill.TestLogger;

public sealed class JsonlEventWriter : IDisposable
{
    private readonly StreamWriter _writer;

    public JsonlEventWriter(string outputPath)
    {
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _writer = new StreamWriter(outputPath, append: false, Encoding.UTF8);
    }

    public void WriteRunStart(DateTimeOffset utc)
    {
        WriteLine(
            "\"type\":\"run-start\"",
            $"\"utc\":\"{Escape(utc.ToString("O", CultureInfo.InvariantCulture))}\"");
    }

    public void WriteTestResult(
        string name,
        string outcome,
        double durationMs,
        string? error,
        string? stack)
    {
        var parts = new List<string>
        {
            "\"type\":\"test-result\"",
            $"\"name\":\"{Escape(name)}\"",
            $"\"outcome\":\"{Escape(outcome)}\"",
            $"\"durationMs\":{durationMs.ToString(CultureInfo.InvariantCulture)}"
        };

        if (!string.IsNullOrEmpty(error))
        {
            parts.Add($"\"error\":\"{Escape(error!)}\"");
        }

        if (!string.IsNullOrEmpty(stack))
        {
            parts.Add($"\"stack\":\"{Escape(stack!)}\"");
        }

        WriteLine(parts.ToArray());
    }

    public void WriteRunComplete(int total, int passed, int failed, int skipped)
    {
        WriteLine(
            "\"type\":\"run-complete\"",
            $"\"total\":{total}",
            $"\"passed\":{passed}",
            $"\"failed\":{failed}",
            $"\"skipped\":{skipped}");
    }

    public static string Escape(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    if (char.IsControl(ch))
                    {
                        builder.Append("\\u");
                        builder.Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(ch);
                    }

                    break;
            }
        }

        return builder.ToString();
    }

    private void WriteLine(params string[] parts)
    {
        _writer.Write("{\"v\":1,");
        _writer.Write(string.Join(",", parts));
        _writer.WriteLine("}");
        _writer.Flush();
    }

    public void Dispose()
    {
        _writer.Dispose();
    }
}
