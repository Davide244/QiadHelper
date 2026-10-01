// Site-wide client behaviour. Compiled by tsc (MSBuild) to wwwroot/js/site.js.
// Page-specific logic lives in ClientScripts/pages/*.ts — never inline in .cshtml.

// Auto-dismiss flash messages after a few seconds.
document.addEventListener("DOMContentLoaded", () => {
    document.querySelectorAll<HTMLElement>("[data-auto-dismiss]").forEach(el => {
        const delay = parseInt(el.dataset.autoDismiss ?? "6000", 10);
        window.setTimeout(() => {
            el.style.transition = "opacity 0.4s";
            el.style.opacity = "0";
            window.setTimeout(() => el.remove(), 400);
        }, delay);
    });
});
