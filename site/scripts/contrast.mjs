// WCAG 2.x contrast for every text/background pair the site uses. Keep in sync with :root in globals.css.
const lum = (h) => {
  const c = [1, 3, 5]
    .map((i) => parseInt(h.slice(i, i + 2), 16) / 255)
    .map((v) => (v <= 0.04045 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4));
  return 0.2126 * c[0] + 0.7152 * c[1] + 0.0722 * c[2];
};
const ratio = (a, b) => {
  const [x, y] = [lum(a), lum(b)].sort((p, q) => q - p);
  return (x + 0.05) / (y + 0.05);
};
const themes = {
  light: {
    bg: "#f1f4f7",
    surface: "#fafbfc",
    ink: "#101c28",
    muted: "#435465",
    accent: "#b0300e",
    tint: "#f9e6df",
    line: "#6c7d8f",
  },
  dark: {
    bg: "#0e1721",
    surface: "#152130",
    ink: "#e6ecf1",
    muted: "#9daebe",
    accent: "#ff9272",
    tint: "#2e1d1c",
    line: "#5f7387",
  },
};
// [foreground, background, minimum, where it is used]
const pairs = [
  ["ink", "bg", 4.5, "body and headings"],
  ["ink", "surface", 4.5, "diagram text, active step"],
  ["muted", "bg", 4.5, "secondary text"],
  ["muted", "surface", 4.5, "code comments, hints in panels"],
  ["accent", "bg", 4.5, "failure label, code highlight"],
  ["accent", "surface", 4.5, "failure label in diagram"],
  ["accent", "tint", 4.5, "failure note text"],
  ["bg", "ink", 4.5, "primary button, applied/skipped notes"],
  ["line", "bg", 3, "lifelines, actor outlines (graphics)"],
  ["line", "surface", 3, "actor boxes, active step outline"],
  ["accent", "bg", 3, "crash cross, failure arrows (graphics)"],
];
let fail = false;
for (const [name, t] of Object.entries(themes)) {
  console.log(name);
  for (const [f, b, min, use] of pairs) {
    const r = ratio(t[f], t[b]);
    const ok = r >= min;
    if (!ok) fail = true;
    console.log(`  ${ok ? "ok  " : "FAIL"} ${r.toFixed(2).padStart(5)}:1 (min ${min})  ${f} on ${b}  ${use}`);
  }
}
process.exit(fail ? 1 : 0);
