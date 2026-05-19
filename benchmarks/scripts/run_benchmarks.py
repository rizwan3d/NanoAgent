from __future__ import annotations

import argparse
import json
import os
import shlex
import shutil
import subprocess
import sys
import tempfile
import textwrap
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Any


REPO_ROOT = Path(__file__).resolve().parents[2]
BENCHMARKS_ROOT = REPO_ROOT / "benchmarks"
DEFAULT_MANIFEST_PATH = BENCHMARKS_ROOT / "manifest.json"
DEFAULT_RESULTS_DIR = BENCHMARKS_ROOT / "results"
DEFAULT_TASK_GLOB = "tasks/**/*.yml"
TRANSIENT_DIFF_SEGMENTS = {
    ".git",
    ".next",
    ".turbo",
    "bin",
    "build",
    "coverage",
    "dist",
    "node_modules",
    "obj",
    "out",
}


@dataclass(frozen=True)
class CommandExpectation:
    command: list[str]
    expect_exit_code: int = 0
    expect_stdout_contains_all: tuple[str, ...] = ()
    expect_stdout_contains_any: tuple[str, ...] = ()
    expect_stdout_not_contains: tuple[str, ...] = ()
    expect_stderr_contains_all: tuple[str, ...] = ()
    expect_stderr_contains_any: tuple[str, ...] = ()
    expect_stderr_not_contains: tuple[str, ...] = ()


@dataclass(frozen=True)
class ResponseExpectation:
    contains_all: tuple[str, ...] = ()
    contains_any: tuple[str, ...] = ()
    contains_at_least: int = 0
    not_contains: tuple[str, ...] = ()


@dataclass(frozen=True)
class DiffExpectation:
    max_changed_files: int | None = None
    changed_files_include: tuple[str, ...] = ()
    changed_files_exclude: tuple[str, ...] = ()


@dataclass(frozen=True)
class TaskDefinition:
    id: str
    name: str
    suite: str
    prompt: str
    workspace: Path
    source_path: Path
    profile: str
    thinking: str | None
    isolation: str
    tags: tuple[str, ...]
    validation_commands: tuple[CommandExpectation, ...]
    response: ResponseExpectation
    diff: DiffExpectation | None


@dataclass(frozen=True)
class AgentSettings:
    provider: str | None
    provider_key: str | None
    model: str | None
    thinking: str | None


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Run NanoAgent benchmark tasks and write scored results.")
    parser.add_argument("--manifest", default=str(DEFAULT_MANIFEST_PATH))
    parser.add_argument("--all", action="store_true", help="Run all discovered tasks.")
    parser.add_argument("--suite", action="append", default=[], help="Run only one suite id.")
    parser.add_argument("--task", action="append", default=[], help="Run only one task id.")
    parser.add_argument("--list", action="store_true", help="List discovered tasks and exit.")
    parser.add_argument("--dry-run", action="store_true", help="Show selected tasks but do not run them.")
    parser.add_argument("--keep-temp", action="store_true", help="Keep isolated task workspaces.")
    parser.add_argument("--skip-preflight", action="store_true", help="Skip the NanoAgent connectivity preflight and start tasks immediately.")
    parser.add_argument("--preflight-timeout", type=int, default=60, help="Seconds to wait for the NanoAgent preflight before failing fast.")
    parser.add_argument("--stream-agent-output", action="store_true", help="Stream NanoAgent stdout/stderr live while still saving it in results.")
    parser.add_argument("--system", action="store_true", help="Use the system-installed nanoai command and bypass NanoAgent provider/model/key environment wiring.")
    parser.add_argument("--provider", help="Provider name passed through NANOAGENT_PROVIDER.")
    parser.add_argument("--provider-key", dest="provider_key", help="Provider API key forwarded to NanoAgent.")
    parser.add_argument("--provide-key", dest="provider_key_alias", help="Alias for --provider-key.")
    parser.add_argument("--model", help="Model id passed through NANOAGENT_MODEL.")
    parser.add_argument("--thinking", help="Optional NanoAgent thinking effort override.")
    parser.add_argument("--profile", help="Override the task profile for every run.")
    parser.add_argument(
        "--agent-command",
        help='Base command for NanoAgent, for example `nanoai` or `dotnet run --project ".../NanoAgent.CLI.csproj" --`.',
    )
    parser.add_argument(
        "--agent-prompt-mode",
        choices=("auto", "option", "positional"),
        default="auto",
        help="How to pass the prompt to NanoAgent. auto uses a positional prompt for `nanoai` and `--prompt` for the dotnet runner.",
    )
    parser.add_argument("--release-tag", help="Optional release tag for the output metadata.")
    parser.add_argument("--output-json", help="Explicit JSON output path.")
    parser.add_argument("--output-md", help="Explicit Markdown summary path.")
    return parser.parse_args()


def utc_now() -> datetime:
    return datetime.now(timezone.utc)


def log_progress(message: str) -> None:
    timestamp = utc_now().isoformat(timespec="seconds").replace("+00:00", "Z")
    print(f"[{timestamp}] {message}", flush=True)


def summarize_task_result(result: dict[str, Any]) -> str:
    status = "PASS" if result.get("passed") else "FAIL"
    duration = result.get("durationSeconds", "n/a")
    failures = result.get("failures") or []
    if failures:
        failure_summary = "; ".join(str(item) for item in failures[:2])
        if len(failures) > 2:
            failure_summary += f"; +{len(failures) - 2} more"
        return f"{status} in {duration}s - {failure_summary}"
    return f"{status} in {duration}s"


