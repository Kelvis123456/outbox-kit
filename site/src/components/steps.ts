export type Step = {
  scene: "a" | "b";
  title: string;
  text: string;
  bad?: boolean;
};

// The same story the SVG draws, in words. Rendered as the visible list and as the text alternative.
export const STEPS: Step[] = [
  {
    scene: "a",
    title: "The app commits",
    text: "The order is saved in Postgres. The next line of code is the publish call.",
  },
  {
    scene: "a",
    title: "The process dies",
    text: "A crash, a deploy or a killed container lands right before the publish.",
    bad: true,
  },
  {
    scene: "a",
    title: "Nobody is told",
    text: "The change exists. The broker has nothing and the consumer never hears about it. Nothing will retry it.",
    bad: true,
  },
  {
    scene: "b",
    title: "One commit, two rows",
    text: "The order and its outbox row are written by the same SaveChanges. Both exist or neither does.",
  },
  {
    scene: "b",
    title: "The dispatcher publishes",
    text: "It locks the pending row with FOR UPDATE SKIP LOCKED and sends it to the broker, still inside the transaction.",
  },
  {
    scene: "b",
    title: "Crash before the commit",
    text: "The process dies after the publish and before the commit. The transaction rolls back: the row is still pending, and the broker already has a copy.",
    bad: true,
  },
  {
    scene: "b",
    title: "A second worker retries",
    text: "Another dispatcher locks the same row and publishes it again. The broker now holds two copies.",
    bad: true,
  },
  {
    scene: "b",
    title: "The row is marked processed",
    text: "This time the commit goes through. Nothing was lost.",
  },
  {
    scene: "b",
    title: "The consumer absorbs the duplicate",
    text: "HandleOnceAsync records the message id and runs the handler in one transaction. The first copy is applied, the second is skipped.",
  },
];

export const SCENE_TITLE = { a: "Without an outbox", b: "With the outbox" } as const;
