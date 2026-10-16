// Switch the theme without CSS transitions so that every element changes its colors at once.
function suppressThemeTransitions() {
    document.documentElement.classList.add('theme-switching');
}

function restoreThemeTransitions() {
    // Apply the new theme colors before enabling transitions again.
    getComputedStyle(document.body).backgroundColor;
    document.documentElement.classList.remove('theme-switching');
}
