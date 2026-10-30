"""Shared input checks for bounded policy operations."""
import math


def _bounds(**values):
    for name, (value, low, high) in values.items():
        if isinstance(value, bool) or not math.isfinite(value) or not low <= value <= high:
            raise ValueError(f"{name} must be finite and between {low} and {high}.")


def _identity(entity):
    values = (entity["handle"], entity["model"])
    if (any(isinstance(v, bool) or not isinstance(v, int) for v in values)
            or not 1 <= values[0] <= 2147483647 or not -2147483648 <= values[1] <= 2147483647):
        raise ValueError("Use an observed positive entity handle and signed 32-bit model hash.")
    return values
