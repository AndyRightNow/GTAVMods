"""Run the normal CLI supervisor, identifying its actual interpreter for profiling.

Usage: uv run --frozen python tools/profiling/profile_policy.py run
       tools/profiling/policy_workload.py --process PID --output FRESH_DIRECTORY
       --timeout 180 --stuck-timeout 120 --check-timeout 10
"""
import os
from gta_use.__main__ import main

if __name__ == "__main__":
    os.environ["GTA_USE_PROFILE_SUPERVISOR"] = str(os.getpid())
    raise SystemExit(main())