def redact_command(command: list[str]) -> list[str]:
    redacted: list[str] = []
    redact_next = False
    sensitive_prefixes = (
        "--provider-key=",
        "--provider-auth-key=",
        "--provide-key=",
    )
    sensitive_flags = {
        "--provider-key",
        "--provider-auth-key",
        "--provide-key",
    }

    for part in command:
        if redact_next:
            redacted.append("[REDACTED]")
            redact_next = False
            continue

        if part in sensitive_flags:
            redacted.append(part)
            redact_next = True
            continue

        matched_prefix = next((prefix for prefix in sensitive_prefixes if part.startswith(prefix)), None)
        if matched_prefix:
            redacted.append(f"{matched_prefix}[REDACTED]")
            continue

        redacted.append(part)
    return redacted


def redact_command_string(command: list[str]) -> str:
    return " ".join(shlex.quote(part) for part in redact_command(command))


def redact_result_command(result: dict[str, Any]) -> dict[str, Any]:
    command = result.get("command")
    if isinstance(command, list):
        result = dict(result)
        result["command"] = redact_command([str(part) for part in command])
    return result


def run_process_streaming(
    command: list[str],
    cwd: Path,
    env: dict[str, str] | None = None,
    timeout_seconds: int | None = None,
    *,
    prefix: str | None = None,
) -> dict[str, Any]:
    log_progress(f"Running command: {redact_command_string(command)} [cwd={cwd}]")
    started = utc_now()
    stdout_lines: list[str] = []
    stderr_lines: list[str] = []
    process = subprocess.Popen(
        command,
        cwd=str(cwd),
        env=env,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        text=True,
        bufsize=1,
    )

    import queue
    import threading

    output_queue: queue.Queue[tuple[str, str | None]] = queue.Queue()

    def reader(stream: Any, stream_name: str) -> None:
        try:
            for line in iter(stream.readline, ""):
                output_queue.put((stream_name, line))
        finally:
            output_queue.put((stream_name, None))
            stream.close()

    threads = []
    if process.stdout is not None:
        threads.append(threading.Thread(target=reader, args=(process.stdout, "stdout"), daemon=True))
    if process.stderr is not None:
        threads.append(threading.Thread(target=reader, args=(process.stderr, "stderr"), daemon=True))
    for thread in threads:
        thread.start()

    finished_streams = 0
    timed_out = False
    while finished_streams < len(threads):
        elapsed = (utc_now() - started).total_seconds()
        if timeout_seconds is not None and elapsed > timeout_seconds:
            timed_out = True
            process.kill()
            break
        try:
            stream_name, line = output_queue.get(timeout=0.2)
        except queue.Empty:
            continue
        if line is None:
            finished_streams += 1
            continue
        if stream_name == "stdout":
            stdout_lines.append(line)
        else:
            stderr_lines.append(line)
        label = prefix or "process"
        print(f"[{label} {stream_name}] {line.rstrip()}", flush=True)

    for thread in threads:
        thread.join(timeout=1)

    # Always reap the child process before reading returncode.
    # process.returncode is None until poll()/wait() observes process exit; the
    # previous version returned None after stdout/stderr closed, causing a
    # successful nanoai preflight to be reported as failed.
    if timed_out:
        try:
            process.kill()
        except ProcessLookupError:
            pass
        try:
            exit_code = process.wait(timeout=2)
        except subprocess.TimeoutExpired:
            exit_code = -9
        stderr_lines.append(f"\nTimed out after {timeout_seconds} seconds.\n")
        if exit_code is None:
            exit_code = -9
    else:
        try:
            exit_code = process.wait(timeout=2)
        except subprocess.TimeoutExpired:
            process.kill()
            exit_code = process.wait(timeout=2)
            stderr_lines.append("\nProcess had to be killed after output streams closed.\n")

    ended = utc_now()

    log_progress(
        f"Command finished with exit code {exit_code}: {redact_command_string(command)} "
        f"[duration={round((ended - started).total_seconds(), 3)}s]"
    )
    return {
        "command": redact_command(command),
        "cwd": str(cwd),
        "exitCode": exit_code,
        "stdout": "".join(stdout_lines),
        "stderr": "".join(stderr_lines),
        "durationSeconds": round((ended - started).total_seconds(), 3),
        "timedOut": timed_out,
    }


def to_rel_path(path: Path) -> str:
    return path.resolve().relative_to(REPO_ROOT.resolve()).as_posix()


def load_json_or_yaml(path: Path) -> dict[str, Any]:
    text = path.read_text(encoding="utf-8")
    try:
        data = json.loads(text)
    except json.JSONDecodeError:
        try:
            import yaml  # type: ignore
        except ImportError as exc:
            raise RuntimeError(
                f"{path} is not valid JSON. Install PyYAML to load general YAML task files."
            ) from exc
        data = yaml.safe_load(text)

    if not isinstance(data, dict):
        raise RuntimeError(f"{path} must deserialize to an object.")

    return data


def load_manifest(path: Path) -> dict[str, Any]:
    manifest = load_json_or_yaml(path)
    suites = manifest.get("suites")
    if not isinstance(suites, list) or not suites:
        raise RuntimeError(f"{path} must define a non-empty suites list.")
    return manifest


