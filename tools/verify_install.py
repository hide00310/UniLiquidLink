#!/usr/bin/env python3
"""
End-to-end install verification for the lliquidlink pip package and the
com.hide00310.uniliquidlink Unity package.

- Python side: pip installs `lliquidlink[test]` from PyPI into a conda env,
  then runs the repo's pytest suite against that installed package.
- Unity side: pushes `develop` (only with --push), adds a git-URL package
  dependency to a target Unity project, launches Unity in batch mode to host
  the Integration Test Server, then lets pytest exercise test_integration.py
  against the live server.
"""
import argparse
import json
import os
import re
import shutil
import socket
import subprocess
import sys
import time
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
PACKAGE_NAME = "com.hide00310.uniliquidlink"
PACKAGE_GIT_URL = "https://github.com/hide00310/UniLiquidLink.git#develop"
INTEGRATION_PORT = 8700
DEFAULT_TARGET_DIR = "../../../../../../../LLiquidLinkForTest/LLiquidLink"

# UniLiquidLink.Tests.asmdef (which hosts BatchModeIntegrationTest) is guarded by
# defineConstraints: ["UNITY_INCLUDE_TESTS"]. That symbol is only defined when
# com.unity.test-framework is a *direct* manifest dependency -- a project that only
# pulls it in transitively (e.g. via com.unity.feature.development) silently excludes
# the Tests assembly, and -executeMethod then fails with "class could not be found".
TEST_FRAMEWORK_PACKAGE = "com.unity.test-framework"
TEST_FRAMEWORK_VERSION = "1.1.33"


def log(msg):
    print(f"[verify_install] {msg}", flush=True)


def run(cmd, cwd=None, env=None, check=True):
    log("$ " + " ".join(str(c) for c in cmd))
    return subprocess.run(cmd, cwd=cwd, env=env, check=check, text=True)


def run_capture(cmd, cwd=None):
    return subprocess.run(cmd, cwd=cwd, text=True, capture_output=True)


def parse_args():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--target-dir", default=DEFAULT_TARGET_DIR,
                         help="Target Unity project. Relative paths are resolved against this script's directory.")
    parser.add_argument("--conda-env", default="lliquidlink")
    parser.add_argument("--push", action="store_true",
                         help="Push develop to origin/develop if it is ahead (required for the Unity steps to see the latest code).")
    parser.add_argument("--skip-unity", action="store_true",
                         help="Skip push, manifest.json update, and launching Unity. Python-only verification.")
    parser.add_argument("--unity-exe", default=None,
                         help="Path to Unity.exe. Defaults to resolving via the target project's ProjectVersion.txt and the Unity Hub install layout.")
    parser.add_argument("--unity-timeout", type=int, default=300,
                         help="Seconds to wait for the Integration Test Server to become ready.")
    return parser.parse_args()


def resolve_target_dir(target_dir_arg):
    p = Path(target_dir_arg)
    if not p.is_absolute():
        p = (Path(__file__).resolve().parent / p)
    return p.resolve()


# ─── Git / prerequisite checks ──────────────────────────────────────────────

def check_branch_and_clean():
    branch = run_capture(["git", "rev-parse", "--abbrev-ref", "HEAD"], cwd=REPO_ROOT).stdout.strip()
    if branch != "develop":
        log(f"ERROR: current branch is '{branch}', expected 'develop'. Aborting.")
        sys.exit(1)

    status = run_capture(["git", "status", "--porcelain"], cwd=REPO_ROOT).stdout
    if status.strip():
        log("ERROR: working tree is not clean:")
        print(status)
        sys.exit(1)


