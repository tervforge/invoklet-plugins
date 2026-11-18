#!/usr/bin/env python3
"""Writes a static NuGet v3 feed of plugin packages that any static host (GitHub Pages) can serve without sign-in.

The feed is a service index (v3/index.json), a package base address (v3/flatcontainer/<id>/index.json and the .nupkg files) and a
search endpoint that is one JSON file listing every package: a static host ignores the query string, and Invoklet filters the
listing itself. Packages come from a local folder and, with --mirror, from every version another NuGet v3 feed already has for
the same ids, so each deployment republishes the versions released before it.

    python3 build/static_feed.py --base-url https://tervforge.github.io/invoklet-plugins/ --packages artifacts/packages --out site
"""

from __future__ import annotations

import argparse
import base64
import io
import json
import os
import shutil
import sys
import urllib.error
import urllib.request
import xml.etree.ElementTree as ElementTree
import zipfile
from pathlib import Path

SEARCH_TYPES = ["SearchQueryService", "SearchQueryService/3.0.0-beta", "SearchQueryService/3.0.0-rc", "SearchQueryService/3.5.0"]


def read_nuspec(package: bytes) -> dict:
    """The id, version and listing metadata of a .nupkg, from the nuspec at its root."""
    with zipfile.ZipFile(io.BytesIO(package)) as archive:
        names = [name for name in archive.namelist() if "/" not in name and name.endswith(".nuspec")]
        if len(names) != 1:
            raise ValueError("a package must have exactly one nuspec at its root")
        root = ElementTree.fromstring(archive.read(names[0]))
    metadata = next(element for element in root if element.tag.endswith("metadata"))
    values = {element.tag.split("}")[-1]: (element.text or "").strip() for element in metadata}
    if not values.get("id") or not values.get("version"):
        raise ValueError("the nuspec has no id or version")
    return values


def request(url: str, token: str | None) -> urllib.request.Request:
    headers = {"User-Agent": "invoklet-static-feed"}
    if token:
        headers["Authorization"] = "Basic " + base64.b64encode(f"token:{token}".encode()).decode()
    return urllib.request.Request(url, headers=headers)


def fetch(url: str, token: str | None) -> bytes | None:
    """The body of a GET, or None for 404; any other failure stops the script."""
    try:
        with urllib.request.urlopen(request(url, token), timeout=60) as response:
            return response.read()
    except urllib.error.HTTPError as error:
        if error.code == 404:
            return None
        raise SystemExit(f"error: {url} answered {error.code}")


def mirror(index_url: str, ids: set[str], token: str | None) -> list[bytes]:
    """Every version the feed at index_url has of the given ids."""
    index = json.loads(fetch(index_url, token) or b"{}")
    base = next((resource["@id"] for resource in index.get("resources", [])
                 if resource.get("@type", "").startswith("PackageBaseAddress/")), None)
    if base is None:
        raise SystemExit(f"error: {index_url} has no package base address")
    base = base.rstrip("/") + "/"
    packages = []
    for package_id in sorted(ids):
        listing = fetch(f"{base}{package_id}/index.json", token)
        for version in json.loads(listing)["versions"] if listing else []:
            body = fetch(f"{base}{package_id}/{version}/{package_id}.{version}.nupkg", token)
            if body is None:
                raise SystemExit(f"error: {package_id} {version} is listed but cannot be downloaded")
            packages.append(body)
    return packages


def write_feed(base_url: str, packages: list[bytes], out: Path) -> None:
    base_url = base_url.rstrip("/") + "/"
    flat = out / "v3" / "flatcontainer"
    found: dict[str, dict[str, dict]] = {}
    for body in packages:
        nuspec = read_nuspec(body)
        package_id, version = nuspec["id"].lower(), nuspec["version"].lower()
        found.setdefault(package_id, {})[version] = nuspec
        folder = flat / package_id / version
        folder.mkdir(parents=True, exist_ok=True)
        (folder / f"{package_id}.{version}.nupkg").write_bytes(body)

    def order(version: str):
        core, _, prerelease = version.partition("-")
        return [int(part) for part in core.split(".")], prerelease == "", prerelease

    data = []
    for package_id, versions in sorted(found.items()):
        ordered = sorted(versions, key=order)
        (flat / package_id / "index.json").write_text(json.dumps({"versions": ordered}), encoding="utf-8")
        latest = versions[ordered[-1]]
        data.append({
            "@id": f"{base_url}v3/flatcontainer/{package_id}/index.json",
            "id": latest["id"],
            "version": ordered[-1],
            "title": latest.get("title") or latest["id"],
            "description": latest.get("description", ""),
            "authors": [latest.get("authors", "")],
            "tags": latest.get("tags", "").split(),
            "totalDownloads": 0,
            "versions": [{"version": version, "downloads": 0, "@id": f"{base_url}v3/flatcontainer/{package_id}/index.json"}
                         for version in ordered],
        })

    resources = [{"@id": f"{base_url}v3/flatcontainer/", "@type": "PackageBaseAddress/3.0.0"}]
    resources += [{"@id": f"{base_url}v3/search.json", "@type": kind} for kind in SEARCH_TYPES]
    (out / "v3").mkdir(parents=True, exist_ok=True)
    (out / "v3" / "index.json").write_text(json.dumps({"version": "3.0.0", "resources": resources}, indent=2), encoding="utf-8")
    (out / "v3" / "search.json").write_text(json.dumps({"totalHits": len(data), "data": data}), encoding="utf-8")
    # GitHub Pages would otherwise run Jekyll over the folder.
    (out / ".nojekyll").write_text("", encoding="utf-8")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--base-url", required=True, help="where the feed will be served, such as https://owner.github.io/repo/")
    parser.add_argument("--packages", required=True, type=Path, help="folder of .nupkg files to publish")
    parser.add_argument("--mirror", help="a NuGet v3 index.json whose versions of the same ids are republished too")
    parser.add_argument("--out", required=True, type=Path, help="output folder, replaced")
    arguments = parser.parse_args()

    local = [path.read_bytes() for path in sorted(arguments.packages.glob("*.nupkg"))]
    if not local:
        raise SystemExit(f"error: no .nupkg files in {arguments.packages}")
    packages = list(local)
    if arguments.mirror:
        ids = {read_nuspec(body)["id"].lower() for body in local}
        packages = mirror(arguments.mirror, ids, os.environ.get("NUGET_AUTH_TOKEN")) + local

    if arguments.out.exists():
        shutil.rmtree(arguments.out)
    write_feed(arguments.base_url, packages, arguments.out)
    print(f"Wrote a feed of {len(packages)} packages to {arguments.out}", file=sys.stderr)
    return 0


if __name__ == "__main__":
    sys.exit(main())
