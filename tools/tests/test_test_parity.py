"""Tests für tools/test_parity.py gegen ein künstliches Android- und C#-Verzeichnis."""

from __future__ import annotations

import contextlib
import io
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

import test_parity  # noqa: E402

KOTLIN_TWO_TESTS = """
class RefinementTest {
    @Test fun a() {}
    @Test
    fun b() {}
    @TestOnly fun helper() {}
}
"""

CSHARP_TWO_CASES = """
namespace WhisperLoom.Core.Tests.Contracts;
public class RefinementTest
{
    [Fact] public void A() {}
    [Theory]
    [InlineData(1)]
    public void B(int x) {}
}
"""

CSHARP_ONE_CASE = """
namespace WhisperLoom.Core.Tests.Contracts;
public class RefinementTest
{
    [Fact] public void A() {}
}
"""

KOTLIN_THREE_CLASSES = """
class WavEncoderTest {
    @Test fun a() { val s = "class Fake { @Test }" }
    @Test fun b() {}
    @Test fun c() {}
}

class WavHeaderTest {
    @Test fun a() {}
    // @Test fun auskommentiert() {}
    @Test fun b() {}
}

class WavSamplesTest {
    @Test fun a() {}
}
"""

KOTLIN_HELPER_FIRST = """
private data class Lookup(val a: Int)

private class FakeLookup : ServerModelLookup {
    override fun find(id: String) = null
}

class AccessResolverTest {
    private data class P(val x: Int)
    @Test fun a() { check(P(1)) }
    @Test fun b() {}
}
"""

CSHARP_HELPER_FIRST = """
namespace WhisperLoom.Core.Tests;

internal sealed class FakeLookup
{
    public void Helper() { var s = "[Fact] {"; }
}

public class AccessResolverTest
{
    [Fact] public void A() {}
    private sealed class Nested { }
    [Fact] public void B() {}
}
"""

CSHARP_PARTIAL = """
namespace WhisperLoom.Core.Tests;
public partial class EngineTest
{
    [Fact] public void X() {}
}
"""

CSHARP_SKIP_AND_TRAIT = """
namespace WhisperLoom.Core.Tests.Contracts;
public class RefinementTest
{
    // [Fact] public void Kommentar() {}
    [Fact(Skip = "später")] public void Uebersprungen() {}
    [Trait("Category", "LocalStt"), Fact] public void MitTrait() {}
}
"""

HEADER = "| Kotlin-Klasse | C#-Klasse | Status | Begründung |\n|---|---|---|---|\n"


