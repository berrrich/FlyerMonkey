export function openInvitation(dialog, invoker, controls) {
    if (!dialog.isConnected || dialog.open || !controls.isConnected) return;
    dialog.returnValue = "";
    dialog.addEventListener("close", () => {
        if (dialog.returnValue === "controls" && controls.isConnected) {
            controls.focus({ preventScroll: true });
            controls.scrollIntoView({
                behavior: matchMedia("(prefers-reduced-motion: reduce)").matches ? "instant" : "smooth",
                block: "start", inline: "nearest"
            });
        } else if (invoker.isConnected) {
            invoker.focus({ preventScroll: true });
        }
    }, { once: true });
    dialog.showModal();
}

export function closeInvitation(dialog, accepted) {
    if (dialog.isConnected && dialog.open) dialog.close(accepted ? "controls" : "dismissed");
}
