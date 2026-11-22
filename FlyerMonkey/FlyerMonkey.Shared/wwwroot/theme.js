(() => {
    if (customElements.get("flyermonkey-theme-picker")) return;
    const key = "flyermonkey-theme";
    const themes = [
        ["dark", "Dark theme", "☾"],
        ["medium", "Medium contrast theme", "◐"],
        ["light", "Light theme", "☀︎"]
    ];
    const root = document.documentElement;
    let selected = "dark";

    function apply(theme) {
        selected = themes.some(([name]) => name === theme) ? theme : "dark";
        root.dataset.fmTheme = selected;
        root.dataset.bsTheme = selected === "light" ? "light" : "dark";
        document.querySelector('meta[name="color-scheme"]')?.setAttribute("content", root.dataset.bsTheme);
        document.querySelectorAll("flyermonkey-theme-picker").forEach(picker => picker.updateSelection?.());
    }

    try {
        apply(localStorage.getItem(key));
    } catch {
        apply("dark");
    }

    customElements.define("flyermonkey-theme-picker", class extends HTMLElement {
        connectedCallback() {
            if (this.shadowRoot) return;
            const shadow = this.attachShadow({ mode: "open" });
            shadow.innerHTML = `
                <style>
                    :host { display: inline-flex; flex: 0 0 auto; }
                    div { display: inline-flex; gap: .125rem; }
                    button {
                        display: grid; place-items: center; width: 2.5rem; height: 2.5rem;
                        padding: 0; border: 1px solid transparent; border-radius: .375rem;
                        background: transparent; color: var(--bs-secondary-color);
                        font: 1.25rem/1 system-ui, sans-serif; cursor: pointer;
                    }
                    button:hover { background: var(--bs-tertiary-bg); }
                    button[aria-pressed="true"] {
                        border-color: var(--bs-border-color); background: var(--bs-secondary-bg);
                        color: var(--bs-emphasis-color);
                    }
                    button:focus-visible { outline: 2px solid var(--bs-link-color); outline-offset: 1px; }
                </style>
                <div role="group" aria-label="Color theme"></div>`;
            const group = shadow.querySelector("div");
            for (const [theme, label, icon] of themes) {
                const button = document.createElement("button");
                button.type = "button";
                button.dataset.theme = theme;
                button.setAttribute("aria-label", label);
                button.title = label;
                button.textContent = icon;
                button.addEventListener("click", () => {
                    apply(theme);
                    try { localStorage.setItem(key, theme); } catch { }
                });
                group.append(button);
            }
            this.updateSelection();
        }

        updateSelection() {
            this.shadowRoot?.querySelectorAll("button").forEach(button => {
                button.setAttribute("aria-pressed", String(button.dataset.theme === selected));
            });
        }
    });
})();
