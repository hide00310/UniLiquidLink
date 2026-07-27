
from pathlib import Path
import sys
import importlib.util
from glob import glob
import os
import shutil

script_dir = Path(__file__).resolve().parent

paths = [
    f"{script_dir}",
    f"{script_dir}/../Docs~",
    f"{script_dir}/../Samples~",
    f"{script_dir}/../Python~",
    f"{script_dir}/../",
]
def clean_meta():
    for path in paths:
        for file in glob(f"{path}/**/*.meta", recursive=True):
            # Prepend the \\?\ extended-length path prefix so os.remove
            # can handle paths at/over Windows' 260-char MAX_PATH limit
            # (nuget analyzer packages nest deep enough to hit this).
            long_path = "\\\\?\\" + str(Path(file).resolve())
            try:
                os.remove(long_path)
                print(file)
            except FileNotFoundError:
                pass

def touch_meta():
    for path in paths:
        for file in glob(f"{path}/**/*.meta", recursive=True):
            # Prepend the \\?\ extended-length path prefix so os.utime
            # can handle paths at/over Windows' 260-char MAX_PATH limit
            # (nuget analyzer packages nest deep enough to hit this).
            long_path = "\\\\?\\" + str(Path(file).resolve())
            try:
                os.utime(long_path, None)
                print(file)
            except FileNotFoundError:
                pass

# clean_meta()
touch_meta()
