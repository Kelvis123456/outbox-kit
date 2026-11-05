"use client";

import { useCallback, useEffect, useRef, useState, useSyncExternalStore } from "react";
import { SCENE_TITLE, STEPS } from "./steps";

const TOTAL = STEPS.length;
const STEP_MS = 3600;

const reducedQuery = "(prefers-reduced-motion: reduce)";
function subscribeReduced(cb: () => void) {
  const mq = window.matchMedia(reducedQuery);
  mq.addEventListener("change", cb);
  return () => mq.removeEventListener("change", cb);
}
const getReduced = () => window.matchMedia(reducedQuery).matches;

/* ---------- SVG helpers ---------- */

type Cls = { bad?: boolean };

function Arrow({
  x1,
  x2,
  y,
  label,
  at,
  step,
  bad,
  ghost,
}: Cls & { x1: number; x2: number; y: number; label: string; at: number; step: number; ghost?: boolean }) {
  const dir = Math.sign(x2 - x1);
  const tip = x2;
  const base = x2 - dir * 9;
  return (
    <g className={`arrow ev${bad ? " bad" : ""}${ghost ? " ghost" : ""}${at >= step ? " on" : ""}`}>
      <line x1={x1} y1={y} x2={ghost ? x2 : base + dir} y2={y} pathLength={ghost ? undefined : 1} />
      {!ghost && <polygon points={`${tip},${y} ${base},${y - 5} ${base},${y + 5}`} />}
      <text x={(x1 + x2) / 2} y={y - 8} textAnchor="middle">
        {label}
      </text>
    </g>
  );
}

function Note({
  x,
  y,
  w,
  text,
  at,
  step,
  tone,
}: {
  x: number;
  y: number;
  w: number;
  text: string;
  at: number;
  step: number;
  tone?: "bad" | "good";
}) {
  return (
    <g className={`note ev${tone ? " " + tone : ""}${at >= step ? " on" : ""}`}>
      <rect x={x - w / 2} y={y} width={w} height={24} rx={4} />
      <text x={x} y={y + 16}>
        {text}
      </text>
    </g>
  );
}

function Cross({ x, y, at, step }: { x: number; y: number; at: number; step: number }) {
  return (
    <g className={`cross ev${at >= step ? " on" : ""}`}>
      <line x1={x - 9} y1={y - 9} x2={x + 9} y2={y + 9} />
      <line x1={x - 9} y1={y + 9} x2={x + 9} y2={y - 9} />
    </g>
  );
}

function Actor({ x, label, h = 38 }: { x: number; label: string; h?: number }) {
  return (
    <g className="actor">
      <rect x={x - 46} y={6} width={92} height={30} rx={4} />
      <text x={x} y={26}>
        {label}
      </text>
      <line className="life" x1={x} y1={36} x2={x} y2={h} />
    </g>
  );
}

/* ---------- the two scenes ---------- */

// Scene A: three steps (1-3). `at` is the global step reached so far.
function SceneA({ at }: { at: number }) {
  const [app, pg, br, co] = [70, 196, 322, 448];
  return (
    <svg className="dg-svg" viewBox="0 0 520 400" aria-hidden="true" focusable="false">
      {[
        [app, "App"],
        [pg, "Postgres"],
        [br, "Broker"],
        [co, "Consumer"],
      ].map(([x, l]) => (
        <Actor key={l} x={x as number} label={l as string} h={290} />
      ))}
      <Arrow x1={app} x2={pg} y={96} label="commit" at={at} step={1} />
      <Note x={pg} y={108} w={104} text="order saved" at={at} step={1} />
      <Cross x={app} y={190} at={at} step={2} />
      <Note x={app} y={206} w={92} text="dies here" at={at} step={2} tone="bad" />
      <Arrow x1={app + 14} x2={br} y={190} label="publish: never sent" at={at} step={3} bad ghost />
      <Note x={br} y={206} w={92} text="empty" at={at} step={3} tone="bad" />
      <Note x={co} y={206} w={104} text="told nothing" at={at} step={3} tone="bad" />
      <g className={`verdict bad ev${at >= 3 ? " on" : ""}`}>
        <text x={260} y={320}>
          Order saved. Nobody told.
        </text>
      </g>
    </svg>
  );
}

