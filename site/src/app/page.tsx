import { Diagram } from "@/components/Diagram";
import { DECISIONS_URL, DESCRIPTION, REPO_URL, SITE_URL } from "@/site";

const jsonLd = {
  "@context": "https://schema.org",
  "@type": "SoftwareSourceCode",
  name: "outbox-kit",
  description: DESCRIPTION,
  programmingLanguage: "C#",
  runtimePlatform: ".NET",
  codeRepositoryUrl: REPO_URL,
  license: "https://opensource.org/license/mit",
  url: SITE_URL,
  author: { "@type": "Person", name: "Kelvis Guerrero" },
};

const groups = [
  {
    name: "Writing",
    tests: [
      {
        name: "Enqueue_is_saved_in_the_same_transaction_as_the_business_change",
        what: "The business row and the outbox row both exist after one SaveChanges.",
      },
      {
        name: "A_failing_save_leaves_neither_the_business_row_nor_the_message",
        what: "A payload Postgres rejects fails the whole save: no business row, no message.",
      },
    ],
  },
  {
    name: "Dispatching",
    tests: [
      {
        name: "Dispatcher_publishes_pending_messages_in_batches_and_marks_them_processed",
        what: "25 pending messages go out in batches of 10, 10 and 5, and all 25 end up processed.",
      },
      {
        name: "Three_dispatchers_running_at_once_never_publish_the_same_message",
        what: "Three dispatchers running at once publish each of 40 messages exactly once, and more than one of them handled messages, so the runs overlapped.",
      },
      {
        name: "A_dispatcher_skips_rows_another_transaction_has_locked_instead_of_waiting",
        what: "Another transaction holds the locks of five rows. The dispatcher publishes the other five without waiting, which plain FOR UPDATE would not do.",
      },
      {
        name: "RunOnce_tells_a_batch_that_all_failed_apart_from_an_empty_queue",
        what: "A run where every publish fails reports Failed and Claimed of 3. An empty queue reports Claimed of 0.",
      },
      {
        name: "Options_that_would_never_make_progress_are_rejected",
        what: "A batch size below 1 or MaxAttempts below 1 throws when the dispatcher is created (three cases).",
      },
    ],
  },
  {
    name: "Failing",
    tests: [
      {
        name: "A_poison_message_is_retried_then_parked_and_does_not_block_the_others",
        what: "A message the broker keeps rejecting is parked after three attempts. The other four still go out.",
      },
      {
        name: "A_publisher_timeout_counts_as_a_failed_attempt_and_does_not_stall_the_queue",
        what: "A TaskCanceledException from the publisher counts as one attempt. The other four go out and nothing is published twice. This is the test for the bug the audit found.",
      },
      {
        name: "Cancelling_the_run_itself_still_rolls_back_and_throws",
        what: "Cancelling the token you passed to RunOnceAsync aborts the run and rolls it back.",
      },
    ],
  },
  {
    name: "The caller's context",
    tests: [
      {
        name: "RunOnce_does_not_save_unrelated_pending_changes_on_the_callers_context",
        what: "A row you added but did not save stays unsaved after a run, and still tracked as yours.",
      },
      {
        name: "A_stale_tracked_row_cannot_make_the_dispatcher_lose_an_attempt",
        what: "Two contexts failing the same row in turn count three attempts, not two.",
      },
      {
        name: "The_change_tracker_does_not_grow_with_the_number_of_runs",
        what: "After six runs over 30 messages the context tracks nothing.",
      },
      {
        name: "Both_throw_a_clear_error_when_a_transaction_is_already_open",
        what: "RunOnceAsync and HandleOnceAsync throw InvalidOperationException if the context already has a transaction.",
      },
      {
        name: "HandleOnce_refuses_a_context_that_already_has_pending_changes",
        what: "HandleOnceAsync throws instead of committing changes it did not make.",
      },
      {
        name: "Dispatcher_and_consumer_work_on_a_context_with_EnableRetryOnFailure",
        what: "Both go through the EF execution strategy, so a retrying context works.",
      },
      {
        name: "Dispatcher_and_consumer_work_with_a_custom_default_schema",
        what: "With HasDefaultSchema the raw SQL still finds the tables.",
      },
    ],
  },
  {
    name: "Consuming",
    tests: [
      {
        name: "Idempotent_consumer_runs_the_handler_once_per_message_even_when_called_twice",
        what: "The second call for the same message id is skipped and the effect exists once.",
      },
      {
        name: "Idempotent_consumer_keeps_consumers_separate",
        what: "The same message id is handled once per consumer name, not once overall.",
      },
      {
        name: "A_handler_that_throws_records_nothing_so_the_message_can_be_retried",
        what: "A failed handler leaves nothing in the inbox, so the retry runs it again.",
      },
      {
        name: "A_failed_handler_can_be_retried_on_the_same_context_without_applying_the_effect_twice",
        what: "After a failure the handler's added rows are dropped from the tracker, so a retry applies the effect once.",
      },
      {
        name: "A_failed_handler_also_reverts_modified_entities",
        what: "Entities the handler modified go back to their original values.",
      },
    ],
  },
  {
    name: "Crashing",
    tests: [
      {
        name: "Killing_the_process_between_publish_and_commit_loses_nothing_and_applies_each_effect_once",
        what: "The chaos test above. A real process, a real kill, a second worker to finish.",
      },
    ],
  },
];