def sync_with_origin(push):
    """Returns True if Unity-side steps can proceed (local develop == origin/develop after this call)."""
    run(["git", "fetch", "origin", "develop"], cwd=REPO_ROOT)
    counts = run_capture(
        ["git", "rev-list", "--left-right", "--count", "origin/develop...HEAD"], cwd=REPO_ROOT
    ).stdout.split()
    behind, ahead = int(counts[0]), int(counts[1])

    if behind > 0:
        log(f"WARNING: local develop is {behind} commit(s) behind origin/develop. "
            "Pull/rebase manually before running this script. Skipping Unity steps.")
        return False

    if ahead == 0:
        log("develop is already in sync with origin/develop.")
        return True

    if not push:
        log(f"develop is {ahead} commit(s) ahead of origin/develop. "
            "Re-run with --push to publish it, or push manually. Skipping Unity steps for this run.")
        return False

    run(["git", "push", "origin", "develop"], cwd=REPO_ROOT)
    return True


# ─── Unity package install (manifest.json) ──────────────────────────────────

def update_manifest(target_dir):
    manifest_path = target_dir / "Packages" / "manifest.json"
    with open(manifest_path, encoding="utf-8") as f:
        data = json.load(f)
    deps = data.setdefault("dependencies", {})
    deps[PACKAGE_NAME] = PACKAGE_GIT_URL
    deps.setdefault(TEST_FRAMEWORK_PACKAGE, TEST_FRAMEWORK_VERSION)

    # Unity only compiles a git/registry package's Tests/ folder (where
    # BatchModeIntegrationTest lives) if the package is explicitly opted into
    # via "testables" -- unlike local/embedded packages, this isn't automatic.
    testables = data.setdefault("testables", [])
    if PACKAGE_NAME not in testables:
        testables.append(PACKAGE_NAME)

    with open(manifest_path, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=2)
        f.write("\n")
    log(f"Updated {manifest_path} with {PACKAGE_NAME} -> {PACKAGE_GIT_URL} "
        f"(ensured {TEST_FRAMEWORK_PACKAGE} dependency and testables entry)")


def reset_package_cache(target_dir):
    """Wipe the target project's Library cache (regenerable) so Unity re-resolves the
    latest push and re-imports/recompiles from scratch. Surgically removing just the
    packages-lock.json entry and PackageCache dir was tried first but left stale Bee
    incremental-build (.rsp) artifacts referencing the deleted path, causing spurious
    CS2001 "source file not found" errors -- a full wipe is slower but reliable."""
    library_dir = target_dir / "Library"
    if library_dir.exists():
        shutil.rmtree(library_dir, ignore_errors=True)
        log(f"Removed {library_dir} to force a clean re-import")


# ─── conda / pip ─────────────────────────────────────────────────────────────

def ensure_conda_python(env_name):
    result = run_capture(["conda", "run", "-n", env_name, "python", "--version"])
    if result.returncode == 0:
        log(f"conda env '{env_name}' has Python: {result.stdout.strip() or result.stderr.strip()}")
        return

    log(f"conda env '{env_name}' has no usable Python, provisioning...")
    envs = json.loads(run_capture(["conda", "env", "list", "--json"]).stdout)
    env_names = [Path(p).name for p in envs.get("envs", [])]
    if env_name in env_names:
        run(["conda", "install", "-n", env_name, "python", "-y"])
    else:
        run(["conda", "create", "-n", env_name, "python", "-y"])


def pip_install_from_pypi(env_name):
    run(["conda", "run", "-n", env_name, "pip", "install", "--upgrade", "lliquidlink[test]"])
    version = run_capture(["conda", "run", "-n", env_name, "pip", "show", "lliquidlink"]).stdout
    m = re.search(r"^Version:\s*(\S+)", version, re.MULTILINE)
    installed_version = m.group(1) if m else "unknown"
    log(f"Installed lliquidlink=={installed_version} into conda env '{env_name}'")
    return installed_version


# ─── Unity batch mode ────────────────────────────────────────────────────────

def resolve_unity_exe(target_dir, unity_exe_arg):
    if unity_exe_arg:
        return Path(unity_exe_arg)
    version_file = target_dir / "ProjectSettings" / "ProjectVersion.txt"
    text = version_file.read_text(encoding="utf-8")
    m = re.search(r"^m_EditorVersion:\s*(\S+)", text, re.MULTILINE)
    if not m:
        log(f"ERROR: could not parse Unity version from {version_file}")
        sys.exit(1)
    version = m.group(1)
    exe = Path(r"C:\Program Files\Unity\Hub\Editor") / version / "Editor" / "Unity.exe"
    if not exe.exists():
        log(f"ERROR: Unity {version} not found at {exe}. Pass --unity-exe to override.")
        sys.exit(1)
    return exe


