"""Review N-19: NuGet nur aus nuget.org (Source-Mapping) und feste Paketstände (Lockfiles, CI im locked mode)."""
from __future__ import annotations

import re
import unittest
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
NUGET_ORG = "https://api.nuget.org/v3/index.json"


def projects_with_packages() -> list[Path]:
    found = []
    for csproj in sorted((ROOT / "src").rglob("*.csproj")) + sorted((ROOT / "tests").rglob("*.csproj")):
        if "<PackageReference" in csproj.read_text(encoding="utf-8"):
            found.append(csproj)
    return found


class NugetSupplyChainTest(unittest.TestCase):
    def test_nuget_config_clears_sources_and_maps_everything_to_nuget_org(self) -> None:
        path = ROOT / "nuget.config"
        self.assertTrue(path.is_file(), "nuget.config fehlt")
        root = ET.parse(path).getroot()
        sources = root.find("packageSources")
        self.assertIsNotNone(sources)
        self.assertEqual("clear", sources[0].tag, "<clear/> muss zuerst stehen (keine Quellen aus Benutzer-/Maschinen-Konfig)")
        adds = [(a.get("key"), a.get("value")) for a in sources.findall("add")]
        self.assertEqual([("nuget.org", NUGET_ORG)], adds)
        mapping = root.find("packageSourceMapping")
        self.assertIsNotNone(mapping, "packageSourceMapping fehlt")
        patterns = {(s.get("key"), p.get("pattern")) for s in mapping.findall("packageSource") for p in s.findall("package")}
        self.assertEqual({("nuget.org", "*")}, patterns)

    def test_lock_files_are_on_and_present_for_every_project_with_packages(self) -> None:
        props = (ROOT / "Directory.Build.props").read_text(encoding="utf-8")
        self.assertRegex(props, r"<RestorePackagesWithLockFile>\s*true\s*</RestorePackagesWithLockFile>")
        projects = projects_with_packages()
        self.assertGreaterEqual(len(projects), 3)
        for csproj in projects:
            with self.subTest(project=csproj.name):
                self.assertTrue((csproj.parent / "packages.lock.json").is_file(), f"packages.lock.json fehlt bei {csproj.name}")

    def test_ci_restores_in_locked_mode(self) -> None:
        ci = (ROOT / ".github/workflows/ci.yml").read_text(encoding="utf-8")
        restores = re.findall(r"run: dotnet restore[^\n]*", ci)
        self.assertTrue(restores)
        for line in restores:
            self.assertIn("--locked-mode", line)


if __name__ == "__main__":
    unittest.main()