const limits = [
  {
    lead: "PostgreSQL only.",
    text: "It relies on FOR UPDATE SKIP LOCKED, jsonb and ON CONFLICT DO NOTHING. Supporting SQL Server would roughly double the SQL and the tests.",
  },
  {
    lead: "The publish holds the locks.",
    text: "The publish call runs inside the locking transaction, so a slow broker means a long transaction. Fine for modest volume. For more, move the lock to a short lease and publish outside the transaction.",
  },
  {
    lead: "No ordering guarantee at all.",
    text: "created_at is stamped when you call Enqueue, not when the transaction commits, so a message can commit after a later one. Parallel dispatchers and retries reorder further. If order matters, put a sequence number in the payload and have the consumer check it.",
  },
  {
    lead: "Your own transaction is not welcome.",
    text: "RunOnceAsync and HandleOnceAsync open their own transaction and throw if the context already has one. HandleOnceAsync also throws if the context has pending changes.",
  },
  {
    lead: "Retries can duplicate.",
    text: "Both go through the EF execution strategy, so EnableRetryOnFailure works. After a transient failure a dispatcher run repeats and may publish a row again, so the consumer has to be idempotent anyway.",
  },
  {
    lead: "The payload is normalized.",
    text: "It is stored as jsonb, so Postgres changes key order and whitespace and collapses duplicate keys. Do not rely on byte-for-byte equality with what you enqueued.",
  },
  {
    lead: "No cleanup, no metrics.",
    text: "You will want a job that deletes old rows from outbox_messages and inbox_messages.",
  },
  {
    lead: "Not benchmarked.",
    text: "There is no throughput claim on this page because there is no measurement behind one.",
  },
  {
    lead: "Not on NuGet.",
    text: "Clone the repository to use it.",
  },
];