def load_tasks(manifest: dict[str, Any], manifest_path: Path) -> list[TaskDefinition]:
    task_root_value = manifest.get("taskRoot", "benchmarks/tasks")
    task_root = resolve_repo_path(task_root_value)
    task_paths = sorted(task_root.rglob("*.yml"))
    if not task_paths:
        raise RuntimeError(f"No benchmark task files were found under {task_root}.")

    tasks: list[TaskDefinition] = []
    for task_path in task_paths:
        raw = load_json_or_yaml(task_path)
        task_id = require_string(raw, "id", task_path)
        name = require_string(raw, "name", task_path)
        suite = require_string(raw, "suite", task_path)
        prompt = require_string(raw, "prompt", task_path)
        workspace = resolve_repo_path(require_string(raw, "workspace", task_path))
        profile = string_or_default(raw.get("profile"), "build")
        thinking = string_or_none(raw.get("thinking"))
        isolation = string_or_default(raw.get("isolation"), "copy")
        tags = tuple(normalize_string_list(raw.get("tags", []), task_path, "tags"))
        validation_commands = tuple(parse_command_expectation(item, task_path) for item in raw.get("validationCommands", []))
        response = parse_response_expectation(raw.get("response", {}), task_path)
        diff = parse_diff_expectation(raw.get("diff"), task_path)

        tasks.append(
            TaskDefinition(
                id=task_id,
                name=name,
                suite=suite,
                prompt=prompt,
                workspace=workspace,
                source_path=task_path,
                profile=profile,
                thinking=thinking,
                isolation=isolation,
                tags=tags,
                validation_commands=validation_commands,
                response=response,
                diff=diff,
            )
        )

    suite_ids = {require_string(item, "id", manifest_path) for item in manifest["suites"]}
    for task in tasks:
        if task.suite not in suite_ids:
            raise RuntimeError(
                f"Task {task.id} in {task.source_path} references unknown suite '{task.suite}'."
            )

    task_ids = [task.id for task in tasks]
    duplicates = sorted({task_id for task_id in task_ids if task_ids.count(task_id) > 1})
    if duplicates:
        raise RuntimeError(f"Duplicate benchmark task ids found: {', '.join(duplicates)}.")

    return tasks


def parse_command_expectation(raw: Any, source_path: Path) -> CommandExpectation:
    if not isinstance(raw, dict):
        raise RuntimeError(f"{source_path}: validationCommands entries must be objects.")

    command_value = raw.get("command")
    if not isinstance(command_value, list) or not command_value or not all(isinstance(item, str) for item in command_value):
        raise RuntimeError(f"{source_path}: validation command must be a non-empty string array.")

    return CommandExpectation(
        command=list(command_value),
        expect_exit_code=int(raw.get("expectExitCode", 0)),
        expect_stdout_contains_all=tuple(normalize_string_list(raw.get("expectStdoutContainsAll", []), source_path, "expectStdoutContainsAll")),
        expect_stdout_contains_any=tuple(normalize_string_list(raw.get("expectStdoutContainsAny", []), source_path, "expectStdoutContainsAny")),
        expect_stdout_not_contains=tuple(normalize_string_list(raw.get("expectStdoutNotContains", []), source_path, "expectStdoutNotContains")),
        expect_stderr_contains_all=tuple(normalize_string_list(raw.get("expectStderrContainsAll", []), source_path, "expectStderrContainsAll")),
        expect_stderr_contains_any=tuple(normalize_string_list(raw.get("expectStderrContainsAny", []), source_path, "expectStderrContainsAny")),
        expect_stderr_not_contains=tuple(normalize_string_list(raw.get("expectStderrNotContains", []), source_path, "expectStderrNotContains")),
    )


def parse_response_expectation(raw: Any, source_path: Path) -> ResponseExpectation:
    if raw is None:
        raw = {}
    if not isinstance(raw, dict):
        raise RuntimeError(f"{source_path}: response must be an object.")

    return ResponseExpectation(
        contains_all=tuple(normalize_string_list(raw.get("containsAll", []), source_path, "response.containsAll")),
        contains_any=tuple(normalize_string_list(raw.get("containsAny", []), source_path, "response.containsAny")),
        contains_at_least=int(raw.get("containsAtLeast", 0)),
        not_contains=tuple(normalize_string_list(raw.get("notContains", []), source_path, "response.notContains")),
    )


def parse_diff_expectation(raw: Any, source_path: Path) -> DiffExpectation | None:
    if raw is None:
        return None
    if not isinstance(raw, dict):
        raise RuntimeError(f"{source_path}: diff must be an object.")

    max_changed_files_value = raw.get("maxChangedFiles")
    max_changed_files = None if max_changed_files_value is None else int(max_changed_files_value)
    return DiffExpectation(
        max_changed_files=max_changed_files,
        changed_files_include=tuple(normalize_string_list(raw.get("changedFilesInclude", []), source_path, "diff.changedFilesInclude")),
        changed_files_exclude=tuple(normalize_string_list(raw.get("changedFilesExclude", []), source_path, "diff.changedFilesExclude")),
    )


def normalize_string_list(value: Any, source_path: Path, field_name: str) -> list[str]:
    if value is None:
        return []
    if not isinstance(value, list) or not all(isinstance(item, str) for item in value):
        raise RuntimeError(f"{source_path}: {field_name} must be a string array.")
    return [item.strip() for item in value if item.strip()]


def require_string(raw: dict[str, Any], key: str, source_path: Path) -> str:
    value = raw.get(key)
    if not isinstance(value, str) or not value.strip():
        raise RuntimeError(f"{source_path}: {key} must be a non-empty string.")
    return value.strip()


def string_or_none(value: Any) -> str | None:
    if not isinstance(value, str):
        return None
    normalized = value.strip()
    return normalized or None


def string_or_default(value: Any, default: str) -> str:
    normalized = string_or_none(value)
    return normalized if normalized is not None else default


def resolve_repo_path(value: str) -> Path:
    candidate = Path(value)
    return candidate if candidate.is_absolute() else (REPO_ROOT / candidate)


def select_tasks(
    tasks: list[TaskDefinition],
    suites: set[str],
    task_ids: set[str],
    regression_tag: str,
) -> list[TaskDefinition]:
    if not suites and not task_ids:
        return list(tasks)

    selected: list[TaskDefinition] = []
    for task in tasks:
        suite_membership = set(get_task_suite_membership(task, regression_tag))
        if suites and suite_membership.isdisjoint(suites):
            continue
        if task_ids and task.id not in task_ids:
            continue
        selected.append(task)
    return selected


def get_task_suite_membership(task: TaskDefinition, regression_tag: str) -> list[str]:
    memberships = [task.suite]
    if regression_tag in task.tags:
        memberships.append("regression")
    return memberships


