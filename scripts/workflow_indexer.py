#!/usr/bin/env python3
"""
========================================================================================
Enterprise n8n Workflow Catalog Indexer & Search Engine
Author: Hsini Mohamed (https://hsini.dev | https://github.com/hsinidev)
========================================================================================
Scans and indexes thousands of n8n JSON workflow templates into a lightweight SQLite DB
and search index for ultra-fast querying by category, node type, domain, or keyword.
"""

import os
import json
import sqlite3
import time
from pathlib import Path

BASE_DIR = Path(__file__).resolve().parent.parent
WORKFLOWS_DIR = BASE_DIR / "workflows"
OUTPUT_DB = BASE_DIR / "workflows_index.sqlite"
OUTPUT_JSON = BASE_DIR / "workflows_catalog.json"

def build_index():
    start_time = time.time()
    print(f"[*] Indexing workflows from: {WORKFLOWS_DIR}")
    
    # Initialize SQLite database
    conn = sqlite3.connect(OUTPUT_DB)
    cursor = conn.cursor()
    
    cursor.execute("""
        CREATE TABLE IF NOT EXISTS workflows (
            id TEXT PRIMARY KEY,
            name TEXT,
            category TEXT,
            filepath TEXT,
            node_count INTEGER,
            node_types TEXT,
            tags TEXT,
            created_at TEXT,
            author TEXT
        )
    """)
    cursor.execute("CREATE INDEX IF NOT EXISTS idx_category ON workflows(category);")
    cursor.execute("CREATE INDEX IF NOT EXISTS idx_name ON workflows(name);")
    cursor.execute("DELETE FROM workflows;")
    
    catalog_list = []
    category_counts = {}
    total_indexed = 0
    
    json_files = list(WORKFLOWS_DIR.glob("**/*.json"))
    total_files = len(json_files)
    print(f"[*] Found {total_files} workflow files to index...")
    
    batch = []
    for fpath in json_files:
        try:
            with open(fpath, "r", encoding="utf-8") as f:
                data = json.load(f)
                
            w_id = data.get("id", fpath.stem)
            w_name = data.get("name", fpath.stem)
            category = fpath.parent.name
            meta = data.get("meta", {})
            created_at = meta.get("createdAt", "")
            author = meta.get("owner", "Hsini Mohamed (https://hsini.dev)")
            tags = ",".join(data.get("tags", []))
            
            nodes = data.get("nodes", [])
            node_count = len(nodes)
            node_types = ",".join(list(set(n.get("type", "") for n in nodes if "type" in n)))
            
            rel_path = str(fpath.relative_to(BASE_DIR)).replace("\\", "/")
            
            batch.append((
                w_id,
                w_name,
                category,
                rel_path,
                node_count,
                node_types,
                tags,
                created_at,
                author
            ))
            
            category_counts[category] = category_counts.get(category, 0) + 1
            total_indexed += 1
            
            if len(batch) >= 1000:
                cursor.executemany("INSERT OR REPLACE INTO workflows VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)", batch)
                conn.commit()
                batch = []
                
        except Exception as e:
            print(f"[!] Error parsing {fpath}: {e}")
            
    if batch:
        cursor.executemany("INSERT OR REPLACE INTO workflows VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)", batch)
        conn.commit()
        
    conn.close()
    
    # Save a summary catalog JSON
    summary_data = {
        "title": "Enterprise n8n Workflow Collection",
        "author": "Hsini Mohamed",
        "website": "https://hsini.dev",
        "total_workflows": total_indexed,
        "total_categories": len(category_counts),
        "categories": category_counts,
        "indexed_at": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())
    }
    
    with open(OUTPUT_JSON, "w", encoding="utf-8") as f:
        json.dump(summary_data, f, indent=2)
        
    elapsed = time.time() - start_time
    print(f"[OK] Indexed {total_indexed} workflows across {len(category_counts)} categories in {elapsed:.2f}s!")
    print(f"[OK] SQLite Database: {OUTPUT_DB}")
    print(f"[OK] Summary Catalog: {OUTPUT_JSON}")

if __name__ == "__main__":
    build_index()
