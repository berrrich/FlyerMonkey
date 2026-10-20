// Decode and allow a paint opportunity before reporting the preview as displayed.
// This is a WebView paint estimate, not a measurement of physical screen scanout.
export async function afterPaint(image) {
    await image.decode();
    await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
}

export function reveal(image, expectedSource) {
    // Navigation or another preview can replace the element while interop is queued.
    if (!image.isConnected || image.getAttribute("src") !== expectedSource ||
        !image.complete || image.naturalWidth === 0) return;
    image.scrollIntoView({
        behavior: matchMedia("(prefers-reduced-motion: reduce)").matches ? "instant" : "smooth",
        block: "center",
        inline: "nearest"
    });
}