def default_agent_command() -> list[str]:
    return [
        "dotnet",
        "run",
        "--project",
        str(REPO_ROOT / "NanoAgent.CLI" / "NanoAgent.CLI.csproj"),
        "--configuration",
        "Release",
        "--verbosity",
        "quiet",
        "--",
    ]


def build_agent_command(args: argparse.Namespace) -> list[str]:
    if getattr(args, "system", False):
        return ["nanoai"]
    if not args.agent_command:
        return default_agent_command()
    return shlex.split(args.agent_command, posix=(os.name != "nt"))


def resolve_agent_settings(args: argparse.Namespace) -> AgentSettings:
    if getattr(args, "system", False):
        return AgentSettings(
            provider=None,
            provider_key=None,
            model=None,
            thinking=args.thinking,
        )

    provider_key = (
        args.provider_key
        or args.provider_key_alias
        or os.environ.get("NANOAGENT_PROVIDER_AUTH_KEY")
        or os.environ.get("OPENAI_API_KEY")
    )
    provider = args.provider or os.environ.get("NANOAGENT_PROVIDER")
    if provider_key and not provider:
        provider = "openai"

    return AgentSettings(
        provider=provider,
        provider_key=provider_key,
        model=args.model,
        thinking=args.thinking,
    )


def build_system_agent_env() -> dict[str, str]:
    # Keep only the OS variables needed to locate and launch the installed CLI.
    # Do not forward provider/model/API-key variables; nanoai should use its own
    # system/user configuration when --system is selected.
    keep_names_upper = {
        "PATH",
        "PATHEXT",
        "SYSTEMROOT",
        "WINDIR",
        "COMSPEC",
        "TEMP",
        "TMP",
        "HOME",
        "USERPROFILE",
        "APPDATA",
        "LOCALAPPDATA",
        "PROGRAMFILES",
        "PROGRAMFILES(X86)",
        "PROGRAMDATA",
    }
    return {
        key: value
        for key, value in os.environ.items()
        if key.upper() in keep_names_upper
    }


def build_agent_env(agent_settings: AgentSettings, *, system: bool = False) -> dict[str, str]:
    if system:
        return build_system_agent_env()

    agent_env = os.environ.copy()
    if agent_settings.model:
        agent_env["NANOAGENT_MODEL"] = agent_settings.model
    if agent_settings.provider:
        agent_env["NANOAGENT_PROVIDER"] = agent_settings.provider
    if agent_settings.provider_key:
        agent_env["NANOAGENT_API_KEY"] = agent_settings.provider_key
    return agent_env


def command_executable_name(command: list[str]) -> str:
    if not command:
        return ""
    executable = str(command[0]).strip().strip('"')
    executable = executable.replace("\\", "/")
    return executable.rsplit("/", 1)[-1].lower()


def infer_agent_prompt_mode(agent_base_command: list[str], requested_mode: str = "auto") -> str:
    if requested_mode != "auto":
        return requested_mode
    executable = command_executable_name(agent_base_command)
    if executable in {"nanoai", "nanoai.exe"}:
        return "positional"
    return "option"


def build_agent_prompt_command(
    agent_base_command: list[str],
    prompt: str,
    profile: str,
    agent_settings: AgentSettings,
    *,
    include_yes: bool,
    prompt_mode: str = "auto",
    system: bool = False,
) -> list[str]:
    command = list(agent_base_command)
    command.extend(["--json"])

    if system:
        command.extend(["--profile", profile])
        command.append(prompt)
        return command

    if include_yes:
        command.append("--yes")
    command.extend(["--profile", profile])
    if agent_settings.thinking:
        command.extend(["--thinking", agent_settings.thinking])
    if agent_settings.provider_key:
        command.extend(["--provider-auth-key", agent_settings.provider_key])

    resolved_prompt_mode = infer_agent_prompt_mode(agent_base_command, prompt_mode)
    if resolved_prompt_mode == "positional":
        command.append(prompt)
    else:
        command.extend(["--prompt", prompt])
    return command


def run_process(
    command: list[str],
    cwd: Path,
    env: dict[str, str] | None = None,
    timeout_seconds: int | None = None,
) -> dict[str, Any]:
    log_progress(f"Running command: {redact_command_string(command)} [cwd={cwd}]")
    started = utc_now()
    completed = subprocess.run(
        command,
        cwd=str(cwd),
        env=env,
        capture_output=True,
        text=True,
        timeout=timeout_seconds,
        check=False,
    )
    ended = utc_now()
    log_progress(
        f"Command finished with exit code {completed.returncode}: {redact_command_string(command)} "
        f"[duration={round((ended - started).total_seconds(), 3)}s]"
    )
    return {
        "command": redact_command(command),
        "cwd": str(cwd),
        "exitCode": completed.returncode,
        "stdout": completed.stdout,
        "stderr": completed.stderr,
        "durationSeconds": round((ended - started).total_seconds(), 3),
    }


def parse_agent_error(cli_payload: dict[str, Any] | None) -> str | None:
    if not isinstance(cli_payload, dict):
        return None

    status = str(cli_payload.get("status", "") or "").strip().lower()
    payload_type = str(cli_payload.get("type", "") or "").strip().lower()
    if status != "error" and payload_type != "error":
        return None

    message = str(cli_payload.get("message", "") or "").strip()
    error_code = str(cli_payload.get("errorCode", "") or "").strip()
    if message:
        return message
    if error_code:
        return error_code
    return "NanoAgent returned an error response."


def create_temp_dir(prefix: str) -> Path:
    return Path(tempfile.mkdtemp(prefix=prefix))


def should_ignore_copy_path(path: str, names: list[str]) -> set[str]:
    ignored = set()
    for name in names:
        normalized = name.strip()
        if normalized in TRANSIENT_DIFF_SEGMENTS:
            ignored.add(name)
            continue
        if normalized == ".git":
            ignored.add(name)
    return ignored


