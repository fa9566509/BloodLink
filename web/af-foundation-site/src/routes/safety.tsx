import { createFileRoute } from '@tanstack/react-router';

export const Route = createFileRoute('/safety')({
  component: SafetyPage,
});

function SafetyPage() {
  return (
    <section className="section">
      <div className="container">
        <h1>Donation safety and eligibility</h1>

        <div className="notice">
          <strong>This is a coordination service, not a medical service.</strong>{' '}
          AF Foundation does not operate a blood bank, does not certify
          medical eligibility, does not guarantee that a donor will attend,
          and does not replace professional screening at a donation centre.
        </div>

        <h2>What we record — and what it means</h2>
        <p>
          Donation outcomes are reported by donors and, where possible,
          acknowledged by requesters. A donation may be <em>verified</em> by
          the foundation (for example against a centre record) or remain{' '}
          <em>unverified</em>. We always keep these states distinct, and a
          self-reported donation is never presented as medically verified.
        </p>

        <h2>Deferrals</h2>
        <p>
          After a recorded donation, a deferral policy determines when you are
          shown to matching again. The countdown in the app reflects that
          recorded policy only: <strong>when the countdown ends, final
          eligibility is still determined by the donation centre.</strong>
        </p>

        <h2>At the donation centre</h2>
        <p>
          Qualified transfusion professionals will check your haemoglobin,
          health history and other criteria before every donation. Be honest
          with them — the screening exists to protect both you and the
          recipient. If you are unwell, or something in your health has
          changed, tell the centre staff.
        </p>

        <h2>Who should not use this service to donate</h2>
        <p>
          If a donation centre has told you not to donate — for any reason —
          please do not enable donor discovery. Coordination never overrides a
          professional's judgement.
        </p>
      </div>
    </section>
  );
}
