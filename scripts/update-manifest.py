"""Prepend a version entry to the Jellyfin plugin repository manifest."""
import argparse
import json


def update_manifest(path, entry):
    with open(path, encoding="utf-8") as f:
        manifest = json.load(f)
    plugin = manifest[0]
    versions = [v for v in plugin.get("versions", []) if v.get("version") != entry["version"]]
    plugin["versions"] = [entry] + versions
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        json.dump(manifest, f, indent=2, ensure_ascii=False)
        f.write("\n")


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--manifest", required=True)
    parser.add_argument("--version", required=True)
    parser.add_argument("--target-abi", required=True)
    parser.add_argument("--source-url", required=True)
    parser.add_argument("--checksum", required=True)
    parser.add_argument("--changelog", required=True)
    parser.add_argument("--timestamp", required=True)
    args = parser.parse_args(argv)
    update_manifest(args.manifest, {
        "version": args.version,
        "changelog": args.changelog,
        "targetAbi": args.target_abi,
        "sourceUrl": args.source_url,
        "checksum": args.checksum,
        "timestamp": args.timestamp,
    })


if __name__ == "__main__":
    main()
