import { createFileRoute } from '@tanstack/react-router';

export const Route = createFileRoute('/privacy')({
  component: PrivacyPage,
});

function PrivacyPage() {
  return (
    <section className="section">
      <div className="container">
        <h1>Privacy policy</h1>
        <p className="notice">
          <strong>Placeholder policy.</strong> This text documents the data
          handling the platform actually implements, but it must be reviewed
          and completed by AF Foundation (including applicable Pakistani legal
          requirements) before public launch. See
          docs/launch-configuration.md.
        </p>

        <h2>What we collect</h2>
        <ul>
          <li>Account data: email or phone number, display name, password (hashed).</li>
          <li>Donor profile: blood group, discovery consent record (version and timestamp), availability, approximate location.</li>
          <li>Requests: requested blood group, destination (approximate), urgency, status.</li>
          <li>Notification delivery records and device push subscriptions.</li>
          <li>Donation reports and their verification status; deferral records.</li>
          <li>Security and audit logs (e.g. logins, administrative actions).</li>
        </ul>

        <h2>What we never collect</h2>
        <ul>
          <li>Precise home coordinates — the stored donor location is generalised to roughly one kilometre.</li>
          <li>Medical histories, identity documents or patient records.</li>
          <li>Payment information — the service is free.</li>
        </ul>

        <h2>Who can see what</h2>
        <ul>
          <li>Other users never see donor locations, and requesters never see donor coordinates — only a request's own destination.</li>
          <li>Push notifications contain no medical details, phone numbers or precise locations.</li>
          <li>Moderators and administrators can access records needed to handle abuse reports and verify donations; every administrative action is written to an audit log.</li>
        </ul>

        <h2>Your controls</h2>
        <ul>
          <li>Withdraw discovery consent at any time: you are removed from new matching immediately and your stored approximate location is deleted.</li>
          <li>Disable a push device registration from the app.</li>
          <li>Request account deletion through support (configurable retention windows apply to legally relevant audit records).</li>
        </ul>

        <h2>Processors</h2>
        <p>
          Push notifications are delivered through OneSignal under its own
          privacy policy. The application database is hosted by AF
          Foundation's infrastructure provider.
        </p>

        <h2>Contact</h2>
        <p>Privacy questions: see the contact page.</p>
      </div>
    </section>
  );
}
