import subprocess


def preview_branch(branch_name: str) -> subprocess.CompletedProcess[str]:
    command = f"git show {branch_name} --stat"
    return subprocess.run(
        command,
        shell=True,
        check=False,
        capture_output=True,
        text=True,
    )
