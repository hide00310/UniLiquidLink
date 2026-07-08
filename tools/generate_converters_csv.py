import pandas as pd
from pathlib import Path

script_dir = Path(__file__).resolve().parent

def main():
    dfs = pd.read_excel(f"{script_dir}/converters.xlsx", sheet_name=None)
    settings = dfs["Settings"]
    for r in settings.to_dict(orient='records'):
        dfs[r["name"]].to_csv(f"{script_dir}/{r['out']}/{r['name']}.csv")

main()