class TestParity(unittest.TestCase):
    def setUp(self) -> None:
        self._tmp = tempfile.TemporaryDirectory()
        self.root = Path(self._tmp.name)
        self.android = self.root / "android"
        self.tests = self.root / "tests"
        self.write(self.android / "app/src/test/java/com/x/RefinementTest.kt", KOTLIN_TWO_TESTS)
        self.write(self.android / "app/src/test/java/com/x/api/HttpTest.kt", "class HttpTest {\n    @Test fun a() {}\n}\n")

    def tearDown(self) -> None:
        self._tmp.cleanup()

    @staticmethod
    def write(path: Path, text: str) -> Path:
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text, encoding="utf-8")
        return path

    def run_main(self, *mappings: str) -> tuple[int, str]:
        paths = [self.write(self.root / f"map{i}.md", HEADER + m) for i, m in enumerate(mappings)]
        args = ["--android", str(self.android), "--tests-dir", str(self.tests)]
        for p in paths:
            args += ["--mapping", str(p)]
        out = io.StringIO()
        with contextlib.redirect_stdout(out):
            code = test_parity.main(args)
        return code, out.getvalue()

    def test_portiert_mit_genug_faellen_ist_ok(self) -> None:
        self.write(self.tests / "Core/RefinementTest.cs", CSHARP_TWO_CASES)

        code, out = self.run_main("| RefinementTest | RefinementTest | portiert | |\n")

        self.assertEqual(0, code, out)
        self.assertIn("| RefinementTest | 2 | WhisperLoom.Core.Tests.Contracts.RefinementTest | 2 |", out)
        self.assertTrue(out.strip().splitlines()[-1].startswith("TEST-PARITY OK"))

    def test_zu_wenige_faelle_scheitert(self) -> None:
        self.write(self.tests / "Core/RefinementTest.cs", CSHARP_ONE_CASE)

        code, out = self.run_main("| RefinementTest | RefinementTest | portiert | |\n")

        self.assertEqual(1, code)
        self.assertIn("1 C#-Fälle < 2 @Test", out)
        self.assertTrue(out.strip().splitlines()[-1].startswith("TEST-PARITY FAIL"))

    def test_fehlende_csharp_klasse_scheitert(self) -> None:
        code, out = self.run_main("| RefinementTest | RefinementTest | portiert | |\n")

        self.assertEqual(1, code)
        self.assertIn("nicht gefunden", out)

    def test_qualifizierter_name_muss_passen(self) -> None:
        self.write(self.tests / "Core/RefinementTest.cs", CSHARP_TWO_CASES)

        code, out = self.run_main("| RefinementTest | Falscher.Namespace.RefinementTest | portiert | |\n")

        self.assertEqual(1, code, out)

    def test_ausnahme_braucht_begruendung(self) -> None:
        code, out = self.run_main("| api/HttpTest | — | ausgenommen | |\n")
        self.assertEqual(1, code)
        self.assertIn("ohne Begründung", out)

        code, out = self.run_main("| api/HttpTest | — | ausgenommen | HttpURLConnection-spezifisch |\n")
        self.assertEqual(0, code, out)
        self.assertIn("exceptions=1", out)

    def test_unbekannte_und_mehrdeutige_kotlin_klasse(self) -> None:
        self.write(self.android / "app/src/testDebug/java/com/y/RefinementTest.kt", KOTLIN_TWO_TESTS)
        self.write(self.tests / "Core/RefinementTest.cs", CSHARP_TWO_CASES)

        code, out = self.run_main("| RefinementTest | RefinementTest | portiert | |\n| GibtEsNicht | X | portiert | |\n")

        self.assertEqual(1, code)
        self.assertIn("mehrdeutig", out)
        self.assertIn("GibtEsNicht: Kotlin-Klasse nicht gefunden", out)

    def test_doppelt_ueber_mehrere_mappings(self) -> None:
        self.write(self.tests / "Core/RefinementTest.cs", CSHARP_TWO_CASES)
        row = "| RefinementTest | RefinementTest | portiert | |\n"

        code, out = self.run_main(row, row)

        self.assertEqual(1, code)
        self.assertIn("doppelt", out)

    def test_unbekannter_status_und_leere_datei(self) -> None:
        code, out = self.run_main("| RefinementTest | RefinementTest | vielleicht | |\n")
        self.assertEqual(1, code)
        self.assertIn("unbekannter Status", out)

        empty = self.write(self.root / "leer.md", "# nichts\n")
        rows, errors = test_parity.parse_mapping(empty)
        self.assertEqual([], rows)
        self.assertTrue(errors)

    def test_mehrere_kotlin_klassen_je_datei(self) -> None:
        self.write(self.android / "app/src/test/java/com/x/WavEncoderTest.kt", KOTLIN_THREE_CLASSES)
        self.write(self.tests / "Core/WavHeaderTest.cs", CSHARP_TWO_CASES.replace("RefinementTest", "WavHeaderTest"))

        code, out = self.run_main("| WavHeaderTest | WavHeaderTest | portiert | |\n")

        self.assertEqual(0, code, out)
        self.assertIn("| WavHeaderTest | 2 |", out)

    def test_hilfsklasse_vor_der_testklasse_und_verschachtelt(self) -> None:
        self.write(self.android / "app/src/test/java/com/x/AccessResolverTest.kt", KOTLIN_HELPER_FIRST)
        self.write(self.tests / "Core/AccessResolverTest.cs", CSHARP_HELPER_FIRST)

        code, out = self.run_main("| AccessResolverTest | AccessResolverTest | portiert | |\n")

        self.assertEqual(0, code, out)
        self.assertIn("| AccessResolverTest | 2 | WhisperLoom.Core.Tests.AccessResolverTest | 2 |", out)

    def test_partial_und_mehrere_csharp_klassen_werden_summiert(self) -> None:
        self.write(self.android / "app/src/test/java/com/x/EngineTest.kt", "class EngineTest {\n" + "@Test fun a() {}\n" * 3 + "}")
        self.write(self.tests / "Core/EngineTest.Run.cs", CSHARP_PARTIAL)
        self.write(self.tests / "Core/EngineTest.Refine.cs", CSHARP_PARTIAL)
        self.write(self.tests / "Core/EngineExtraTest.cs", CSHARP_ONE_CASE.replace("RefinementTest", "EngineExtraTest"))

        code, out = self.run_main("| EngineTest | EngineTest, EngineExtraTest | portiert | |\n")

        self.assertEqual(0, code, out)
        self.assertIn("| EngineTest | 3 | WhisperLoom.Core.Tests.EngineTest, WhisperLoom.Core.Tests.Contracts.EngineExtraTest | 3 |", out)

    def test_auskommentierte_und_uebersprungene_faelle_zaehlen_nicht(self) -> None:
        self.write(self.tests / "Core/RefinementTest.cs", CSHARP_SKIP_AND_TRAIT)

        code, out = self.run_main("| RefinementTest | RefinementTest | portiert | |\n")

        self.assertEqual(1, code)
        self.assertIn("1 C#-Fälle < 2 @Test", out)

    def test_soll_liste_meldet_fehlende_klassen(self) -> None:
        self.write(self.tests / "Core/RefinementTest.cs", CSHARP_TWO_CASES)
        required = self.write(self.root / "soll.txt", "# Kommentar\nRefinementTest\napi/HttpTest  # fehlt\n")
        mapping = self.write(self.root / "map.md", HEADER + "| RefinementTest | RefinementTest | portiert | |\n")
        args = ["--android", str(self.android), "--tests-dir", str(self.tests), "--mapping", str(mapping),
                "--require-file", str(required)]
        out = io.StringIO()
        with contextlib.redirect_stdout(out):
            code = test_parity.main(args)

        self.assertEqual(1, code)
        self.assertIn("Soll-Klasse api/HttpTest: fehlt in allen Mapping-Dateien", out.getvalue())
        self.assertNotIn("Soll-Klasse RefinementTest", out.getvalue())

    def test_doppelt_auch_bei_anderer_schreibweise(self) -> None:
        self.write(self.tests / "Core/RefinementTest.cs", CSHARP_TWO_CASES)

        code, out = self.run_main("| RefinementTest | RefinementTest | portiert | |\n| x/RefinementTest | RefinementTest | portiert | |\n")

        self.assertEqual(1, code)
        self.assertIn("doppelt", out)

    def test_bin_und_obj_werden_ignoriert(self) -> None:
        self.write(self.tests / "Core/RefinementTest.cs", CSHARP_TWO_CASES)
        self.write(self.tests / "Core/obj/RefinementTest.cs", CSHARP_TWO_CASES)

        code, out = self.run_main("| RefinementTest | RefinementTest | portiert | |\n")

        self.assertEqual(0, code, out)


if __name__ == "__main__":
    unittest.main()