def copy_workspace_to_temp(source: Path, destination: Path) -> None:
    shutil.copytree(source, destination, ignore=should_ignore_copy_path)


def initialize_git_baseline(workspace: Path) -> None:
    run_process(["git", "init", "-q"], workspace)
    run_process(["git", "config", "user.email", "benchmarks@nanoagent.local"], workspace)
    run_process(["git", "config", "user.name", "NanoAgent Benchmarks"], workspace)
    run_process(["git", "add", "-A"], workspace)
    run_process(["git", "commit", "-q", "-m", "benchmark baseline"], workspace)


def collect_diff_stats(workspace: Path) -> dict[str, Any]:
    if not workspace.exists():
        return {
            "available": False,
            "reason": "workspace_missing",
            "changedFiles": [],
            "changedFileCount": None,
            "insertions": None,
            "deletions": None,
        }

    status = run_process(["git", "status", "--porcelain=v1"], workspace)
    files = run_process(["git", "diff", "--name-only", "--diff-filter=ACMR"], workspace)
    numstat = run_process(["git", "diff", "--numstat"], workspace)
    if status["exitCode"] != 0 or files["exitCode"] != 0 or numstat["exitCode"] != 0:
        return {
            "available": False,
            "reason": "git_diff_failed",
            "changedFiles": [],
            "changedFileCount": None,
            "insertions": None,
            "deletions": None,
        }

    changed_files = [
        line.strip().replace("\\", "/")
        for line in files["stdout"].splitlines()
        if line.strip()
    ]
    changed_files = [
        path for path in changed_files
        if not is_transient_diff_path(path)
    ]
    insertions = 0
    deletions = 0
    for line in numstat["stdout"].splitlines():
        parts = line.split("\t")
        if len(parts) < 3:
            continue
        relative_path = parts[2].strip().replace("\\", "/")
        if is_transient_diff_path(relative_path):
            continue
        if parts[0].isdigit():
            insertions += int(parts[0])
        if parts[1].isdigit():
            deletions += int(parts[1])

    return {
        "available": True,
        "changedFiles": changed_files,
        "changedFileCount": len(changed_files),
        "insertions": insertions,
        "deletions": deletions,
        "statusLines": [
            line for line in status["stdout"].splitlines()
            if line.strip() and not is_transient_diff_path(extract_status_path(line))
        ],
    }


def extract_status_path(status_line: str) -> str:
    if len(status_line) <= 3:
        return ""
    return status_line[3:].strip().replace("\\", "/")


def is_transient_diff_path(path: str) -> bool:
    normalized = path.strip().replace("\\", "/")
    if not normalized:
        return False

    parts = [part for part in normalized.split("/") if part]
    return any(part in TRANSIENT_DIFF_SEGMENTS for part in parts)


def parse_cli_json(stdout: str) -> dict[str, Any]:
    # NanoAI may print human-readable thinking/log lines before the final JSON.
    # Prefer the last complete JSON object in stdout instead of assuming stdout is JSON-only.
    lines = [line.strip() for line in stdout.splitlines() if line.strip()]
    for line in reversed(lines):
        if not (line.startswith("{") and line.endswith("}")):
            continue
        try:
            payload = json.loads(line)
        except json.JSONDecodeError:
            continue
        if isinstance(payload, dict):
            return payload

    decoder = json.JSONDecoder()
    for start in reversed([index for index, char in enumerate(stdout) if char == "{"]):
        candidate = stdout[start:].lstrip()
        try:
            payload, _ = decoder.raw_decode(candidate)
        except json.JSONDecodeError:
            continue
        if isinstance(payload, dict):
            return payload

    raise RuntimeError("NanoAgent did not emit a parseable JSON object.")


def evaluate_text_expectation(
    text: str,
    expectation: ResponseExpectation,
) -> tuple[bool, list[str]]:
    lowered = text.lower()
    notes: list[str] = []
    passed = True

    for needle in expectation.contains_all:
        if needle.lower() not in lowered:
            passed = False
            notes.append(f"missing required response text: {needle}")

    matched_any = [needle for needle in expectation.contains_any if needle.lower() in lowered]
    if expectation.contains_at_least:
        if len(matched_any) < expectation.contains_at_least:
            passed = False
            notes.append(
                f"matched {len(matched_any)} of {expectation.contains_at_least} required optional response hints"
            )

    for needle in expectation.not_contains:
        if needle.lower() in lowered:
            passed = False
            notes.append(f"response contained forbidden text: {needle}")

    if expectation.contains_any:
        notes.append(f"matched response hints: {', '.join(matched_any) if matched_any else 'none'}")

    return passed, notes


def evaluate_command_expectation(result: dict[str, Any], expectation: CommandExpectation) -> tuple[bool, list[str]]:
    notes: list[str] = []
    passed = True

    if result["exitCode"] != expectation.expect_exit_code:
        passed = False
        notes.append(f"exit code {result['exitCode']} != expected {expectation.expect_exit_code}")

    stdout_lower = result["stdout"].lower()
    stderr_lower = result["stderr"].lower()

    for needle in expectation.expect_stdout_contains_all:
        if needle.lower() not in stdout_lower:
            passed = False
            notes.append(f"stdout missing: {needle}")

    if expectation.expect_stdout_contains_any:
        matches = [needle for needle in expectation.expect_stdout_contains_any if needle.lower() in stdout_lower]
        if not matches:
            passed = False
            notes.append("stdout missing any allowed match")
        else:
            notes.append(f"stdout matched: {', '.join(matches)}")

    for needle in expectation.expect_stdout_not_contains:
        if needle.lower() in stdout_lower:
            passed = False
            notes.append(f"stdout contained forbidden text: {needle}")

    for needle in expectation.expect_stderr_contains_all:
        if needle.lower() not in stderr_lower:
            passed = False
            notes.append(f"stderr missing: {needle}")

    if expectation.expect_stderr_contains_any:
        matches = [needle for needle in expectation.expect_stderr_contains_any if needle.lower() in stderr_lower]
        if not matches:
            passed = False
            notes.append("stderr missing any allowed match")
        else:
            notes.append(f"stderr matched: {', '.join(matches)}")

    for needle in expectation.expect_stderr_not_contains:
        if needle.lower() in stderr_lower:
            passed = False
            notes.append(f"stderr contained forbidden text: {needle}")

    return passed, notes


