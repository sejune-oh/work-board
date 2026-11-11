// Mockup helper: ?lang=ko|en and ?theme=light|dark pick the variant. English text sits in data-en attributes.
(function () {
  var params = new URLSearchParams(location.search);
  var lang = params.get("lang") === "en" ? "en" : "ko";
  var theme = params.get("theme");
  var root = document.documentElement;
  root.lang = lang;
  if (theme === "light" || theme === "dark") root.dataset.theme = theme;

  function go(key, value) {
    params.set(key, value);
    location.search = params.toString();
  }

  document.addEventListener("DOMContentLoaded", function () {
    if (lang === "en") {
      document.querySelectorAll("[data-en]").forEach(function (el) { el.innerHTML = el.dataset.en; });
      document.querySelectorAll("[data-en-placeholder]").forEach(function (el) { el.placeholder = el.dataset.enPlaceholder; });
      document.querySelectorAll("[data-en-label]").forEach(function (el) { el.setAttribute("aria-label", el.dataset.enLabel); });
      if (document.body.dataset.enTitle) document.title = document.body.dataset.enTitle;
    }
    var current = root.dataset.theme || (matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light");
    document.querySelectorAll("[data-set-lang]").forEach(function (b) {
      b.setAttribute("aria-pressed", String(b.dataset.setLang === lang));
      b.addEventListener("click", function () { go("lang", b.dataset.setLang); });
    });
    document.querySelectorAll("[data-set-theme]").forEach(function (b) {
      b.setAttribute("aria-pressed", String(b.dataset.setTheme === current));
      b.addEventListener("click", function () { go("theme", b.dataset.setTheme); });
    });
  });
})();