// Scene B: steps 4-9.
function SceneB({ at }: { at: number }) {
  const [app, pg, dp, br, co] = [52, 156, 260, 364, 468];
  return (
    <svg className="dg-svg" viewBox="0 0 520 520" aria-hidden="true" focusable="false">
      {[
        [app, "App"],
        [pg, "Postgres"],
        [dp, "Dispatcher"],
        [br, "Broker"],
        [co, "Consumer"],
      ].map(([x, l]) => (
        <Actor key={l} x={x as number} label={l as string} h={x === dp ? 232 : 490} />
      ))}
      {/* the dispatcher lifeline resumes under a new "worker 2" box after the crash */}
      <g className={`ev${at >= 7 ? " on" : ""}`}>
        <line className="life" x1={dp} y1={262} x2={dp} y2={490} />
      </g>

      <Arrow x1={app} x2={pg} y={76} label="one commit" at={at} step={4} />
      <Note x={pg} y={88} w={96} text="row: pending" at={at} step={4} />

      <Arrow x1={dp} x2={pg} y={134} label="lock" at={at} step={5} />
      <Arrow x1={dp} x2={br} y={162} label="publish" at={at} step={5} />
      <Note x={br} y={174} w={72} text="1 copy" at={at} step={5} />

      <Cross x={dp} y={214} at={at} step={6} />
      <Note x={pg} y={202} w={96} text="still pending" at={at} step={6} tone="bad" />

      <g className={`note ev${at >= 7 ? " on" : ""}`}>
        <rect x={dp - 40} y={256} width={80} height={24} rx={4} />
        <text x={dp} y={272}>
          worker 2
        </text>
      </g>
      <Arrow x1={dp} x2={pg} y={310} label="lock again" at={at} step={7} />
      <Arrow x1={dp} x2={br} y={338} label="publish" at={at} step={7} bad />
      <Note x={br} y={350} w={80} text="2 copies" at={at} step={7} tone="bad" />

      <Arrow x1={dp} x2={pg} y={392} label="commit" at={at} step={8} />
      <Note x={pg} y={404} w={96} text="processed" at={at} step={8} tone="good" />

      <Arrow x1={br} x2={co} y={392} label="copy 1" at={at} step={9} />
      <Note x={co} y={404} w={80} text="applied" at={at} step={9} tone="good" />
      <Arrow x1={br} x2={co} y={448} label="copy 2" at={at} step={9} />
      <Note x={co} y={460} w={80} text="skipped" at={at} step={9} tone="good" />

      <g className={`verdict ev${at >= 9 ? " on" : ""}`}>
        <text x={260} y={512}>
          Delivered twice. Applied once.
        </text>
      </g>
    </svg>
  );
}

/* ---------- the player ---------- */

