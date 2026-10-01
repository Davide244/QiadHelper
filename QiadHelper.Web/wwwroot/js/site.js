document.addEventListener("DOMContentLoaded", () => {
    document.querySelectorAll("[data-auto-dismiss]").forEach(el => {
        const delay = parseInt(el.dataset.autoDismiss ?? "6000", 10);
        window.setTimeout(() => {
            el.style.transition = "opacity 0.4s";
            el.style.opacity = "0";
            window.setTimeout(() => el.remove(), 400);
        }, delay);
    });
});