def start_unity_server(target_dir, unity_exe, env_name, timeout):
    conda_exe = shutil.which("conda")
    if not conda_exe:
        log("ERROR: conda not found on PATH.")
        sys.exit(1)
    python_server_command = f'"{conda_exe}" run -n {env_name} python -m lliquidlink.server'

    log_path = target_dir / "verify_install_unity.log"
    if log_path.exists():
        log_path.unlink()

    child_env = dict(os.environ)
    child_env["LLIQUIDLINK_PYTHON_SERVER_COMMAND"] = python_server_command

    cmd = [
        str(unity_exe), "-batchmode", "-nographics",
        "-projectPath", str(target_dir),
        "-executeMethod", "UniLiquidLink.BatchModeIntegrationTest.StartServer",
        "-logFile", str(log_path),
    ]
    log("$ " + " ".join(cmd))
    proc = subprocess.Popen(cmd, env=child_env, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)

    outcome = _wait_for_ready(proc, log_path, timeout)
    return proc, outcome, log_path


def _wait_for_ready(proc, log_path, timeout):
    deadline = time.time() + timeout
    while time.time() < deadline:
        if proc.poll() is not None:
            return f"unity_exited:{proc.returncode}"

        if log_path.exists():
            text = log_path.read_text(encoding="utf-8", errors="ignore")
            error_lines = [line for line in text.splitlines() if "error CS" in line]
            if error_lines:
                return "compile_error:" + "\n".join(error_lines[:20])

        with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as s:
            s.settimeout(0.5)
            try:
                s.connect(("localhost", INTEGRATION_PORT))
                return "ready"
            except OSError:
                pass

        time.sleep(1)
    return "timeout"


def kill_process_tree(pid):
    subprocess.run(["taskkill", "/F", "/T", "/PID", str(pid)],
                    capture_output=True, text=True)


# ─── pytest ──────────────────────────────────────────────────────────────────

def run_pytest(env_name):
    result = subprocess.run(["conda", "run", "-n", env_name, "pytest"], cwd=REPO_ROOT)
    return result.returncode


# ─── main ────────────────────────────────────────────────────────────────────

def main():
    args = parse_args()
    target_dir = resolve_target_dir(args.target_dir)
    log(f"Target Unity project: {target_dir}")

    check_branch_and_clean()

    unity_ready_for_launch = False
    if not args.skip_unity:
        if sync_with_origin(args.push):
            update_manifest(target_dir)
            reset_package_cache(target_dir)
            unity_ready_for_launch = True

    ensure_conda_python(args.conda_env)
    installed_version = pip_install_from_pypi(args.conda_env)

    unity_proc = None
    unity_outcome = "skipped"
    unity_log_path = None
    if unity_ready_for_launch:
        unity_exe = resolve_unity_exe(target_dir, args.unity_exe)
        unity_proc, unity_outcome, unity_log_path = start_unity_server(
            target_dir, unity_exe, args.conda_env, args.unity_timeout)
        if unity_outcome == "ready":
            log("Integration Test Server is ready (localhost:8700 reachable).")
        else:
            log(f"Unity did not become ready: {unity_outcome}")
            if unity_proc.poll() is None:
                kill_process_tree(unity_proc.pid)
            unity_proc = None

    try:
        pytest_code = run_pytest(args.conda_env)
    finally:
        if unity_proc is not None and unity_proc.poll() is None:
            log("Stopping Unity batch mode process...")
            kill_process_tree(unity_proc.pid)

    log("─── Summary ───────────────────────────────────────────")
    log(f"Unity install/server: {unity_outcome}")
    log(f"lliquidlink version installed: {installed_version}")
    log(f"pytest exit code: {pytest_code}")
    sys.exit(pytest_code)


if __name__ == "__main__":
    main()