export function Diagram() {
  const reduced = useSyncExternalStore(subscribeReduced, getReduced, () => false);
  const [step, setStep] = useState(0);
  const [playing, setPlaying] = useState(false);
  const rootRef = useRef<HTMLDivElement>(null);
  const autoStarted = useRef(false);

  const go = useCallback((n: number) => setStep(Math.min(TOTAL, Math.max(0, n))), []);
  const manual = useCallback(
    (n: number) => {
      setPlaying(false);
      go(n);
    },
    [go],
  );

  // advance while playing; stop on the last step
  useEffect(() => {
    if (!playing) return;
    const t = window.setTimeout(
      () => {
        setStep(step + 1);
        if (step + 1 >= TOTAL) setPlaying(false);
      },
      step === 0 ? 600 : STEP_MS,
    );
    return () => window.clearTimeout(t);
  }, [playing, step]);

  // start once, the first time the diagram is mostly on screen (never under reduced motion)
  useEffect(() => {
    const el = rootRef.current;
    if (!el || reduced || autoStarted.current) return;
    const io = new IntersectionObserver(
      (entries) => {
        if (entries.some((e) => e.isIntersecting) && !autoStarted.current) {
          autoStarted.current = true;
          setPlaying(true);
          io.disconnect();
        }
      },
      { threshold: 0.5 },
    );
    io.observe(el);
    return () => io.disconnect();
  }, [reduced]);

  const onKey = (e: React.KeyboardEvent) => {
    if (e.key === "ArrowRight") {
      e.preventDefault();
      manual(step + 1);
    } else if (e.key === "ArrowLeft") {
      e.preventDefault();
      manual(step - 1);
    }
  };

  const current = step > 0 ? STEPS[step - 1] : null;
  const scene = current ? current.scene : "a";
  const finished = step >= TOTAL;

  const stepList = (
    <ol className="m-0 grid list-none gap-1 p-0" aria-label="The same story in words">
      {STEPS.map((s, i) => {
        const inner = (
          <>
            <strong>{s.title}</strong>
            <span className="block text-[0.95rem] leading-snug">{s.text}</span>
          </>
        );
        return (
          <li key={s.title}>
            {reduced ? (
              <div className="step-btn" style={{ cursor: "default", color: "var(--ink)" }}>
                {inner}
              </div>
            ) : (
              <button
                type="button"
                className="step-btn"
                data-bad={s.bad ? "true" : undefined}
                aria-current={step === i + 1 ? "step" : undefined}
                onClick={() => manual(i + 1)}
              >
                {inner}
              </button>
            )}
          </li>
        );
      })}
    </ol>
  );

  if (reduced) {
    // Static version of the same story: both scenes fully drawn, no controls, no motion.
    return (
      <div className="dg-static grid grid-cols-[minmax(0,1fr)] gap-10 lg:grid-cols-[minmax(0,32rem)_minmax(0,1fr)] lg:gap-14">
        <div className="grid gap-8">
          <figure className="m-0">
            <figcaption className="mb-2 font-display text-lg font-bold">{SCENE_TITLE.a}</figcaption>
            <SceneA at={99} />
          </figure>
          <figure className="m-0">
            <figcaption className="mb-2 font-display text-lg font-bold">{SCENE_TITLE.b}</figcaption>
            <SceneB at={99} />
          </figure>
        </div>
        {stepList}
      </div>
    );
  }

  return (
    <div
      ref={rootRef}
      onKeyDown={onKey}
      className="grid grid-cols-[minmax(0,1fr)] gap-8 lg:grid-cols-[minmax(0,32rem)_minmax(0,1fr)] lg:gap-14"
    >
      <div>
        <div className="mb-3 flex flex-wrap items-center gap-2" role="group" aria-label="Diagram controls">
          <button
            type="button"
            className="ctl ctl-primary"
            onClick={() => {
              if (finished) {
                setStep(0);
                setPlaying(true);
              } else setPlaying((p) => !p);
            }}
          >
            {finished ? "Replay" : playing ? "Pause" : step === 0 ? "Play" : "Resume"}
          </button>
          <button type="button" className="ctl" disabled={step <= 0} onClick={() => manual(step - 1)}>
            Back
          </button>
          <button type="button" className="ctl" disabled={finished} onClick={() => manual(step + 1)}>
            Next
          </button>
          <span className="num ml-auto text-sm text-muted" aria-hidden="true">
            Step {step} of {TOTAL}
          </span>
        </div>
        <p className="mb-3 mt-0 font-display text-lg font-bold" aria-hidden="true">
          {SCENE_TITLE[scene]}
        </p>
        <div
          className="rounded-md border border-rule bg-surface p-1.5 sm:p-3"
          style={{ borderRadius: "var(--radius)" }}
        >
          <div className="grid min-h-[20rem] place-items-center">
            {scene === "a" ? <SceneA at={step} /> : <SceneB at={step} />}
          </div>
        </div>
        <p className="mb-0 mt-3 text-sm text-muted">
          Buttons work with the keyboard. Left and right arrow keys move between steps.
        </p>
        <div className="sr-only" aria-live={playing ? "off" : "polite"}>
          {current ? `Step ${step} of ${TOTAL}. ${current.title}. ${current.text}` : "Diagram ready. Press Play."}
        </div>
      </div>
      {stepList}
    </div>
  );
}
