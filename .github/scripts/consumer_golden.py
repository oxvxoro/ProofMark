#!/usr/bin/env python3
"""Consumer golden: packaged proof must reach PROVEN on a standalone fixture repo.

Usage:
  consumer_golden.py <tool-dir> <fixture-dir> <work-dir>

The fixture is copied out of this monorepo into <work-dir> and given its own
git history: a baseline commit, then one body-only change. The change comes
from <fixture-dir>/golden.json when present:

  {
    "changedFile": "App/OrderService.cs",
    "original": "...",
    "changed": "...",
    "provenRules": ["P005"],
    "evidenceKinds": ["RUNTIME_COVERAGE"]
  }

refs/remotes/origin/master points at the baseline, so the fixture's
`proof.base` (mergeBase of origin/master) sees exactly that change. The run
passes only when `proof verify` exits 0, the certificate verdict is PROVEN,
every obligation of each rule in `provenRules` exists and is PROVEN, and the
certificate holds evidence of each kind in `evidenceKinds`.
"""

import glob
import json
import os
import shutil
import stat
import subprocess
import sys

DEFAULT_GOLDEN = {
    "changedFile": "App/OrderService.cs",
    "original": "_billing.Refund(orderId)",
    "changed": "_billing.Refund(orderId.Trim())",
    "provenRules": [],
    "evidenceKinds": [],
}
IGNORED = shutil.ignore_patterns("bin", "obj", ".codemap", ".proof", ".distill", "golden.json")


def tool(tool_dir, name):
    path = shutil.which(name, path=tool_dir)
    if path is None:
        raise SystemExit(f"tool '{name}' not found in {tool_dir}")
    return path


def run(argv, cwd, check=True):
    print("+", " ".join(argv), flush=True)
    completed = subprocess.run(argv, cwd=cwd)
    if check and completed.returncode != 0:
        raise SystemExit(f"command failed with exit {completed.returncode}: {' '.join(argv)}")
    return completed.returncode


def git(work, *args):
    return subprocess.run(
        ["git", "-c", "user.name=proof-golden", "-c", "user.email=proof-golden@invalid",
         "-c", "commit.gpgsign=false", *args],
        cwd=work, check=True, capture_output=True, text=True).stdout.strip()


def remove_tree(path):
    def make_writable(function, target, _):
        os.chmod(target, stat.S_IWRITE)
        function(target)

    shutil.rmtree(path, onexc=make_writable)


def load_golden(fixture):
    golden = dict(DEFAULT_GOLDEN)
    path = os.path.join(fixture, "golden.json")
    if os.path.exists(path):
        with open(path, encoding="utf-8") as handle:
            golden.update(json.load(handle))
    return golden


def prepare(fixture, work, golden):
    if os.path.exists(work):
        remove_tree(work)
    shutil.copytree(fixture, work, ignore=IGNORED)
    git(work, "init", "--quiet")
    git(work, "add", "-A")
    git(work, "commit", "--quiet", "-m", "baseline")
    baseline = git(work, "rev-parse", "HEAD")

    changed_file = golden["changedFile"]
    path = os.path.join(work, *changed_file.split("/"))
    with open(path, encoding="utf-8") as handle:
        source = handle.read()
    if golden["original"] not in source:
        raise SystemExit(f"{changed_file} no longer contains '{golden['original']}'; update the golden change")
    with open(path, "w", encoding="utf-8", newline="") as handle:
        handle.write(source.replace(golden["original"], golden["changed"]))
    git(work, "commit", "--quiet", "-am", f"change {changed_file}")
    git(work, "update-ref", "refs/remotes/origin/master", baseline)


def latest_certificate(work):
    candidates = [path for path in glob.glob(os.path.join(work, ".proof", "certificates", "*.json"))
                  if not path.endswith(".summary.json")]
    if not candidates:
        raise SystemExit("proof verify wrote no certificate body")
    return max(candidates, key=os.path.getmtime)


def failures(certificate, exit_code, golden):
    problems = []
    if certificate.get("verdict") != "PROVEN" or exit_code != 0:
        problems.append("expected verdict PROVEN with exit 0")
    obligations = certificate.get("evaluation", {}).get("obligations", [])
    for rule in golden["provenRules"]:
        statuses = [item.get("status") for item in obligations
                    if item.get("obligation", {}).get("ruleId") == rule]
        if not statuses:
            problems.append(f"expected at least one {rule} obligation")
        elif any(status != "PROVEN" for status in statuses):
            problems.append(f"expected every {rule} obligation PROVEN, got {statuses}")
    kinds = {item.get("kind") for item in certificate.get("evidence", {}).get("evidence", [])}
    for kind in golden["evidenceKinds"]:
        if kind not in kinds:
            problems.append(f"expected {kind} evidence, got {sorted(k for k in kinds if k)}")
    return problems


def main():
    if len(sys.argv) != 4:
        raise SystemExit(__doc__)
    tool_dir, fixture, work = (os.path.abspath(arg) for arg in sys.argv[1:])
    codemap = tool(tool_dir, "codemap")
    proof = tool(tool_dir, "proof")
    golden = load_golden(fixture)

    prepare(fixture, work, golden)
    run([codemap, "index", "."], work)
    run([proof, "config", "validate"], work)
    exit_code = run([proof, "verify", "--output", "compact"], work, check=False)

    certificate_path = latest_certificate(work)
    with open(certificate_path, encoding="utf-8") as handle:
        certificate = json.load(handle)
    print(f"certificate: {certificate_path}")
    print(f"verdict: {certificate.get('verdict')}  exit: {exit_code}")
    problems = failures(certificate, exit_code, golden)
    if problems:
        for item in certificate.get("evaluation", {}).get("obligations", []):
            obligation = item.get("obligation", {})
            print(f"  {obligation.get('ruleId')}  {item.get('status')}  {obligation.get('claim')}")
        raise SystemExit("consumer golden failed: " + "; ".join(problems))
    print("consumer golden passed")


if __name__ == "__main__":
    main()
