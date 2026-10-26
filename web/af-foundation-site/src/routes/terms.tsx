import { createFileRoute } from '@tanstack/react-router';

export const Route = createFileRoute('/terms')({
  component: TermsPage,
});

function TermsPage() {
  return (
    <section className="section">
      <div className="container">
        <h1>Terms of use</h1>
        <p className="notice">
          <strong>Placeholder terms.</strong> Must be reviewed and completed by
          AF Foundation before public launch (see docs/launch-configuration.md).
        </p>

        <h2>1. What this service is</h2>
        <p>
          AF Foundation operates a platform that helps people who need
          whole-blood donations find willing, nearby donors, and helps donors
          respond. The platform coordinates communication only. It is not a
          blood bank and provides no medical services or guarantees.
        </p>

        <h2>2. Your account</h2>
        <p>
          Provide accurate information, keep your credentials secure, and use
          one account per person. Do not use the service to harass, spam, or
          pressure anyone. We may suspend accounts that abuse the service,
          with a recorded reason.
        </p>

        <h2>3. Donors</h2>
        <p>
          Accepting a request is a statement of willingness to help, not a
          binding medical commitment. You may decline or stop participating at
          any time. Only report a donation that actually happened; false
          reports are treated as abuse. The donation centre makes all medical
          eligibility decisions.
        </p>

        <h2>4. Requesters</h2>
        <p>
          Create requests truthfully and cancel them when the need passes.
          Requests are for voluntary, non-remunerated donation: offering or
          requesting payment for blood is prohibited and will lead to
          suspension.
        </p>

        <h2>5. No warranty</h2>
        <p>
          The service is provided "as is". We work to keep it available and
          correct, but we do not guarantee that matches will be found, that
          notifications will be delivered, or that information shown is
          error-free.
        </p>

        <h2>6. Changes</h2>
        <p>
          These terms and the service may change; material changes will be
          announced in the app and on this site.
        </p>
      </div>
    </section>
  );
}
