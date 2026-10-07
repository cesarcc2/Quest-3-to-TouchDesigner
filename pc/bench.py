"""GPU benchmark for Depth Anything V2 (ONNX Runtime + DirectML).

Usage: python pc/bench.py [models_dir]
Prints inference fps per model/precision/input size so we can pick what the GPU sustains.
"""
import sys
import time
from pathlib import Path

import numpy as np
import onnxruntime as ort

MODELS = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(__file__).parent / "models"
SIZES = [364, 518]          # Depth Anything input sizes (multiples of 14); 518 = native
WARMUP, RUNS = 5, 40


def bench(path: Path, size: int) -> float:
    opts = ort.SessionOptions()
    opts.graph_optimization_level = ort.GraphOptimizationLevel.ORT_ENABLE_ALL
    sess = ort.InferenceSession(str(path), opts, providers=["DmlExecutionProvider", "CPUExecutionProvider"])
    inp = sess.get_inputs()[0]
    dtype = np.float16 if "float16" in inp.type else np.float32
    x = np.random.rand(1, 3, size, size).astype(dtype)
    for _ in range(WARMUP):
        sess.run(None, {inp.name: x})
    t = time.perf_counter()
    for _ in range(RUNS):
        sess.run(None, {inp.name: x})
    return RUNS / (time.perf_counter() - t)


if __name__ == "__main__":
    print(f"onnxruntime {ort.__version__}, providers {ort.get_available_providers()}")
    for path in sorted(MODELS.glob("da2_*.onnx")):
        for size in SIZES:
            try:
                print(f"{path.stem:16s} {size}px  {bench(path, size):6.1f} fps", flush=True)
            except Exception as e:  # report and continue with the next combo
                print(f"{path.stem:16s} {size}px  FAILED: {str(e).splitlines()[0][:120]}", flush=True)