def evaluate_diff_expectation(diff_stats: dict[str, Any], expectation: DiffExpectation) -> tuple[bool, list[str]]:
    notes: list[str] = []
    passed = True

    if not diff_stats.get("available"):
        return False, [f"diff unavailable: {diff_stats.get('reason', 'unknown')}"]

    changed_files = diff_stats.get("changedFiles", [])
    if expectation.max_changed_files is not None and len(changed_files) > expectation.max_changed_files:
        passed = False
        notes.append(
            f"changed {len(changed_files)} files > allowed {expectation.max_changed_files}"
        )

    changed_set = set(changed_files)
    for required in expectation.changed_files_include:
        normalized = required.replace("\\", "/")
        if normalized not in changed_set:
            passed = False
            notes.append(f"required changed file missing: {normalized}")

    for forbidden in expectation.changed_files_exclude:
        normalized = forbidden.replace("\\", "/")
        if normalized in changed_set:
            passed = False
            notes.append(f"forbidden file changed: {normalized}")

    return passed, notes


def build_task_workspace(task: TaskDefinition, keep_temp: bool) -> tuple[Path, Path | None]:
    if task.isolation == "in_place":
        return task.workspace, None

    temp_root = create_temp_dir(prefix=f"nanoagent-benchmark-{task.id}-")
    destination = temp_root / "workspace"
    copy_workspace_to_temp(task.workspace, destination)
    return destination, temp_root


def remove_temp_dir(temp_root: Path | None) -> None:
    if temp_root is None:
        return
    shutil.rmtree(temp_root, ignore_errors=True)


def run_task(
    task: TaskDefinition,
    args: argparse.Namespace,
    agent_base_command: list[str],
    regression_tag: str,
    agent_settings: AgentSettings,
) -> dict[str, Any]:
    workspace, temp_root = build_task_workspace(task, args.keep_temp)
    task_started = utc_now()
    agent_env = build_agent_env(agent_settings, system=args.system)

    if task.isolation != "in_place":
        initialize_git_baseline(workspace)

    validation_runs: list[dict[str, Any]] = []
    cli_payload: dict[str, Any] | None = None
    agent_run: dict[str, Any] | None = None
    diff_stats: dict[str, Any] | None = None
    pass_reasons: list[str] = []
    failed_checks: list[str] = []

    try:
        command = build_agent_prompt_command(
            agent_base_command,
            task.prompt,
            args.profile or task.profile,
            AgentSettings(
                provider=agent_settings.provider,
                provider_key=agent_settings.provider_key,
                model=agent_settings.model,
                thinking=args.thinking or task.thinking,
            ),
            include_yes=True,
            prompt_mode=args.agent_prompt_mode,
            system=args.system,
        )

        log_progress(f"Agent command for {task.id}: {redact_command_string(command)}")
        if args.stream_agent_output:
            agent_run = run_process_streaming(
                command,
                workspace,
                env=agent_env,
                timeout_seconds=1800,
                prefix=f"task:{task.id}",
            )
        else:
            agent_run = run_process(command, workspace, env=agent_env, timeout_seconds=1800)

        try:
            cli_payload = parse_cli_json(agent_run["stdout"])
        except RuntimeError as exc:
            failed_checks.append(str(exc))
            cli_payload = {
                "status": "error",
                "type": "parse_error",
                "response": "",
                "message": str(exc),
                "session": None,
            }

        agent_error = parse_agent_error(cli_payload)
        if agent_run["exitCode"] != 0:
            failed_checks.append(f"agent exit code was {agent_run['exitCode']}")
        if agent_error:
            failed_checks.append(f"agent error: {agent_error}")

        if agent_error:
            ended = utc_now()
            return {
                "id": task.id,
                "name": task.name,
                "suite": task.suite,
                "suiteMembership": get_task_suite_membership(task, regression_tag),
                "sourcePath": to_rel_path(task.source_path),
                "workspace": to_rel_path(task.workspace),
                "profile": args.profile or task.profile,
                "thinking": args.thinking or task.thinking,
                "isolation": task.isolation,
                "passed": False,
                "startedAtUtc": task_started.isoformat().replace("+00:00", "Z"),
                "completedAtUtc": ended.isoformat().replace("+00:00", "Z"),
                "durationSeconds": round((ended - task_started).total_seconds(), 3),
                "prompt": task.prompt,
                "agent": agent_run,
                "cliPayload": cli_payload,
                "responsePreview": "",
                "validationRuns": [],
                "diff": None,
                "notes": [],
                "failures": failed_checks,
                "tempWorkspace": str(temp_root) if temp_root is not None else None,
            }

        response_text = str(cli_payload.get("response", "") or "")
        response_passed, response_notes = evaluate_text_expectation(response_text, task.response)
        if response_passed:
            pass_reasons.extend(response_notes)
        else:
            failed_checks.extend(response_notes)

        for expectation in task.validation_commands:
            validation_run = run_process(expectation.command, workspace, timeout_seconds=300)
            validation_passed, validation_notes = evaluate_command_expectation(validation_run, expectation)
            validation_run["passed"] = validation_passed
            validation_run["notes"] = validation_notes
            validation_runs.append(validation_run)
            if validation_passed:
                pass_reasons.extend(validation_notes)
            else:
                failed_checks.extend([f"{' '.join(expectation.command)}: {note}" for note in validation_notes])

        if task.diff is not None:
            diff_stats = collect_diff_stats(workspace)
            diff_passed, diff_notes = evaluate_diff_expectation(diff_stats, task.diff)
            if diff_passed:
                pass_reasons.extend(diff_notes)
            else:
                failed_checks.extend(diff_notes)
        else:
            diff_stats = None

        passed = not failed_checks
        ended = utc_now()
        result = {
            "id": task.id,
            "name": task.name,
            "suite": task.suite,
            "suiteMembership": get_task_suite_membership(task, regression_tag),
            "sourcePath": to_rel_path(task.source_path),
            "workspace": to_rel_path(task.workspace),
            "profile": args.profile or task.profile,
            "thinking": args.thinking or task.thinking,
            "isolation": task.isolation,
            "passed": passed,
            "startedAtUtc": task_started.isoformat().replace("+00:00", "Z"),
            "completedAtUtc": ended.isoformat().replace("+00:00", "Z"),
            "durationSeconds": round((ended - task_started).total_seconds(), 3),
            "prompt": task.prompt,
            "agent": agent_run,
            "cliPayload": cli_payload,
            "responsePreview": textwrap.shorten(response_text.replace("\r", " ").replace("\n", " "), width=240, placeholder="..."),
            "validationRuns": validation_runs,
            "diff": diff_stats,
            "notes": pass_reasons,
            "failures": failed_checks,
            "tempWorkspace": str(temp_root) if temp_root is not None else None,
        }
        return result
    finally:
        if not args.keep_temp:
            remove_temp_dir(temp_root)


