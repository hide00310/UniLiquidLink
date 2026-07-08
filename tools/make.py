from pathlib import Path
import sys
import importlib.util

script_dir = Path(__file__).resolve().parent

for module_path in [
    f"{script_dir}/generate_converters_csv",
    f"{script_dir}/generate_converters",
    f"{script_dir}/generate_fallback_converters",
    f"{script_dir}/generate_schema",
    # f"{script_dir}/generate_docs",
    # f"{script_dir}/generate_python_docs",

    f"{script_dir}/../../SceneCreator\generate_class",
]:
    spec = importlib.util.spec_from_file_location("m", module_path + ".py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    module.main()
