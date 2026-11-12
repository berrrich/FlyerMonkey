export function capture() {
    // Blazor restores after the catalog renders, so browser history must not race it.
    history.scrollRestoration = "manual";
    return window.scrollY;
}

export function restore(scrollY, offerId) {
    window.scrollTo({ top: scrollY, behavior: "instant" });
    const card = document.getElementById(`offer-${offerId}`);
    if (card && (card.getBoundingClientRect().bottom < 0 || card.getBoundingClientRect().top > window.innerHeight)) {
        card.scrollIntoView({ block: "center", behavior: "instant" });
    }
}