export default function Home() {
  return (
    <>
      <a href="#main" className="skip-link">
        Skip to content
      </a>
      <script type="application/ld+json" dangerouslySetInnerHTML={{ __html: JSON.stringify(jsonLd) }} />

      <header className="wrap flex h-16 items-center justify-between">
        <a href="#top" className="font-display text-xl font-extrabold tracking-tight no-underline">
          outbox-kit
        </a>
        <nav aria-label="Sections" className="flex items-center gap-1 text-[0.95rem]">
          {[
            ["#failure", "Failure"],
            ["#usage", "Usage"],
            ["#tests", "Tests"],
            ["#limits", "Limits"],
          ].map(([href, label]) => (
            <a key={href} href={href} className="hidden px-3 py-3 no-underline hover:underline sm:inline-block">
              {label}
            </a>
          ))}
          <a href={REPO_URL} className="btn btn-ghost ml-2">
            GitHub
          </a>
        </nav>
      </header>

      <main id="main">
        <section
          id="top"
          className="wrap grid items-center gap-10 grid-cols-[minmax(0,1fr)] pb-16 pt-10 md:pt-16 lg:grid-cols-[minmax(0,1.05fr)_minmax(0,0.95fr)] lg:gap-16"
        >
          <div>
            <h1 className="text-[clamp(2.3rem,5.2vw,3.9rem)] font-extrabold">
              Commit the change and the message together.
            </h1>
            <p className="mt-6 max-w-[34rem] text-xl leading-snug text-muted">
              outbox-kit is a small .NET library for the transactional outbox pattern on EF Core and PostgreSQL, with an
              idempotent consumer.
            </p>
            <div className="mt-8 flex flex-wrap gap-3">
              <a href="#failure" className="btn btn-solid">
                Watch the failure
              </a>
              <a href={REPO_URL} className="btn btn-ghost">
                View on GitHub
              </a>
            </div>
          </div>

          <figure className="m-0">
            <pre className="code code-lg m-0" tabIndex={0} aria-label="The problem, two lines of C#">
              <code>
                {"await db.SaveChangesAsync();\n"}
                <span className="c">{"// committed"}</span>
                {"\nawait broker.PublishAsync(message);\n"}
                <span className="k">{"// process dies here: the change exists,\n// nobody was told"}</span>
              </code>
            </pre>
            <figcaption className="mt-3 max-w-[34rem] text-[0.95rem] text-muted">
              Swap the two lines and you get the opposite failure: the message goes out for a change that never
              committed.
            </figcaption>
          </figure>
        </section>

        <section id="failure" className="section border-t border-rule">
          <div className="wrap">
            <h2 className="text-[clamp(1.9rem,4vw,3rem)] font-extrabold">Two timelines, one crash.</h2>
            <p className="mb-10 mt-4 max-w-[40rem] text-lg text-muted">
              The first loses a message without raising any error. The second crashes in the worst possible spot and
              still ends with each effect applied once.
            </p>
            <Diagram />
          </div>
        </section>

        <section id="usage" className="section border-t border-rule">
          <div className="wrap grid grid-cols-[minmax(0,1fr)] gap-10 lg:grid-cols-[minmax(0,0.9fr)_minmax(0,1.1fr)] lg:gap-16">
            <div>
              <h2 className="text-[clamp(1.9rem,4vw,3rem)] font-extrabold">Three calls.</h2>
              <p className="mt-4 text-muted">
                <code className="text-[0.9em]">AddOutbox()</code> in{" "}
                <code className="text-[0.9em]">OnModelCreating</code> adds the outbox and inbox to your model. After
                that:
              </p>
              <div className="mt-6 grid gap-6">
                <p className="m-0">
                  <code className="font-semibold">db.Enqueue(type, payload)</code> adds a row to{" "}
                  <code>outbox_messages</code>. The same <code>SaveChanges</code> saves it, so the change and the
                  message commit together or not at all.
                </p>
                <p className="m-0">
                  <code className="font-semibold">OutboxDispatcher.RunOnceAsync()</code> locks a batch with{" "}
                  <code>FOR UPDATE SKIP LOCKED</code>, calls your <code>IOutboxPublisher</code>, marks the rows
                  processed and commits. Several dispatchers can run at once without publishing the same row twice.
                </p>
                <p className="m-0">
                  <code className="font-semibold">db.HandleOnceAsync(consumer, messageId, handler)</code> records the
                  message id in <code>inbox_messages</code> and runs your handler in one transaction. A redelivered
                  message is skipped.
                </p>
              </div>
            </div>
            <pre className="code m-0 self-start" tabIndex={0} aria-label="Usage example in C#">
              <code>
                {"protected override void OnModelCreating(ModelBuilder b) => b.AddOutbox();\n\n"}
                <span className="c">{"// when you change something"}</span>
                {
                  '\norder.MarkPaid();\ndb.Enqueue("order.paid", JsonSerializer.Serialize(new { order.Id }));\nawait db.SaveChangesAsync();\n\n'
                }
                <span className="c">{"// in a background loop"}</span>
                {
                  "\nvar dispatcher = new OutboxDispatcher(db, myPublisher);\nwhile ((await dispatcher.RunOnceAsync()).Claimed > 0) {}\n\n"
                }
                <span className="c">{"// in the consumer"}</span>
                {'\nawait db.HandleOnceAsync("billing", messageId, async ct => { '}
                <span className="c">{"/* change your own tables */"}</span>
                {" });"}
              </code>
            </pre>
          </div>
        </section>

        <section id="tests" className="section border-t border-rule">
          <div className="wrap">
            <h2 className="text-[clamp(1.9rem,4vw,3rem)] font-extrabold">25 tests against a real Postgres.</h2>
            <p className="mt-4 max-w-[44rem] text-lg text-muted">
              <code className="text-[0.9em]">dotnet test</code> needs Docker: it starts{" "}
              <code className="text-[0.9em]">postgres:17-alpine</code> with Testcontainers. The broker is a stand-in
              that writes to a table on its own connection, so a rollback cannot erase a publish.
            </p>
            <p className="mt-4 max-w-[44rem] text-muted">
              An independent audit found a real bug: a publisher timeout was never counted as an attempt, so a message
              could be republished forever. It is fixed and has its own test below. The decisions that came out of it
              are in <a href={DECISIONS_URL}>docs/decisions.md</a>.
            </p>

            <div className="mt-12 max-w-[52rem]">
              <h3 className="text-2xl font-bold">The chaos test</h3>
              <p className="mt-3 text-lg">
                A separate process publishes three messages and is killed with{" "}
                <code className="text-[0.9em]">Environment.FailFast</code> before the commit. That call skips{" "}
                <code className="text-[0.9em]">finally</code> blocks, so nothing gets to clean up. A second worker then
                finishes the job.
              </p>
              <dl className="num m-0 mt-8 grid grid-cols-2 gap-x-6 gap-y-8 sm:grid-cols-4">
                {[
                  ["20", "messages processed, none lost"],
                  ["23", "publishes reached the broker"],
                  ["3", "of those were duplicates"],
                  ["1", "effect per message after the consumer replayed every one twice"],
                ].map(([n, label]) => (
                  <div key={label} className="border-l-2 border-ink pl-4">
                    <dt className="font-display text-5xl font-extrabold leading-none">{n}</dt>
                    <dd className="m-0 mt-2 text-[0.95rem] leading-snug text-muted">{label}</dd>
                  </div>
                ))}
              </dl>
              <p className="mt-8 text-muted">
                The duplicates are the point. The outbox gives you at-least-once delivery. The inbox is what turns that
                into one effect per message.
              </p>
            </div>

            <div className="mt-16 grid gap-12">
              {groups.map((g) => (
                <div
                  key={g.name}
                  className="grid grid-cols-[minmax(0,1fr)] gap-4 md:grid-cols-[10rem_minmax(0,1fr)] md:gap-10"
                >
                  <h3 className="font-display text-xl font-bold">{g.name}</h3>
                  <dl className="m-0 grid grid-cols-[minmax(0,1fr)] gap-6">
                    {g.tests.map((t) => (
                      <div key={t.name}>
                        <dt className="mono [overflow-wrap:anywhere] text-[0.85rem] font-medium leading-snug">
                          {t.name}
                        </dt>
                        <dd className="m-0 mt-1 max-w-[48rem] text-muted">{t.what}</dd>
                      </div>
                    ))}
                  </dl>
                </div>
              ))}
            </div>
          </div>
        </section>

        <section id="limits" className="section border-t border-rule">
          <div className="wrap">
            <h2 className="text-[clamp(1.9rem,4vw,3rem)] font-extrabold">What it does not do.</h2>
            <p className="mt-4 max-w-[40rem] text-lg text-muted">
              This is a short reference implementation (about 300 lines), not a replacement for MassTransit or
              Wolverine. Read these before you copy it.
            </p>
            <dl className="m-0 mt-12 grid grid-cols-[minmax(0,1fr)] gap-x-16 gap-y-9 md:grid-cols-2">
              {limits.map((l) => (
                <div key={l.lead}>
                  <dt className="font-display text-lg font-bold">{l.lead}</dt>
                  <dd className="m-0 mt-1 max-w-[34rem] text-muted">{l.text}</dd>
                </div>
              ))}
            </dl>
          </div>
        </section>
      </main>

      <footer className="border-t border-rule">
        <div className="wrap flex flex-wrap items-center justify-between gap-4 py-10 text-[0.95rem]">
          <p className="m-0 text-muted">outbox-kit by Kelvis Guerrero. MIT license.</p>
          <p className="m-0 flex flex-wrap gap-x-6 gap-y-2">
            <a href={REPO_URL}>Source on GitHub</a>
            <a href={DECISIONS_URL}>Design decisions</a>
          </p>
        </div>
      </footer>
    </>
  );
}