def git_commit_sha() -> str | None:
    result = run_process(["git", "rev-parse", "HEAD"], REPO_ROOT)
    if result["exitCode"] != 0:
        return None
    return result["stdout"].strip() or None


def build_suite_summaries(
    manifest: dict[str, Any],
    task_results: list[dict[str, Any]],
    regression_tag: str,
) -> list[dict[str, Any]]:
    suite_map: dict[str, list[dict[str, Any]]] = {}
    for result in task_results:
        for suite_id in result["suiteMembership"]:
            suite_map.setdefault(suite_id, []).append(result)

    summaries: list[dict[str, Any]] = []
    for raw_suite in manifest["suites"]:
        suite_id = str(raw_suite["id"])
        suite_results = suite_map.get(suite_id, [])
        cases_run = len(suite_results)
        passed = sum(1 for item in suite_results if item["passed"])
        score = None if cases_run == 0 else round(passed / cases_run, 4)
        status = "measured" if cases_run else "not-run"
        summary = (
            "No tasks were run."
            if cases_run == 0
            else f"{passed} of {cases_run} tasks passed."
        )
        summaries.append(
            {
                "id": suite_id,
                "name": raw_suite["name"],
                "purpose": raw_suite["purpose"],
                "primaryMetric": raw_suite["primaryMetric"],
                "status": status,
                "casesRun": cases_run,
                "score": score,
                "summary": summary,
                "sourcePath": raw_suite["sourcePath"],
                "releaseRequired": bool(raw_suite.get("releaseRequired", False)),
                "releaseMinimumCases": int(raw_suite.get("releaseMinimumCases", 0)),
            }
        )
    return summaries


def create_output_paths(args: argparse.Namespace) -> tuple[Path, Path]:
    timestamp = utc_now().strftime("%Y%m%dT%H%M%SZ")
    json_path = Path(args.output_json) if args.output_json else DEFAULT_RESULTS_DIR / f"run-{timestamp}.json"
    md_path = Path(args.output_md) if args.output_md else DEFAULT_RESULTS_DIR / f"run-{timestamp}.md"
    if not json_path.is_absolute():
        json_path = REPO_ROOT / json_path
    if not md_path.is_absolute():
        md_path = REPO_ROOT / md_path
    json_path.parent.mkdir(parents=True, exist_ok=True)
    md_path.parent.mkdir(parents=True, exist_ok=True)
    return json_path, md_path


def write_json(path: Path, payload: dict[str, Any]) -> None:
    path.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")


def format_percent(score: float | None) -> str:
    return "n/a" if score is None else f"{score * 100:.1f}%"


def write_markdown(path: Path, payload: dict[str, Any]) -> None:
    lines: list[str] = []
    lines.append("## Benchmark Results")
    lines.append("")
    lines.append("| Field | Value |")
    lines.append("| --- | --- |")
    lines.append(f"| Generated at | {payload['generatedAtUtc']} |")
    lines.append(f"| Release tag | {payload['results']['releaseTag'] or 'n/a'} |")
    lines.append(f"| Commit | {payload['results']['commit'] or 'n/a'} |")
    lines.append(f"| Runner | {payload['results']['runner']['command']} |")
    lines.append("")
    lines.append("| Suite | Purpose | Status | Cases | Score | Metric |")
    lines.append("| --- | --- | --- | ---: | ---: | --- |")
    for suite in payload["suites"]:
        lines.append(
            f"| {suite['name']} | {suite['purpose']} | {suite['status']} | "
            f"{suite['casesRun']} | {format_percent(suite['score'])} | {suite['primaryMetric']} |"
        )
    lines.append("")
    lines.append("| Task | Suite | Result | Notes |")
    lines.append("| --- | --- | --- | --- |")
    for task in payload["taskResults"]:
        note = "; ".join(task["failures"][:2] or ["passed"])
        lines.append(
            f"| {task['id']} | {task['suite']} | {'pass' if task['passed'] else 'fail'} | {note} |"
        )
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")


