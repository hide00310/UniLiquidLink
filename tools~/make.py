from pathlib import Path
import sys
import importlib.util
from glob import glob
import os
import shutil

script_dir = Path(__file__).resolve().parent

def make():
    for module_path in [
        # f"{script_dir}/generate_converters_csv",
        f"{script_dir}/generate_converters",
        f"{script_dir}/generate_fallback_converters",
        f"{script_dir}/generate_schema",
        f"{script_dir}/generate_docs",
        f"{script_dir}/generate_python_docs",
        f"{script_dir}/generate_class_diagram",
        # f"{script_dir}/../../SceneCreator\generate_class",
    ]:
        spec = importlib.util.spec_from_file_location("m", module_path + ".py")
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        module.main()

def clean_meta():
    for path in [
        f"{script_dir}",
        f"{script_dir}/tools~",
        f"{script_dir}/Docs~",
        f"{script_dir}/Samples~",
    ]:
        for file in glob(f"{path}/**/*.meta", recursive=True):
            # Prepend the \\?\ extended-length path prefix so os.remove
            # can handle paths at/over Windows' 260-char MAX_PATH limit
            # (nuget analyzer packages nest deep enough to hit this).
            long_path = "\\\\?\\" + str(Path(file).resolve())
            try:
                os.remove(long_path)
            except FileNotFoundError:
                pass

# make()
clean_meta()