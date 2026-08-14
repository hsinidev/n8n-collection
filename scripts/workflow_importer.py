#!/usr/bin/env python3
"""
========================================================================================
Enterprise n8n Bulk Workflow Importer
Author: Hsini Mohamed (https://hsini.dev | https://github.com/hsinidev)
========================================================================================
Automates the bulk import of thousands of workflow templates into a running n8n instance
using the n8n Public REST API or containerized CLI interface.
"""

import os
import sys
import json
import argparse
import urllib.request
import urllib.error
from pathlib import Path

BASE_DIR = Path(__file__).resolve().parent.parent
WORKFLOWS_DIR = BASE_DIR / "workflows"

def import_via_api(api_url, api_key, category_filter=None, limit=None):
    api_url = api_url.rstrip("/")
    headers = {
        "X-N8N-API-KEY": api_key,
        "Content-Type": "application/json"
    }
    
    print(f"[*] Connecting to n8n API at: {api_url}")
    
    files = list(WORKFLOWS_DIR.glob("**/*.json"))
    if category_filter:
        files = [f for f in files if category_filter.lower() in str(f).lower()]
        
    if limit:
        files = files[:limit]
        
    print(f"[*] Found {len(files)} workflow templates to import...")
    
    imported = 0
    errors = 0
    
    for idx, fpath in enumerate(files, 1):
        try:
            with open(fpath, "r", encoding="utf-8") as f:
                wf_data = json.load(f)
                
            payload = {
                "name": wf_data.get("name", fpath.stem),
                "nodes": wf_data.get("nodes", []),
                "connections": wf_data.get("connections", {}),
                "settings": wf_data.get("settings", {}),
            }
            
            req = urllib.request.Request(
                f"{api_url}/api/v1/workflows",
                data=json.dumps(payload).encode("utf-8"),
                headers=headers,
                method="POST"
            )
            
            with urllib.request.urlopen(req) as resp:
                if resp.status in (200, 201):
                    imported += 1
                    if imported % 50 == 0 or imported == len(files):
                        print(f"    -> Successfully imported {imported}/{len(files)} workflows...")
                        
        except urllib.error.HTTPError as e:
            errors += 1
            if errors <= 5:
                print(f"[!] HTTP Error importing {fpath.name}: {e.code} - {e.reason}")
        except Exception as e:
            errors += 1
            if errors <= 5:
                print(f"[!] Error importing {fpath.name}: {e}")
                
    print(f"[OK] Import completed: {imported} imported successfully, {errors} errors.")

def main():
    parser = argparse.ArgumentParser(description="n8n Bulk Workflow Importer")
    parser.add_argument("--api-url", default="http://localhost:5678", help="n8n Base URL (default: http://localhost:5678)")
    parser.add_argument("--api-key", help="n8n API Key (Generated in n8n Settings > API)")
    parser.add_argument("--category", help="Optional category filter (e.g., AIAgents, FinTech, DevOpsCloud)")
    parser.add_argument("--limit", type=int, help="Limit number of workflows to import")
    
    args = parser.parse_args()
    
    if not args.api_key:
        print("[!] Note: To import via REST API, provide --api-key <YOUR_N8N_API_KEY>")
        print("[*] Example: python scripts/workflow_importer.py --api-key n8n_api_key_xxx --category AIAgents")
        print("\n[*] For Docker CLI local bulk import, you can also run inside the n8n container:")
        print("    docker compose exec n8n n8n import:workflow --separate --input=/home/node/workflows")
        return
        
    import_via_api(args.api_url, args.api_key, args.category, args.limit)

if __name__ == "__main__":
    main()
