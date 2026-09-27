using System.CommandLine;
using Proof.Adapters.Distill;
using Proof.Core;

namespace Proof.Cli;

public static class ConfigValidateCommand
{
    public static Command Create()
    {
        var command = new Command("config", "Configuration commands");
        var validate = new Command("validate", "Validate proof.yml and Distill config paths.");
        validate.SetAction((_, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Validate());
        });
        command.Subcommands.Add(validate);
        return command;
    }

    // 명시적 테스트 맵의 형식만 검증한다. Wave 1은 비었거나
    // 공백이 있는 항목을 거부한다. 매핑된 테스트가 실제로 있는지는
    // 여기서 의도적으로 범위 밖이다(맵은 작성된 조언이지, 증명이 아니다).
    internal static void ValidateTestMaps(ProofConfig config)
    {
        foreach (var entry in config.Policy.TestMaps)
        {
            if (string.IsNullOrWhiteSpace(entry.Symbol))
            {
                throw new ProofConfigException("policy.testMaps entries must declare a non-empty symbol.");
            }

            foreach (var test in entry.Tests)
            {
                if (string.IsNullOrWhiteSpace(test) || test.Any(char.IsWhiteSpace))
                {
                    throw new ProofConfigException(
                        $"policy.testMaps for '{entry.Symbol}' contains an empty or whitespace test name.");
                }

                // TestMapMatching을 따른다. 짧은 메서드 이름만으로는
                // 쓸 수 있는 맵 키가 되기엔 모호하여 일찍 거부한다.
                if (!TestMapMatching.TrimSignature(test).Contains('.'))
                {
                    throw new ProofConfigException(
                        $"policy.testMaps for '{entry.Symbol}' contains bare test name '{test}'; use a dotted fully-qualified test name.");
                }
            }
        }
    }

    internal static int Validate(bool bootstrap = false)
    {
        var workspaceRoot = Directory.GetCurrentDirectory();
        try
        {
            var config = bootstrap
                ? ProofConfig.LoadForBootstrap(workspaceRoot)
                : ProofConfig.Load(workspaceRoot);
            ValidateTestMaps(config);
            var proofPath = ProofConfig.ResolveConfigPath(workspaceRoot);
            if (!File.Exists(proofPath))
            {
                Console.WriteLine("No proof.yml found; default configuration is valid.");
            }
            else
            {
                Console.WriteLine($"proof.yml: {proofPath}");
            }

            var distillPath = DistillVerificationRunner.ResolveConfigPath(workspaceRoot, config.Verification.DistillConfig);
            if (!File.Exists(distillPath))
            {
                if (bootstrap)
                {
                    Console.WriteLine($"distill.yml not found yet: {distillPath} (run `distill init` or copy distill.yml.example).");
                }
                else
                {
                    throw new ProofConfigException($"{ProofReasonCodes.DistillConfigNotFound}: {distillPath}");
                }
            }
            else
            {
                Distill.Core.Config.DistillConfigLoader.Load(distillPath);
                Console.WriteLine($"distill.yml: {distillPath}");
            }

            if (!string.Equals(config.Policy.PublicApiCompatibility, "advisory", StringComparison.OrdinalIgnoreCase))
            {
                // api-compatibility는 생산자 병합이 항상 카탈로그에 넣으므로,
                // 필수 정책은 이제 항상 충족 가능하다.
                Console.WriteLine("api-compatibility: covered by the CodeMap API compatibility producer.");
            }

            Console.WriteLine("Configuration is valid.");
            return 0;
        }
        catch (ProofConfigException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
    }
}
