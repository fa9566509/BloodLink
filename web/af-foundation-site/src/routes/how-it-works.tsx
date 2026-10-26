import { createFileRoute } from '@tanstack/react-router';

export const Route = createFileRoute('/how-it-works')({
  component: HowItWorksPage,
});

function HowItWorksPage() {
  return (
    <section className="section">
      <div className="container">
        <h1>How it works</h1>
        <ol className="steps">
          <li>
            <strong>Register.</strong> Create an account with your email or
            phone number. Registration is free.
          </li>
          <li>
            <strong>Become a discoverable donor (optional).</strong> Add your
            blood group, consent to discovery, and an approximate location —
            stored generalised to roughly a one-kilometre area. Your precise
            location is never shared with other users.
          </li>
          <li>
            <strong>Someone needs blood.</strong> A requester creates a
            whole-blood request at a destination such as a hospital, with an
            urgency level and an expiry time.
          </li>
          <li>
            <strong>Eligible donors nearby are notified.</strong> The system
            finds compatible, available donors within the search radius
            (starting at 25 km and widening if needed) and notifies them in
            waves.
          </li>
          <li>
            <strong>You respond.</strong> Accept if you are willing to help, or
            decline quietly. The first acceptance takes the request's single
            donor slot, and the requester is notified to coordinate with you.
          </li>
          <li>
            <strong>Donate at the centre.</strong> The donation itself, and all
            medical screening, happen at the donation centre with qualified
            professionals.
          </li>
          <li>
            <strong>The outcome is recorded.</strong> The donation is reported
            and — where confirmable — acknowledged by the requester and
            verified by the foundation. Confirmed donations activate the
            applicable deferral policy, shown as a countdown in your app.
          </li>
        </ol>

        <div className="notice" style={{ marginTop: '2rem' }}>
          <strong>Your consent, your control.</strong> You can switch off
          discovery or availability at any time. Switching off discovery
          removes you from new matching immediately, and your stored
          approximate location is deleted.
        </div>
      </div>
    </section>
  );
}
