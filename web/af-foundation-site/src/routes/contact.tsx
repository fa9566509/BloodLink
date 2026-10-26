import { createFileRoute } from '@tanstack/react-router';
import { site } from '../config/site';

export const Route = createFileRoute('/contact')({
  component: ContactPage,
});

function ContactPage() {
  const { email, phone, address } = site.contact;
  return (
    <section className="section">
      <div className="container">
        <h1>Contact and support</h1>

        <h2>Support</h2>
        <p>
          For account problems, abuse reports or questions about the service:
        </p>
        <ul>
          <li>
            Email: {email.startsWith('TODO') ? <em>to be published before launch</em> : <a href={`mailto:${email}`}>{email}</a>}
          </li>
          <li>Phone: {phone.startsWith('TODO') ? <em>to be published before launch</em> : phone}</li>
          <li>Address: {address.startsWith('TODO') ? <em>to be published before launch</em> : address}</li>
        </ul>

        <h2>In-app reporting</h2>
        <p>
          Abuse reports submitted from the app go directly to the moderation
          team and are reviewed there. Use the contact details above for
          anything requiring a reply.
        </p>

        <h2>Media and partnerships</h2>
        <p>
          Hospitals, blood centres and organisations interested in working
          with {site.foundationName} can reach us through the same contact
          details once published.
        </p>
      </div>
    </section>
  );
}