def refresh_latest_files(json_path: Path, md_path: Path) -> None:
    latest_json = json_path.parent / "latest.json"
    latest_md = md_path.parent / "latest.md"
    shutil.copyfile(json_path, latest_json)
    shutil.copyfile(md_path, latest_md)


def print_task_list(tasks: list[TaskDefinition], regression_tag: str) -> None:
    for task in tasks:
        suites = ", ".join(get_task_suite_membership(task, regression_tag))
        print(f"{task.id:28} {task.suite:22} [{suites}] {to_rel_path(task.source_path)}")


def run_preflight(
    args: argparse.Namespace,
    agent_base_command: list[str],
    agent_settings: AgentSettings,
) -> tuple[bool, str | None]:
    temp_root = create_temp_dir(prefix="nanoagent-benchmark-preflight-")
    try:
        agent_env = build_agent_env(agent_settings, system=args.system)
        command = build_agent_prompt_command(
            agent_base_command,
            "Reply with the single word OK.",
            "plan",
            agent_settings,
            include_yes=True,
            prompt_mode=args.agent_prompt_mode,
            system=args.system,
        )
        log_progress(f"Preflight command: {redact_command_string(command)}")
        result = run_process_streaming(
            command,
            temp_root,
            env=agent_env,
            timeout_seconds=max(1, int(args.preflight_timeout)),
            prefix="preflight",
        )
        cli_payload: dict[str, Any] | None = None
        try:
            cli_payload = parse_cli_json(result["stdout"])
        except RuntimeError:
            cli_payload = None

        agent_error = parse_agent_error(cli_payload)
        if result["exitCode"] == 0 and agent_error is None:
            return True, None

        details = agent_error
        if result.get("timedOut"):
            details = f"preflight timed out after {args.preflight_timeout} seconds"
        if not details:
            stderr = str(result.get("stderr", "") or "").strip()
            stdout = str(result.get("stdout", "") or "").strip()
            details = stderr or stdout or f"exit code {result['exitCode']}"

        return False, details
    finally:
        remove_temp_dir(temp_root)


def main() -> int:
    args = parse_args()
    manifest_path = resolve_repo_path(args.manifest)
    manifest = load_manifest(manifest_path)
    regression_tag = string_or_default(manifest.get("regressionTag"), "regression")
    tasks = load_tasks(manifest, manifest_path)

    if args.list:
        print_task_list(tasks, regression_tag)
        return 0

    selected = select_tasks(tasks, set(args.suite or []), set(args.task or []), regression_tag)
    if not args.all and not args.suite and not args.task:
        selected = tasks

    if not selected:
        print("No benchmark tasks matched the provided filters.", file=sys.stderr)
        return 2

    if args.dry_run:
        print_task_list(selected, regression_tag)
        return 0

    agent_base_command = build_agent_command(args)
    agent_settings = resolve_agent_settings(args)
    if args.system:
        log_progress("System mode enabled: using nanoai with --json, --profile, and positional prompt; provider/model/key env wiring is bypassed.")
    if args.skip_preflight:
        log_progress("Skipping benchmark preflight because --skip-preflight was provided.")
    else:
        log_progress(f"Running benchmark preflight for {len(selected)} selected task(s).")
        preflight_ok, preflight_error = run_preflight(args, agent_base_command, agent_settings)
        if not preflight_ok:
            log_progress("Benchmark preflight failed before any tasks were measured.")
            print(
                "Benchmark preflight failed before any tasks were measured.",
                file=sys.stderr,
                flush=True,
            )
            if preflight_error:
                print(preflight_error, file=sys.stderr, flush=True)
            print(
                "Provide valid provider credentials, or pass --provider-key / set NANOAGENT_PROVIDER_AUTH_KEY, then rerun.",
                file=sys.stderr,
                flush=True,
            )
            return 2
        log_progress("Benchmark preflight passed.")

    task_results: list[dict[str, Any]] = []
    total_selected = len(selected)
    for task_index, task in enumerate(selected, start=1):
        profile = args.profile or task.profile
        log_progress(
            f"Task {task_index}/{total_selected} started: {task.id} "
            f"({task.name}) [suite={task.suite}, profile={profile}]"
        )
        result = run_task(task, args, agent_base_command, regression_tag, agent_settings)
        task_results.append(result)
        log_progress(
            f"Task {task_index}/{total_selected} finished: {task.id} - "
            f"{summarize_task_result(result)}"
        )

    suite_summaries = build_suite_summaries(manifest, task_results, regression_tag)

    json_path, md_path = create_output_paths(args)
    generated_at = utc_now().isoformat().replace("+00:00", "Z")
    runner_command = redact_command_string([sys.executable, "benchmarks/scripts/run_benchmarks.py", *sys.argv[1:]])
    payload = {
        "generatedAtUtc": generated_at,
        "manifestPath": to_rel_path(manifest_path),
        "resultsPath": to_rel_path(json_path),
        "releaseAssetBaseName": manifest.get("releaseAssetBaseName", "NanoAgent-benchmarks"),
        "resultsPresent": True,
        "results": {
            "status": "measured",
            "publishedAt": utc_now().date().isoformat(),
            "releaseTag": args.release_tag,
            "commit": git_commit_sha(),
            "runner": {
                "name": "benchmarks/scripts/run_benchmarks.py",
                "command": runner_command,
                "judgeModel": None,
            },
        },
        "notes": [],
        "suites": suite_summaries,
        "taskResults": task_results,
    }

    write_json(json_path, payload)
    write_markdown(md_path, payload)
    refresh_latest_files(json_path, md_path)

    total = len(task_results)
    passed = sum(1 for item in task_results if item["passed"])
    print(f"Benchmarks complete: {passed}/{total} tasks passed.", flush=True)
    print(f"JSON: {json_path}", flush=True)
    print(f"Markdown: {md_path}", flush=True)

    return 0 if passed == total else 1


if __name__ == "__main__":
    raise SystemExit(main())
