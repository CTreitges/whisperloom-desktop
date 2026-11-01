// Basis: whisperloom-android feat/simple-portal server/simple-whisper/static/app.js

// Richtlinie: Bei „Frei“ bzw. „Wie Firma“ gilt kein Wert → Wertfeld ausgrauen und sperren (ohne JS bleibt es nur aktiv).
function wertFeldAnpassen(stufe) {
  const zeile = stufe.closest(".einstellung");
  if (!zeile) return;
  const ohne = stufe.value === "free" || stufe.value === "inherit";
  zeile.classList.toggle("ohne-wert", ohne);
  zeile.querySelectorAll(".einstellung-wert input, .einstellung-wert select, .einstellung-wert textarea").forEach((el) => {
    el.disabled = ohne;
  });
}
// Bereiche mit data-zeigen-wenn="feld=wert" bzw. "feld!=wert" nur zeigen, wenn das Formularfeld passt.
function bedingteBereiche(formular) {
  formular.querySelectorAll("[data-zeigen-wenn]").forEach((bereich) => {
    const [, feld, nicht, wert] = bereich.dataset.zeigenWenn.match(/^(\w+)(!?)=(.*)$/);
    const aktuell = formular.elements[feld] ? formular.elements[feld].value : "";
    bereich.hidden = nicht ? aktuell === wert : aktuell !== wert;
  });
}
document.addEventListener("change", (e) => {
  if (e.target.matches("select.stufe")) wertFeldAnpassen(e.target);
  if (e.target.form) bedingteBereiche(e.target.form);
});
document.addEventListener("DOMContentLoaded", () => {
  document.querySelectorAll("select.stufe").forEach(wertFeldAnpassen);
  document.querySelectorAll("form").forEach(bedingteBereiche);
});
// Kopieren-Knopf: <button data-kopieren="<id>" data-kopiert="<Text>"> kopiert den Text des Elements (kein Inline-JS, CSP).
document.addEventListener("click", (e) => {
  const knopf = e.target.closest("[data-kopieren]");
  if (!knopf) return;
  const quelle = document.getElementById(knopf.dataset.kopieren);
  if (!quelle || !navigator.clipboard) return;
  navigator.clipboard.writeText(quelle.textContent.trim()).then(() => {
    const alt = knopf.textContent;
    knopf.textContent = knopf.dataset.kopiert || "✓";
    setTimeout(() => {
      knopf.textContent = alt;
    }, 2000);
  });
});
