// Basis: whisperloom-android feat/simple-portal server/simple-whisper/static/app.js
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
