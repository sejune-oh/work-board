export const THEME_KEY = "workboard.theme";

/** Runs before paint so a pinned theme does not flash the OS one first. */
export const THEME_INIT_SCRIPT = `try{var t=localStorage.getItem("${THEME_KEY}");if(t==="light"||t==="dark")document.documentElement.dataset.theme=t}catch(e){}`;
