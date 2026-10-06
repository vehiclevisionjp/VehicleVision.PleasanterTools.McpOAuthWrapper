const key = document.querySelector<HTMLInputElement>("#api-key");
const toggle = document.querySelector<HTMLButtonElement>("[data-key-toggle]");
if (key && toggle) {
  toggle.hidden = false;
  toggle.addEventListener("click", () => {
    const visible = key.type === "password";
    key.type = visible ? "text" : "password";
    toggle.setAttribute("aria-pressed", String(visible));
    toggle.textContent = (visible ? toggle.dataset.hide : toggle.dataset.show) ?? "";
  });
  // 戻る操作で表示状態や入力したキーを残さない。
  window.addEventListener("pagehide", () => { key.type = "password"; key.value = ""; });
}
document.querySelector<HTMLFormElement>("[data-auth-form]")?.addEventListener("submit", (event) => {
  const form = event.currentTarget as HTMLFormElement;
  // submitter を無効化すると decision が送信されないため、状態表示だけを変更する。
  form.setAttribute("aria-busy", "true");
});
